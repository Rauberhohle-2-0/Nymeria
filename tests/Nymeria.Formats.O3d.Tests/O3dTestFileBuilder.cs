using System.Numerics;

namespace Nymeria.Formats.O3d.Tests;

/// <summary>
/// Writes small synthetic O3D files from the documented layout. It is original
/// test code, not a copy of any OMSI or openOMSI implementation.
/// </summary>
internal sealed class O3dTestFileBuilder
{
    public const uint UnscrambledKey = 0xFFFFFFFF;

    private readonly List<byte> _bytes = [];

    public O3dTestFileBuilder(
        byte version = 3,
        byte flags = 0,
        uint? key = null)
    {
        Version = version;
        Flags = flags;
        Key = key ?? UnscrambledKey;

        U8(0x84);
        U8(0x19);
        U8(version);

        if (version >= 3)
        {
            U8(flags);
        }

        if (version >= 4)
        {
            U32(Key);
        }
    }

    public byte Version { get; }

    public byte Flags { get; }

    public uint Key { get; }

    public O3dTestFileBuilder U8(byte value)
    {
        _bytes.Add(value);
        return this;
    }

    public O3dTestFileBuilder U16(ushort value)
    {
        _bytes.Add((byte)value);
        _bytes.Add((byte)(value >> 8));
        return this;
    }

    public O3dTestFileBuilder U32(uint value)
    {
        _bytes.Add((byte)value);
        _bytes.Add((byte)(value >> 8));
        _bytes.Add((byte)(value >> 16));
        _bytes.Add((byte)(value >> 24));
        return this;
    }

    public O3dTestFileBuilder F32(float value)
    {
        U32(BitConverter.SingleToUInt32Bits(value));
        return this;
    }

    public O3dTestFileBuilder Raw(params byte[] values)
    {
        _bytes.AddRange(values);
        return this;
    }

    public O3dTestFileBuilder Count(int value)
    {
        return Version >= 3 ? U32((uint)value) : U16((ushort)value);
    }

    public O3dTestFileBuilder Vertex(TestVertex vertex)
    {
        F32(vertex.Position.X);
        F32(vertex.Position.Y);
        F32(vertex.Position.Z);
        F32(vertex.Normal.X);
        F32(vertex.Normal.Y);
        F32(vertex.Normal.Z);
        F32(vertex.TextureCoordinate.X);
        F32(vertex.TextureCoordinate.Y);
        return this;
    }

    public O3dTestFileBuilder Vertices(params TestVertex[] vertices)
    {
        U8(0x17);
        Count(vertices.Length);
        return VerticesBody(vertices);
    }

    public O3dTestFileBuilder VerticesBody(params TestVertex[] vertices)
    {
        foreach (TestVertex vertex in vertices)
        {
            Vertex(vertex);
        }

        return this;
    }

    public O3dTestFileBuilder Triangles(params TestTriangle[] triangles)
    {
        U8(0x49);
        Count(triangles.Length);
        return TrianglesBody(triangles);
    }

    public O3dTestFileBuilder TrianglesBody(params TestTriangle[] triangles)
    {
        bool wideIndices = (Flags & 0x01) != 0;

        foreach (TestTriangle triangle in triangles)
        {
            WriteIndex(triangle.First, wideIndices);
            WriteIndex(triangle.Second, wideIndices);
            WriteIndex(triangle.Third, wideIndices);
            U16(triangle.Material);
        }

        return this;
    }

    public O3dTestFileBuilder Materials(params string[] names)
    {
        U8(0x26);
        U16((ushort)names.Length);

        foreach (string name in names)
        {
            MaterialBody(System.Text.Encoding.Latin1.GetBytes(name));
        }

        return this;
    }

    public O3dTestFileBuilder MaterialBytes(params byte[][] names)
    {
        U8(0x26);
        U16((ushort)names.Length);

        foreach (byte[] name in names)
        {
            MaterialBody(name);
        }

        return this;
    }

    public O3dTestFileBuilder MaterialBody(params byte[] name)
    {
        MaterialBodyPrefix();
        U8((byte)name.Length);
        Raw(name);
        return this;
    }

    public O3dTestFileBuilder MaterialBodyPrefix()
    {
        for (int i = 0; i < 11; i++)
        {
            F32(i == 3 || i == 10 ? 1.0f : 0.0f);
        }

        return this;
    }

    public O3dTestFileBuilder IdentityMatrix()
    {
        return Matrix(
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1);
    }

    public O3dTestFileBuilder Matrix(params float[] values)
    {
        if (values.Length != 16)
        {
            throw new ArgumentException("A O3D matrix has 16 values.", nameof(values));
        }

        U8(0x79);

        foreach (float value in values)
        {
            F32(value);
        }

        return this;
    }

    public O3dTestFileBuilder BonesSection()
    {
        U8(0x54);
        U16(0);
        return this;
    }

    public byte[] Build()
    {
        return _bytes.ToArray();
    }

    private void WriteIndex(uint value, bool wide)
    {
        if (wide)
        {
            U32(value);
        }
        else
        {
            U16((ushort)value);
        }
    }
}

internal readonly record struct TestVertex(
    Vector3 Position,
    Vector3 Normal,
    Vector2 TextureCoordinate);

internal readonly record struct TestTriangle(
    uint First,
    uint Second,
    uint Third,
    ushort Material = 0);
