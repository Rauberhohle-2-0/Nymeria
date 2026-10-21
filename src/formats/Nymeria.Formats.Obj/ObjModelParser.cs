using System.Globalization;
using System.Numerics;
using System.Text;
using Nymeria.Core.Models;

namespace Nymeria.Formats.Obj;

/// <summary>
/// Imports the triangular geometry of a Wavefront OBJ stream into Nymeria's
/// format-independent model data.
/// </summary>
/// <remarks>
/// <para>
/// The parser supports <c>v</c>, <c>vt</c>, <c>vn</c>, and <c>f</c> statements,
/// the four standard face-reference forms (<c>v</c>, <c>v/vt</c>,
/// <c>v//vn</c>, and <c>v/vt/vn</c>), one-based and negative indices, and fan
/// triangulation of convex polygon faces. <c>usemtl</c> names become mesh subset
/// material names. <c>mtllib</c> is recognized but no MTL file is loaded.
/// </para>
/// <para>
/// The complete supported subset, conversion rules, and limitations are
/// documented in <c>docs/OBJ.md</c>. The parser has no SDL3, GPU, window, or
/// platform dependency and does not dispose the caller's stream.
/// </para>
/// </remarks>
public sealed class ObjModelParser : IModelParser
{
    /// <summary>The largest accepted OBJ input, in bytes.</summary>
    public const int MaxInputBytes = 64 * 1024 * 1024;

    /// <summary>
    /// The largest accepted count for each position, texture-coordinate, and
    /// normal stream, and for the deduplicated mesh vertices.
    /// </summary>
    public const int MaxVertexCount = 2_000_000;

    /// <summary>The largest accepted triangle count.</summary>
    public const int MaxTriangleCount = 4_000_000;

    /// <summary>The largest accepted number of material subsets.</summary>
    public const int MaxSubsetCount = 65_536;

    /// <summary>The largest accepted source line, in characters.</summary>
    public const int MaxLineLength = 1024 * 1024;

    private readonly string? _sourceName;
    private readonly ObjImportLimits _limits;

    /// <summary>
    /// Creates a Wavefront OBJ parser.
    /// </summary>
    /// <param name="sourceName">
    /// An optional file or source name included in diagnostics. It does not
    /// affect parsing or open any file.
    /// </param>
    public ObjModelParser(string? sourceName = null)
        : this(sourceName, ObjImportLimits.Default)
    {
    }

    internal ObjModelParser(string? sourceName, ObjImportLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        _sourceName = string.IsNullOrWhiteSpace(sourceName) ? null : sourceName;
        _limits = limits;
    }

    /// <summary>
    /// Parses one model from the supplied stream.
    /// </summary>
    /// <param name="stream">A readable stream positioned at the first OBJ line.</param>
    /// <returns>The validated model data.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">The stream is not readable.</exception>
    /// <exception cref="ModelFormatException">
    /// A line is malformed, an index is out of range, or the input contains no
    /// faces.
    /// </exception>
    public ModelData Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
        {
            throw new ArgumentException("The stream must be readable.", nameof(stream));
        }

        RejectOversizedSeekableInput(stream);

        // The caller owns the stream. Leave it open even when parsing fails.
        using var boundedStream = new BoundedReadStream(
            stream,
            _limits.MaxInputBytes,
            () => Error(
                "the input is larger than the supported limit of " +
                $"{_limits.MaxInputBytes} bytes."));
        using var reader = new StreamReader(
            boundedStream,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: true);
        var lineReader = new BoundedLineReader(reader, _limits.MaxLineLength);
        var state = new ParseState(_sourceName, _limits);
        int lineNumber = 0;
        string? line;

        try
        {
            while ((line = lineReader.ReadLine(lineNumber + 1, Error)) is not null)
            {
                lineNumber++;
                state.ParseLine(line, lineNumber);
            }
        }
        catch (DecoderFallbackException exception)
        {
            throw Error($"the input is not valid UTF-8 text: {exception.Message}");
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
            // The bounded wrapper still enforces the limit while reading.
            return;
        }

