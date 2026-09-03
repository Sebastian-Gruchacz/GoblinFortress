using System.Diagnostics;
using GoblinStronghold.Simulation.Presentation;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

public readonly record struct TimedPresentationOperationMetrics(
    long Calls,
    TimeSpan LastDuration,
    TimeSpan TotalDuration);

public readonly record struct WorldPresentationPerformanceMetrics(
    TimedPresentationOperationMetrics EmitterQueries,
    long EmitterQueryResults,
    TimedPresentationOperationMetrics ActiveLightMapBuilds,
    long ActiveLightCells,
    long ActiveLightEmitterEvaluations,
    TimedPresentationOperationMetrics LowerChunkRebuildBatches,
    long LowerChunksRebuilt,
    long GeometryTextureRebuilds,
    long StaticLightTextureRebuilds,
    int VisibleDirtyChunks,
    PresentationSliceWorkload SliceWorkload,
    TimedPresentationOperationMetrics DynamicWorldDraws,
    TimedPresentationOperationMetrics StaticWorldDraws);

internal readonly record struct PresentationWarmupStatus(
    double Progress,
    bool IsReady);

internal sealed class TimedPresentationOperationCounter
{
    private long _calls;
    private TimeSpan _lastDuration;
    private TimeSpan _totalDuration;

    public TimedPresentationOperationMetrics Snapshot => new(
        _calls,
        _lastDuration,
        _totalDuration);

    public void Record(long startedAt)
    {
        _lastDuration = Stopwatch.GetElapsedTime(startedAt);
        _totalDuration += _lastDuration;
        _calls = checked(_calls + 1);
    }

    public void Reset()
    {
        _calls = 0;
        _lastDuration = TimeSpan.Zero;
        _totalDuration = TimeSpan.Zero;
    }
}
