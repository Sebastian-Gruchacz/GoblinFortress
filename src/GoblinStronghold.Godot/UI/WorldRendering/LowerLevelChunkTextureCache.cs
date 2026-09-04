using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Map.Generation;
using GoblinStronghold.Simulation.Presentation;
using System.Diagnostics;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal sealed record LowerLevelChunkTexture(
    PresentationChunkKey Key,
    Texture2D Geometry,
    Texture2D Lighting,
    Texture2D SkyLighting,
    Texture2D ExposureMask,
    int ChunkSize,
    int PixelsPerCell);

internal sealed record LowerLevelOpeningTexture(
    Texture2D Geometry,
    Texture2D Lighting,
    Texture2D SkyLighting,
    Rect2 SourceRegion,
    int Level);

internal readonly record struct LowerLevelChunkRebuildResult(
    int RebuiltChunks,
    double NextEligibleSeconds);

internal sealed class LowerLevelChunkTextureCache : IDisposable
{
    public const int PixelsPerCell = 20;
    public const int MaximumRebuildsPerFrame = 2;

    private readonly Dictionary<PresentationChunkKey, CachedChunk> _chunks = [];
    private readonly Dictionary<(TerrainSprite Sprite, TerrainKind Terrain), Image> _surfaceTiles = [];
    private readonly Dictionary<(RockKind Rock, bool IsOpen, LooseMaterialKind LooseMaterial), Image>
        _caveTiles = [];
    private readonly TimedPresentationOperationCounter _rebuildBatches = new();
    private Image? _terrainAtlas;
    private Image? _caveAtlas;
    private Image? _environmentAtlas;
    private Image? _itemIconAtlas;
    private Image? _treePartAtlas;
    private Image? _treeCrownAtlas;
    private Image? _lavaTile;
    private int _chunkSize = LowerLevelExposureIndex.DefaultChunkSize;
    private long _chunksRebuilt;
    private long _geometryTexturesRebuilt;
    private long _staticLightTexturesRebuilt;

    public (
        TimedPresentationOperationMetrics Timings,
        long Chunks,
        long GeometryTextures,
        long StaticLightTextures) GetMetrics() => (
        _rebuildBatches.Snapshot,
        _chunksRebuilt,
        _geometryTexturesRebuilt,
        _staticLightTexturesRebuilt);

    public void Initialize(
        Texture2D terrainAtlas,
        Texture2D caveAtlas,
        Texture2D environmentAtlas,
        Texture2D itemIconAtlas,
        Texture2D treePartAtlas,
        Texture2D treeCrownAtlas)
    {
        ArgumentNullException.ThrowIfNull(terrainAtlas);
        ArgumentNullException.ThrowIfNull(caveAtlas);
        ArgumentNullException.ThrowIfNull(environmentAtlas);
        ArgumentNullException.ThrowIfNull(itemIconAtlas);
        ArgumentNullException.ThrowIfNull(treePartAtlas);
        ArgumentNullException.ThrowIfNull(treeCrownAtlas);
        ClearTiles();
        _terrainAtlas?.Dispose();
        _caveAtlas?.Dispose();
        _environmentAtlas?.Dispose();
        _itemIconAtlas?.Dispose();
        _treePartAtlas?.Dispose();
        _treeCrownAtlas?.Dispose();
        _terrainAtlas = terrainAtlas.GetImage();
        _caveAtlas = caveAtlas.GetImage();
        _environmentAtlas = environmentAtlas.GetImage();
        _itemIconAtlas = itemIconAtlas.GetImage();
        _treePartAtlas = treePartAtlas.GetImage();
        _treeCrownAtlas = treeCrownAtlas.GetImage();
    }

    public void ResetWorld()
    {
        foreach (var chunk in _chunks.Values)
        {
            chunk.Dispose();
        }
        _chunks.Clear();
        _rebuildBatches.Reset();
        _chunksRebuilt = 0;
        _geometryTexturesRebuilt = 0;
        _staticLightTexturesRebuilt = 0;
    }

    public void ConfigureChunkSize(int chunkSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
        if (_chunkSize == chunkSize)
        {
            return;
        }

        ResetWorld();
        _chunkSize = chunkSize;
    }

