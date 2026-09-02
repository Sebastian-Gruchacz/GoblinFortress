using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Lighting;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Presentation;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal static class LowerLevelStaticLightPainter
{
    private const float MaximumLayerAlpha = 0.58f;

    public static Image Paint(
        SimulationEngine engine,
        PresentationChunkKey key,
        Image exposureMask,
        int chunkSize,
        int pixelsPerCell)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(exposureMask);

        var image = Image.CreateEmpty(
            chunkSize * pixelsPerCell,
            chunkSize * pixelsPerCell,
            false,
            Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        foreach (var emitter in CollectEmitters(engine, key, chunkSize))
        {
            PaintEmitter(image, key, chunkSize, pixelsPerCell, emitter);
        }

        ApplyExposureMask(image, exposureMask, pixelsPerCell);
        return image;
    }

    private static IReadOnlyList<CachedLightEmitter> CollectEmitters(
        SimulationEngine engine,
        PresentationChunkKey key,
        int chunkSize)
    {
        var emitters = new List<CachedLightEmitter>();
        var chunkMinimumX = key.X * chunkSize;
        var chunkMinimumY = key.Y * chunkSize;
        var chunkMaximumX = chunkMinimumX + chunkSize;
        var chunkMaximumY = chunkMinimumY + chunkSize;
        foreach (var worldObject in engine.World.CreateWorldObjectSnapshot())
        {
            if (worldObject.Anchor.Z != key.Level ||
                !LightEmitterCatalog.TryGet(worldObject.Kind, out var definition) ||
                definition.Activation != LightEmitterActivation.Always ||
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

            emitters.Add(new CachedLightEmitter(
                worldObject.Anchor,
                definition,
                worldObject.Id.Value));
        }

        if (key.Level >= 0)
        {
            return emitters;
        }

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
                    emitters.Add(new CachedLightEmitter(position, lava, instanceId));
                }
            }
        }

        return emitters
            .OrderBy(emitter => emitter.Definition.Id.Value, StringComparer.Ordinal)
            .ThenBy(emitter => emitter.InstanceId)
            .ToArray();
    }

    private static void PaintEmitter(
        Image target,
        PresentationChunkKey key,
        int chunkSize,
        int pixelsPerCell,
        CachedLightEmitter emitter)
    {
        var localCenterX =
            (emitter.Position.X - (key.X * chunkSize) + 0.5f) * pixelsPerCell;
        var localCenterY =
            (emitter.Position.Y - (key.Y * chunkSize) + 0.5f) * pixelsPerCell;
        var radiusPixels = emitter.Definition.RadiusCells * pixelsPerCell;
        var minimumX = Math.Max(0, (int)Math.Floor(localCenterX - radiusPixels));
        var minimumY = Math.Max(0, (int)Math.Floor(localCenterY - radiusPixels));
        var maximumX = Math.Min(
            target.GetWidth() - 1,
            (int)Math.Ceiling(localCenterX + radiusPixels));
        var maximumY = Math.Min(
            target.GetHeight() - 1,
            (int)Math.Ceiling(localCenterY + radiusPixels));
        var sourceColor = new Color(
            emitter.Definition.Color.Red,
            emitter.Definition.Color.Green,
            emitter.Definition.Color.Blue);
        for (var y = minimumY; y <= maximumY; y++)
        {
            for (var x = minimumX; x <= maximumX; x++)
            {
                var deltaX = (x + 0.5f) - localCenterX;
                var deltaY = (y + 0.5f) - localCenterY;
                var distance = MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
                if (distance >= radiusPixels)
                {
                    continue;
                }

                var falloff = 1f - (distance / radiusPixels);
                var contribution = Math.Min(
                    MaximumLayerAlpha,
                    emitter.Definition.Intensity * falloff * falloff * MaximumLayerAlpha);
                BlendLight(target, x, y, sourceColor, contribution);
            }
        }
    }

    private static void BlendLight(
        Image target,
        int x,
        int y,
        Color source,
        float contribution)
    {
        var existing = target.GetPixel(x, y);
        var combinedAlpha = 1f - ((1f - existing.A) * (1f - contribution));
        if (combinedAlpha <= 0f)
        {
            return;
        }

        var sourceWeight = contribution / combinedAlpha;
        target.SetPixel(x, y, new Color(
            Mathf.Lerp(existing.R, source.R, sourceWeight),
            Mathf.Lerp(existing.G, source.G, sourceWeight),
            Mathf.Lerp(existing.B, source.B, sourceWeight),
            combinedAlpha));
    }

    private static void ApplyExposureMask(
        Image lighting,
        Image mask,
        int pixelsPerCell)
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
        GridPosition Position,
        LightEmitterDefinition Definition,
        ulong InstanceId);
}
