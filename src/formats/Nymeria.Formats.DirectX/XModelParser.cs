using System.Globalization;
using System.Numerics;
using System.Text;
using Nymeria.Core.Models;

namespace Nymeria.Formats.DirectX;

/// <summary>
/// Imports static geometry from a text DirectX <c>.x</c> file into Nymeria's
/// format-independent model data.
/// </summary>
/// <remarks>
/// <para>
/// The supported subset is a text header (<c>xof 0302txt</c> or
/// <c>xof 0303txt</c>), <c>Frame</c> hierarchies with optional
/// <c>FrameTransformMatrix</c> transforms, and <c>Mesh</c> objects with
/// positions, polygon faces, optional <c>MeshNormals</c> and
/// <c>MeshTextureCoords</c>, and optional <c>MeshMaterialList</c> names. Frame
/// transforms are baked into every mesh so one flat model can express the
/// hierarchy. Polygon faces are fan-triangulated deterministically while
/// preserving source order and winding.
/// </para>
/// <para>
/// Binary (<c>bin</c>), compressed (<c>tzip</c> and <c>bzip</c>), skinned, and
/// animated files are rejected explicitly. Template declarations are skipped
/// because they describe schemas rather than scene data; unknown template
/// instances inside a frame are rejected. Materials contribute their instance
/// names as opaque subset labels only. Texture files, diffuse/specular/emissive
/// values, effects, and unknown properties are validated where present but are
/// not represented, because the model contract has no material properties.
/// </para>
/// <para>
/// The complete supported subset, conversions, and limitations are documented in
/// <c>docs/DIRECTX_X.md</c>. The parser treats the stream as untrusted input: it
/// bounds the encoded bytes and token stream, validates the header, nesting,
/// counts, faces, indices, numbers, and transforms, and does not dispose or close
/// the caller's stream. It has no SDL3, GPU, window, or platform dependency.
/// </para>
/// </remarks>
public sealed class XModelParser : IModelParser
{
    /// <summary>The largest accepted text <c>.x</c> input, in bytes.</summary>
    public const int MaxInputBytes = 64 * 1024 * 1024;

    /// <summary>The largest accepted vertex count across the model.</summary>
    public const int MaxVertexCount = 2_000_000;

    /// <summary>The largest accepted triangle count across the model.</summary>
    public const int MaxTriangleCount = 4_000_000;

    /// <summary>The largest accepted number of material subsets.</summary>
    public const int MaxSubsetCount = 65_536;

    /// <summary>The largest accepted vertex count in one polygon face.</summary>
    public const int MaxFaceVertexCount = 65_536;

    /// <summary>
    /// The largest accepted number of face indices in one face list. Three
    /// indices per triangle bound the source data even when the triangle budget
    /// has already been consumed by another mesh.
    /// </summary>
    public const int MaxFaceIndexCount = MaxTriangleCount * 3;

    /// <summary>The largest accepted frame nesting depth.</summary>
    public const int MaxNestingDepth = 64;

    /// <summary>The largest accepted number of frames in one file.</summary>
    public const int MaxFrameCount = 4_096;

    /// <summary>The largest accepted length of one token, in characters.</summary>
    public const int MaxTokenLength = 1024 * 1024;

    /// <summary>The largest accepted number of tokens in one file.</summary>
    public const long MaxTokenCount = 32_000_000;

    private const int HeaderProbeLength = 4096;

    private static ReadOnlySpan<byte> Utf8ByteOrderMark => [0xEF, 0xBB, 0xBF];

    private readonly string? _sourceName;
    private readonly XImportLimits _limits;

    /// <summary>
    /// Creates a text DirectX <c>.x</c> parser.
    /// </summary>
    /// <param name="sourceName">
    /// An optional file or source name included in diagnostics. It does not
    /// affect parsing or open any file.
    /// </param>
    public XModelParser(string? sourceName = null)
        : this(sourceName, XImportLimits.Default)
    {
    }

    internal XModelParser(string? sourceName, XImportLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        _sourceName = string.IsNullOrWhiteSpace(sourceName) ? null : sourceName;
        _limits = limits;
    }

    /// <summary>
    /// Parses one static model from the supplied stream.
    /// </summary>
    /// <param name="stream">
    /// A readable stream positioned at the <c>xof</c> header. The caller owns and
    /// disposes it; the parser leaves it open.
    /// </param>
    /// <returns>The validated model data.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">The stream is not readable.</exception>
    /// <exception cref="ModelFormatException">
    /// The input is malformed, unsupported, or exceeds a documented limit.
    /// </exception>
    public ModelData Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
        {
            throw new ArgumentException("The stream must be readable.", nameof(stream));
        }

        RejectOversizedSeekableInput(stream);

        PrefixStream prefixed = ProbeHeader(stream);

        // The caller owns the stream. Leave it open even when parsing fails.
        using var bounded = new BoundedReadStream(
            prefixed,
            _limits.MaxInputBytes,
            () => Error(
                "the input is larger than the supported limit of " +
                $"{_limits.MaxInputBytes} bytes."));
        using var reader = new StreamReader(
            bounded,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: true);
        var tokens = new XTokenReader(reader, _limits, Error);
        var state = new ParseState(_sourceName, _limits, Error);

        try
        {
            state.ParseHeader(tokens);
            state.ParseTopLevel(tokens);
        }
        catch (DecoderFallbackException exception)
        {
            throw Error(
                $"the input is not valid UTF-8 text: {exception.Message}",
                exception);
        }

