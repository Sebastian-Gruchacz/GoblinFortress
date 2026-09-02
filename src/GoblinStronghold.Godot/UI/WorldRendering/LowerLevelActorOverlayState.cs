using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Presentation;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal sealed class LowerLevelActorOverlayState
{
    private const long RefreshIntervalTicks = 10;
    private SampleKey? _sampleKey;
    private IReadOnlyList<LowerLevelActorMarker> _markers = [];

    public IReadOnlyList<LowerLevelActorMarker> Markers => _markers;

    public void Reset()
    {
        _sampleKey = null;
        _markers = [];
    }

    public bool Synchronize(
        SimulationSnapshot snapshot,
        LowerLevelExposureIndex exposure,
        PresentationCellBounds visibleBounds,
        bool force)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(exposure);
        var key = new SampleKey(
            snapshot.Tick.Value / RefreshIntervalTicks,
            exposure.ActiveLevel,
            visibleBounds);
        if (!force && _sampleKey == key)
        {
            return false;
        }

        var next = LowerLevelActorOverlayPolicy.SelectVisible(
            snapshot.Actors.Select(actor => new LowerLevelActorMarker(
                actor.Id,
                actor.Position,
                Math.Clamp(
                    (float)actor.Health / Math.Max(1, actor.EffectiveMaximumHealth),
                    0f,
                    1f),
                actor.IsElderly)),
            exposure,
            visibleBounds);
        var changed = !_markers.SequenceEqual(next);
        _markers = next;
        _sampleKey = key;
        return changed;
    }

    private readonly record struct SampleKey(
        long TickBucket,
        int ActiveLevel,
        PresentationCellBounds VisibleBounds);
}
