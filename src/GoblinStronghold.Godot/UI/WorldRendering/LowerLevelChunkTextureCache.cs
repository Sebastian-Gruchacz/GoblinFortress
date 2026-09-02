using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Presentation;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal sealed record LowerLevelChunkTexture(
    PresentationChunkKey Key,
    Texture2D Geometry,
    Texture2D Lighting,
    Texture2D ExposureMask,
    int PixelsPerCell);

internal sealed record LowerLevelOpeningTexture(
    Texture2D Geometry,
    Texture2D Lighting,
    Rect2 SourceRegion,
    int Level);

internal sealed class LowerLevelChunkTextureCache : IDisposable
{
    public const int PixelsPerCell = 10;
    public const int MaximumRebuildsPerFrame = 2;

    private readonly Dictionary<PresentationChunkKey, CachedChunk> _chunks = [];
    private readonly Dictionary<TerrainSprite, Image> _surfaceTiles = [];
    private readonly Dictionary<(RockKind Rock, bool IsOpen), Image> _caveTiles = [];
    private Image? _terrainAtlas;
    private Image? _caveAtlas;
    private Image? _lavaTile;

    public void Initialize(Texture2D terrainAtlas, Texture2D caveAtlas)
    {
        ArgumentNullException.ThrowIfNull(terrainAtlas);
        ArgumentNullException.ThrowIfNull(caveAtlas);
        ClearTiles();
        _terrainAtlas?.Dispose();
        _caveAtlas?.Dispose();
        _terrainAtlas = terrainAtlas.GetImage();
        _caveAtlas = caveAtlas.GetImage();
    }

    public void ResetWorld()
    {
        foreach (var chunk in _chunks.Values)
        {
            chunk.Dispose();
        }
        _chunks.Clear();
    }

    public int RebuildVisibleDirty(
        SimulationEngine engine,
        SimulationSnapshot snapshot,
        LowerLevelExposureIndex exposure,
        LowerLevelPresentationCacheState cacheState)
    {
        if (_terrainAtlas is null || _caveAtlas is null)
        {
            throw new InvalidOperationException(
                "Lower-level texture atlases must be initialized before rebuilding chunks.");
        }

        var candidates = cacheState.GetVisibleRebuildCandidates()
            .Take(MaximumRebuildsPerFrame)
            .ToArray();
        if (candidates.Length == 0)
        {
            return 0;
        }

        var cellsByChunk = exposure.VisibleChunkCells;
        foreach (var candidate in candidates)
        {
            var cells = cellsByChunk.GetValueOrDefault(candidate.Key) ?? [];
            var replacement = BuildChunk(
                engine,
                snapshot,
                candidate.Key,
                cells,
                exposure.ChunkSize);
            if (_chunks.Remove(candidate.Key, out var previous))
            {
                previous.Dispose();
            }
            _chunks.Add(candidate.Key, replacement);
            cacheState.MarkRebuilt(candidate.Key);
        }
        return candidates.Length;
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
        var key = GetChunkKey(position);
        return _chunks.TryGetValue(key, out var chunk) &&
            chunk.GeometryCells.Contains(position);
    }

