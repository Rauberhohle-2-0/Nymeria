using System.Numerics;

namespace Nymeria.Formats.O3d.Tests;

/// <summary>
/// Produces the scrambled vertex records of a version 4+ O3D file from plain ones.
/// </summary>
/// <remarks>
/// <para>
/// This is original test code written from the documented writer direction: it
/// applies the position swaps, the normal swaps and sign changes, and the
/// texture-coordinate additions that OMSI's writer applies, in that order, which
/// is the inverse of the order the loader applies them. It deliberately does not
/// share code with the parser, so a decode test proves that two independent
/// transcriptions agree rather than that one function calls itself.
/// </para>
/// <para>
/// The state chain is the documented one: the header seeds the state, key
/// <c>0</c> restarts it at every vertex, and a byte derived from the fractional
/// parts of the <em>stored</em> position of the previous vertex mixes into the
/// next state. The writer therefore mixes in the byte after scrambling each
/// position, because that is the value the file will hold.
/// </para>
/// </remarks>
internal static class O3dScrambleWriter
{
    /// <summary>Scrambles plain vertex records into the stored form.</summary>
    /// <param name="version">The O3D version, at least 4.</param>
    /// <param name="flags">The O3D header flags.</param>
    /// <param name="key">The vertex key; <c>0xFFFFFFFF</c> means plain.</param>
    /// <param name="vertices">The plain records.</param>
    /// <returns>The records exactly as a scrambled file stores them.</returns>
    public static TestVertex[] Scramble(
        byte version,
        byte flags,
        uint key,
        IReadOnlyList<TestVertex> vertices)
    {
        if (version < 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                "Scrambling only exists in version 4 and later.");
        }

        bool alternate = (flags & 0x02) != 0;
        ushort state = (ushort)(((long)key + version - 4 + (alternate ? 0x17D : 0)) % 0xFDE8);
        uint countModulus = (uint)vertices.Count % 0xFDE8;
        uint mixing = 0;

        var stored = new TestVertex[vertices.Count];

        for (int i = 0; i < vertices.Count; i++)
        {
            if (key == 0)
            {
                state = alternate ? (ushort)0x130 : (ushort)0;
            }

            state = (ushort)((((uint)state * countModulus) + (countModulus * mixing)) % 8000);

            TestVertex vertex = ScrambleRecord(state, vertices[i]);
            stored[i] = vertex;
            mixing = MixingByte(vertex.Position);
        }

        return stored;
    }

    private static TestVertex ScrambleRecord(ushort state, TestVertex vertex)
    {
        Vector3 position = vertex.Position;
        Vector3 normal = vertex.Normal;
        Vector2 uv = vertex.TextureCoordinate;

        if (state < 1000)
        {
            (position.X, position.Y) = (position.Y, position.X);
        }
        else if (state < 3000)
        {
            (position.X, position.Z) = (position.Z, position.X);
        }
        else if (state > 7000)
        {
            (position.Y, position.Z) = (position.Z, position.Y);
        }

        if (state < 600)
        {
            (normal.Y, normal.Z) = (normal.Z, normal.Y);
        }
        else if (state > 4500)
        {
            (normal.X, normal.Y) = (normal.Y, normal.X);
        }

        if (state % 4 == 0)
        {
            normal.X = -normal.X;
        }

        if (state % 6 == 0)
        {
            normal.Y = -normal.Y;
        }

        if (state % 7 == 0)
        {
            normal.Z = -normal.Z;
        }

        if (state % 5 == 0)
        {
            float shift = state % 100;
            uv.X += shift * shift / 10000f;
        }

        if (state % 3 == 0)
        {
            float shift = state % 50;
            uv.Y += shift * shift / 2500f;
        }

        return new TestVertex(position, normal, uv);
    }

    private static uint MixingByte(Vector3 storedPosition)
    {
        double x = Fraction(storedPosition.X);
        double y = Fraction(storedPosition.Y);
        double z = Fraction(storedPosition.Z);
        double product = Math.Abs(x * y * z) * 600.0;

        return (uint)(Math.Truncate(product) % 256);
    }

    private static double Fraction(float value)
    {
        double exact = value;

        return exact - Math.Truncate(exact);
    }
}
