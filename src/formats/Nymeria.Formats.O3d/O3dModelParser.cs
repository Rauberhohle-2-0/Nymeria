using System.Buffers.Binary;
using System.Numerics;
using Nymeria.Core.Models;

namespace Nymeria.Formats.O3d;

/// <summary>
/// Imports an unscrambled OMSI O3D static mesh into Nymeria's format-independent
/// model data.
/// </summary>
/// <remarks>
/// <para>
/// The parser reads the documented O3D header, vertex, triangle, material, and
/// pivot-matrix sections. It does not apply the pivot matrix, decode scrambled
/// vertex records, or import bones, animation, textures, or material shading.
/// </para>
/// <para>
/// The supported version and feature matrix, conversions, and limitations are
/// documented in <c>docs/O3D.md</c>. The parser uses only the base class library
/// and <c>Nymeria.Core</c>; it has no SDL3, GPU, window, or platform dependency
/// and does not dispose the caller's stream.
/// </para>
/// </remarks>
public sealed class O3dModelParser : IModelParser
{
    private const byte Magic0 = 0x84;
    private const byte Magic1 = 0x19;
    private const byte FirstSupportedVersion = 1;
    private const byte LastSupportedVersion = 7;
    private const byte KnownFlagMask = 0x03;
    private const byte WideIndexFlag = 0x01;
    private const uint UnscrambledKey = 0xFFFFFFFF;
    private const byte VerticesTag = 0x17;
    private const byte TrianglesTag = 0x49;
    private const byte MaterialsTag = 0x26;
    private const byte MatrixTag = 0x79;
    private const byte BonesTag = 0x54;
    private const int FloatSize = sizeof(float);
    private const int VertexRecordSize = 8 * FloatSize;
    private const int MaterialFloatCount = 11;
    private const int MatrixFloatCount = 16;
    private const int MinimumMaterialRecordSize = MaterialFloatCount * FloatSize + 1;

    /// <summary>The largest accepted O3D input, in bytes.</summary>
    public const int MaxInputBytes = 64 * 1024 * 1024;

    /// <summary>The largest accepted vertex count.</summary>
    public const int MaxVertexCount = 2_000_000;

    /// <summary>The largest accepted triangle count.</summary>
    public const int MaxTriangleCount = 4_000_000;

    private readonly string? _sourceName;

    /// <summary>
    /// Creates an OMSI O3D parser.
    /// </summary>
    /// <param name="sourceName">
    /// An optional file or source name included in diagnostics. It does not
    /// affect parsing or open any file.
    /// </param>
    public O3dModelParser(string? sourceName = null)
    {
        _sourceName = string.IsNullOrWhiteSpace(sourceName) ? null : sourceName;
    }

    /// <summary>
    /// Parses one static mesh from the supplied stream.
    /// </summary>
    /// <param name="stream">A readable stream positioned at the O3D signature.</param>
    /// <returns>The validated model data.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">The stream is not readable.</exception>
    /// <exception cref="ModelFormatException">
    /// The input is truncated, malformed, larger than the documented limit, or uses
    /// an unsupported version, flag, key, section, or feature.
    /// </exception>
    public ModelData Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
        {
            throw new ArgumentException("The stream must be readable.", nameof(stream));
        }

