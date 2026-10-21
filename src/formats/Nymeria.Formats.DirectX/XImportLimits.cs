namespace Nymeria.Formats.DirectX;

/// <summary>
/// Bounds applied while importing one text DirectX <c>.x</c> file, including
/// while it is still untrusted input.
/// </summary>
internal sealed class XImportLimits
{
    public static XImportLimits Default { get; } = new(
        XModelParser.MaxInputBytes,
        XModelParser.MaxVertexCount,
        XModelParser.MaxTriangleCount,
        XModelParser.MaxSubsetCount,
        XModelParser.MaxFaceVertexCount,
        XModelParser.MaxFaceIndexCount,
        XModelParser.MaxNestingDepth,
        XModelParser.MaxFrameCount,
        XModelParser.MaxTokenLength,
        XModelParser.MaxTokenCount);

    public XImportLimits(
        int maxInputBytes,
        int maxVertexCount,
        int maxTriangleCount,
        int maxSubsetCount,
        int maxFaceVertexCount,
        int maxFaceIndexCount,
        int maxNestingDepth,
        int maxFrameCount,
        int maxTokenLength,
        long maxTokenCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxInputBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxVertexCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTriangleCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSubsetCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFaceVertexCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFaceIndexCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxNestingDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrameCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTokenLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTokenCount);

        MaxInputBytes = maxInputBytes;
        MaxVertexCount = maxVertexCount;
        MaxTriangleCount = maxTriangleCount;
        MaxSubsetCount = maxSubsetCount;
        MaxFaceVertexCount = maxFaceVertexCount;
        MaxFaceIndexCount = maxFaceIndexCount;
        MaxNestingDepth = maxNestingDepth;
        MaxFrameCount = maxFrameCount;
        MaxTokenLength = maxTokenLength;
        MaxTokenCount = maxTokenCount;
    }

    public int MaxInputBytes { get; }

    public int MaxVertexCount { get; }

    public int MaxTriangleCount { get; }

    public int MaxSubsetCount { get; }

    public int MaxFaceVertexCount { get; }

    public int MaxFaceIndexCount { get; }

    public int MaxNestingDepth { get; }

    public int MaxFrameCount { get; }

    public int MaxTokenLength { get; }

    public long MaxTokenCount { get; }
}
