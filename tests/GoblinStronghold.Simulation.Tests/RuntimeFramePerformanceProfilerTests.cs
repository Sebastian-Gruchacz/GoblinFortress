using GoblinStronghold.Simulation.Diagnostics;
using GoblinStronghold.Simulation.Presentation;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class RuntimeFramePerformanceProfilerTests
{
    [Fact]
    public void RecordsOnlyFramesAtOrAboveTheSpikeThreshold()
    {
        var profiler = new RuntimeFramePerformanceProfiler(
            TimeSpan.FromMilliseconds(50),
            spikeCapacity: 4);

        Assert.Null(profiler.Observe(Sample(frameMilliseconds: 16, processMilliseconds: 3)));
        var spike = profiler.Observe(Sample(frameMilliseconds: 51, processMilliseconds: 4));

        Assert.NotNull(spike);
        Assert.Equal(2, spike.Value.FrameIndex);
        var profile = profiler.Snapshot();
        Assert.Equal(2, profile.FramesObserved);
        Assert.Equal(1, profile.SpikesObserved);
        Assert.Equal(spike, profile.WorstSample);
        Assert.Equal([spike.Value], profile.RecentSpikes);
    }

    [Fact]
    public void RetainsBoundedSpikesInChronologicalOrderAndKeepsTheWorst()
    {
        var profiler = new RuntimeFramePerformanceProfiler(
            TimeSpan.FromMilliseconds(10),
            spikeCapacity: 2);

        profiler.Observe(Sample(frameMilliseconds: 20, processMilliseconds: 5));
        profiler.Observe(Sample(frameMilliseconds: 80, processMilliseconds: 6));
        profiler.Observe(Sample(frameMilliseconds: 30, processMilliseconds: 70));

        var profile = profiler.Snapshot();

        Assert.Equal(3, profile.SpikesObserved);
        Assert.Equal([2L, 3L], profile.RecentSpikes.Select(sample => sample.FrameIndex));
        Assert.Equal(TimeSpan.FromMilliseconds(80), profile.WorstSample?.EffectiveDuration);
    }

    private static RuntimeFramePerformanceSample Sample(
        double frameMilliseconds,
        double processMilliseconds) => new(
        FrameIndex: 0,
        SimulationTick: 12,
        FrameInterval: TimeSpan.FromMilliseconds(frameMilliseconds),
        MainProcessDuration: TimeSpan.FromMilliseconds(processMilliseconds),
        SimulationDuration: TimeSpan.Zero,
        SnapshotDuration: TimeSpan.Zero,
        PresentationRefreshDuration: TimeSpan.Zero,
        AutosaveDuration: TimeSpan.Zero,
        TicksAdvanced: 0,
        ActiveActors: 8,
        EmitterQueryDuration: TimeSpan.Zero,
        ActiveLightMapDuration: TimeSpan.Zero,
        LowerChunkRebuildDuration: TimeSpan.Zero,
        VisibleDirtyChunks: 0,
        SliceWorkload: default);
}
