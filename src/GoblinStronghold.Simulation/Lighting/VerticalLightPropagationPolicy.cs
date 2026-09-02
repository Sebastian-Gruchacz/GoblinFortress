using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Lighting;

public static class VerticalLightPropagationPolicy
{
    private const float PassageTransmission = 0.78f;
    private const float MinimumProjectedRadius = 0.75f;

    public static bool TryProjectThrough(
        LightEmitterSnapshot emitter,
        VerticalPassage passage,
        IReadOnlySet<GridPosition> blockingCells,
        out LightEmitterSnapshot projected)
    {
        ArgumentNullException.ThrowIfNull(blockingCells);
        if (emitter.Position.Z != passage.Lower.Z ||
            passage.Upper.Z <= passage.Lower.Z)
        {
            projected = default;
            return false;
        }

        var contributionAtOpening = LightOcclusionPolicy.CalculateContribution(
            emitter,
            passage.Lower,
            blockingCells);
        var horizontalX = passage.Lower.X - emitter.Position.X;
        var horizontalY = passage.Lower.Y - emitter.Position.Y;
        var distanceToOpening = MathF.Sqrt(
            (horizontalX * horizontalX) + (horizontalY * horizontalY));
        var passageX = passage.Upper.X - passage.Lower.X;
        var passageY = passage.Upper.Y - passage.Lower.Y;
        var passageZ = passage.Upper.Z - passage.Lower.Z;
        var passageDistance = MathF.Sqrt(
            (passageX * passageX) +
            (passageY * passageY) +
            (passageZ * passageZ));
        var remainingRadius = emitter.RadiusCells - distanceToOpening - passageDistance;
        if (contributionAtOpening <= 0f || remainingRadius < MinimumProjectedRadius)
        {
            projected = default;
            return false;
        }

        projected = new LightEmitterSnapshot(
            new LightEmitterHandle(
                emitter.Handle.DefinitionId,
                CreateProjectedInstanceId(emitter.Handle.InstanceId, passage)),
            passage.Upper,
            remainingRadius,
            Math.Clamp(contributionAtOpening * PassageTransmission, 0.01f, 1f));
        return true;
    }

    private static ulong CreateProjectedInstanceId(ulong source, VerticalPassage passage)
    {
        const ulong offset = 14_695_981_039_346_656_037UL;
        const ulong prime = 1_099_511_628_211UL;
        var result = (offset ^ source) * prime;
        result = (result ^ unchecked((uint)passage.Upper.X)) * prime;
        result = (result ^ unchecked((uint)passage.Upper.Y)) * prime;
        result = (result ^ unchecked((uint)passage.Upper.Z)) * prime;
        result = (result ^ unchecked((uint)passage.Lower.X)) * prime;
        result = (result ^ unchecked((uint)passage.Lower.Y)) * prime;
        return (result ^ unchecked((uint)passage.Lower.Z)) * prime;
    }
}
