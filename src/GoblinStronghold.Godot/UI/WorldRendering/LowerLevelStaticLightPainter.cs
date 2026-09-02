using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Lighting;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Presentation;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal static class LowerLevelStaticLightPainter
{
    private const float MaximumBrightnessGain = 0.48f;
    private const float WarmTintAmount = 0.035f;

    public static Image Paint(
        SimulationEngine engine,
        PresentationChunkKey key,
        Image geometry,
        Image exposureMask,
        int chunkSize,
        int pixelsPerCell)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(exposureMask);

        var image = Image.CreateEmpty(
            geometry.GetWidth(),
            geometry.GetHeight(),
            false,
            Image.Format.Rgba8);
        image.BlitRect(
            geometry,
            new Rect2I(0, 0, geometry.GetWidth(), geometry.GetHeight()),
            Vector2I.Zero);
        var worldObjects = engine.World.CreateWorldObjectSnapshot();
        var emitters = CollectEmitters(engine, worldObjects, key, chunkSize);
        if (emitters.Count > 0)
        {
            var maximumRadius = (int)Math.Ceiling(
                emitters.Max(emitter => emitter.Snapshot.RadiusCells));
            var minimumX = (key.X * chunkSize) - maximumRadius;
            var minimumY = (key.Y * chunkSize) - maximumRadius;
            var blockers = LightBlockingCellIndex.Collect(
                engine,
                worldObjects,
                key.Level,
                minimumX,
                minimumY,
                minimumX + chunkSize + (maximumRadius * 2),
                minimumY + chunkSize + (maximumRadius * 2));
            PaintCells(
                image,
                exposureMask,
                key,
                chunkSize,
                pixelsPerCell,
                emitters,
                blockers);
        }

        ApplyExposureMask(image, exposureMask, pixelsPerCell);
        return image;
    }

    private static IReadOnlyList<CachedLightEmitter> CollectEmitters(
        SimulationEngine engine,
        IReadOnlyList<WorldObjectSnapshot> worldObjects,
        PresentationChunkKey key,
        int chunkSize)
    {
        var emitters = new List<CachedLightEmitter>();
        var chunkMinimumX = key.X * chunkSize;
        var chunkMinimumY = key.Y * chunkSize;
        var chunkMaximumX = chunkMinimumX + chunkSize;
        var chunkMaximumY = chunkMinimumY + chunkSize;
        foreach (var worldObject in worldObjects)
        {
            if (worldObject.Anchor.Z != key.Level ||
                !LightEmitterCatalog.TryGet(worldObject.Kind, out var definition) ||
                !LightEmitterActivationPolicy.IsStaticallyActive(definition) ||
                !Intersects(
                    worldObject.Anchor,
                    definition.RadiusCells,
                    chunkMinimumX,
                    chunkMinimumY,
                    chunkMaximumX,
                    chunkMaximumY))
            {
                continue;
            }

            emitters.Add(CreateEmitter(
                worldObject.Anchor,
                definition,
                worldObject.Id.Value,
                worldObject.Kind == WorldObjectKind.WallTorch
                    ? worldObject.Orientation
                    : null));
        }

        if (key.Level < 0)
        {
            var lava = LightEmitterCatalog.Get(LightEmitterCatalog.LavaId);
            var padding = (int)Math.Ceiling(lava.RadiusCells);
            var minimumX = Math.Max(0, chunkMinimumX - padding);
            var minimumY = Math.Max(0, chunkMinimumY - padding);
            var maximumX = Math.Min(engine.Map.Width, chunkMaximumX + padding);
            var maximumY = Math.Min(engine.Map.Height, chunkMaximumY + padding);
            for (var y = minimumY; y < maximumY; y++)
            {
                for (var x = minimumX; x < maximumX; x++)
                {
                    var position = new GridPosition(x, y, key.Level);
                    if (engine.World.TryGetFluid(position, out var fluid, out _) &&
                        fluid == CellFluidKind.Lava)
                    {
                        var instanceId = checked((ulong)(y * engine.Map.Width + x) + 1UL);
                        emitters.Add(CreateEmitter(position, lava, instanceId));
                    }
                }
            }
        }

        return emitters
            .OrderBy(emitter => emitter.Snapshot.Handle.DefinitionId.Value, StringComparer.Ordinal)
            .ThenBy(emitter => emitter.Snapshot.Handle.InstanceId)
            .ToArray();
    }

    private static CachedLightEmitter CreateEmitter(
        GridPosition position,
        LightEmitterDefinition definition,
        ulong instanceId,
        CardinalOrientation? facing = null) => new(
        new LightEmitterSnapshot(
            new LightEmitterHandle(definition.Id, instanceId),
            position,
            definition.RadiusCells,
            definition.Intensity,
            facing),
        new Color(definition.Color.Red, definition.Color.Green, definition.Color.Blue));

    private static void PaintCells(
        Image target,
        Image mask,
        PresentationChunkKey key,
        int chunkSize,
        int pixelsPerCell,
        IReadOnlyList<CachedLightEmitter> emitters,
        IReadOnlySet<GridPosition> blockers)
    {
        for (var localY = 0; localY < chunkSize; localY++)
        {
            for (var localX = 0; localX < chunkSize; localX++)
            {
                if (mask.GetPixel(localX, localY).R < 1f)
                {
                    continue;
                }

                var position = new GridPosition(
                    (key.X * chunkSize) + localX,
                    (key.Y * chunkSize) + localY,
                    key.Level);
                var remainingDarkness = 1f;
                var tint = Colors.Black;
                var tintWeight = 0f;
                foreach (var emitter in emitters)
                {
                    var contribution = Math.Clamp(
                        LightOcclusionPolicy.CalculateContribution(
                            emitter.Snapshot,
                            position,
                            blockers),
                        0f,
                        1f);
                    remainingDarkness *= 1f - contribution;
                    tint += emitter.Color * contribution;
                    tintWeight += contribution;
                }

                var illumination = 1f - remainingDarkness;
                if (illumination <= 0f)
                {
                    continue;
                }

                var normalizedTint = tintWeight > 0f ? tint / tintWeight : Colors.White;
                BrightenCell(
                    target,
                    localX,
                    localY,
                    pixelsPerCell,
                    illumination,
                    normalizedTint);
            }
        }
    }

    private static void BrightenCell(
        Image target,
        int cellX,
        int cellY,
        int pixelsPerCell,
        float illumination,
        Color tint)
    {
        var gain = illumination * MaximumBrightnessGain;
        var tintAmount = illumination * WarmTintAmount;
        var minimumX = cellX * pixelsPerCell;
        var minimumY = cellY * pixelsPerCell;
        for (var y = minimumY; y < minimumY + pixelsPerCell; y++)
        {
            for (var x = minimumX; x < minimumX + pixelsPerCell; x++)
            {
                var source = target.GetPixel(x, y);
                if (source.A <= 0f)
                {
                    continue;
                }

                target.SetPixel(x, y, new Color(
                    Math.Clamp((source.R * (1f + gain)) + (tint.R * tintAmount), 0f, 1f),
                    Math.Clamp((source.G * (1f + gain)) + (tint.G * tintAmount), 0f, 1f),
                    Math.Clamp((source.B * (1f + gain)) + (tint.B * tintAmount), 0f, 1f),
                    source.A));
            }
        }
    }

    private static void ApplyExposureMask(Image lighting, Image mask, int pixelsPerCell)
    {
        for (var y = 0; y < lighting.GetHeight(); y++)
        {
            for (var x = 0; x < lighting.GetWidth(); x++)
            {
                if (mask.GetPixel(x / pixelsPerCell, y / pixelsPerCell).R < 1f)
                {
                    lighting.SetPixel(x, y, Colors.Transparent);
                }
            }
        }
    }

    private static bool Intersects(
        GridPosition position,
        float radius,
        int minimumX,
        int minimumY,
        int maximumX,
        int maximumY)
    {
        var closestX = Math.Clamp(position.X, minimumX, maximumX - 1);
        var closestY = Math.Clamp(position.Y, minimumY, maximumY - 1);
        var deltaX = position.X - closestX;
        var deltaY = position.Y - closestY;
        return (deltaX * deltaX) + (deltaY * deltaY) <= radius * radius;
    }

    private readonly record struct CachedLightEmitter(
        LightEmitterSnapshot Snapshot,
        Color Color);
}
