using System;

namespace Nymeria.Core.Diagnostics;

/// <summary>
/// Summary statistics for the samples currently held by a
/// <see cref="FrameTimingWindow"/>.
/// </summary>
/// <remarks>
/// Durations are wall-clock CPU frame durations in milliseconds, not GPU
/// execution times. An empty window reports zero samples and zero values.
/// </remarks>
public readonly record struct FrameTimingSummary(
    int SampleCount,
    double AverageMilliseconds,
    double P95Milliseconds,
    double MaximumMilliseconds,
    double FramesPerSecond);

/// <summary>
/// A fixed-capacity rolling window of frame durations with optional warm-up
/// exclusion.
/// </summary>
/// <remarks>
/// <para>
/// The window accepts finite, non-negative millisecond samples. Negative,
/// non-finite, and other invalid samples are rejected and counted by
/// <see cref="RejectedSamples"/> instead of corrupting the statistics. Zero is a
/// valid sample; it produces a zero frame rate rather than an infinite one.
/// </para>
/// <para>
/// The first <see cref="WarmupSamples"/> accepted samples are discarded so
/// startup, shader compilation, and first-upload costs are excluded. Once the
/// capacity is full, each new sample replaces the oldest one. Storage is fixed at
/// construction, so <see cref="AddSample"/> does not allocate.
/// </para>
/// <para>
/// <see cref="GetSummary"/> copies and sorts the retained samples to calculate
/// the 95th percentile. Call it once per reporting interval, not per frame. This
/// type is not thread-safe; each window must be used from one thread.
/// </para>
/// </remarks>
public sealed class FrameTimingWindow
{
    private readonly double[] _samples;
    private readonly int _warmupSamples;
    private int _warmupRemaining;
    private int _nextIndex;
    private int _count;

    /// <summary>
    /// Creates a bounded timing window.
    /// </summary>
    /// <param name="capacity">The maximum number of retained samples, at least 1.</param>
    /// <param name="warmupSamples">
    /// The number of valid samples to discard before collecting, at least 0.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="capacity"/> is less than 1 or
    /// <paramref name="warmupSamples"/> is negative.
    /// </exception>
    public FrameTimingWindow(int capacity = 600, int warmupSamples = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(warmupSamples);

        _samples = new double[capacity];
        _warmupSamples = warmupSamples;
        _warmupRemaining = warmupSamples;
    }

    /// <summary>
    /// Gets the maximum number of retained samples.
    /// </summary>
    public int Capacity => _samples.Length;

    /// <summary>
    /// Gets the configured number of accepted samples discarded as warm-up.
    /// </summary>
    public int WarmupSamples => _warmupSamples;

    /// <summary>
    /// Gets the number of retained samples.
    /// </summary>
    public int SampleCount => _count;

    /// <summary>
    /// Gets the number of samples rejected because they were negative or
    /// non-finite.
    /// </summary>
    public int RejectedSamples { get; private set; }

    /// <summary>
    /// Adds one frame duration in milliseconds.
    /// </summary>
    /// <remarks>
    /// Invalid samples are ignored and do not consume warm-up. Negative samples
    /// are invalid; an exact zero is retained as a valid, very short frame.
    /// </remarks>
    public void AddSample(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0.0)
        {
            RejectedSamples++;
            return;
        }

        if (_warmupRemaining > 0)
        {
            _warmupRemaining--;
            return;
        }

        _samples[_nextIndex] = milliseconds;
        _nextIndex = (_nextIndex + 1) % _samples.Length;

        if (_count < _samples.Length)
        {
            _count++;
        }
    }

    /// <summary>
    /// Clears retained samples, rejected-sample count, and warm-up progress.
    /// </summary>
    public void Reset()
    {
        Array.Clear(_samples);
        _warmupRemaining = _warmupSamples;
        _nextIndex = 0;
        _count = 0;
        RejectedSamples = 0;
    }

    /// <summary>
    /// Calculates statistics for the retained samples.
    /// </summary>
    /// <returns>
    /// A summary with a nearest-rank 95th percentile and a frame rate derived
    /// from the average frame duration. An empty or zero-duration window reports
    /// zero for all values.
    /// </returns>
    /// <exception cref="OutOfMemoryException">The sample copy cannot be allocated.</exception>
    public FrameTimingSummary GetSummary()
    {
        if (_count == 0)
        {
            return default;
        }

        var samples = new double[_count];
        Array.Copy(_samples, samples, _count);
        Array.Sort(samples);

        // Divide each term before summing so a set of very large finite samples
        // cannot overflow the average into infinity.
        double average = 0.0;

        foreach (double sample in samples)
        {
            average += sample / samples.Length;
        }

        double averageMilliseconds = double.IsFinite(average) ? average : 0.0;
        int percentileIndex = (int)Math.Ceiling(0.95 * samples.Length) - 1;
        percentileIndex = Math.Clamp(percentileIndex, 0, samples.Length - 1);
        double p95 = samples[percentileIndex];
        double maximum = samples[^1];
        double framesPerSecond = averageMilliseconds > 0.0
            ? 1000.0 / averageMilliseconds
            : 0.0;

        return new FrameTimingSummary(
            samples.Length,
            averageMilliseconds,
            p95,
            maximum,
            framesPerSecond);
    }
}