    public bool TryGetOpeningTexture(
        GridPosition position,
        out LowerLevelOpeningTexture texture)
    {
        var key = GetChunkKey(position);
        if (!_chunks.TryGetValue(key, out var chunk) ||
            !chunk.GeometryCells.Contains(position))
        {
            texture = null!;
            return false;
        }

        var localX = position.X - (key.X * LowerLevelExposureIndex.DefaultChunkSize);
        var localY = position.Y - (key.Y * LowerLevelExposureIndex.DefaultChunkSize);
        texture = new LowerLevelOpeningTexture(
            chunk.Snapshot.Geometry,
            chunk.Snapshot.Lighting,
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
    }

    private CachedChunk BuildChunk(
        SimulationEngine engine,
        SimulationSnapshot snapshot,
        PresentationChunkKey key,
        IReadOnlyCollection<GridPosition> exposedCells,
        int chunkSize)
    {
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
        var geometryCells = new HashSet<GridPosition>();
        foreach (var position in exposedCells)
        {
            var localX = position.X - (key.X * chunkSize);
            var localY = position.Y - (key.Y * chunkSize);
            mask.SetPixel(localX, localY, Colors.White);
            var tile = ResolveGeometryTile(engine, position);
            if (tile is null)
            {
                continue;
            }

            geometry.BlitRect(
                tile,
                new Rect2I(0, 0, PixelsPerCell, PixelsPerCell),
                new Vector2I(localX * PixelsPerCell, localY * PixelsPerCell));
            LowerLevelStaticStructurePainter.PaintCell(
                geometry,
                new Vector2I(localX * PixelsPerCell, localY * PixelsPerCell),
                position,
                engine.World.GetWorldObjectsAt(position));
            LowerLevelStaticContaminationPainter.PaintCell(
                geometry,
                new Vector2I(localX * PixelsPerCell, localY * PixelsPerCell),
                position,
                bloodByPosition.GetValueOrDefault(position),
                grimeByPosition.GetValueOrDefault(position));
            geometryCells.Add(position);
        }

        ApplyExposureMask(geometry, mask);
        var lighting = LowerLevelStaticLightPainter.Paint(
            engine,
            key,
            mask,
            chunkSize,
            PixelsPerCell);

        var geometryTexture = ImageTexture.CreateFromImage(geometry);
        var lightingTexture = ImageTexture.CreateFromImage(lighting);
        var maskTexture = ImageTexture.CreateFromImage(mask);
        geometry.Dispose();
        lighting.Dispose();
        mask.Dispose();
        return new CachedChunk(
            key,
            geometryTexture,
            lightingTexture,
            maskTexture,
            geometryCells);
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

    private static PresentationChunkKey GetChunkKey(GridPosition position) => new(
        position.Z,
        FloorDivide(position.X, LowerLevelExposureIndex.DefaultChunkSize),
        FloorDivide(position.Y, LowerLevelExposureIndex.DefaultChunkSize));

    private static int FloorDivide(int value, int divisor)
    {
        var quotient = value / divisor;
        return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
    }

    private Image? ResolveGeometryTile(SimulationEngine engine, GridPosition position)
    {
        if (position.Z >= 0)
        {
            var cell = engine.Map.GetColumnCell(position);
            return cell.SurfaceLevel == position.Z
                ? GetSurfaceTile(cell.Terrain)
                : null;
        }

        if (!engine.Map.IsCavePosition(position))
        {
            return null;
        }
        if (engine.World.TryGetFluid(position, out var fluid, out _))
        {
            return fluid == CellFluidKind.Lava
                ? GetLavaTile()
                : GetSurfaceTile(TerrainKind.DeepWater);
        }

        var cave = engine.Map.GetCaveCell(position);
        return GetCaveTile(cave.Rock, cave.IsOpen ||
            engine.World.ExcavatedCaveCells.Contains(position));
    }

    private Image GetSurfaceTile(TerrainKind terrain)
    {
        var sprite = terrain switch
        {
            TerrainKind.SolidGround => TerrainSprite.Meadow,
            TerrainKind.Mud => TerrainSprite.BogGround,
            TerrainKind.ShallowWater => TerrainSprite.ShallowWaterA,
            TerrainKind.DeepWater => TerrainSprite.DeepWaterA,
            _ => throw new ArgumentOutOfRangeException(nameof(terrain), terrain, null),
        };
        if (_surfaceTiles.TryGetValue(sprite, out var tile))
        {
            return tile;
        }

        tile = ExtractScaledTile(
            _terrainAtlas!,
            TerrainSprites.GetRegionFromImage(_terrainAtlas!, sprite));
        _surfaceTiles.Add(sprite, tile);
        return tile;
    }

    private Image GetCaveTile(RockKind rock, bool isOpen)
    {
        var key = (rock, isOpen);
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
        ImageTexture exposureMask,
        HashSet<GridPosition> geometryCells) : IDisposable
    {
        public LowerLevelChunkTexture Snapshot { get; } = new(
            key,
            geometry,
            lighting,
            exposureMask,
            PixelsPerCell);

        public IReadOnlySet<GridPosition> GeometryCells { get; } = geometryCells;

        public void Dispose()
        {
            geometry.Dispose();
            lighting.Dispose();
            exposureMask.Dispose();
        }
    }
}