        return state.Build();
    }

    private void RejectOversizedSeekableInput(Stream stream)
    {
        if (!stream.CanSeek)
        {
            return;
        }

        long remaining;

        try
        {
            remaining = stream.Length - stream.Position;
        }
        catch (NotSupportedException)
        {
            return;
        }

        if (remaining > _limits.MaxInputBytes)
        {
            throw Error(
                "the input is larger than the supported limit of " +
                $"{_limits.MaxInputBytes} bytes.");
        }
    }

    /// <summary>
    /// Validates the short ASCII header before the text reader is created, so a
    /// binary or compressed file is rejected with an accurate message rather than
    /// an invalid-UTF-8 error. The consumed bytes are replayed to the tokenizer.
    /// </summary>
    private PrefixStream ProbeHeader(Stream stream)
    {
        byte[] prefix = new byte[HeaderProbeLength];
        int length = 0;

        while (length < prefix.Length)
        {
            int read = stream.Read(prefix, length, prefix.Length - length);

            if (read == 0)
            {
                break;
            }

            length += read;
        }

        int position = length >= 3 && prefix.AsSpan(0, 3).SequenceEqual(Utf8ByteOrderMark)
            ? 3
            : 0;
        var words = new List<string>(4);

        while (words.Count < 4 && position < length)
        {
            while (position < length && IsAsciiWhitespace(prefix[position]))
            {
                position++;
            }

            if (position >= length)
            {
                break;
            }

            // Text exporters commonly put a comment above the header. The
            // tokenizer skips it; the probe must too so it can reach 'xof'.
            if (prefix[position] == (byte)'/' &&
                position + 1 < length &&
                prefix[position + 1] == (byte)'/')
            {
                while (position < length && prefix[position] != (byte)'\n')
                {
                    position++;
                }

                continue;
            }

            int start = position;

            while (position < length &&
                   !IsAsciiWhitespace(prefix[position]) &&
                   prefix[position] != (byte)'/')
            {
                position++;
            }

            if (position == start)
            {
                break;
            }

            words.Add(Encoding.ASCII.GetString(prefix, start, position - start));
        }

        if (words.Count < 3)
        {
            throw Error(
                1,
                "the input does not start with a complete DirectX " +
                "'xof <version><format> <precision>' header.");
        }

        if (!string.Equals(words[0], "xof", StringComparison.Ordinal))
        {
            throw Error(
                1,
                $"the input starts with '{words[0]}', not the DirectX 'xof' header. " +
                "This parser reads text DirectX .x files only.");
        }

        // The standard header joins the version and format in one token, such as
        // '0303txt'. A few writers insert whitespace, so both spellings are
        // accepted here and by the tokenizing header parser.
        string format;

        if (words[1].Length == 4)
        {
            if (words.Count < 4)
            {
                throw Error(
                    1,
                    "the DirectX header is missing its format type or float " +
                    "precision.");
            }

            format = words[2];
        }
        else if (words[1].Length > 4)
        {
            format = words[1][4..];
        }
        else
        {
            throw Error(
                1,
                $"the DirectX version token '{words[1]}' is not valid. Expected a " +
                "token such as '0303txt'.");
        }

        switch (format)
        {
            case "txt":
                break;
            case "bin":
                throw Error(
                    1,
                    "the input is a binary DirectX .x file (xof ... bin ...). " +
                    "Binary .x files are not supported; export the model as text " +
                    "(for example 'xof 0303txt 0032').");
            case "tzip":
            case "bzip":
                throw Error(
                    1,
                    $"the input is a compressed DirectX .x file (xof ... " +
                    $"{format} ...). Compressed .x files are not supported; " +
                    "decompress and export the model as text.");
            default:
                throw Error(
                    1,
                    $"the DirectX .x format type '{format}' is not supported. " +
                    "Only text ('txt') files are supported.");
        }

        return new PrefixStream(prefix, length, stream);
    }

    private static bool IsAsciiWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'
            or (byte)'\f' or (byte)'\v';
    }

    private ModelFormatException Error(int lineNumber, string message)
    {
        string location = _sourceName is null
            ? $"line {lineNumber}"
            : $"{_sourceName}: line {lineNumber}";

        return new ModelFormatException($"{location}: {message}");
    }

    private ModelFormatException Error(string message, Exception? innerException = null)
    {
        string text = _sourceName is null ? message : $"{_sourceName}: {message}";

        return innerException is null
            ? new ModelFormatException(text)
            : new ModelFormatException(text, innerException);
    }

    private sealed class BoundedReadStream(
        Stream inner,
        int maxBytes,
        Func<ModelFormatException> error) : Stream
    {
        private int _bytesRead;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadCore(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            return ReadCore(buffer);
        }

        public override int ReadByte()
        {
            Span<byte> buffer = stackalloc byte[1];
            return ReadCore(buffer) == 0 ? -1 : buffer[0];
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        private int ReadCore(Span<byte> buffer)
        {
            if (buffer.IsEmpty)
            {
                return 0;
            }

            int remaining = maxBytes - _bytesRead;

            if (remaining <= 0)
            {
                // Probe one byte so a file that ends exactly at the limit is
                // accepted, while any additional byte fails predictably.
                Span<byte> probe = stackalloc byte[1];
                return inner.Read(probe) == 0 ? 0 : throw error();
            }

            int read = inner.Read(buffer[..Math.Min(buffer.Length, remaining)]);
            _bytesRead += read;
            return read;
        }
    }

    private sealed class ParseState(
        string? sourceName,
        XImportLimits limits,
        Func<int, string, ModelFormatException> error)
    {
        private readonly List<MeshData> _meshes = [];
        private readonly string? _sourceName = sourceName;
        private readonly XImportLimits _limits = limits;
        private readonly Func<int, string, ModelFormatException> _error = error;

        private string? _modelName;
        private int _totalSourceVertices;
        private int _totalUnifiedVertices;
        private long _totalTriangles;
        private int _totalSubsets;
        private int _frameCount;

        public void ParseHeader(XTokenReader tokens)
        {
            XToken first = tokens.Next();

            if (first.Kind != XTokenKind.Word ||
                !string.Equals(first.Text, "xof", StringComparison.Ordinal))
            {
                throw Error(
                    first.LineNumber,
                    $"expected the DirectX 'xof' header, but found {Describe(first)}.");
            }

            XToken versionAndFormat = ReadWord(tokens, "the DirectX version and format");
            string versionText;
            string formatText;

            if (versionAndFormat.Text.Length == 4)
            {
                versionText = versionAndFormat.Text;
                formatText = ReadWord(tokens, "the format type").Text;
            }
            else
            {
                if (versionAndFormat.Text.Length < 5)
                {
                    throw Error(
                        versionAndFormat.LineNumber,
                        $"the DirectX version token '{versionAndFormat.Text}' is " +
                        "not valid. Expected a token such as '0303txt'.");
                }

                versionText = versionAndFormat.Text[..4];
                formatText = versionAndFormat.Text[4..];
            }

            XToken precision = ReadWord(tokens, "the float precision");

            if (versionText is not ("0302" or "0303"))
            {
                throw Error(
                    versionAndFormat.LineNumber,
                    $"DirectX version '{versionText}' is not supported. Supported " +
                    "text versions are 0302 and 0303.");
            }

            if (!string.Equals(formatText, "txt", StringComparison.Ordinal))
            {
                throw Error(
                    versionAndFormat.LineNumber,
                    $"the DirectX .x format type '{formatText}' is not supported. " +
                    "Only text ('txt') files are supported.");
            }

            if (precision.Text is not ("0032" or "0064"))
            {
                throw Error(
                    precision.LineNumber,
                    $"the float precision '{precision.Text}' is not supported. " +
                    "Supported text precisions are 0032 and 0064.");
            }
        }

        public void ParseTopLevel(XTokenReader tokens)
        {
            while (true)
            {
                XToken token = tokens.Next();

                if (token.Kind == XTokenKind.EndOfInput)
                {
                    return;
                }

                if (token.Kind is XTokenKind.Comma or XTokenKind.Semicolon)
                {
                    continue;
                }

                if (token.Kind == XTokenKind.CloseBrace)
                {
                    throw Error(
                        token.LineNumber,
                        "unexpected '}' at the top level of the file.");
                }

                if (token.Kind != XTokenKind.Word)
                {
                    throw Error(
                        token.LineNumber,
                        $"expected a top-level Frame or Mesh, but found " +
                        $"{Describe(token)}.");
                }

                switch (token.Text)
                {
                    case "template":
                        SkipTemplateDeclaration(tokens, token.LineNumber);
                        break;
                    case "Frame":
                        ParseFrame(tokens, token.LineNumber, Matrix4x4.Identity, 0);
                        break;
                    case "Mesh":
                        ParseMesh(tokens, token.LineNumber, Matrix4x4.Identity);
                        break;
                    case "AnimationSet":
                    case "Animation":
                    case "AnimationKey":
                    case "AnimTicksPerSecond":
                        throw AnimationUnsupported(token);
                    case "SkinWeights":
                    case "XSkinMeshHeader":
                        throw SkinningUnsupported(token);
                    default:
                        throw Error(
                            token.LineNumber,
                            $"the top-level object '{token.Text}' is not supported. " +
                            "Only Frame, Mesh, and template declarations are " +
                            "imported.");
                }
            }
        }

        public ModelData Build()
        {
            if (_meshes.Count == 0)
            {
                throw Error(
                    "the file contains no Mesh objects; a static model requires " +
                    "at least one mesh.");
            }

            return new ModelData(
                _modelName ?? _sourceName ?? "DirectX X",
                _meshes.ToArray());
        }

        private void SkipTemplateDeclaration(XTokenReader tokens, int typeLine)
        {
            XToken token = tokens.Next();

            if (token.Kind == XTokenKind.Word)
            {
                token = tokens.Next();
            }

            if (token.Kind != XTokenKind.OpenBrace)
            {
                throw Error(
                    token.LineNumber,
                    $"expected a template name and '{{' after the template keyword, " +
                    $"but found {Describe(token)}.");
            }

            int depth = 1;

            while (depth > 0)
            {
                token = tokens.Next();

                if (token.Kind == XTokenKind.EndOfInput)
                {
                    throw Error(
                        typeLine,
                        "the template declaration is not closed by '}'.");
                }

                if (token.Kind == XTokenKind.OpenBrace)
                {
                    depth++;

                    if (depth > _limits.MaxNestingDepth)
                    {
                        throw Error(
                            token.LineNumber,
                            $"the template declaration is nested more than " +
                            $"{_limits.MaxNestingDepth} levels deep.");
                    }
                }
                else if (token.Kind == XTokenKind.CloseBrace)
                {
                    depth--;
                }
            }
        }

        private void ParseFrame(
            XTokenReader tokens,
            int typeLine,
            Matrix4x4 parentWorld,
            int depth)
        {
            if (depth >= _limits.MaxNestingDepth)
            {
                throw Error(
                    typeLine,
                    $"frame nesting exceeds the supported limit of " +
                    $"{_limits.MaxNestingDepth} levels.");
            }

            _frameCount++;

            if (_frameCount > _limits.MaxFrameCount)
            {
                throw Error(
                    typeLine,
                    $"the file contains more than {_limits.MaxFrameCount} frames, " +
                    "which exceeds the supported limit.");
            }

            string? name = ReadOptionalNameAndOpen(tokens, typeLine, "Frame");

            if (_modelName is null && name is not null)
            {
                _modelName = name;
            }

            Matrix4x4 world = parentWorld;
            bool hasTransform = false;
            bool hasGeometry = false;

            while (true)
            {
                XToken token = tokens.Next();

                if (token.Kind == XTokenKind.CloseBrace)
                {
                    break;
                }

                if (token.Kind == XTokenKind.EndOfInput)
                {
                    throw Error(
                        typeLine,
                        "the Frame is not closed by '}'.");
                }

                if (token.Kind is XTokenKind.Comma or XTokenKind.Semicolon)
                {
                    continue;
                }

                if (token.Kind != XTokenKind.Word)
                {
                    throw Error(
                        token.LineNumber,
                        $"expected a frame element, but found {Describe(token)}.");
                }

                switch (token.Text)
                {
                    case "FrameTransformMatrix":
                        if (hasTransform)
                        {
                            throw Error(
                                token.LineNumber,
                                "the Frame contains more than one " +
                                "FrameTransformMatrix.");
                        }

                        if (hasGeometry)
                        {
                            throw Error(
                                token.LineNumber,
                                "FrameTransformMatrix must precede the Frame's Mesh " +
                                "and child Frame objects. Move it to the start of " +
                                "the Frame so transforms can be baked in order.");
                        }

                        Matrix4x4 local = ReadTransformMatrix(tokens, token.LineNumber);
                        world = local * parentWorld;
                        hasTransform = true;
                        break;
                    case "Mesh":
                        hasGeometry = true;
                        ParseMesh(tokens, token.LineNumber, world);
                        break;
                    case "Frame":
                        hasGeometry = true;
                        ParseFrame(tokens, token.LineNumber, world, depth + 1);
                        break;
                    case "AnimationSet":
                    case "Animation":
                    case "AnimationKey":
                    case "AnimTicksPerSecond":
                        throw AnimationUnsupported(token);
                    case "SkinWeights":
                    case "XSkinMeshHeader":
                        throw SkinningUnsupported(token);
                    case "template":
                        throw Error(
                            token.LineNumber,
                            "template declarations are supported only at the top " +
                            "level of the file, before Frame objects.");
                    default:
                        throw Error(
                            token.LineNumber,
                            $"the Frame element '{token.Text}' is not supported. " +
                            "Only FrameTransformMatrix, Mesh, and Frame are " +
                            "imported.");
                }
            }
        }

        private Matrix4x4 ReadTransformMatrix(XTokenReader tokens, int typeLine)
        {
            ReadOptionalNameAndOpen(tokens, typeLine, "FrameTransformMatrix");
            float[] values = new float[16];

            for (int i = 0; i < values.Length; i++)
            {
                values[i] = ReadFloat(tokens, "a frame transform matrix value");

                if (i < values.Length - 1)
                {
                    Expect(tokens, XTokenKind.Comma, "',' between matrix values");
                }
                else
                {
                    Expect(tokens, XTokenKind.Semicolon, "';' after the matrix");
                    Expect(tokens, XTokenKind.Semicolon, "the second ';' after the matrix");
                }
            }

            Expect(tokens, XTokenKind.CloseBrace, "'}' after the frame transform matrix");

            bool isAffine =
                MathF.Abs(values[3]) <= 1e-6f &&
                MathF.Abs(values[7]) <= 1e-6f &&
                MathF.Abs(values[11]) <= 1e-6f &&
                MathF.Abs(values[15] - 1.0f) <= 1e-6f;

            if (!isAffine)
            {
                throw Error(
                    typeLine,
                    "the FrameTransformMatrix is not an affine transform. The last " +
                    "row must be 0, 0, 0, 1.");
            }

            return new Matrix4x4(
                values[0], values[1], values[2], values[3],
                values[4], values[5], values[6], values[7],
                values[8], values[9], values[10], values[11],
                values[12], values[13], values[14], values[15]);
        }

        private void ParseMesh(XTokenReader tokens, int typeLine, Matrix4x4 world)
        {
            string? name = ReadOptionalNameAndOpen(tokens, typeLine, "Mesh");

            if (_modelName is null && name is not null)
            {
                _modelName = name;
            }

            int vertexCount = ReadCount(
                tokens,
                "the mesh vertex count",
                _limits.MaxVertexCount);

            if (vertexCount == 0)
            {
                throw Error(
                    typeLine,
                    "the Mesh contains no vertices.");
            }

            if (vertexCount > _limits.MaxVertexCount - _totalSourceVertices)
            {
                throw Error(
                    typeLine,
                    $"the model would contain more than {_limits.MaxVertexCount} " +
                    "vertices.");
            }

            Vector3[] positions = ReadVectorArray(
                tokens,
                vertexCount,
                "vertex position");
            _totalSourceVertices += vertexCount;

            int faceCount = ReadCount(
                tokens,
                "the mesh face count",
                _limits.MaxTriangleCount);

            if (faceCount == 0)
            {
                throw Error(
                    typeLine,
                    "the Mesh contains no faces.");
            }

            FaceList faces = ReadFaces(
                tokens,
                faceCount,
                vertexCount,
                "face");

            long remainingTriangles = _limits.MaxTriangleCount - _totalTriangles;

            if (faces.TriangleCount > remainingTriangles)
            {
                throw Error(
                    typeLine,
                    $"the model would contain {faces.TriangleCount} triangle(s), " +
                    "which exceeds the supported limit of " +
                    $"{_limits.MaxTriangleCount}.");
            }

            Vector3[]? normals = null;
            FaceList? normalFaces = null;
            Vector2[]? textureCoordinates = null;
            int[]? faceMaterials = null;
            string?[]? materialNames = null;

            while (true)
            {
                XToken token = tokens.Next();

                if (token.Kind == XTokenKind.CloseBrace)
                {
                    break;
                }

                if (token.Kind == XTokenKind.EndOfInput)
                {
                    throw Error(typeLine, "the Mesh is not closed by '}'.");
                }

                if (token.Kind is XTokenKind.Comma or XTokenKind.Semicolon)
                {
                    continue;
                }

                if (token.Kind != XTokenKind.Word)
                {
                    throw Error(
                        token.LineNumber,
                        $"expected a mesh element, but found {Describe(token)}.");
                }

                switch (token.Text)
                {
                    case "MeshNormals":
                        if (normals is not null)
                        {
                            throw Error(
                                token.LineNumber,
                                "the Mesh contains more than one MeshNormals object.");
                        }

                        ReadMeshNormals(
                            tokens,
                            token.LineNumber,
                            faces,
                            out normals,
                            out normalFaces);
                        break;
                    case "MeshTextureCoords":
                        if (textureCoordinates is not null)
                        {
                            throw Error(
                                token.LineNumber,
                                "the Mesh contains more than one " +
                                "MeshTextureCoords object.");
                        }

                        textureCoordinates = ReadTextureCoordinates(
                            tokens,
                            token.LineNumber,
                            vertexCount);
                        break;
                    case "MeshMaterialList":
                        if (faceMaterials is not null)
                        {
                            throw Error(
                                token.LineNumber,
                                "the Mesh contains more than one " +
                                "MeshMaterialList object.");
                        }

                        ReadMaterialList(
                            tokens,
                            token.LineNumber,
                            faceCount,
                            out faceMaterials,
                            out materialNames);
                        break;
                    case "SkinWeights":
                    case "XSkinMeshHeader":
                        throw SkinningUnsupported(token);
                    case "MeshVertexColors":
                    case "VertexDuplicationIndices":
                    case "DeclData":
                    case "FVFData":
                        throw Error(
                            token.LineNumber,
                            $"the mesh element '{token.Text}' is not supported. " +
                            "Only MeshNormals, MeshTextureCoords, and " +
                            "MeshMaterialList are imported.");
                    default:
                        throw Error(
                            token.LineNumber,
                            $"the mesh element '{token.Text}' is not supported. " +
                            "Only MeshNormals, MeshTextureCoords, and " +
                            "MeshMaterialList are imported.");
                }
            }

            BuildMesh(
                typeLine,
                positions,
                faces,
                normals,
                normalFaces,
                textureCoordinates,
                faceMaterials,
                materialNames,
                world);

            _totalTriangles += faces.TriangleCount;
        }

        private void ReadMeshNormals(
            XTokenReader tokens,
            int typeLine,
            FaceList meshFaces,
            out Vector3[] normals,
            out FaceList normalFaces)
        {
            ReadOptionalNameAndOpen(tokens, typeLine, "MeshNormals");

            int normalCount = ReadCount(
                tokens,
                "the normal count",
                _limits.MaxVertexCount);

            if (normalCount == 0)
            {
                throw Error(typeLine, "the MeshNormals object contains no normals.");
            }

            normals = ReadVectorArray(tokens, normalCount, "normal");

            int faceCount = ReadCount(
                tokens,
                "the MeshNormals face count",
                _limits.MaxTriangleCount);

            if (faceCount != meshFaces.VertexCounts.Length)
            {
                throw Error(
                    typeLine,
                    $"the MeshNormals object describes {faceCount} face(s), but " +
                    $"the Mesh has {meshFaces.VertexCounts.Length}.");
            }

            normalFaces = ReadFaces(tokens, faceCount, normalCount, "normal face");

            for (int i = 0; i < faceCount; i++)
            {
                if (normalFaces.VertexCounts[i] != meshFaces.VertexCounts[i])
                {
                    throw Error(
                        typeLine,
                        $"MeshNormals face {i} has " +
                        $"{normalFaces.VertexCounts[i]} corner(s), but the Mesh " +
                        $"face has {meshFaces.VertexCounts[i]}.");
                }
            }

            Expect(tokens, XTokenKind.CloseBrace, "'}' after the MeshNormals object");
        }

        private Vector2[] ReadTextureCoordinates(
            XTokenReader tokens,
            int typeLine,
            int vertexCount)
        {
            ReadOptionalNameAndOpen(tokens, typeLine, "MeshTextureCoords");

            int count = ReadCount(
                tokens,
                "the texture coordinate count",
                _limits.MaxVertexCount);

            if (count != vertexCount)
            {
                throw Error(
                    typeLine,
                    $"the MeshTextureCoords object contains {count} coordinate(s), " +
                    $"but the Mesh has {vertexCount} vertices.");
            }

            Expect(
                tokens,
                XTokenKind.Semicolon,
                "';' after the texture coordinate count");

            var coordinates = new Vector2[count];

            for (int i = 0; i < count; i++)
            {
                float u = ReadFloat(tokens, "texture coordinate u");
                Expect(tokens, XTokenKind.Semicolon, "';' after texture coordinate u");
                float v = ReadFloat(tokens, "texture coordinate v");
                Expect(tokens, XTokenKind.Semicolon, "';' after texture coordinate v");
                coordinates[i] = new Vector2(u, v);

                if (i < count - 1)
                {
                    Expect(
                        tokens,
                        XTokenKind.Comma,
                        "',' between texture coordinates");
                }
                else
                {
                    Expect(
                        tokens,
                        XTokenKind.Semicolon,
                        "the second ';' after the texture coordinates");
                }
            }

            Expect(
                tokens,
                XTokenKind.CloseBrace,
                "'}' after the MeshTextureCoords object");

            return coordinates;
        }

        private void ReadMaterialList(
            XTokenReader tokens,
            int typeLine,
            int meshFaceCount,
            out int[] faceMaterials,
            out string?[] materialNames)
        {
            ReadOptionalNameAndOpen(tokens, typeLine, "MeshMaterialList");

            int materialCount = ReadCount(
                tokens,
                "the material count",
                _limits.MaxSubsetCount);

            Expect(
                tokens,
                XTokenKind.Semicolon,
                "';' after the material count");

            if (materialCount == 0)
            {
                throw Error(
                    typeLine,
                    "the MeshMaterialList declares no materials.");
            }

            int faceCount = ReadCount(
                tokens,
                "the material face count",
                _limits.MaxTriangleCount);

            if (faceCount != meshFaceCount)
            {
                throw Error(
                    typeLine,
                    $"the MeshMaterialList describes {faceCount} face(s), but the " +
                    $"Mesh has {meshFaceCount}.");
            }

            Expect(
                tokens,
                XTokenKind.Semicolon,
                "';' after the material face count");

            faceMaterials = new int[faceCount];

            for (int i = 0; i < faceCount; i++)
            {
                faceMaterials[i] = ReadIndex(
                    tokens,
                    $"material index for face {i}",
                    materialCount);

                if (i < faceCount - 1)
                {
                    Expect(tokens, XTokenKind.Comma, "',' between material indices");
                }
                else
                {
                    Expect(
                        tokens,
                        XTokenKind.Semicolon,
                        "';' after the material indices");
                    Expect(
                        tokens,
                        XTokenKind.Semicolon,
                        "the second ';' after the material indices");
                }
            }

            materialNames = new string?[materialCount];

            for (int i = 0; i < materialCount; i++)
            {
                SkipSeparators(tokens);
                XToken token = tokens.Next();

                if (token.Kind != XTokenKind.Word ||
                    !string.Equals(token.Text, "Material", StringComparison.Ordinal))
                {
                    throw Error(
                        token.LineNumber,
                        $"expected Material {i} of {materialCount}, but found " +
                        $"{Describe(token)}.");
                }

                materialNames[i] = ReadMaterial(tokens, token.LineNumber);
            }

            Expect(
                tokens,
                XTokenKind.CloseBrace,
                "'}' after the MeshMaterialList object");
        }

        private string? ReadMaterial(XTokenReader tokens, int typeLine)
        {
            string? name = ReadOptionalNameAndOpen(tokens, typeLine, "Material");

            // Diffuse RGBA, specular power, specular RGB, and emissive RGB are
            // validated for structure and finite values. Nymeria's model data has
            // no material properties, so only the instance name is preserved.
            ReadTerminatedFloats(tokens, 4, "diffuse color");
            ReadFloat(tokens, "specular power");
            Expect(tokens, XTokenKind.Semicolon, "';' after the specular power");
            ReadTerminatedFloats(tokens, 3, "specular color");
            ReadTerminatedFloats(tokens, 3, "emissive color");

            while (true)
            {
                XToken token = tokens.Next();

                if (token.Kind == XTokenKind.CloseBrace)
                {
                    return name;
                }

                if (token.Kind == XTokenKind.EndOfInput)
                {
                    throw Error(typeLine, "the Material object is not closed by '}'.");
                }

                if (token.Kind is XTokenKind.Comma or XTokenKind.Semicolon)
                {
                    continue;
                }

                if (token.Kind == XTokenKind.Word &&
                    string.Equals(token.Text, "TextureFilename", StringComparison.Ordinal))
                {
                    ReadTextureFilename(tokens, token.LineNumber);
                    continue;
                }

                throw Error(
                    token.LineNumber,
                    $"the material property {Describe(token)} is not supported. " +
                    "Only the material instance name is preserved; TextureFilename " +
                    "is validated but no texture is loaded.");
            }
        }

        private void ReadTextureFilename(XTokenReader tokens, int typeLine)
        {
            ReadOptionalNameAndOpen(tokens, typeLine, "TextureFilename");
            XToken value = tokens.Next();

            if (value.Kind != XTokenKind.String)
            {
                throw Error(
                    value.LineNumber,
                    $"expected a quoted texture filename, but found " +
                    $"{Describe(value)}.");
            }

            Expect(
                tokens,
                XTokenKind.Semicolon,
                "';' after the texture filename");
            Expect(
                tokens,
                XTokenKind.CloseBrace,
                "'}' after the TextureFilename object");
        }

        private void ReadTerminatedFloats(
            XTokenReader tokens,
            int count,
            string description)
        {
            for (int i = 0; i < count; i++)
            {
                ReadFloat(tokens, description);
                Expect(tokens, XTokenKind.Semicolon, $"';' after a {description} value");
            }

            Expect(
                tokens,
                XTokenKind.Semicolon,
                $"the second ';' after the {description}");
        }

        private void BuildMesh(
            int typeLine,
            Vector3[] positions,
            FaceList faces,
            Vector3[]? normals,
            FaceList? normalFaces,
            Vector2[]? textureCoordinates,
            int[]? faceMaterials,
            string?[]? materialNames,
            Matrix4x4 world)
        {
            Matrix4x4 normalMatrix = default;

            if (normals is not null)
            {
                if (!Matrix4x4.Invert(world, out Matrix4x4 inverse))
                {
                    throw Error(
                        typeLine,
                        "the frame transform is singular, so the mesh normals " +
                        "cannot be transformed. Supply an invertible " +
                        "FrameTransformMatrix or remove MeshNormals.");
                }

                normalMatrix = Matrix4x4.Transpose(inverse);
            }

            // A negative determinant mirrors the mesh. The source already needs
            // one winding reversal for Direct3D's left-handed frame, so a mirror
            // cancels it.
            bool keepSourceWinding = world.GetDeterminant() < 0.0f;
            var indices = new uint[checked((int)(faces.TriangleCount * 3))];
            int indexCursor = 0;
            var outputPositions = new List<Vector3>(positions.Length);
            List<Vector3>? outputNormals = normals is null
                ? null
                : new List<Vector3>(positions.Length);
            List<Vector2>? outputTextureCoordinates = textureCoordinates is null
                ? null
                : new List<Vector2>(positions.Length);
            var corners = new Dictionary<CornerKey, uint>();
            var subsets = new List<MeshSubset>();
            int faceCornerOffset = 0;
            int groupStart = 0;
            int groupMaterial = -1;

            uint AddCorner(int positionIndex, int normalIndex)
            {
                var key = new CornerKey(positionIndex, normalIndex);

                if (corners.TryGetValue(key, out uint existing))
                {
                    return existing;
                }

                if (outputPositions.Count >=
                    _limits.MaxVertexCount - _totalUnifiedVertices)
                {
                    throw Error(
                        typeLine,
                        $"the model would contain more than " +
                        $"{_limits.MaxVertexCount} mesh vertices after combining " +
                        "positions, normals, and texture coordinates.");
                }

                Vector3 position = Vector3.Transform(positions[positionIndex], world);
                position = new Vector3(position.X, position.Y, -position.Z);

                if (!IsFinite(position))
                {
                    throw Error(
                        typeLine,
                        $"vertex {positionIndex} is not finite after the frame " +
                        "transform.");
                }

                outputPositions.Add(position);

                if (outputNormals is not null)
                {
                    Vector3 normal = Vector3.TransformNormal(
                        normals![normalIndex],
                        normalMatrix);
                    normal = new Vector3(normal.X, normal.Y, -normal.Z);

                    if (!IsFinite(normal))
                    {
                        throw Error(
                            typeLine,
                            $"normal {normalIndex} is not finite after the frame " +
                            "transform.");
                    }

                    float length = normal.Length();

                    if (length > 0.0f && float.IsFinite(length))
                    {
                        normal /= length;
                    }

                    outputNormals.Add(normal);
                }

                if (outputTextureCoordinates is not null)
                {
                    // Direct3D texture coordinates already use a top-left origin
                    // with +V down, which matches Nymeria's convention.
                    outputTextureCoordinates.Add(textureCoordinates![positionIndex]);
                }

                uint index = (uint)outputPositions.Count - 1;
                corners.Add(key, index);
                return index;
            }

            for (int face = 0; face < faces.VertexCounts.Length; face++)
            {
                int faceStart = indexCursor;
                int material = faceMaterials?[face] ?? 0;

                if (faceMaterials is not null && material != groupMaterial)
                {
                    if (groupMaterial >= 0)
                    {
                        AddSubset(
                            subsets,
                            groupStart,
                            faceStart - groupStart,
                            groupMaterial,
                            materialNames,
                            typeLine);
                    }

                    groupStart = faceStart;
                    groupMaterial = material;
                }

                int vertexCount = faces.VertexCounts[face];
                uint first = AddCorner(
                    faces.Indices[faceCornerOffset],
                    normalFaces?.Indices[faceCornerOffset] ?? -1);

                for (int i = 1; i < vertexCount - 1; i++)
                {
                    int normalOffset = faceCornerOffset + i;
                    uint second = AddCorner(
                        faces.Indices[normalOffset],
                        normalFaces?.Indices[normalOffset] ?? -1);
                    uint third = AddCorner(
                        faces.Indices[normalOffset + 1],
                        normalFaces?.Indices[normalOffset + 1] ?? -1);

                    // Direct3D uses clockwise front faces in a left-handed frame.
                    // Reversing produces counter-clockwise faces after Z negation.
                    if (keepSourceWinding)
                    {
                        indices[indexCursor++] = first;
                        indices[indexCursor++] = second;
                        indices[indexCursor++] = third;
                    }
                    else
                    {
                        indices[indexCursor++] = first;
                        indices[indexCursor++] = third;
                        indices[indexCursor++] = second;
                    }
                }

                faceCornerOffset += vertexCount;
            }

            if (faceMaterials is null)
            {
                AddSubset(
                    subsets,
                    0,
                    indices.Length,
                    0,
                    null,
                    typeLine);
            }
            else
            {
                AddSubset(
                    subsets,
                    groupStart,
                    indexCursor - groupStart,
                    groupMaterial,
                    materialNames,
                    typeLine);
            }

            if (indexCursor != indices.Length)
            {
                throw Error(
                    typeLine,
                    "the triangulated face index count does not match the " +
                    "validated triangle count.");
            }

            Vector3[]? normalArray = outputNormals?.ToArray();
            Vector2[]? textureCoordinateArray = outputTextureCoordinates?.ToArray();
            _totalUnifiedVertices += outputPositions.Count;

            var mesh = new MeshData(
                outputPositions.ToArray(),
                normalArray is null
                    ? ReadOnlyMemory<Vector3>.Empty
                    : normalArray,
                textureCoordinateArray is null
                    ? ReadOnlyMemory<Vector2>.Empty
                    : textureCoordinateArray,
                indices,
                subsets);

            _meshes.Add(mesh);
        }

        private void AddSubset(
            List<MeshSubset> subsets,
            int indexOffset,
            int indexCount,
            int materialIndex,
            string?[]? materialNames,
            int typeLine)
        {
            if (indexCount == 0)
            {
                return;
            }

            if (subsets.Count + _totalSubsets >= _limits.MaxSubsetCount)
            {
                throw Error(
                    typeLine,
                    $"the model would contain more than " +
                    $"{_limits.MaxSubsetCount} material subsets.");
            }

            string? name = materialNames is null
                ? null
                : materialNames[materialIndex];

            if (string.IsNullOrEmpty(name))
            {
                name = null;
            }

            subsets.Add(new MeshSubset(indexOffset, indexCount, name));
            _totalSubsets++;
        }

        private FaceList ReadFaces(
            XTokenReader tokens,
            int faceCount,
            int indexLimit,
            string description)
        {
            Expect(
                tokens,
                XTokenKind.Semicolon,
                $"';' after the {description} count");

            var indices = new List<int>();
            int[] vertexCounts = new int[faceCount];
            long triangleCount = 0;

            for (int face = 0; face < faceCount; face++)
            {
                int countLine = tokens.Peek().LineNumber;
                int vertexCount = ReadCount(
                    tokens,
                    $"{description} {face} vertex count",
                    _limits.MaxFaceVertexCount);

                if (vertexCount < 3)
                {
                    throw Error(
                        countLine,
                        $"{description} {face} has {vertexCount} vertex or " +
                        "vertices. A polygon face requires at least three.");
                }

                vertexCounts[face] = vertexCount;
                Expect(
                    tokens,
                    XTokenKind.Semicolon,
                    $"';' after {description} {face} vertex count");

                for (int corner = 0; corner < vertexCount; corner++)
                {
                    int index = ReadIndex(
                        tokens,
                        $"{description} {face} corner {corner}",
                        indexLimit);
                    indices.Add(index);

                    if (corner < vertexCount - 1)
                    {
                        Expect(
                            tokens,
                            XTokenKind.Comma,
                            "',' between face indices");
                    }
                    else
                    {
                        Expect(
                            tokens,
                            XTokenKind.Semicolon,
                            "';' after the face indices");
                    }
                }

                if (indices.Count > _limits.MaxFaceIndexCount)
                {
                    throw Error(
                        tokens.Peek().LineNumber,
                        $"the {description} list contains more than " +
                        $"{_limits.MaxFaceIndexCount} indices, which exceeds the " +
                        "supported limit.");
                }

                if (face < faceCount - 1)
                {
                    Expect(
                        tokens,
                        XTokenKind.Comma,
                        "',' between faces");
                }
                else
                {
                    Expect(
                        tokens,
                        XTokenKind.Semicolon,
                        "the second ';' after the face list");
                }

                triangleCount += vertexCount - 2;
            }

            return new FaceList(indices.ToArray(), vertexCounts, triangleCount);
        }

        private Vector3[] ReadVectorArray(
            XTokenReader tokens,
            int count,
            string description)
        {
            Expect(
                tokens,
                XTokenKind.Semicolon,
                $"';' after the {description} count");

            var values = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                float x = ReadFloat(tokens, $"{description} x");
                Expect(tokens, XTokenKind.Semicolon, "';' after the x component");
                float y = ReadFloat(tokens, $"{description} y");
                Expect(tokens, XTokenKind.Semicolon, "';' after the y component");
                float z = ReadFloat(tokens, $"{description} z");
                Expect(tokens, XTokenKind.Semicolon, "';' after the z component");
                values[i] = new Vector3(x, y, z);

                if (i < count - 1)
                {
                    Expect(
                        tokens,
                        XTokenKind.Comma,
                        $"',' between {description} values");
                }
                else
                {
                    Expect(
                        tokens,
                        XTokenKind.Semicolon,
                        $"the second ';' after the {description} list");
                }
            }

            return values;
        }

        private int ReadCount(
            XTokenReader tokens,
            string description,
            int maximum)
        {
            XToken token = tokens.Next();

            if (token.Kind != XTokenKind.Word ||
                !int.TryParse(
                    token.Text,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out int value))
            {
                throw Error(
                    token.LineNumber,
                    $"expected {description}, but found {Describe(token)}.");
            }

            if (value < 0)
            {
                throw Error(
                    token.LineNumber,
                    $"{description} is {value}. Counts cannot be negative.");
            }

            if (value > maximum)
            {
                throw Error(
                    token.LineNumber,
                    $"{description} is {value}, which exceeds the supported limit " +
                    $"of {maximum}.");
            }

            return value;
        }

        private int ReadIndex(
            XTokenReader tokens,
            string description,
            int upperBound)
        {
            XToken token = tokens.Next();

            if (token.Kind != XTokenKind.Word ||
                !int.TryParse(
                    token.Text,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out int value))
            {
                throw Error(
                    token.LineNumber,
                    $"expected {description}, but found {Describe(token)}.");
            }

            if (value < 0 || value >= upperBound)
            {
                throw Error(
                    token.LineNumber,
                    $"{description} is {value}, but valid indices are 0 through " +
                    $"{upperBound - 1}.");
            }

            return value;
        }

        private float ReadFloat(XTokenReader tokens, string description)
        {
            XToken token = tokens.Next();

            if (token.Kind != XTokenKind.Word ||
                !float.TryParse(
                    token.Text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float value) ||
                !float.IsFinite(value))
            {
                throw Error(
                    token.LineNumber,
                    $"expected {description} as a finite invariant-culture number, " +
                    $"but found {Describe(token)}.");
            }

            return value;
        }

        private XToken ReadWord(XTokenReader tokens, string description)
        {
            XToken token = tokens.Next();

            if (token.Kind != XTokenKind.Word)
            {
                throw Error(
                    token.LineNumber,
                    $"expected {description}, but found {Describe(token)}.");
            }

            return token;
        }

        private string? ReadOptionalNameAndOpen(
            XTokenReader tokens,
            int typeLine,
            string typeName)
        {
            string? name = null;
            XToken token = tokens.Next();

            if (token.Kind == XTokenKind.Word)
            {
                name = token.Text;
                token = tokens.Next();
            }

            if (token.Kind != XTokenKind.OpenBrace)
            {
                throw Error(
                    token.LineNumber,
                    $"expected '{{' after {typeName}" +
                    (name is null ? string.Empty : $" '{name}'") +
                    $", but found {Describe(token)}.");
            }

            return name;
        }

        private XToken Expect(
            XTokenReader tokens,
            XTokenKind expected,
            string description)
        {
            XToken token = tokens.Next();

            if (token.Kind != expected)
            {
                throw Error(
                    token.LineNumber,
                    $"expected {description}, but found {Describe(token)}.");
            }

            return token;
        }

        private void SkipSeparators(XTokenReader tokens)
        {
            while (tokens.Peek().Kind is XTokenKind.Comma or XTokenKind.Semicolon)
            {
                tokens.Next();
            }
        }

        private ModelFormatException Error(int lineNumber, string message)
        {
            return _error(lineNumber, message);
        }

        private ModelFormatException Error(string message)
        {
            return _sourceName is null
                ? new ModelFormatException(message)
                : new ModelFormatException($"{_sourceName}: {message}");
        }

        private ModelFormatException AnimationUnsupported(XToken token)
        {
            return Error(
                token.LineNumber,
                $"the '{token.Text}' object is animation data, which is not " +
                "supported. Only static meshes are imported.");
        }

        private ModelFormatException SkinningUnsupported(XToken token)
        {
            return Error(
                token.LineNumber,
                $"the '{token.Text}' object is skinning data, which is not " +
                "supported. Only static meshes are imported.");
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.X) &&
                   float.IsFinite(value.Y) &&
                   float.IsFinite(value.Z);
        }

        private static string Describe(XToken token)
        {
            return token.Kind switch
            {
                XTokenKind.Word => $"'{token.Text}'",
                XTokenKind.String => "a quoted string",
                XTokenKind.OpenBrace => "'{'",
                XTokenKind.CloseBrace => "'}'",
                XTokenKind.Comma => "','",
                XTokenKind.Semicolon => "';'",
                XTokenKind.EndOfInput => "the end of the file",
                _ => "an unexpected token",
            };
        }

        private readonly record struct CornerKey(int Position, int Normal);

        private sealed record FaceList(
            int[] Indices,
            int[] VertexCounts,
            long TriangleCount);
    }
}