    public LowerLevelChunkRebuildResult RebuildVisibleDirty(
        SimulationEngine engine,
        SimulationSnapshot snapshot,
        LowerLevelExposureIndex exposure,
        LowerLevelPresentationCacheState cacheState,
        int activeLevel,
        double currentSeconds,
        double baseIntervalSeconds)
    {
        if (exposure.ChunkSize != _chunkSize)
        {
            throw new ArgumentException(
                "The exposure index and texture cache must use the same chunk size.",
                nameof(exposure));
        }
        if (_terrainAtlas is null || _caveAtlas is null ||
            _environmentAtlas is null || _itemIconAtlas is null ||
            _treePartAtlas is null ||
            _treeCrownAtlas is null)
        {
            throw new InvalidOperationException(
                "Lower-level texture atlases must be initialized before rebuilding chunks.");
        }

        var dirtyCandidates = cacheState.GetVisibleRebuildCandidates();
        var candidates = dirtyCandidates
            .Where(candidate =>
                !_chunks.TryGetValue(candidate.Key, out var cached) ||
                LowerLevelRefreshCadencePolicy.IsRebuildDue(
                    cached.LastRebuildSeconds,
                    currentSeconds,
                    baseIntervalSeconds,
                    activeLevel,
                    candidate.Key.Level))
            .Take(MaximumRebuildsPerFrame)
            .ToArray();
        if (candidates.Length == 0)
        {
            return new LowerLevelChunkRebuildResult(
                0,
                GetNextEligibleSeconds(
                    dirtyCandidates,
                    activeLevel,
                    currentSeconds,
                    baseIntervalSeconds));
        }

        var startedAt = Stopwatch.GetTimestamp();
        var cellsByChunk = exposure.VisibleChunkCells;
        foreach (var candidate in candidates)
        {
            var cells = cellsByChunk.GetValueOrDefault(candidate.Key) ?? [];
            var replacement = BuildChunk(
                engine,
                snapshot,
                candidate.Key,
                cells,
                exposure,
                currentSeconds);
            if (_chunks.Remove(candidate.Key, out var previous))
            {
                previous.Dispose();
            }
            _chunks.Add(candidate.Key, replacement);
            cacheState.MarkRebuilt(candidate.Key);
        }
        _chunksRebuilt = checked(_chunksRebuilt + candidates.Length);
        _geometryTexturesRebuilt = checked(_geometryTexturesRebuilt + candidates.Length);
        _staticLightTexturesRebuilt = checked(
            _staticLightTexturesRebuilt + candidates.Length);
        _rebuildBatches.Record(startedAt);
        return new LowerLevelChunkRebuildResult(
            candidates.Length,
            GetNextEligibleSeconds(
                cacheState.GetVisibleRebuildCandidates(),
                activeLevel,
                currentSeconds,
                baseIntervalSeconds));
    }

    public IReadOnlyList<LowerLevelChunkTexture> GetVisibleTextures(
        LowerLevelExposureIndex exposure) => exposure.VisibleChunks
        .Select(key => _chunks.GetValueOrDefault(key))
        .Where(chunk => chunk is not null)
        .Select(chunk => chunk!.Snapshot)
        .OrderBy(chunk => chunk.Key.Level)
        .ThenBy(chunk => chunk.Key.Y)
        .ThenBy(chunk => chunk.Key.X)
        .ToArray();

    public bool HasGeometryAt(GridPosition position)
    {
        var key = GetChunkKey(position, _chunkSize);
        return _chunks.TryGetValue(key, out var chunk) &&
            chunk.GeometryCells.Contains(position);
    }

    public bool TryGetOpeningTexture(
        GridPosition position,
        out LowerLevelOpeningTexture texture)
    {
        var key = GetChunkKey(position, _chunkSize);
        if (!_chunks.TryGetValue(key, out var chunk) ||
            !chunk.GeometryCells.Contains(position))
        {
            texture = null!;
            return false;
        }

        var localX = position.X - (key.X * _chunkSize);
        var localY = position.Y - (key.Y * _chunkSize);
        texture = new LowerLevelOpeningTexture(
            chunk.Snapshot.Geometry,
            chunk.Snapshot.Lighting,
            chunk.Snapshot.SkyLighting,
            new Rect2(
                localX * PixelsPerCell,
                localY * PixelsPerCell,
                PixelsPerCell,
                PixelsPerCell),
            position.Z);
        return true;
    }

