using Nymeria.Core.Diagnostics;

namespace Nymeria.Core.Tests;

public sealed class FrameTimingWindowTests
{
    [Fact]
    public void EmptyWindow_ReportsZeroValues()
    {
        var window = new FrameTimingWindow(capacity: 4);

        FrameTimingSummary summary = window.GetSummary();

        Assert.Equal(0, summary.SampleCount);
        Assert.Equal(0.0, summary.AverageMilliseconds);
        Assert.Equal(0.0, summary.P95Milliseconds);
        Assert.Equal(0.0, summary.MaximumMilliseconds);
        Assert.Equal(0.0, summary.FramesPerSecond);
    }

    [Fact]
    public void Summary_UsesAverageNearestRankP95AndMaximum()
    {
        var window = new FrameTimingWindow(capacity: 100);

        for (int i = 1; i <= 100; i++)
        {
            window.AddSample(i);
        }

        FrameTimingSummary summary = window.GetSummary();

        Assert.Equal(100, summary.SampleCount);
        Assert.Equal(50.5, summary.AverageMilliseconds, precision: 10);
        Assert.Equal(95.0, summary.P95Milliseconds);
        Assert.Equal(100.0, summary.MaximumMilliseconds);
        Assert.Equal(1000.0 / 50.5, summary.FramesPerSecond, precision: 10);
    }

    [Fact]
    public void Capacity_KeepsOnlyNewestSamples()
    {
        var window = new FrameTimingWindow(capacity: 3);

        window.AddSample(1.0);
        window.AddSample(2.0);
        window.AddSample(3.0);
        window.AddSample(4.0);

        FrameTimingSummary summary = window.GetSummary();

        Assert.Equal(3, summary.SampleCount);
        Assert.Equal(3.0, summary.AverageMilliseconds, precision: 10);
        Assert.Equal(4.0, summary.MaximumMilliseconds);
    }

    [Fact]
    public void WarmupSamples_AreExcludedFromStatistics()
    {
        var window = new FrameTimingWindow(capacity: 10, warmupSamples: 2);

        window.AddSample(100.0);
        window.AddSample(200.0);
        window.AddSample(1.0);
        window.AddSample(2.0);

        FrameTimingSummary summary = window.GetSummary();

        Assert.Equal(2, summary.SampleCount);
        Assert.Equal(1.5, summary.AverageMilliseconds, precision: 10);
        Assert.Equal(2.0, summary.MaximumMilliseconds);
    }

    [Fact]
    public void InvalidSamples_AreIgnoredAndCountedWithoutConsumingWarmup()
    {
        var window = new FrameTimingWindow(capacity: 4, warmupSamples: 1);

        window.AddSample(double.NaN);
        window.AddSample(double.PositiveInfinity);
        window.AddSample(double.NegativeInfinity);
        window.AddSample(-1.0);
        window.AddSample(2.0);
        window.AddSample(4.0);

        FrameTimingSummary summary = window.GetSummary();

        Assert.Equal(4, window.RejectedSamples);
        Assert.Equal(1, summary.SampleCount);
        Assert.Equal(4.0, summary.AverageMilliseconds, precision: 10);
    }

    [Fact]
    public void ZeroSample_IsValidButReportsZeroFrameRate()
    {
        var window = new FrameTimingWindow(capacity: 4);

        window.AddSample(0.0);

        FrameTimingSummary summary = window.GetSummary();

        Assert.Equal(1, summary.SampleCount);
        Assert.Equal(0.0, summary.AverageMilliseconds);
        Assert.Equal(0.0, summary.FramesPerSecond);
    }

    [Fact]
    public void Reset_ClearsSamplesRejectionsAndWarmupProgress()
    {
        var window = new FrameTimingWindow(capacity: 4, warmupSamples: 1);
        window.AddSample(double.NaN);
        window.AddSample(5.0);
        window.AddSample(10.0);

        window.Reset();

        Assert.Equal(0, window.SampleCount);
        Assert.Equal(0, window.RejectedSamples);

        window.AddSample(20.0);
        window.AddSample(30.0);

        FrameTimingSummary summary = window.GetSummary();

        Assert.Equal(1, summary.SampleCount);
        Assert.Equal(30.0, summary.AverageMilliseconds);
    }

    [Fact]
    public void Constructor_RejectsInvalidBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FrameTimingWindow(capacity: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FrameTimingWindow(capacity: 1, warmupSamples: -1));
    }

    [Fact]
    public void GetSummary_DoesNotChangeTheWindow()
    {
        var window = new FrameTimingWindow(capacity: 4);
        window.AddSample(1.0);
        window.AddSample(2.0);

        FrameTimingSummary first = window.GetSummary();
        FrameTimingSummary second = window.GetSummary();

        Assert.Equal(first, second);
        Assert.Equal(2, window.SampleCount);
    }
}