        if (remaining > _limits.MaxInputBytes)
        {
            throw Error(
                "the input is larger than the supported limit of " +
                $"{_limits.MaxInputBytes} bytes.");
        }
    }

    private ModelFormatException Error(string message)
    {
        return new ModelFormatException(
            _sourceName is null ? message : $"{_sourceName}: {message}");
    }

    private ModelFormatException Error(int lineNumber, string message)
    {
        string location = _sourceName is null
            ? $"line {lineNumber}"
            : $"{_sourceName}: line {lineNumber}";

        return new ModelFormatException($"{location}: {message}");
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

    private sealed class BoundedLineReader(TextReader reader, int maxLineLength)
    {
        private readonly char[] _buffer = new char[4096];
        private int _offset;
        private int _count;

        public string? ReadLine(
            int lineNumber,
            Func<int, string, ModelFormatException> error)
        {
            StringBuilder? builder = null;

            while (true)
            {
                int value = Peek();

                if (value < 0)
                {
                    return builder?.ToString();
                }

                _offset++;

                if (value == '\n')
                {
                    return builder?.ToString() ?? string.Empty;
                }

                if (value == '\r')
                {
                    if (Peek() == '\n')
                    {
                        _offset++;
                    }

                    return builder?.ToString() ?? string.Empty;
                }

                builder ??= new StringBuilder();

                if (builder.Length >= maxLineLength)
                {
                    throw error(
                        lineNumber,
                        $"the line is longer than the supported limit of " +
                        $"{maxLineLength} characters.");
                }

                builder.Append((char)value);
            }
        }

        private int Peek()
        {
            if (_offset >= _count)
            {
                _count = reader.Read(_buffer, 0, _buffer.Length);
                _offset = 0;

                if (_count <= 0)
                {
                    return -1;
                }
            }

            return _buffer[_offset];
        }
    }

    private sealed class ParseState
    {
        private readonly string? _sourceName;
        private readonly ObjImportLimits _limits;
        private readonly List<Vector3> _positions = [];
        private readonly List<Vector2> _textureCoordinates = [];
        private readonly List<Vector3> _normals = [];
        private readonly List<uint> _indices = [];
        private readonly List<CornerKey> _cornerOrder = [];
        private readonly Dictionary<CornerKey, uint> _cornerIndices = [];
        private readonly List<(int Offset, int Count, string? Material)> _subsets = [];

        private string? _objectName;
        private string? _currentMaterial;
        private string? _openSubsetMaterial;
        private int _openSubsetStart = -1;
        private bool _usesTextureCoordinates;
        private bool _usesNormals;

        public ParseState(string? sourceName, ObjImportLimits limits)
        {
            _sourceName = sourceName;
            _limits = limits;
        }

        public void ParseLine(string line, int lineNumber)
        {
            int commentStart = line.IndexOf('#');

            if (commentStart >= 0)
            {
                line = line[..commentStart];
            }

            line = line.Trim();

            if (line.Length == 0)
            {
                return;
            }

            int separator = IndexOfWhitespace(line);
            string keyword = separator < 0 ? line : line[..separator];
            string arguments = separator < 0 ? string.Empty : line[(separator + 1)..].Trim();

            switch (keyword)
            {
                case "v":
                    ParsePosition(arguments, lineNumber);
                    break;
                case "vt":
                    ParseTextureCoordinate(arguments, lineNumber);
                    break;
                case "vn":
                    ParseNormal(arguments, lineNumber);
                    break;
                case "f":
                    ParseFace(arguments, lineNumber);
                    break;
                case "usemtl":
                    // A name is preserved as an opaque subset label. An empty
                    // usemtl clears the current material, matching common OBJ
                    // writers that end a material section that way.
                    _currentMaterial = arguments.Length == 0 ? null : arguments;
                    break;
                case "o":
                    // The model representation has no object hierarchy. The
                    // first object name is still useful as a diagnostic name.
                    if (_objectName is null && arguments.Length > 0)
                    {
                        _objectName = arguments;
                    }

                    break;
                case "g":
                case "s":
                case "mtllib":
                case "maplib":
                case "usemap":
                case "vp":
                    // Recognized but not represented by the model data. In
                    // particular, mtllib is not resolved and no MTL file is read.
                    break;
                case "l":
                case "p":
                case "curv":
                case "curv2":
                case "surf":
                    throw Error(
                        lineNumber,
                        $"the '{keyword}' directive is not supported; only positions, " +
                        "texture coordinates, normals, and faces are imported.");
                default:
                    throw Error(lineNumber, $"unsupported OBJ directive '{keyword}'.");
            }
        }

        public ModelData Build()
        {
            CloseSubset();

            if (_indices.Count == 0)
            {
                throw Error(
                    "the OBJ input contains no faces; at least one 'f' statement is required.");
            }

            int vertexCount = _cornerOrder.Count;
            var positions = new Vector3[vertexCount];
            Vector3[]? normals = _usesNormals ? new Vector3[vertexCount] : null;
            Vector2[]? textureCoordinates = _usesTextureCoordinates
                ? new Vector2[vertexCount]
                : null;

            for (int i = 0; i < vertexCount; i++)
            {
                CornerKey corner = _cornerOrder[i];
                positions[i] = _positions[corner.Position];

                if (textureCoordinates is not null)
                {
                    textureCoordinates[i] = corner.TextureCoordinate >= 0
                        ? _textureCoordinates[corner.TextureCoordinate]
                        : Vector2.Zero;
                }

                if (normals is not null)
                {
                    normals[i] = corner.Normal >= 0
                        ? _normals[corner.Normal]
                        : Vector3.Zero;
                }
            }

            var subsets = new MeshSubset[_subsets.Count];

            for (int i = 0; i < subsets.Length; i++)
            {
                (int offset, int count, string? material) = _subsets[i];
                subsets[i] = new MeshSubset(offset, count, material);
            }

            var mesh = new MeshData(
                positions,
                normals,
                textureCoordinates,
                _indices.ToArray(),
                subsets);

            return new ModelData(
                _objectName ?? _sourceName ?? "OBJ",
                new[] { mesh });
        }

        private void ParsePosition(string arguments, int lineNumber)
        {
            string[] parts = SplitArguments(arguments);

            if (parts.Length is < 3 or > 4)
            {
                throw Error(
                    lineNumber,
                    "a position requires three coordinates and an optional weight, " +
                    $"but {parts.Length} value(s) were found.");
            }

            float x = ParseFloat(parts[0], lineNumber, "position x");
            float y = ParseFloat(parts[1], lineNumber, "position y");
            float z = ParseFloat(parts[2], lineNumber, "position z");

            if (parts.Length == 4)
            {
                // The optional rational weight is part of the OBJ grammar but
                // has no role in the triangle mesh representation.
                ParseFloat(parts[3], lineNumber, "position weight");
            }

            EnsureAttributeCapacity(_positions.Count, "position", lineNumber);
            _positions.Add(new Vector3(x, y, z));
        }

        private void ParseTextureCoordinate(string arguments, int lineNumber)
        {
            string[] parts = SplitArguments(arguments);

            if (parts.Length is < 1 or > 3)
            {
                throw Error(
                    lineNumber,
                    "a texture coordinate requires one to three values, " +
                    $"but {parts.Length} value(s) were found.");
            }

            float u = ParseFloat(parts[0], lineNumber, "texture coordinate u");
            float v = parts.Length >= 2
                ? ParseFloat(parts[1], lineNumber, "texture coordinate v")
                : 0.0f;

            if (parts.Length == 3)
            {
                ParseFloat(parts[2], lineNumber, "texture coordinate weight");
            }

            // OBJ uses a bottom-left UV origin. Nymeria's model data uses a
            // top-left origin with +V down.
            EnsureAttributeCapacity(
                _textureCoordinates.Count,
                "texture coordinate",
                lineNumber);
            _textureCoordinates.Add(new Vector2(u, 1.0f - v));
        }

        private void ParseNormal(string arguments, int lineNumber)
        {
            string[] parts = SplitArguments(arguments);

            if (parts.Length != 3)
            {
                throw Error(
                    lineNumber,
                    $"a normal requires three components, but {parts.Length} value(s) " +
                    "were found.");
            }

            EnsureAttributeCapacity(_normals.Count, "normal", lineNumber);
            _normals.Add(new Vector3(
                ParseFloat(parts[0], lineNumber, "normal x"),
                ParseFloat(parts[1], lineNumber, "normal y"),
                ParseFloat(parts[2], lineNumber, "normal z")));
        }

        private void EnsureAttributeCapacity(
            int count,
            string kind,
            int lineNumber)
        {
            if (count >= _limits.MaxVertexCount)
            {
                throw Error(
                    lineNumber,
                    $"the input defines more than {_limits.MaxVertexCount} {kind} " +
                    "values, which exceeds the supported limit.");
            }
        }

        private void ParseFace(string arguments, int lineNumber)
        {
            string[] tokens = SplitArguments(arguments);

            if (tokens.Length < 3)
            {
                throw Error(
                    lineNumber,
                    $"a face requires at least three vertices, but {tokens.Length} " +
                    "value(s) were found.");
            }

            if ((long)_cornerOrder.Count + tokens.Length > _limits.MaxVertexCount)
            {
                throw Error(
                    lineNumber,
                    $"the input would exceed the supported limit of " +
                    $"{_limits.MaxVertexCount} mesh vertices.");
            }

            long triangleCount = ((long)_indices.Count / 3) + (tokens.Length - 2);

            if (triangleCount > _limits.MaxTriangleCount)
            {
                throw Error(
                    lineNumber,
                    $"the input would exceed the supported limit of " +
                    $"{_limits.MaxTriangleCount} triangles.");
            }

            var corners = new CornerKey[tokens.Length];

            for (int i = 0; i < tokens.Length; i++)
            {
                corners[i] = ResolveFaceVertex(tokens[i], lineNumber);
            }

            BeginSubset();

            // Fan triangulation: (0, 1, 2), (0, 2, 3), ... for a convex polygon.
            // The source winding is preserved.
            uint first = AddCorner(corners[0]);

            for (int i = 1; i < corners.Length - 1; i++)
            {
                uint second = AddCorner(corners[i]);
                uint third = AddCorner(corners[i + 1]);

                _indices.Add(first);
                _indices.Add(second);
                _indices.Add(third);
            }
        }

        private CornerKey ResolveFaceVertex(string token, int lineNumber)
        {
            string[] parts = token.Split('/');

            if (parts.Length is < 1 or > 3)
            {
                throw Error(
                    lineNumber,
                    $"face vertex '{token}' has more than three slash-separated " +
                    "components.");
            }

            if (parts[0].Length == 0)
            {
                throw Error(lineNumber, $"face vertex '{token}' has no position index.");
            }

            int position = ResolveIndex(
                parts[0],
                _positions.Count,
                "position",
                token,
                lineNumber);

            int textureCoordinate = -1;
            int normal = -1;

            if (parts.Length >= 2 && parts[1].Length > 0)
            {
                textureCoordinate = ResolveIndex(
                    parts[1],
                    _textureCoordinates.Count,
                    "texture coordinate",
                    token,
                    lineNumber);
            }

            if (parts.Length == 3 && parts[2].Length > 0)
            {
                normal = ResolveIndex(
                    parts[2],
                    _normals.Count,
                    "normal",
                    token,
                    lineNumber);
            }

            if (parts.Length == 2 && parts[1].Length == 0)
            {
                throw Error(
                    lineNumber,
                    $"face vertex '{token}' has an empty texture coordinate index.");
            }

            if (parts.Length == 3 && parts[1].Length == 0 && parts[2].Length == 0)
            {
                throw Error(
                    lineNumber,
                    $"face vertex '{token}' has neither a texture coordinate nor a " +
                    "normal index.");
            }

            if (textureCoordinate >= 0)
            {
                _usesTextureCoordinates = true;
            }

            if (normal >= 0)
            {
                _usesNormals = true;
            }

            return new CornerKey(position, textureCoordinate, normal);
        }

        private int ResolveIndex(
            string token,
            int count,
            string kind,
            string faceToken,
            int lineNumber)
        {
            if (!int.TryParse(
                    token,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out int value))
            {
                throw Error(
                    lineNumber,
                    $"face vertex '{faceToken}' has an invalid {kind} index '{token}'.");
            }

            if (value == 0)
            {
                throw Error(
                    lineNumber,
                    $"face vertex '{faceToken}' uses {kind} index 0. OBJ indices are " +
                    "one-based or negative.");
            }

            // Positive indices are absolute. Negative indices are relative to the
            // number of values defined before this face.
            int resolved = value > 0 ? value - 1 : count + value;

            if (resolved < 0 || resolved >= count)
            {
                throw Error(
                    lineNumber,
                    $"face vertex '{faceToken}' references {kind} {value}, but {count} " +
                    $"{kind} value(s) are defined.");
            }

            return resolved;
        }

        private uint AddCorner(CornerKey corner)
        {
            if (_cornerIndices.TryGetValue(corner, out uint index))
            {
                return index;
            }

            index = (uint)_cornerOrder.Count;
            _cornerIndices.Add(corner, index);
            _cornerOrder.Add(corner);
            return index;
        }

        private void BeginSubset()
        {
            if (_openSubsetStart >= 0 &&
                string.Equals(_openSubsetMaterial, _currentMaterial, StringComparison.Ordinal))
            {
                return;
            }

            CloseSubset();
            _openSubsetStart = _indices.Count;
            _openSubsetMaterial = _currentMaterial;
        }

        private void CloseSubset()
        {
            if (_openSubsetStart < 0)
            {
                return;
            }

            int count = _indices.Count - _openSubsetStart;

            if (count > 0)
            {
                if (_subsets.Count >= _limits.MaxSubsetCount)
                {
                    throw Error(
                        $"the input creates more than {_limits.MaxSubsetCount} material " +
                        "subsets, which exceeds the supported limit.");
                }

                _subsets.Add((_openSubsetStart, count, _openSubsetMaterial));
            }

            _openSubsetStart = -1;
            _openSubsetMaterial = null;
        }

        private float ParseFloat(string token, int lineNumber, string description)
        {
            if (!float.TryParse(
                    token,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float value) ||
                !float.IsFinite(value))
            {
                throw Error(
                    lineNumber,
                    $"{description} value '{token}' is not a finite invariant-culture " +
                    "number.");
            }

            return value;
        }

        private ModelFormatException Error(int lineNumber, string message)
        {
            string location = _sourceName is null
                ? $"line {lineNumber}"
                : $"{_sourceName}: line {lineNumber}";

            return new ModelFormatException($"{location}: {message}");
        }

        private ModelFormatException Error(string message)
        {
            return new ModelFormatException(
                _sourceName is null ? message : $"{_sourceName}: {message}");
        }

        private static string[] SplitArguments(string arguments)
        {
            return arguments.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries);
        }

        private static int IndexOfWhitespace(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsWhiteSpace(value[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        private readonly record struct CornerKey(
            int Position,
            int TextureCoordinate,
            int Normal);
    }
}

internal sealed class ObjImportLimits
{
    public static ObjImportLimits Default { get; } = new(
        ObjModelParser.MaxInputBytes,
        ObjModelParser.MaxVertexCount,
        ObjModelParser.MaxTriangleCount,
        ObjModelParser.MaxSubsetCount,
        ObjModelParser.MaxLineLength);

    public ObjImportLimits(
        int maxInputBytes,
        int maxVertexCount,
        int maxTriangleCount,
        int maxSubsetCount,
        int maxLineLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxInputBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxVertexCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTriangleCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSubsetCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLineLength);

        MaxInputBytes = maxInputBytes;
        MaxVertexCount = maxVertexCount;
        MaxTriangleCount = maxTriangleCount;
        MaxSubsetCount = maxSubsetCount;
        MaxLineLength = maxLineLength;
    }

    public int MaxInputBytes { get; }

    public int MaxVertexCount { get; }

    public int MaxTriangleCount { get; }

    public int MaxSubsetCount { get; }

    public int MaxLineLength { get; }
}