    public void Dispose()
    {
        ResetWorld();
        ClearTiles();
        _terrainAtlas?.Dispose();
        _terrainAtlas = null;
        _caveAtlas?.Dispose();
        _caveAtlas = null;
        _environmentAtlas?.Dispose();
        _environmentAtlas = null;
        _itemIconAtlas?.Dispose();
        _itemIconAtlas = null;
        _treePartAtlas?.Dispose();
        _treePartAtlas = null;
        _treeCrownAtlas?.Dispose();
        _treeCrownAtlas = null;
    }

    private CachedChunk BuildChunk(
        SimulationEngine engine,
        SimulationSnapshot snapshot,
        PresentationChunkKey key,
        IReadOnlyCollection<GridPosition> exposedCells,
        LowerLevelExposureIndex exposure,
        double currentSeconds)
    {
        var chunkSize = exposure.ChunkSize;
        var geometry = Image.CreateEmpty(
            chunkSize * PixelsPerCell,
            chunkSize * PixelsPerCell,
            false,
            Image.Format.Rgba8);
        geometry.Fill(Colors.Transparent);
        var mask = Image.CreateEmpty(chunkSize, chunkSize, false, Image.Format.R8);
        mask.Fill(Colors.Black);
        var bloodByPosition = snapshot.BloodStains
            .Where(stain => stain.Position.Z == key.Level)
            .ToDictionary(stain => stain.Position, stain => stain.Volume);
        var grimeByPosition = snapshot.SurfaceGrime
            .Where(stain => stain.Position.Z == key.Level)
            .ToDictionary(stain => stain.Position, stain => stain.Volume);
        var structuresByPosition = CreateStructureIndex(engine, snapshot, key.Level);
        var plantsByPosition = snapshot.PlantPatches
            .Select(plant => (
                Position: ResolvePlantPosition(engine, plant),
                Plant: plant))
            .Where(item => item.Position.Z == key.Level)
            .ToDictionary(item => item.Position, item => item.Plant);
        var livingTrees = snapshot.WorldObjects
            .Where(worldObject => worldObject.Kind == WorldObjectKind.Tree)
            .Select(engine.World.GetEffectiveWorldObjectAnchor)
            .ToHashSet();
        var geometryCells = new HashSet<GridPosition>();
        foreach (var position in exposedCells)
        {
            var localX = position.X - (key.X * chunkSize);
            var localY = position.Y - (key.Y * chunkSize);
            mask.SetPixel(localX, localY, Colors.White);
            var tile = ResolveGeometryTile(engine, position, livingTrees);
            var structureParts = structuresByPosition.GetValueOrDefault(position) ?? [];
            var hasPlant = plantsByPosition.TryGetValue(position, out var plant);
            if (tile is null && structureParts.Count == 0 && !hasPlant)
            {
                continue;
            }

            var origin = new Vector2I(localX * PixelsPerCell, localY * PixelsPerCell);
            if (tile is not null)
            {
                geometry.BlitRect(
                    tile,
                    new Rect2I(0, 0, PixelsPerCell, PixelsPerCell),
                    origin);
            }
            if (engine.World.TryGetCaveFlora(position, out var caveFlora))
            {
                LowerLevelStaticCaveFloraPainter.PaintCell(
                    geometry,
                    origin,
                    caveFlora);
            }
            if (hasPlant)
            {
                LowerLevelStaticVegetationPainter.PaintCell(
                    geometry,
                    origin,
                    plant,
                    _environmentAtlas!);
            }
            LowerLevelStaticStructurePainter.PaintCell(
                geometry,
                origin,
                structureParts,
                _environmentAtlas!,
                _itemIconAtlas!,
                _treePartAtlas!,
                _treeCrownAtlas!,
                engine.WorldSeed,
                engine.Map.Width);
            LowerLevelStaticContaminationPainter.PaintCell(
                geometry,
                origin,
                position,
                bloodByPosition.GetValueOrDefault(position),
                grimeByPosition.GetValueOrDefault(position));
            geometryCells.Add(position);
        }

        ApplyExposureMask(geometry, mask);
        var lighting = LowerLevelStaticLightPainter.Paint(
            engine,
            key,
            geometry,
            mask,
            chunkSize,
            PixelsPerCell);
        LowerLevelOpeningVignettePainter.Paint(
            lighting,
            key,
            exposure);
        var skyLighting = Image.CreateEmpty(
            lighting.GetWidth(),
            lighting.GetHeight(),
            false,
            Image.Format.Rgba8);
        skyLighting.Fill(Colors.Transparent);
        skyLighting.BlitRect(
            lighting,
            new Rect2I(Vector2I.Zero, lighting.GetSize()),
            Vector2I.Zero);
        ApplySkyExposureMask(engine, key, skyLighting, chunkSize);

        var geometryTexture = ImageTexture.CreateFromImage(geometry);
        var lightingTexture = ImageTexture.CreateFromImage(lighting);
        var skyLightingTexture = ImageTexture.CreateFromImage(skyLighting);
        var maskTexture = ImageTexture.CreateFromImage(mask);
        geometry.Dispose();
        lighting.Dispose();
        skyLighting.Dispose();
        mask.Dispose();
        return new CachedChunk(
            key,
            geometryTexture,
            lightingTexture,
            skyLightingTexture,
            maskTexture,
            chunkSize,
            currentSeconds,
            geometryCells);
    }

