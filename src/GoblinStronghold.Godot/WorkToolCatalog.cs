using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Planning;
using GoblinStronghold.Simulation.Terrain;
using Godot;

namespace GoblinStronghold.GodotClient;

internal enum WorkAreaSelectionBehavior : byte
{
    FilterTargets = 1,
    ApplyToApplicableCells = 2,
    SingleApplicableCell = 3,
}

internal readonly record struct WorkPreviewStyle(
    Color Color,
    float Inset,
    float Width,
    bool Filled = false);

internal static class WorkToolCatalog
{
    public static WorkAreaSelectionBehavior GetSelectionBehavior(WorkDesignationKind kind)
    {
        if (TerrainModificationCatalog.TryGet(kind, out var terrain))
        {
            return terrain.PlacementMode == WorldToolPlacementMode.Point
                ? WorkAreaSelectionBehavior.SingleApplicableCell
                : WorkAreaSelectionBehavior.ApplyToApplicableCells;
        }

        return kind switch
        {
            WorkDesignationKind.GatherFood or
            WorkDesignationKind.GatherReeds or
            WorkDesignationKind.GatherBrushwood or
            WorkDesignationKind.GatherStone or
            WorkDesignationKind.UprootBerryBush or
            WorkDesignationKind.FellTree or
            WorkDesignationKind.QuarryBoulder or
            WorkDesignationKind.HuntAnimal => WorkAreaSelectionBehavior.FilterTargets,
            _ => WorkAreaSelectionBehavior.ApplyToApplicableCells,
        };
    }

    public static WorkPreviewStyle GetPreviewStyle(WorkDesignationKind kind) => kind switch
    {
        WorkDesignationKind.GatherFood =>
            new(new Color(0.65f, 1f, 0.3f, 0.9f), 1.5f, 2f),
        WorkDesignationKind.GatherReeds =>
            new(new Color(0.78f, 0.96f, 0.36f, 0.92f), 1.5f, 2f),
        WorkDesignationKind.GatherBrushwood =>
            new(new Color(0.9f, 0.58f, 0.25f, 0.9f), 1.5f, 2f),
        WorkDesignationKind.UprootBerryBush =>
            new(new Color(1f, 0.32f, 0.2f, 0.92f), 1.5f, 2f),
        WorkDesignationKind.FellTree =>
            new(new Color(1f, 0.78f, 0.18f, 0.95f), 1.5f, 2f),
        WorkDesignationKind.GatherStone =>
            new(new Color(0.7f, 0.8f, 0.88f, 0.92f), 1.5f, 2f),
        WorkDesignationKind.QuarryBoulder =>
            new(new Color(0.84f, 0.9f, 0.96f, 0.96f), 1.5f, 2f),
        WorkDesignationKind.MineRock =>
            new(new Color(1f, 0.7f, 0.24f, 0.96f), 1.5f, 2f),
        WorkDesignationKind.CarveRampDown =>
            new(new Color(0.34f, 0.76f, 1f, 0.98f), 1.5f, 2f),
        WorkDesignationKind.CarveRampUp =>
            new(new Color(1f, 0.82f, 0.34f, 0.98f), 1.5f, 2f),
        WorkDesignationKind.Scout =>
            new(new Color(0.42f, 0.86f, 1f, 0.88f), 1.5f, 2f),
        WorkDesignationKind.HuntAnimal =>
            new(new Color(1f, 0.36f, 0.2f, 0.96f), 1.5f, 2f),
        WorkDesignationKind.CleanBlood =>
            new(new Color(0.92f, 0.84f, 1f, 0.98f), 1.5f, 2f),
        _ => new(new Color(0.95f, 0.28f, 0.24f, 0.9f), 1.5f, 2f),
    };
}
