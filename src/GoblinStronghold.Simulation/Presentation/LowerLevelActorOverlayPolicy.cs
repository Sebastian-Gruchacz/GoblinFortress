using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Presentation;

public readonly record struct LowerLevelActorMarker(
    EntityId Id,
    GridPosition Position,
    float HealthRatio,
    bool IsElderly,
    ContentId? LightEmitterDefinitionId = null);

public static class LowerLevelActorOverlayPolicy
{
    public static IReadOnlyList<LowerLevelActorMarker> SelectVisible(
        IEnumerable<LowerLevelActorMarker> actors,
        LowerLevelExposureIndex exposure,
        PresentationCellBounds visibleBounds)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(exposure);
        var candidates = actors.ToArray();
        if (candidates.Any(actor => actor.HealthRatio is < 0f or > 1f))
        {
            throw new ArgumentOutOfRangeException(
                nameof(actors),
                "Actor health ratios must remain between zero and one.");
        }

        return candidates
            .Where(actor =>
                visibleBounds.Contains(actor.Position) &&
                exposure.IsContinuouslyExposed(actor.Position))
            .OrderBy(actor => actor.Position.Z)
            .ThenBy(actor => actor.Position.Y)
            .ThenBy(actor => actor.Position.X)
            .ThenBy(actor => actor.Id)
            .ToArray();
    }
}