    private static void ApplySkyExposureMask(
        SimulationEngine engine,
        PresentationChunkKey key,
        Image image,
        int chunkSize)
    {
        for (var localY = 0; localY < chunkSize; localY++)
        {
            for (var localX = 0; localX < chunkSize; localX++)
            {
                var position = new GridPosition(
                    key.X * chunkSize + localX,
                    key.Y * chunkSize + localY,
                    key.Level);
                if (engine.World.IsOpenToSky(position))
                {
                    continue;
                }

                image.FillRect(
                    new Rect2I(
                        localX * PixelsPerCell,
                        localY * PixelsPerCell,
                        PixelsPerCell,
                        PixelsPerCell),
                    Colors.Transparent);
            }
        }
    }

    private static void ApplyExposureMask(Image geometry, Image mask)
    {
        for (var cellY = 0; cellY < mask.GetHeight(); cellY++)
        {
            for (var cellX = 0; cellX < mask.GetWidth(); cellX++)
            {
                var coverage = mask.GetPixel(cellX, cellY).R;
                if (coverage >= 1f)
                {
                    continue;
                }

                var origin = new Vector2I(cellX * PixelsPerCell, cellY * PixelsPerCell);
                geometry.FillRect(
                    new Rect2I(origin, new Vector2I(PixelsPerCell, PixelsPerCell)),
                    Colors.Transparent);
            }
        }
    }

    private static PresentationChunkKey GetChunkKey(
        GridPosition position,
        int chunkSize) => new(
        position.Z,
        FloorDivide(position.X, chunkSize),
        FloorDivide(position.Y, chunkSize));

    private static int FloorDivide(int value, int divisor)
    {
        var quotient = value / divisor;
        return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
    }

    private double GetNextEligibleSeconds(
        IReadOnlyList<PresentationChunkCacheSnapshot> candidates,
        int activeLevel,
        double currentSeconds,
        double baseIntervalSeconds)
    {
        var next = double.PositiveInfinity;
        foreach (var candidate in candidates)
        {
            if (!_chunks.TryGetValue(candidate.Key, out var cached))
            {
                return currentSeconds;
            }

            next = Math.Min(
                next,
                cached.LastRebuildSeconds +
                LowerLevelRefreshCadencePolicy.GetMinimumIntervalSeconds(
                    baseIntervalSeconds,
                    activeLevel,
                    candidate.Key.Level));
        }
        return next;
    }

    private static IReadOnlyDictionary<GridPosition, List<LowerLevelStaticStructurePart>>
        CreateStructureIndex(
            SimulationEngine engine,
            SimulationSnapshot snapshot,
            int level)
    {
        var result = new Dictionary<GridPosition, List<LowerLevelStaticStructurePart>>();
        foreach (var worldObject in snapshot.WorldObjects)
        {
            var anchor = engine.World.GetEffectiveWorldObjectAnchor(worldObject);
            foreach (var part in worldObject.Parts)
            {
                var position = Add(anchor, part.RelativePosition);
                if (position.Z != level)
                {
                    continue;
                }

                if (!result.TryGetValue(position, out var parts))
                {
                    parts = [];
                    result.Add(position, parts);
                }
                parts.Add(new LowerLevelStaticStructurePart(worldObject, part));
            }

            if (worldObject.Kind == WorldObjectKind.WoodenLadder &&
                level == anchor.Z + 1 &&
                !worldObject.Parts.Any(part => part.RelativePosition.Z == 1))
            {
                var upperOffset = worldObject.Orientation switch
                {
                    CardinalOrientation.North => new GridPosition(0, -1, 1),
                    CardinalOrientation.East => new GridPosition(1, 0, 1),
                    CardinalOrientation.South => new GridPosition(0, 1, 1),
                    CardinalOrientation.West => new GridPosition(-1, 0, 1),
                    _ => default,
                };
                var upper = Add(anchor, upperOffset);
                if (!result.TryGetValue(upper, out var parts))
                {
                    parts = [];
                    result.Add(upper, parts);
                }
                parts.Add(new LowerLevelStaticStructurePart(
                    worldObject,
                    new WorldObjectPartSnapshot(
                        upperOffset,
                        SpatialOccupancyChannel.Fixture,
                        WorldObjectPartKind.Ladder)));
            }
        }
        return result;
    }