        // The caller owns the stream. Leave it open even when parsing fails.
        byte[] data = ReadAllBytes(stream);
        return ParseFile(new Reader(data, _sourceName));
    }

    private byte[] ReadAllBytes(Stream stream)
    {
        if (stream.CanSeek)
        {
            long remaining = stream.Length - stream.Position;

            if (remaining > MaxInputBytes)
            {
                throw Error(
                    "the input is larger than the supported limit of " +
                    $"{MaxInputBytes} bytes.");
            }
        }

        using var buffer = new MemoryStream();
        byte[] chunk = new byte[64 * 1024];
        long total = 0;

        while (true)
        {
            int read = stream.Read(chunk, 0, chunk.Length);

            if (read == 0)
            {
                break;
            }

            total += read;

            if (total > MaxInputBytes)
            {
                throw Error(
                    "the input is larger than the supported limit of " +
                    $"{MaxInputBytes} bytes.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private ModelData ParseFile(Reader reader)
    {
        int headerOffset = reader.Position;
        byte magic0 = reader.U8("the O3D signature");
        byte magic1 = reader.U8("the O3D signature");

        if (magic0 != Magic0 || magic1 != Magic1)
        {
            throw reader.Error(
                headerOffset,
                $"the file signature is {magic0:X2} {magic1:X2}, not the O3D " +
                $"signature {Magic0:X2} {Magic1:X2}.");
        }

        int versionOffset = reader.Position;
        byte version = reader.U8("the O3D version");

        if (version is < FirstSupportedVersion or > LastSupportedVersion)
        {
            throw reader.Error(
                versionOffset,
                $"O3D version {version} is not supported. This importer supports " +
                $"unscrambled static meshes of versions {FirstSupportedVersion} " +
                $"through {LastSupportedVersion}.");
        }

        byte flags = 0;

        if (version >= 3)
        {
            int flagsOffset = reader.Position;
            flags = reader.U8("the O3D flags");

            if ((flags & ~KnownFlagMask) != 0)
            {
                throw reader.Error(
                    flagsOffset,
                    $"O3D flags 0x{flags:X2} contain unsupported bits. Only bit 0 " +
                    "(32-bit triangle indices) and bit 1 (an inert scrambling hint) " +
                    "are known.");
            }
        }

        if (version >= 4)
        {
            int keyOffset = reader.Position;
            uint key = reader.U32("the O3D vertex key");

            if (key != UnscrambledKey)
            {
                throw reader.Error(
                    keyOffset,
                    $"O3D vertex data is scrambled (key 0x{key:X8}), which is not " +
                    $"supported. Only key 0x{UnscrambledKey:X8} (unscrambled) is " +
                    "accepted.");
            }
        }

        bool wideCounts = version >= 3;
        bool wideIndices = (flags & WideIndexFlag) != 0;
        uint[]? indexBuffer = null;
        ushort[]? triangleMaterials = null;
        Vector3[]? positions = null;
        Vector3[]? normals = null;
        Vector2[]? textureCoordinates = null;
        string[]? materialNames = null;
        bool hasVertices = false;
        bool hasTriangles = false;
        bool hasMaterials = false;
        bool hasMatrix = false;

        while (reader.Remaining > 0)
        {
            int tagOffset = reader.Position;
            byte tag = reader.U8("a section tag");

            switch (tag)
            {
                case VerticesTag:
                    if (hasVertices)
                    {
                        throw reader.Error(
                            tagOffset,
                            "the file contains more than one vertices section.");
                    }

                    hasVertices = true;
                    ReadVertices(reader, wideCounts, out positions, out normals,
                        out textureCoordinates);
                    break;

                case TrianglesTag:
                    if (hasTriangles)
                    {
                        throw reader.Error(
                            tagOffset,
                            "the file contains more than one triangles section.");
                    }

                    hasTriangles = true;
                    ReadTriangles(reader, wideCounts, wideIndices, out indexBuffer,
                        out triangleMaterials);
                    break;

                case MaterialsTag:
                    if (hasMaterials)
                    {
                        throw reader.Error(
                            tagOffset,
                            "the file contains more than one materials section.");
                    }

                    hasMaterials = true;
                    materialNames = ReadMaterials(reader);
                    break;

                case MatrixTag:
                    if (hasMatrix)
                    {
                        throw reader.Error(
                            tagOffset,
                            "the file contains more than one pivot-matrix section.");
                    }

                    hasMatrix = true;
                    ReadPivotMatrix(reader);
                    break;

                case BonesTag:
                    throw reader.Error(
                        tagOffset,
                        "O3D bones or skinning are not supported by this importer.");

                default:
                    throw reader.Error(
                        tagOffset,
                        $"unsupported O3D section tag 0x{tag:X2}. Supported tags are " +
                        $"0x{VerticesTag:X2} (vertices), 0x{TrianglesTag:X2} " +
                        $"(triangles), 0x{MaterialsTag:X2} (materials), and " +
                        $"0x{MatrixTag:X2} (pivot matrix); 0x{BonesTag:X2} (bones) " +
                        "is recognized but unsupported.");
            }
        }

        if (positions is null || normals is null || textureCoordinates is null)
        {
            throw reader.Error(
                reader.Position,
                "the file has no vertices section; a static mesh requires vertices.");
        }

        if (indexBuffer is null || triangleMaterials is null)
        {
            throw reader.Error(
                reader.Position,
                "the file has no triangles section; a static mesh requires triangles.");
        }

        ValidateTriangleIndices(reader, indexBuffer, positions.Length);
        ValidateMaterialIndices(reader, triangleMaterials, materialNames);
        IReadOnlyList<MeshSubset> subsets =
            BuildSubsets(triangleMaterials, materialNames);

        var mesh = new MeshData(
            positions,
            normals,
            textureCoordinates,
            indexBuffer,
            subsets);

        return new ModelData(
            _sourceName ?? "O3D",
            new[] { mesh });
    }

    private static void ReadVertices(
        Reader reader,
        bool wideCounts,
        out Vector3[] positions,
        out Vector3[] normals,
        out Vector2[] textureCoordinates)
    {
        int sectionOffset = reader.Position;
        uint count = wideCounts
            ? reader.U32("the vertex count")
            : reader.U16("the vertex count");

        if (count == 0)
        {
            throw reader.Error(sectionOffset, "the vertices section is empty.");
        }

        if (count > MaxVertexCount)
        {
            throw reader.Error(
                sectionOffset,
                $"the vertex count {count} exceeds the supported limit of " +
                $"{MaxVertexCount}.");
        }

        long recordBytes = (long)count * VertexRecordSize;
        reader.Need(recordBytes, "the vertex records");

        int vertexCount = (int)count;
        positions = new Vector3[vertexCount];
        normals = new Vector3[vertexCount];
        textureCoordinates = new Vector2[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            float x = reader.F32("a vertex position");
            float y = reader.F32("a vertex position");
            float z = reader.F32("a vertex position");
            float nx = reader.F32("a vertex normal");
            float ny = reader.F32("a vertex normal");
            float nz = reader.F32("a vertex normal");
            float u = reader.F32("a texture coordinate");
            float v = reader.F32("a texture coordinate");

            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
            {
                throw reader.Error(
                    sectionOffset,
                    $"vertex {i} contains a non-finite position.");
            }

            if (!float.IsFinite(nx) || !float.IsFinite(ny) || !float.IsFinite(nz))
            {
                throw reader.Error(
                    sectionOffset,
                    $"vertex {i} contains a non-finite normal.");
            }

            if (!float.IsFinite(u) || !float.IsFinite(v))
            {
                throw reader.Error(
                    sectionOffset,
                    $"vertex {i} contains a non-finite texture coordinate.");
            }

            // O3D stores Direct3D's left-handed frame (X right, Y up, Z forward).
            // Nymeria uses a right-handed frame with -Z forward, so Z is negated
            // for positions and normals. Direct3D texture coordinates already use
            // a top-left origin with +V down, matching the model convention.
            positions[i] = new Vector3(x, y, -z);
            normals[i] = new Vector3(nx, ny, -nz);
            textureCoordinates[i] = new Vector2(u, v);
        }
    }

    private static void ReadTriangles(
        Reader reader,
        bool wideCounts,
        bool wideIndices,
        out uint[] indices,
        out ushort[] materials)
    {
        int sectionOffset = reader.Position;
        uint count = wideCounts
            ? reader.U32("the triangle count")
            : reader.U16("the triangle count");

        if (count == 0)
        {
            throw reader.Error(sectionOffset, "the triangles section is empty.");
        }

        if (count > MaxTriangleCount)
        {
            throw reader.Error(
                sectionOffset,
                $"the triangle count {count} exceeds the supported limit of " +
                $"{MaxTriangleCount}.");
        }

        int indexSize = wideIndices ? 4 : 2;
        long recordSize = 3L * indexSize + sizeof(ushort);
        reader.Need((long)count * recordSize, "the triangle records");

        int triangleCount = (int)count;
        indices = new uint[checked(triangleCount * 3)];
        materials = new ushort[triangleCount];

        for (int i = 0; i < triangleCount; i++)
        {
            uint first = wideIndices
                ? reader.U32("a triangle index")
                : reader.U16("a triangle index");
            uint second = wideIndices
                ? reader.U32("a triangle index")
                : reader.U16("a triangle index");
            uint third = wideIndices
                ? reader.U32("a triangle index")
                : reader.U16("a triangle index");

            // Negating Z reflects the mesh. Reverse each triangle's winding so
            // its front face remains counter-clockwise in the Nymeria frame.
            int offset = i * 3;
            indices[offset] = first;
            indices[offset + 1] = third;
            indices[offset + 2] = second;
            materials[i] = reader.U16("a triangle material index");
        }
    }

    private static string[] ReadMaterials(Reader reader)
    {
        int sectionOffset = reader.Position;
        int count = reader.U16("the material count");

        if (count == 0)
        {
            return [];
        }

        reader.Need(
            (long)count * MinimumMaterialRecordSize,
            "the material records");

        var names = new string[count];

        for (int i = 0; i < count; i++)
        {
            // Diffuse RGBA, specular RGB, emissive RGB, and specular power are
            // part of the record but have no place in Nymeria's geometry-only
            // model. Read them to keep the stream aligned.
            for (int value = 0; value < MaterialFloatCount; value++)
            {
                reader.F32("a material value");
            }

            int length = reader.U8("a material name length");
            ReadOnlySpan<byte> bytes = reader.Bytes(length, "a material name");
            names[i] = DecodeWindows1252(bytes);
        }

        return names;
    }

    private static void ReadPivotMatrix(Reader reader)
    {
        int sectionOffset = reader.Position;
        reader.Need(
            (long)MatrixFloatCount * FloatSize,
            "the pivot matrix");

        for (int i = 0; i < MatrixFloatCount; i++)
        {
            float value = reader.F32("a pivot-matrix value");

            if (!float.IsFinite(value))
            {
                throw reader.Error(
                    sectionOffset,
                    "the pivot matrix contains a non-finite value.");
            }
        }

        // The O3D matrix is the mesh's pivot frame, used by OMSI's
        // origin_from_mesh animation. Vertices are already stored in the parent
        // frame, so the matrix must not be applied when rendering. ModelData has
        // no transform metadata, so the validated matrix is intentionally
        // discarded.
    }

    private static void ValidateTriangleIndices(
        Reader reader,
        ReadOnlySpan<uint> indices,
        int vertexCount)
    {
        for (int i = 0; i < indices.Length; i++)
        {
            if (indices[i] >= (uint)vertexCount)
            {
                throw reader.Error(
                    reader.Position,
                    $"triangle {i / 3}, corner {i % 3} references vertex " +
                    $"{indices[i]}, but the mesh has {vertexCount} vertices.");
            }
        }
    }

    private static void ValidateMaterialIndices(
        Reader reader,
        ReadOnlySpan<ushort> triangleMaterials,
        string[]? materialNames)
    {
        int materialCount = materialNames?.Length ?? 0;

        for (int i = 0; i < triangleMaterials.Length; i++)
        {
            ushort material = triangleMaterials[i];

            if (materialCount == 0)
            {
                if (material != 0)
                {
                    throw reader.Error(
                        reader.Position,
                        $"triangle {i} references material {material}, but the file " +
                        "has no materials section.");
                }

                continue;
            }

            if (material >= materialCount)
            {
                throw reader.Error(
                    reader.Position,
                    $"triangle {i} references material {material}, but only " +
                    $"{materialCount} material(s) are defined.");
            }
        }
    }

    private static IReadOnlyList<MeshSubset> BuildSubsets(
        ReadOnlySpan<ushort> triangleMaterials,
        string[]? materialNames)
    {
        var subsets = new List<MeshSubset>();
        int groupStart = 0;

        for (int i = 1; i <= triangleMaterials.Length; i++)
        {
            if (i < triangleMaterials.Length &&
                triangleMaterials[i] == triangleMaterials[groupStart])
            {
                continue;
            }

            int triangleCount = i - groupStart;
            string? materialName = ResolveMaterialName(
                triangleMaterials[groupStart],
                materialNames);
            subsets.Add(new MeshSubset(
                groupStart * 3,
                triangleCount * 3,
                materialName));
            groupStart = i;
        }

        return subsets;
    }

    private static string? ResolveMaterialName(
        ushort material,
        string[]? materialNames)
    {
        if (materialNames is null ||
            material >= materialNames.Length ||
            materialNames[material].Length == 0)
        {
            return null;
        }

        return materialNames[material];
    }

    private static string DecodeWindows1252(ReadOnlySpan<byte> bytes)
    {
        // The O3D material name is a byte string in the author's Windows code
        // page. OMSI's stock content is Windows-1252, which the base class
        // library does not expose without a code-page provider. The mapping is
        // small and fixed, so decode it here instead of adding a dependency.
        Span<char> characters = bytes.Length <= 256
            ? stackalloc char[bytes.Length]
            : new char[bytes.Length];

        for (int i = 0; i < bytes.Length; i++)
        {
            characters[i] = DecodeWindows1252Byte(bytes[i]);
        }

        return new string(characters);
    }

    private static char DecodeWindows1252Byte(byte value)
    {
        if (value < 0x80 || value >= 0xA0)
        {
            return (char)value;
        }

        return value switch
        {
            0x80 => '\u20AC',
            0x82 => '\u201A',
            0x83 => '\u0192',
            0x84 => '\u201E',
            0x85 => '\u2026',
            0x86 => '\u2020',
            0x87 => '\u2021',
            0x88 => '\u02C6',
            0x89 => '\u2030',
            0x8A => '\u0160',
            0x8B => '\u2039',
            0x8C => '\u0152',
            0x8E => '\u017D',
            0x91 => '\u2018',
            0x92 => '\u2019',
            0x93 => '\u201C',
            0x94 => '\u201D',
            0x95 => '\u2022',
            0x96 => '\u2013',
            0x97 => '\u2014',
            0x98 => '\u02DC',
            0x99 => '\u2122',
            0x9A => '\u0161',
            0x9B => '\u203A',
            0x9C => '\u0153',
            0x9E => '\u017E',
            0x9F => '\u0178',
            _ => (char)value,
        };
    }

    private ModelFormatException Error(string message)
    {
        return new ModelFormatException(
            _sourceName is null ? message : $"{_sourceName}: {message}");
    }

    private sealed class Reader(byte[] data, string? sourceName)
    {
        private readonly byte[] _data = data;
        private readonly string? _sourceName = sourceName;

        public int Position { get; private set; }

        public int Remaining => _data.Length - Position;

        public ModelFormatException Error(string message)
        {
            return Error(Position, message);
        }

        public ModelFormatException Error(int offset, string message)
        {
            string location = $"offset 0x{offset:X8}";
            return new ModelFormatException(
                _sourceName is null
                    ? $"{location}: {message}"
                    : $"{_sourceName}: {location}: {message}");
        }

        public void Need(long count, string context)
        {
            if (count < 0 || count > Remaining)
            {
                throw Error(
                    $"unexpected end of file while reading {context}: {count} " +
                    $"byte(s) required, {Remaining} remain.");
            }
        }

        public byte U8(string context)
        {
            Need(1, context);
            return _data[Position++];
        }

        public ushort U16(string context)
        {
            Need(sizeof(ushort), context);
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(
                _data.AsSpan(Position, sizeof(ushort)));
            Position += sizeof(ushort);
            return value;
        }

        public uint U32(string context)
        {
            Need(sizeof(uint), context);
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(
                _data.AsSpan(Position, sizeof(uint)));
            Position += sizeof(uint);
            return value;
        }

        public float F32(string context)
        {
            Need(FloatSize, context);
            float value = BinaryPrimitives.ReadSingleLittleEndian(
                _data.AsSpan(Position, FloatSize));
            Position += FloatSize;
            return value;
        }

        public ReadOnlySpan<byte> Bytes(int count, string context)
        {
            Need(count, context);
            ReadOnlySpan<byte> value = _data.AsSpan(Position, count);
            Position += count;
            return value;
        }
    }
}
