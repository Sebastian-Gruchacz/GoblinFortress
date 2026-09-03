using GoblinStronghold.Simulation.Presentation;

namespace GoblinStronghold.Simulation.Diagnostics;

public readonly record struct RuntimeFramePerformanceSample(
    long FrameIndex,
    long SimulationTick,
    TimeSpan FrameInterval,
    TimeSpan MainProcessDuration,
    TimeSpan SimulationDuration,
    TimeSpan SnapshotDuration,
    TimeSpan PresentationRefreshDuration,
    TimeSpan AutosaveDuration,
    int TicksAdvanced,
    int ActiveActors,
    TimeSpan EmitterQueryDuration,
    TimeSpan ActiveLightMapDuration,
    TimeSpan ActiveFogMapDuration,
    TimeSpan LowerChunkRebuildDuration,
    int VisibleDirtyChunks,
    PresentationSliceWorkload SliceWorkload,
    TimeSpan DynamicWorldDrawDuration,
    TimeSpan StaticWorldDrawDuration)
{
    public TimeSpan EffectiveDuration => FrameInterval > MainProcessDuration
        ? FrameInterval
        : MainProcessDuration;
}

public readonly record struct RuntimeFramePerformanceProfile(
    long FramesObserved,
    long SpikesObserved,
    RuntimeFramePerformanceSample? WorstSample,
    IReadOnlyList<RuntimeFramePerformanceSample> RecentSpikes);

public sealed class RuntimeFramePerformanceProfiler
{
    public static readonly TimeSpan DefaultSpikeThreshold = TimeSpan.FromMilliseconds(50);
    public const int DefaultSpikeCapacity = 24;

    private readonly TimeSpan _spikeThreshold;
    private readonly RuntimeFramePerformanceSample[] _recentSpikes;
    private long _framesObserved;
    private long _spikesObserved;
    private int _nextSpikeIndex;
    private int _storedSpikeCount;
    private RuntimeFramePerformanceSample? _worstSample;

    public RuntimeFramePerformanceProfiler(
        TimeSpan? spikeThreshold = null,
        int spikeCapacity = DefaultSpikeCapacity)
    {
        _spikeThreshold = spikeThreshold ?? DefaultSpikeThreshold;
        if (_spikeThreshold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(spikeThreshold),
                "The spike threshold must be positive.");
        }
        if (spikeCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(spikeCapacity),
                "The spike capacity must be positive.");
        }

        _recentSpikes = new RuntimeFramePerformanceSample[spikeCapacity];
    }

    public RuntimeFramePerformanceSample? Observe(RuntimeFramePerformanceSample observation)
    {
        _framesObserved = checked(_framesObserved + 1);
        var sample = observation with { FrameIndex = _framesObserved };
        if (sample.EffectiveDuration < _spikeThreshold)
        {
            return null;
        }

        _spikesObserved = checked(_spikesObserved + 1);
        _recentSpikes[_nextSpikeIndex] = sample;
        _nextSpikeIndex = (_nextSpikeIndex + 1) % _recentSpikes.Length;
        _storedSpikeCount = Math.Min(_storedSpikeCount + 1, _recentSpikes.Length);
        if (_worstSample is not { } worst ||
            sample.EffectiveDuration > worst.EffectiveDuration)
        {
            _worstSample = sample;
        }

        return sample;
    }

    public RuntimeFramePerformanceProfile Snapshot()
    {
        var recent = new RuntimeFramePerformanceSample[_storedSpikeCount];
        var oldestIndex = (_nextSpikeIndex - _storedSpikeCount + _recentSpikes.Length) %
            _recentSpikes.Length;
        for (var index = 0; index < recent.Length; index++)
        {
            recent[index] = _recentSpikes[(oldestIndex + index) % _recentSpikes.Length];
        }

        return new RuntimeFramePerformanceProfile(
            _framesObserved,
            _spikesObserved,
            _worstSample,
            recent);
    }
}