    private static GridPosition ResolvePlantPosition(
        SimulationEngine engine,
        PlantPatchSnapshot plant) =>
        PlantPresentationPositionPolicy.Resolve(engine.Map, plant);

    private static GridPosition Add(GridPosition left, GridPosition right) => new(
        checked(left.X + right.X),
        checked(left.Y + right.Y),
        checked(left.Z + right.Z));

    private Image? ResolveGeometryTile(
        SimulationEngine engine,
        GridPosition position,
        IReadOnlySet<GridPosition> livingTrees)
    {
        if (engine.Map.IsTerrainSurfacePosition(position))
        {
            var surface = engine.Map.GetColumnCell(position);
            return engine.World.TryGetFluid(position, out var surfaceFluid, out _)
                ? surfaceFluid == CellFluidKind.Lava
                    ? GetLavaTile()
                    : GetSurfaceTile(
                        TerrainKind.DeepWater,
                        position,
                        livingTrees,
                        engine.Map.Width,
                        engine.Map.Height)
                : GetSurfaceTile(
                    surface.Terrain,
                    position,
                    livingTrees,
                    engine.Map.Width,
                    engine.Map.Height);
        }

        if (position.Z >= 0)
        {
            return null;
        }

        if (!engine.Map.IsCavePosition(position))
        {
            return null;
        }
        if (engine.World.TryGetFluid(position, out var fluid, out _))
        {
            return fluid == CellFluidKind.Lava
                ? GetLavaTile()
                : GetSurfaceTile(
                    TerrainKind.DeepWater,
                    position,
                    livingTrees,
                    engine.Map.Width,
                    engine.Map.Height);
        }

        var cave = engine.Map.GetCaveCell(position);
        return GetCaveTile(
            cave.Rock,
            cave.IsOpen || engine.World.ExcavatedCaveCells.Contains(position),
            cave.LooseMaterial);
    }

    private Image GetSurfaceTile(
        TerrainKind terrain,
        GridPosition position,
        IReadOnlySet<GridPosition> livingTrees,
        int mapWidth,
        int mapHeight)
    {
        var sprite = terrain switch
        {
            TerrainKind.SolidGround when IsForestFloor(position, livingTrees) =>
                IsConiferPatch(position.X, position.Y)
                    ? TerrainSprite.ConiferForestFloor
                    : TerrainSprite.DeciduousForestFloor,
            TerrainKind.SolidGround => TerrainSprite.Meadow,
            TerrainKind.Mud => TerrainSprite.BogGround,
            TerrainKind.Sand => TerrainSprite.BogGround,
            TerrainKind.ShallowWater when IsSwampWater(
                position.X, position.Y, mapWidth, mapHeight) =>
                TerrainSprite.MuddyWaterA,
            TerrainKind.ShallowWater => TerrainSprite.ShallowWaterA,
            TerrainKind.DeepWater => TerrainSprite.DeepWaterA,
            _ => throw new ArgumentOutOfRangeException(nameof(terrain), terrain, null),
        };
        var key = (sprite, terrain);
        if (_surfaceTiles.TryGetValue(key, out var tile))
        {
            return tile;
        }

        tile = ExtractScaledTile(
            _terrainAtlas!,
            TerrainSprites.GetRegionFromImage(_terrainAtlas!, sprite));
        if (terrain == TerrainKind.Sand)
        {
            ApplyCaveShade(tile, new Color(0.72f, 0.58f, 0.34f, 0.62f));
        }
        _surfaceTiles.Add(key, tile);
        return tile;
    }

    private static bool IsSwampWater(int x, int y, int mapWidth, int mapHeight) =>
        x < mapWidth * 0.38f || y > mapHeight * 0.66f;

    private static bool IsForestFloor(
        GridPosition position,
        IReadOnlySet<GridPosition> livingTrees)
    {
        for (var offsetY = -2; offsetY <= 2; offsetY++)
        {
            for (var offsetX = -2; offsetX <= 2; offsetX++)
            {
                if (livingTrees.Contains(new GridPosition(
                        position.X + offsetX,
                        position.Y + offsetY,
                        position.Z)))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool IsConiferPatch(int x, int y) =>
        unchecked((((x / 7) * 73_856_093) ^ ((y / 7) * 19_349_663)) & 3) == 0;

    private Image GetCaveTile(
        RockKind rock,
        bool isOpen,
        LooseMaterialKind looseMaterial = LooseMaterialKind.None)
    {
        var key = (rock, isOpen, looseMaterial);
        if (_caveTiles.TryGetValue(key, out var tile))
        {
            return tile;
        }

        tile = ExtractScaledTile(
            _caveAtlas!,
            CaveSprites.GetFloorRegionFromImage(_caveAtlas!, rock));
        ApplyCaveShade(tile, CaveSprites.GetFloorShade(rock));
        if (!isOpen)
        {
            Darken(tile, 0.48f);
            if (looseMaterial != LooseMaterialKind.None)
            {
                ApplyCaveShade(
                    tile,
                    looseMaterial == LooseMaterialKind.Sand
                        ? new Color(0.70f, 0.54f, 0.30f, 0.72f)
                        : new Color(0.34f, 0.22f, 0.12f, 0.78f));
            }
        }
        _caveTiles.Add(key, tile);
        return tile;
    }

    private Image GetLavaTile()
    {
        if (_lavaTile is not null)
        {
            return _lavaTile;
        }

        _lavaTile = Image.CreateEmpty(
            PixelsPerCell,
            PixelsPerCell,
            false,
            Image.Format.Rgba8);
        _lavaTile.Fill(new Color("6e1608"));
        for (var pixel = 1; pixel < PixelsPerCell - 1; pixel += 3)
        {
            _lavaTile.SetPixel(pixel, (pixel * 2) % PixelsPerCell, new Color("f05a16"));
        }
        return _lavaTile;
    }

    private static Image ExtractScaledTile(Image atlas, Rect2I region)
    {
        var tile = atlas.GetRegion(region);
        tile.Resize(
            PixelsPerCell,
            PixelsPerCell,
            Image.Interpolation.Bilinear);
        tile.Convert(Image.Format.Rgba8);
        return tile;
    }

    private static void ApplyCaveShade(Image image, Color shade)
    {
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var source = image.GetPixel(x, y);
                image.SetPixel(x, y, new Color(
                    Mathf.Lerp(source.R, shade.R, shade.A),
                    Mathf.Lerp(source.G, shade.G, shade.A),
                    Mathf.Lerp(source.B, shade.B, shade.A),
                    source.A));
            }
        }
    }

    private static void Darken(Image image, float amount)
    {
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var source = image.GetPixel(x, y);
                image.SetPixel(x, y, new Color(
                    source.R * amount,
                    source.G * amount,
                    source.B * amount,
                    source.A));
            }
        }
    }

    private void ClearTiles()
    {
        foreach (var tile in _surfaceTiles.Values)
        {
            tile.Dispose();
        }
        _surfaceTiles.Clear();
        foreach (var tile in _caveTiles.Values)
        {
            tile.Dispose();
        }
        _caveTiles.Clear();
        _lavaTile?.Dispose();
        _lavaTile = null;
    }

    private sealed class CachedChunk(
        PresentationChunkKey key,
        ImageTexture geometry,
        ImageTexture lighting,
        ImageTexture skyLighting,
        ImageTexture exposureMask,
        int chunkSize,
        double lastRebuildSeconds,
        HashSet<GridPosition> geometryCells) : IDisposable
    {
        public LowerLevelChunkTexture Snapshot { get; } = new(
            key,
            geometry,
            lighting,
            skyLighting,
            exposureMask,
            chunkSize,
            PixelsPerCell);

        public IReadOnlySet<GridPosition> GeometryCells { get; } = geometryCells;

        public double LastRebuildSeconds { get; } = lastRebuildSeconds;

        public void Dispose()
        {
            geometry.Dispose();
            lighting.Dispose();
            skyLighting.Dispose();
            exposureMask.Dispose();
        }
    }
}
