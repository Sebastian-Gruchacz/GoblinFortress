using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.GodotClient;

public partial class WorldView : Node2D
{
    private const float TileSize = 20f;
    private const double WaterAnimationCycleSeconds = 4.0;
    private const double WaterAnimationRedrawSeconds = 1d / 20d;
    private const double CombatEffectDurationSeconds = 0.42;
    private const int MaximumCombatEffects = 32;
    private const int CombatAudioPlayerCount = 4;
    private readonly Dictionary<EntityId, Vector2> _visualActorPositions = [];
    private readonly Dictionary<EntityId, Vector2> _targetActorPositions = [];
    private readonly Dictionary<ulong, Vector2> _visualAnimalPositions = [];
    private readonly Dictionary<ulong, Vector2> _targetAnimalPositions = [];
    private SimulationEngine _engine = null!;
    private SimulationSnapshot _snapshot = null!;
    private HashSet<EntityId> _selectedActorIds = [];
    private int _simulationSpeed = 1;
    private double _secondsPerTick = 0.1;
    private IReadOnlyList<GridPosition> _constructionPreview = [];
    private bool _constructionPreviewValid;
    private WorkDesignationKind _workPreviewKind;
    private IReadOnlyList<GridPosition> _workPreview = [];
    private (GridPosition Start, GridPosition End)? _workAreaPreview;
    private GridPosition? _raidTargetPreview;
    private int _raidTargetRadius;
    private Texture2D _iconAtlas = null!;
    private Texture2D _itemIconAtlas = null!;
    private Texture2D? _foodIconAtlas;
    private Texture2D _environmentAtlas = null!;
    private Texture2D _treePartAtlas = null!;
    private Texture2D _treeCrownAtlas = null!;
    private Texture2D _terrainAtlas = null!;
    private Texture2D _terrainTransitionAtlas = null!;
    private Texture2D _caveAtlas = null!;
    private Texture2D _caveWallAtlas = null!;
    private Texture2D _humanStructureAtlas = null!;
    private Texture2D _walkwayAtlas = null!;
    private Texture2D _structureWallAtlas = null!;
    private Texture2D _bloodAtlas = null!;
    private Texture2D _undergroundFaunaAtlas = null!;
    private Texture2D _mineralDepositAtlas = null!;
    private int _visibleLevel;
    private double _waterAnimationElapsed;
    private double _waterAnimationRedrawElapsed;
    private ulong _snapshotTopologyVersion;
    private readonly Dictionary<int, (ulong TopologyVersion, HashSet<GridPosition> Solids)>
        _cachedCaveSolids = [];
    private readonly Dictionary<int, StructureRenderCache> _structureRenderCaches = [];
    private readonly MaterialPaletteTextureCache _materialPaletteTextures = new();
    private readonly List<CombatEffect> _combatEffects = [];
    private readonly AudioStreamPlayer[] _combatAudioPlayers = new AudioStreamPlayer[CombatAudioPlayerCount];
    private AudioStreamWav _stoneHitSound = null!;
    private AudioStreamWav _meleeHitSound = null!;
    private int _nextCombatAudioPlayer;

    public int VisibleLevel => _visibleLevel;

    public Vector2 WorldSize => _engine is null
        ? Vector2.Zero
        : new Vector2(_engine.Map.Width * TileSize, _engine.Map.Height * TileSize);

    public override void _Ready()
    {
        _iconAtlas = UiIcons.LoadAtlas();
        _itemIconAtlas = ItemIcons.LoadAtlas();
        _foodIconAtlas = ResourceThumbnails.TryLoadFoodAtlas();
        _environmentAtlas = EnvironmentSprites.LoadAtlas();
        _treePartAtlas = TreePartSprites.LoadAtlas();
        _treeCrownAtlas = TreeCrownSprites.LoadAtlas();
        _terrainAtlas = TerrainSprites.LoadAtlas();
        _terrainTransitionAtlas = TerrainTransitionSprites.LoadAtlas();
        _caveAtlas = CaveSprites.LoadAtlas();
        _caveWallAtlas = CaveSprites.LoadWallAtlas();
        _humanStructureAtlas = HumanStructureSprites.LoadAtlas();
        _walkwayAtlas = WalkwaySprites.LoadAtlas();
        _structureWallAtlas = StructureWallSprites.LoadAtlas();
        _bloodAtlas = BloodSprites.LoadAtlas();
        _undergroundFaunaAtlas = UndergroundSprites.LoadFaunaAtlas();
        _mineralDepositAtlas = UndergroundSprites.LoadMineralAtlas();
        _stoneHitSound = CreateCombatSound(720f, 0.075f, 0.22f);
        _meleeHitSound = CreateCombatSound(145f, 0.11f, 0.38f);
        for (var index = 0; index < _combatAudioPlayers.Length; index++)
        {
            var player = new AudioStreamPlayer
            {
                Name = $"CombatAudio{index + 1}",
                VolumeDb = -13f,
            };
            AddChild(player);
            _combatAudioPlayers[index] = player;
        }
    }

    public override void _ExitTree() => _materialPaletteTextures.Dispose();

    public void SetWorld(SimulationEngine engine)
    {
        _visualActorPositions.Clear();
        _targetActorPositions.Clear();
        _visualAnimalPositions.Clear();
        _targetAnimalPositions.Clear();
        _combatEffects.Clear();
        _snapshotTopologyVersion = 0;
        _cachedCaveSolids.Clear();
        _structureRenderCaches.Clear();
        _engine = engine;
        Refresh(engine.CreatePresentationSnapshot());
    }

    public void Refresh(SimulationSnapshot snapshot)
    {
        _snapshot = snapshot;
        _snapshotTopologyVersion = _engine.World.TopologyVersion;
        SynchronizeActorPositions();
        SynchronizeAnimalPositions();
        QueueRedraw();
    }

    public void SetSimulationSpeed(int speed, double secondsPerTick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(speed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(secondsPerTick);
        _simulationSpeed = speed;
        _secondsPerTick = secondsPerTick;
    }

    public void SetVisibleLevel(int level)
    {
        _visibleLevel = level;
        QueueRedraw();
    }

    public void SetSelectedActors(IEnumerable<EntityId> actorIds)
    {
        _selectedActorIds = actorIds.ToHashSet();
        QueueRedraw();
    }

    public void SetConstructionPreview(
        IReadOnlyList<GridPosition> cells,
        bool isValid = false)
    {
        _constructionPreview = cells;
        _constructionPreviewValid = isValid;
        QueueRedraw();
    }

    public void SetWorkPreview(WorkDesignationKind kind, IReadOnlyList<GridPosition> cells)
    {
        _workPreviewKind = kind;
        _workPreview = cells;
        QueueRedraw();
    }

    public void SetWorkAreaPreview(GridPosition? start, GridPosition? end)
    {
        _workAreaPreview = start is { } first && end is { } last
            ? (first, last)
            : null;
        QueueRedraw();
    }

    public void SetRaidTargetPreview(GridPosition? center, int radius)
    {
        _raidTargetPreview = center;
        _raidTargetRadius = radius;
        QueueRedraw();
    }

    public void ShowCombatEvents(
        IReadOnlyList<SimulationEvent> events,
        SimulationSnapshot snapshot)
    {
        var soundCount = 0;
        foreach (var simulationEvent in events.Where(item => item.Kind is
                     SimulationEventKind.HumanGuardHitGoblin or
                     SimulationEventKind.GoblinHitHumanGuard or
                     SimulationEventKind.GoblinHitHumanCivilian))
        {
            if (!TryGetCombatPositions(simulationEvent, snapshot, out var from, out var to,
                    out var level, out var ranged))
            {
                continue;
            }

            if (_combatEffects.Count >= MaximumCombatEffects)
            {
                _combatEffects.RemoveAt(0);
            }
            _combatEffects.Add(new CombatEffect(from, to, level, ranged));
            if (level == _visibleLevel && soundCount++ < 3)
            {
                PlayCombatSound(ranged ? _stoneHitSound : _meleeHitSound,
                    simulationEvent.Sequence);
            }
        }

        if (_combatEffects.Count > 0)
        {
            QueueRedraw();
        }
    }

    public GridPosition WorldToCell(Vector2 position) => new(
        Mathf.FloorToInt(position.X / TileSize),
        Mathf.FloorToInt(position.Y / TileSize));

    public Vector2 CellToWorld(GridPosition position) => CellCenter(position);

    public override void _Process(double delta)
    {
        if (_engine is null)
        {
            return;
        }

        _waterAnimationElapsed = (_waterAnimationElapsed + delta) % WaterAnimationCycleSeconds;
        var combatEffectsChanged = false;
        for (var index = _combatEffects.Count - 1; index >= 0; index--)
        {
            _combatEffects[index].ElapsedSeconds += delta;
            combatEffectsChanged = true;
            if (_combatEffects[index].ElapsedSeconds >= CombatEffectDurationSeconds)
            {
                _combatEffects.RemoveAt(index);
            }
        }
        _waterAnimationRedrawElapsed += delta;
        if (_waterAnimationRedrawElapsed >= WaterAnimationRedrawSeconds)
        {
            _waterAnimationRedrawElapsed %= WaterAnimationRedrawSeconds;
            if (HasVisibleAnimatedWater())
            {
                QueueRedraw();
            }
        }

        if (_simulationSpeed == 0 ||
            (_visualActorPositions.Count == 0 && _visualAnimalPositions.Count == 0))
        {
            if (combatEffectsChanged)
            {
                QueueRedraw();
            }
            return;
        }

        var movementDuration = _engine.Definitions.ActorMovementIntervalTicks *
            _secondsPerTick / _simulationSpeed;
        var maximumDistance = (float)(TileSize * delta / movementDuration);
        var changed = false;
        foreach (var id in _visualActorPositions.Keys.ToArray())
        {
            var current = _visualActorPositions[id];
            var target = _targetActorPositions[id];
            var next = current.MoveToward(target, maximumDistance);
            if (!next.IsEqualApprox(current))
            {
                _visualActorPositions[id] = next;
                changed = true;
            }
        }

        var animalMovementDuration = SimulationEngine.AnimalUpdateIntervalTicks *
            _secondsPerTick / _simulationSpeed;
        var maximumAnimalDistance = (float)(TileSize * delta / animalMovementDuration);
        foreach (var id in _visualAnimalPositions.Keys.ToArray())
        {
            var current = _visualAnimalPositions[id];
            var target = _targetAnimalPositions[id];
            var next = current.MoveToward(target, maximumAnimalDistance);
            if (!next.IsEqualApprox(current))
            {
                _visualAnimalPositions[id] = next;
                changed = true;
            }
        }

        if (changed || combatEffectsChanged)
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_engine is null)
        {
            return;
        }

        DrawTerrain();
        if (_visibleLevel >= 0)
        {
            DrawPlants();
            DrawHumanFields();
        }

        DrawStructures();
        DrawBloodStains();
        if (_visibleLevel >= 0)
        {
            DrawHumanCohorts();
        }
        DrawStorageAreas();
        DrawStorageZones();
        DrawConstructionSites();
        DrawCraftingOrders();
        DrawItems();
        DrawCorpses();
        DrawJobTargets();
        DrawGoblinBuds();
        DrawAnimals();
        DrawActors();
        DrawCombatEffects();
        DrawNightLighting();
        DrawFog();
        DrawWorkDesignations();
        DrawWorkAreaPreview();
        DrawWorkPreview();
        DrawOrderedDestination();
        DrawConstructionPreview();
        DrawRaidTargetPreview();
    }

    private void DrawRaidTargetPreview()
    {
        if (_raidTargetPreview is not { } center || center.Z != _visibleLevel)
        {
            return;
        }

        var worldCenter = CellCenter(center);
        var radius = (_raidTargetRadius + 0.5f) * TileSize;
        DrawCircle(worldCenter, radius, new Color(0.85f, 0.12f, 0.08f, 0.12f));
        DrawArc(worldCenter, radius, 0f, Mathf.Tau, 64,
            new Color(1f, 0.25f, 0.12f, 0.95f), 2.5f, true);
        DrawLine(worldCenter - new Vector2(8f, 0f), worldCenter + new Vector2(8f, 0f),
            new Color(1f, 0.75f, 0.25f), 2f);
        DrawLine(worldCenter - new Vector2(0f, 8f), worldCenter + new Vector2(0f, 8f),
            new Color(1f, 0.75f, 0.25f), 2f);
    }

    private void DrawTerrain()
    {
        if (_visibleLevel < 0)
        {
            DrawCaveTerrain();
            return;
        }

        var livingTrees = _snapshot.WorldObjects
            .Where(worldObject => worldObject.Kind == WorldObjectKind.Tree)
            .Select(worldObject => worldObject.Anchor)
            .ToHashSet();
        var caveMouths = _engine.World.CreateVerticalPassageSnapshot()
            .Where(passage => passage.Upper.Z == 0)
            .Select(passage => passage.Upper)
            .ToHashSet();
        var bounds = GetVisibleCellBounds();
        for (var y = bounds.MinimumY; y < bounds.MaximumY; y++)
        {
            for (var x = bounds.MinimumX; x < bounds.MaximumX; x++)
            {
                var position = new GridPosition(x, y, _visibleLevel);
                var cell = _engine.Map.GetColumnCell(position);
                var terrainRampIntact = _engine.World.IsTerrainRampIntact(
                    new GridPosition(x, y, cell.SurfaceLevel));
                if (cell.SurfaceLevel == _visibleLevel)
                {
                    if (cell.Terrain is TerrainKind.ShallowWater or TerrainKind.DeepWater)
                    {
                        DrawWaterTile(x, y, cell.Terrain);
                        continue;
                    }

                    var sprite = ResolveTerrainSprite(x, y, cell.Terrain, livingTrees);
                    var useGeneratedTransition = terrainRampIntact &&
                        TerrainTransitionSprites.Supports(sprite);
                    DrawTextureRectRegion(
                        useGeneratedTransition ? _terrainTransitionAtlas : _terrainAtlas,
                        CellRect(x, y),
                        useGeneratedTransition
                            ? TerrainTransitionSprites.GetRegion(
                                _terrainTransitionAtlas, sprite, cell.RampDirection)
                            : TerrainSprites.GetRegion(_terrainAtlas, sprite));
                    DrawTerrainRelief(x, y, cell, drawSlopeOverlay: !useGeneratedTransition);
                    if (terrainRampIntact)
                    {
                        DrawTerrainRampSteps(CellRect(x, y), cell.RampDirection);
                    }
                    DrawCaveMouth(x, y, caveMouths);
                    continue;
                }

                if (cell.SurfaceLevel > _visibleLevel)
                {
                    DrawHillInterior(position);
                    continue;
                }

                DrawLowerTerrainSurface(x, y, cell, livingTrees);
            }
        }
    }

    private void DrawLowerTerrainSurface(
        int x,
        int y,
        MapCell cell,
        HashSet<GridPosition> livingTrees)
    {
        var rect = CellRect(x, y);
        var terrainRampIntact = _engine.World.IsTerrainRampIntact(
            new GridPosition(x, y, cell.SurfaceLevel));
        if (cell.Terrain is TerrainKind.ShallowWater or TerrainKind.DeepWater)
        {
            DrawWaterTile(x, y, cell.Terrain);
        }
        else
        {
            var sprite = ResolveTerrainSprite(x, y, cell.Terrain, livingTrees);
            var useGeneratedTransition = terrainRampIntact &&
                TerrainTransitionSprites.Supports(sprite);
            DrawTextureRectRegion(
                useGeneratedTransition ? _terrainTransitionAtlas : _terrainAtlas,
                rect,
                useGeneratedTransition
                    ? TerrainTransitionSprites.GetRegion(
                        _terrainTransitionAtlas, sprite, cell.RampDirection)
                    : TerrainSprites.GetRegion(_terrainAtlas, sprite));
            DrawTerrainRelief(x, y, cell, drawSlopeOverlay: !useGeneratedTransition);
        }

        var levelDifference = _visibleLevel - cell.SurfaceLevel;
        var darkness = levelDifference == 1 && terrainRampIntact
            ? 0.44f
            : Math.Min(0.84f, 0.68f + ((levelDifference - 1) * 0.08f));
        DrawRect(rect, new Color(0.008f, 0.014f, 0.016f, darkness));
        if (terrainRampIntact)
        {
            DrawTerrainRampSteps(rect, cell.RampDirection, 0.82f);
        }
    }

    private void DrawBloodStains()
    {
        foreach (var stain in _snapshot.BloodStains.Where(stain =>
                     stain.Position.Z == _visibleLevel &&
                     _snapshot.GetVisibility(stain.Position, _engine.Map.Width).IsDiscovered()))
        {
            var variant = unchecked(
                (stain.Position.X * 73_856_093) ^
                (stain.Position.Y * 19_349_663) ^
                (stain.Position.Z * 83_492_791));
            var alpha = stain.Volume <= 3
                ? 0.58f
                : stain.Surface == BloodSurfaceKind.ConstructedFloor
                    ? 0.88f
                    : Math.Clamp(0.28f + stain.Volume / 80f, 0.32f, 0.86f);
            var rect = CellRect(stain.Position.X, stain.Position.Y);
            var scale = stain.Volume switch
            {
                <= 3 => 0.42f,
                <= 8 => 0.68f,
                _ => 0.96f,
            };
            var size = rect.Size * scale;
            var destination = new Rect2(rect.GetCenter() - size / 2f, size);
            DrawTextureRectRegion(
                _bloodAtlas,
                destination,
                BloodSprites.GetRegion(_bloodAtlas, stain.Volume, variant),
                new Color(1f, 1f, 1f, alpha));
        }
    }

    private void DrawHillInterior(GridPosition position)
    {
        var rect = CellRect(position.X, position.Y);
        if (!_engine.Map.IsHillMassPosition(position))
        {
            DrawRect(rect, new Color("17130f"));
            return;
        }

        var rock = _engine.Map.GetHillMassCell(position).Rock;
        if (!_engine.World.IsSolidHillRock(position))
        {
            DrawTextureRectRegion(
                _caveAtlas,
                rect,
                CaveSprites.GetFloorRegion(_caveAtlas, rock));
            DrawRect(rect, CaveSprites.GetFloorShade(rock));
            return;
        }

        var openNeighborMask = GetOpenHillNeighborMask(position);
        if (openNeighborMask != 0)
        {
            DrawTextureRectRegion(
                _caveWallAtlas,
                rect,
                CaveSprites.GetWallRegion(_caveWallAtlas, rock, openNeighborMask));
            DrawHillInnerCorners(position, rock);
        }
        else
        {
            var rockTint = rock == RockKind.Sandstone
                ? new Color("19140f")
                : new Color("101216");
            DrawRect(rect, rockTint);
        }
    }

    private void DrawHillInnerCorners(GridPosition position, RockKind rock)
    {
        var corners = CaveWallTopology.GetInnerOpenCorners(position, IsOpenHillCell);
        foreach (var corner in new[]
                 {
                     CaveInnerCorner.NorthWest,
                     CaveInnerCorner.NorthEast,
                     CaveInnerCorner.SouthEast,
                     CaveInnerCorner.SouthWest,
                 })
        {
            if ((corners & corner) == 0)
            {
                continue;
            }

            DrawTextureRectRegion(
                _caveWallAtlas,
                CellRect(position.X, position.Y),
                CaveSprites.GetInnerCornerRegion(_caveWallAtlas, rock, corner));
        }
    }

    private bool IsOpenHillCell(GridPosition position) =>
        _engine.Map.IsColumnWithin(position) &&
        !_engine.World.IsSolidHillRock(position);

    private int GetOpenHillNeighborMask(GridPosition position)
    {
        ReadOnlySpan<(int X, int Y, int Bit)> offsets =
            [(0, -1, 1), (1, 0, 2), (0, 1, 4), (-1, 0, 8)];
        var mask = 0;
        foreach (var offset in offsets)
        {
            if (IsOpenHillCell(new GridPosition(
                    position.X + offset.X,
                    position.Y + offset.Y,
                    position.Z)))
            {
                mask |= offset.Bit;
            }
        }

        return mask;
    }

    private void DrawCaveMouth(int x, int y, ISet<GridPosition> caveMouths)
    {
        var position = new GridPosition(x, y);
        if (!caveMouths.Contains(position))
        {
            return;
        }

        var center = CellCenter(position);
        DrawCircle(center + new Vector2(1f, 1.5f), 7.5f, new Color(0.025f, 0.03f, 0.028f, 0.92f));
        DrawArc(center, 7.5f, Mathf.Pi, Mathf.Tau, 16, new Color("8b7654"), 2f);
        DrawLine(center + new Vector2(-5f, 4f), center + new Vector2(5f, 4f), new Color("2b241b"), 1.5f);
    }

    private void DrawCaveTerrain()
    {
        var passagePositions = _engine.World.CreateVerticalPassageSnapshot()
            .SelectMany(passage => new[] { passage.Upper, passage.Lower })
            .ToHashSet();
        var bounds = GetVisibleCellBounds();
        for (var y = bounds.MinimumY; y < bounds.MaximumY; y++)
        {
            for (var x = bounds.MinimumX; x < bounds.MaximumX; x++)
            {
                var position = new GridPosition(x, y, _visibleLevel);
                if (!_engine.Map.IsCavePosition(position))
                {
                    DrawRect(CellRect(x, y), new Color("080b0d"));
                    continue;
                }

                if (_engine.World.TryGetFluid(position, out var fluid, out var depthLevels))
                {
                    if (fluid == CellFluidKind.Lava)
                    {
                        DrawLavaTile(x, y);
                    }
                    else
                    {
                        DrawWaterTile(x, y, TerrainKind.DeepWater);
                        DrawRect(
                            CellRect(x, y),
                            new Color(0.015f, 0.035f, 0.045f,
                                Math.Min(0.34f, depthLevels * 0.17f)));
                    }
                    continue;
                }

                var cell = _engine.Map.GetCaveCell(position);
                if (cell.IsOpen || _engine.World.ExcavatedCaveCells.Contains(position))
                {
                    DrawTextureRectRegion(
                        _caveAtlas,
                        CellRect(x, y),
                        CaveSprites.GetFloorRegion(_caveAtlas, cell.Rock));
                    DrawRect(CellRect(x, y), CaveSprites.GetFloorShade(cell.Rock));
                    if (passagePositions.Contains(position))
                    {
                        DrawCavePassage(position);
                    }

                    continue;
                }

                var openNeighborMask = GetOpenCaveNeighborMask(position);
                if (openNeighborMask != 0)
                {
                    DrawTextureRectRegion(
                        _caveWallAtlas,
                        CellRect(x, y),
                        CaveSprites.GetWallRegion(
                            _caveWallAtlas,
                            cell.Rock,
                            openNeighborMask));
                }
                else
                {
                    var rockTint = cell.Rock switch
                    {
                        RockKind.Sandstone => new Color("19140f"),
                        RockKind.Basalt => new Color("0b090a"),
                        RockKind.Obsidian => new Color("100817"),
                        _ => new Color("101216"),
                    };
                    DrawRect(CellRect(x, y), rockTint);
                }
                DrawCaveInnerCorners(position, cell.Rock);
                if (openNeighborMask != 0 && cell.Deposit != MineralDepositKind.None)
                {
                    DrawMineralDeposit(position, cell.Deposit);
                }
            }
        }
    }

    private void DrawMineralDeposit(GridPosition position, MineralDepositKind deposit)
    {
        var rect = CellRect(position.X, position.Y);
        var palette = deposit switch
        {
            MineralDepositKind.Coal => MaterialPaletteColors.For("coal"),
            MineralDepositKind.IronOre => MaterialPaletteColors.For(ResourceVariant.IronOre),
            MineralDepositKind.CopperOre => MaterialPaletteColors.For(ResourceVariant.CopperOre),
            MineralDepositKind.SilverOre => MaterialPaletteColors.For(ResourceVariant.SilverOre),
            MineralDepositKind.GoldOre => MaterialPaletteColors.For(ResourceVariant.GoldOre),
            MineralDepositKind.Ruby => MaterialPaletteColors.For(ResourceVariant.Ruby),
            MineralDepositKind.Emerald => MaterialPaletteColors.For(ResourceVariant.Emerald),
            MineralDepositKind.Diamond => MaterialPaletteColors.For(ResourceVariant.Diamond),
            _ => throw new ArgumentOutOfRangeException(nameof(deposit), deposit, null),
        };
        DrawCircle(rect.GetCenter(), 4.5f, palette.Midtone);
        DrawCircle(rect.GetCenter() + new Vector2(-5f, 4f), 2.5f, palette.Shadow);
        DrawCircle(rect.GetCenter() + new Vector2(5f, -4f), 2f, palette.Highlight);
    }

    private void DrawCaveInnerCorners(GridPosition position, RockKind rock)
    {
        var corners = CaveWallTopology.GetInnerOpenCorners(position, IsOpenCaveCell);
        if (corners == CaveInnerCorner.None)
        {
            return;
        }

        if ((corners & CaveInnerCorner.NorthWest) != 0)
        {
            DrawCaveInnerCorner(position, rock, CaveInnerCorner.NorthWest);
        }
        if ((corners & CaveInnerCorner.NorthEast) != 0)
        {
            DrawCaveInnerCorner(position, rock, CaveInnerCorner.NorthEast);
        }
        if ((corners & CaveInnerCorner.SouthEast) != 0)
        {
            DrawCaveInnerCorner(position, rock, CaveInnerCorner.SouthEast);
        }
        if ((corners & CaveInnerCorner.SouthWest) != 0)
        {
            DrawCaveInnerCorner(position, rock, CaveInnerCorner.SouthWest);
        }
    }

    private void DrawCaveInnerCorner(
        GridPosition position,
        RockKind rock,
        CaveInnerCorner corner)
    {
        DrawTextureRectRegion(
            _caveWallAtlas,
            CellRect(position.X, position.Y),
            CaveSprites.GetInnerCornerRegion(_caveWallAtlas, rock, corner));
    }

    private bool IsOpenCaveCell(GridPosition position) =>
        _engine.Map.IsCavePosition(position) &&
        _engine.World.IsTerrainReachable(position);

    private int GetOpenCaveNeighborMask(GridPosition position)
    {
        ReadOnlySpan<(int X, int Y, int Bit)> offsets =
            [(0, -1, 1), (1, 0, 2), (0, 1, 4), (-1, 0, 8)];
        var mask = 0;
        foreach (var offset in offsets)
        {
            var neighbor = new GridPosition(position.X + offset.X, position.Y + offset.Y, position.Z);
            if (_engine.Map.IsCavePosition(neighbor) && _engine.World.IsTerrainReachable(neighbor))
            {
                mask |= offset.Bit;
            }
        }

        return mask;
    }

    private void DrawCavePassage(GridPosition position)
    {
        var center = CellCenter(position);
        var passages = _engine.World.CreateVerticalPassageSnapshot();
        var connectsUp = passages.Any(passage => passage.Lower == position);
        var connectsDown = passages.Any(passage => passage.Upper == position);
        var color = connectsUp ? new Color("d8b36a") : new Color("7394ad");
        DrawCircle(center, 6.5f, new Color(0.035f, 0.04f, 0.04f, 0.7f));
        DrawArc(center, 6.5f, 0f, Mathf.Tau, 20, color, 1.5f);
        if (connectsUp)
        {
            DrawLine(center + new Vector2(-3.5f, 2f), center, color, 1.5f);
            DrawLine(center, center + new Vector2(3.5f, 2f), color, 1.5f);
        }
        if (connectsDown)
        {
            DrawLine(center + new Vector2(-3.5f, -2f), center, color, 1.5f);
            DrawLine(center, center + new Vector2(3.5f, -2f), color, 1.5f);
        }
    }

    private void DrawTerrainRelief(int x, int y, MapCell cell, bool drawSlopeOverlay)
    {
        var rect = CellRect(x, y);
        if (cell.SurfaceLevel > 0)
        {
            DrawRect(rect, new Color(1f, 0.94f, 0.68f, 0.085f * cell.SurfaceLevel));
        }
        else if (cell.SurfaceLevel < 0)
        {
            DrawRect(rect, new Color(0.02f, 0.04f, 0.07f, 0.2f));
        }

        if (drawSlopeOverlay && _engine.World.IsTerrainRampIntact(
                new GridPosition(x, y, cell.SurfaceLevel)))
        {
            DrawTerrainSlope(rect, cell.RampDirection);
        }

        DrawCliffEdge(x, y, cell, TerrainRampDirection.North);
        DrawCliffEdge(x, y, cell, TerrainRampDirection.East);
        DrawCliffEdge(x, y, cell, TerrainRampDirection.South);
        DrawCliffEdge(x, y, cell, TerrainRampDirection.West);
    }

    private void DrawTerrainSlope(Rect2 rect, TerrainRampDirection uphill)
    {
        const float highlightThickness = 2.5f;
        const float shadowThickness = 3.5f;
        var highlight = new Color(0.92f, 0.9f, 0.7f, 0.16f);
        var shadow = new Color(0.025f, 0.035f, 0.03f, 0.24f);
        var contour = new Color(0.83f, 0.79f, 0.58f, 0.1f);
        switch (uphill)
        {
            case TerrainRampDirection.North:
                DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, highlightThickness)), highlight);
                DrawRect(new Rect2(rect.Position.X, rect.End.Y - shadowThickness, rect.Size.X, shadowThickness), shadow);
                DrawLine(rect.Position + new Vector2(0f, rect.Size.Y * 0.38f),
                    rect.Position + new Vector2(rect.Size.X, rect.Size.Y * 0.38f), contour, 1f);
                break;
            case TerrainRampDirection.East:
                DrawRect(new Rect2(rect.End.X - highlightThickness, rect.Position.Y, highlightThickness, rect.Size.Y), highlight);
                DrawRect(new Rect2(rect.Position, new Vector2(shadowThickness, rect.Size.Y)), shadow);
                DrawLine(rect.Position + new Vector2(rect.Size.X * 0.62f, 0f),
                    rect.Position + new Vector2(rect.Size.X * 0.62f, rect.Size.Y), contour, 1f);
                break;
            case TerrainRampDirection.South:
                DrawRect(new Rect2(rect.Position.X, rect.End.Y - highlightThickness, rect.Size.X, highlightThickness), highlight);
                DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, shadowThickness)), shadow);
                DrawLine(rect.Position + new Vector2(0f, rect.Size.Y * 0.62f),
                    rect.Position + new Vector2(rect.Size.X, rect.Size.Y * 0.62f), contour, 1f);
                break;
            case TerrainRampDirection.West:
                DrawRect(new Rect2(rect.Position, new Vector2(highlightThickness, rect.Size.Y)), highlight);
                DrawRect(new Rect2(rect.End.X - shadowThickness, rect.Position.Y, shadowThickness, rect.Size.Y), shadow);
                DrawLine(rect.Position + new Vector2(rect.Size.X * 0.38f, 0f),
                    rect.Position + new Vector2(rect.Size.X * 0.38f, rect.Size.Y), contour, 1f);
                break;
        }
    }

    private void DrawTerrainRampSteps(
        Rect2 rect,
        TerrainRampDirection uphill,
        float opacity = 1f)
    {
        var downhillOffset = uphill switch
        {
            TerrainRampDirection.North => new Vector2(0f, 1f),
            TerrainRampDirection.East => new Vector2(-1f, 0f),
            TerrainRampDirection.South => new Vector2(0f, -1f),
            TerrainRampDirection.West => new Vector2(1f, 0f),
            _ => Vector2.Zero,
        };
        if (downhillOffset == Vector2.Zero)
        {
            return;
        }

        var highlight = new Color(0.78f, 0.76f, 0.57f, 0.16f * opacity);
        var shadow = new Color(0.015f, 0.025f, 0.022f, 0.3f * opacity);
        ReadOnlySpan<float> stepPositions = [0.24f, 0.5f, 0.76f];
        foreach (var position in stepPositions)
        {
            Vector2 start;
            Vector2 end;
            if (uphill is TerrainRampDirection.North or TerrainRampDirection.South)
            {
                var y = rect.Position.Y + (rect.Size.Y * position);
                start = new Vector2(rect.Position.X + 2f, y);
                end = new Vector2(rect.End.X - 2f, y);
            }
            else
            {
                var x = rect.Position.X + (rect.Size.X * position);
                start = new Vector2(x, rect.Position.Y + 2f);
                end = new Vector2(x, rect.End.Y - 2f);
            }

            DrawLine(start + downhillOffset, end + downhillOffset, shadow, 1f);
            DrawLine(start, end, highlight, 1f);
        }
    }

    private void DrawCliffEdge(
        int x,
        int y,
        MapCell cell,
        TerrainRampDirection direction)
    {
        var neighbor = direction switch
        {
            TerrainRampDirection.North => new GridPosition(x, y - 1),
            TerrainRampDirection.East => new GridPosition(x + 1, y),
            TerrainRampDirection.South => new GridPosition(x, y + 1),
            TerrainRampDirection.West => new GridPosition(x - 1, y),
            _ => default,
        };
        if (!_engine.Map.IsWithin(neighbor))
        {
            return;
        }

        var neighborCell = _engine.Map.GetCell(neighbor);
        var drop = cell.SurfaceLevel - neighborCell.SurfaceLevel;
        var neighborRampIntact = _engine.World.IsTerrainRampIntact(
            new GridPosition(neighbor.X, neighbor.Y, neighborCell.SurfaceLevel));
        if (drop <= 0 ||
            (drop == 1 && neighborRampIntact &&
             neighborCell.RampDirection == Opposite(direction)))
        {
            return;
        }

        var rect = CellRect(x, y);
        var thickness = Math.Min(5f, 1.5f + (drop * 1.35f));
        var edge = direction switch
        {
            TerrainRampDirection.North =>
                new Rect2(rect.Position, new Vector2(rect.Size.X, thickness)),
            TerrainRampDirection.East =>
                new Rect2(rect.End.X - thickness, rect.Position.Y, thickness, rect.Size.Y),
            TerrainRampDirection.South =>
                new Rect2(rect.Position.X, rect.End.Y - thickness, rect.Size.X, thickness),
            TerrainRampDirection.West =>
                new Rect2(rect.Position, new Vector2(thickness, rect.Size.Y)),
            _ => default,
        };
        DrawRect(edge, new Color(0.08f, 0.065f, 0.055f, 0.38f + (0.08f * drop)));
    }

    private static TerrainRampDirection Opposite(TerrainRampDirection direction) => direction switch
    {
        TerrainRampDirection.North => TerrainRampDirection.South,
        TerrainRampDirection.East => TerrainRampDirection.West,
        TerrainRampDirection.South => TerrainRampDirection.North,
        TerrainRampDirection.West => TerrainRampDirection.East,
        _ => TerrainRampDirection.None,
    };

    private TerrainSprite ResolveTerrainSprite(
        int x,
        int y,
        TerrainKind terrain,
        HashSet<GridPosition> livingTrees)
    {
        return terrain switch
        {
            TerrainKind.Mud => TerrainSprite.BogGround,
            TerrainKind.SolidGround when IsForestFloor(x, y, livingTrees) =>
                IsConiferPatch(x, y)
                    ? TerrainSprite.ConiferForestFloor
                    : TerrainSprite.DeciduousForestFloor,
            TerrainKind.SolidGround => TerrainSprite.Meadow,
            _ => throw new ArgumentOutOfRangeException(nameof(terrain), terrain, null),
        };
    }

    private void DrawWaterTile(int x, int y, TerrainKind terrain)
    {
        var (first, second, phaseOffset) = terrain switch
        {
            TerrainKind.ShallowWater when IsSwampWater(x, y) =>
                (TerrainSprite.MuddyWaterA, TerrainSprite.MuddyWaterB, 0.34f),
            TerrainKind.ShallowWater =>
                (TerrainSprite.ShallowWaterA, TerrainSprite.ShallowWaterB, 0f),
            TerrainKind.DeepWater =>
                (TerrainSprite.DeepWaterA, TerrainSprite.DeepWaterB, 0.67f),
            _ => throw new ArgumentOutOfRangeException(nameof(terrain), terrain, null),
        };
        DrawCrossFadedTerrain(CellRect(x, y), first, second, phaseOffset);
    }

    private void DrawLavaTile(int x, int y)
    {
        var rect = CellRect(x, y);
        var pulse = WaterAnimationBlend(((x * 17 + y * 31) & 15) / 16f);
        DrawRect(rect, new Color("491006"));
        DrawRect(rect.Grow(-2f), new Color(0.92f, 0.19f + (pulse * 0.12f), 0.025f, 0.88f));
        DrawLine(
            rect.Position + new Vector2(2f, 5f + (pulse * 3f)),
            rect.End - new Vector2(2f, 6f - (pulse * 2f)),
            new Color(1f, 0.72f, 0.12f, 0.8f),
            1.5f);
    }

    private void DrawCrossFadedTerrain(
        Rect2 destination,
        TerrainSprite first,
        TerrainSprite second,
        float phaseOffset)
    {
        DrawTextureRectRegion(
            _terrainAtlas,
            destination,
            TerrainSprites.GetRegion(_terrainAtlas, first));
        DrawTextureRectRegion(
            _terrainAtlas,
            destination,
            TerrainSprites.GetRegion(_terrainAtlas, second),
            new Color(1f, 1f, 1f, WaterAnimationBlend(phaseOffset)));
    }

    private float WaterAnimationBlend(float phaseOffset)
    {
        var phase = ((float)(_waterAnimationElapsed / WaterAnimationCycleSeconds) + phaseOffset) % 1f;
        return (1f - MathF.Cos(phase * MathF.Tau)) / 2f;
    }

    private bool IsSwampWater(int x, int y) =>
        x < _engine.Map.Width * 0.38f || y > _engine.Map.Height * 0.66f;

    private static bool IsForestFloor(int x, int y, HashSet<GridPosition> livingTrees)
    {
        for (var offsetY = -2; offsetY <= 2; offsetY++)
        {
            for (var offsetX = -2; offsetX <= 2; offsetX++)
            {
                if (livingTrees.Contains(new GridPosition(x + offsetX, y + offsetY)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsConiferPatch(int x, int y) =>
        unchecked((((x / 7) * 73856093) ^ ((y / 7) * 19349663)) & 3) == 0;

    private void DrawPlants()
    {
        if (_visibleLevel < 0)
        {
            return;
        }

        foreach (var plant in _snapshot.PlantPatches.Where(item =>
                     (item.Biomass > 0 || item.Kind == PlantKind.BerryBush) &&
                     _engine.Map.GetTerrainSurfacePosition(item.Position).Z == _visibleLevel))
        {
            var center = CellCenter(plant.Position);
            if (plant.Kind == PlantKind.FishShoal)
            {
                DrawCrossFadedTerrain(
                    CellRect(plant.Position.X, plant.Position.Y),
                    TerrainSprite.FishShadowsA,
                    TerrainSprite.FishShadowsB,
                    phaseOffset: 0.18f);
                continue;
            }

            var sprite = plant.Kind switch
            {
                PlantKind.BerryBush when plant.Biomass > 0 =>
                    EnvironmentSprite.FruitingBerryBush,
                PlantKind.BerryBush => EnvironmentSprite.BareBerryBush,
                PlantKind.MushroomCluster => EnvironmentSprite.MushroomCluster,
                PlantKind.EdibleRoots => EnvironmentSprite.EdibleRoots,
                PlantKind.ReedBed => EnvironmentSprite.Reeds,
                _ => throw new ArgumentOutOfRangeException(),
            };
            var size = plant.Kind is PlantKind.MushroomCluster or PlantKind.EdibleRoots
                ? 11f
                : 22f;
            DrawTextureRectRegion(
                _environmentAtlas,
                new Rect2(center - new Vector2(size / 2f, size / 2f), new Vector2(size, size)),
                EnvironmentSprites.GetRegion(_environmentAtlas, sprite));
        }
    }

    private void DrawStructures()
    {
        var structureCache = GetStructureRenderCache();
        DrawWalkways(structureCache.WalkwayCells, Colors.White);
        DrawWalkways(structureCache.BasaltWalkwayCells, new Color("8f8982"));
        DrawPrimitiveBarriers(structureCache);

        foreach (var worldObject in _snapshot.WorldObjects)
        {
            if (worldObject.Kind is WorldObjectKind.WoodenWalkway or
                WorldObjectKind.BasaltWalkway or
                WorldObjectKind.WoodenWall or WorldObjectKind.StoneWall or
                WorldObjectKind.WoodenDoorFrame or WorldObjectKind.StoneDoorFrame or
                WorldObjectKind.WoodenDoorLeaf or WorldObjectKind.WallTorch)
            {
                continue;
            }
            if (worldObject.Kind is WorldObjectKind.HumanCottage or
                WorldObjectKind.HumanBarn or
                WorldObjectKind.HumanStorehouse)
            {
                if (worldObject.Orientation == CardinalOrientation.South)
                {
                    DrawIllustratedHumanStructure(worldObject);
                }
                else
                {
                    DrawModularHumanStructure(worldObject);
                }
                continue;
            }
            if (worldObject.Kind == WorldObjectKind.HumanWell)
            {
                DrawIllustratedHumanStructure(worldObject);
                continue;
            }

            if (worldObject.Kind is WorldObjectKind.GoblinHut or WorldObjectKind.GoblinFieldCamp)
            {
                DrawIllustratedGoblinStructure(worldObject);
                continue;
            }

            if (worldObject.Kind is WorldObjectKind.Tree or WorldObjectKind.DeadTreeStump or
                WorldObjectKind.Boulder)
            {
                DrawIllustratedNaturalStructure(worldObject);
                continue;
            }

            if (worldObject.Kind is WorldObjectKind.PrimitiveWorkshop or
                    WorldObjectKind.Bloomery or WorldObjectKind.SmeltingFurnace or
                    WorldObjectKind.CrucibleFurnace &&
                worldObject.Anchor.Z == _visibleLevel)
            {
                DrawWorkshop(worldObject);
                continue;
            }

            var materialPalette = worldObject.MaterialVariant == ResourceVariant.None
                ? (MaterialPaletteColors?)null
                : MaterialPaletteColors.For(worldObject.MaterialVariant);
            var baseColor = materialPalette?.Midtone ??
                (worldObject.Kind == WorldObjectKind.WoodenWalkway
                ? new Color("b8894c")
                : worldObject.Owner == WorldObjectOwner.GoblinTribe
                ? new Color("745b3b")
                : new Color("c08b55"));
            foreach (var (position, part) in worldObject.GetAbsoluteParts().Where(item =>
                         item.Position.Z == _visibleLevel))
            {
                var color = part.Kind switch
                {
                    WorldObjectPartKind.Floor => baseColor.Darkened(0.18f),
                    WorldObjectPartKind.Door => materialPalette?.Highlight ?? new Color("e3c06c"),
                    WorldObjectPartKind.WellRim => new Color("9ca4a1"),
                    WorldObjectPartKind.Walkway => materialPalette?.Midtone ?? new Color("b8894c"),
                    _ => baseColor,
                };
                DrawRect(
                    CellRect(position.X, position.Y).Grow(
                        part.Kind == WorldObjectPartKind.Walkway ? -4f : -1.5f),
                    color);
            }
        }
    }

    private void DrawWorkshop(WorldObjectSnapshot workshop)
    {
        var position = workshop.Anchor;
        var kind = workshop.Kind;
        var palette = workshop.MaterialVariant == ResourceVariant.None
            ? (MaterialPaletteColors?)null
            : MaterialPaletteColors.For(workshop.MaterialVariant);
        var rect = CellRect(position.X, position.Y).Grow(-2.5f);
        DrawRect(rect, palette?.Edge ?? new Color("37281d"));
        if (kind != WorldObjectKind.PrimitiveWorkshop)
        {
            var bodyColor = palette?.Midtone ?? kind switch
            {
                WorldObjectKind.Bloomery => new Color("766753"),
                WorldObjectKind.SmeltingFurnace => new Color("59606a"),
                WorldObjectKind.CrucibleFurnace => new Color("49434f"),
                _ => new Color("766753"),
            };
            var body = rect.Grow(-2f);
            DrawRect(body, bodyColor);
            DrawRect(new Rect2(
                body.Position + new Vector2(body.Size.X * 0.28f, body.Size.Y * 0.57f),
                new Vector2(body.Size.X * 0.44f, body.Size.Y * 0.35f)),
                new Color("211b19"));
            DrawCircle(body.Position + new Vector2(
                body.Size.X * 0.5f,
                body.Size.Y * 0.75f), 2.2f, new Color("ff8a2b"));
            return;
        }

        var bench = new Rect2(
            rect.Position + new Vector2(1.5f, rect.Size.Y * 0.28f),
            new Vector2(rect.Size.X - 3f, rect.Size.Y * 0.38f));
        DrawRect(bench, palette?.Midtone ?? new Color("8b6038"));
        DrawLine(bench.Position, new Vector2(bench.End.X, bench.Position.Y),
            palette?.Highlight ?? new Color("c18a50"), 1.2f);
        DrawLine(bench.Position + new Vector2(3f, bench.Size.Y),
            bench.Position + new Vector2(2f, rect.Size.Y * 0.62f),
            palette?.Shadow ?? new Color("5a3b26"), 2f);
        DrawLine(new Vector2(bench.End.X - 3f, bench.End.Y),
            new Vector2(bench.End.X - 2f, rect.End.Y - 1f),
            palette?.Shadow ?? new Color("5a3b26"), 2f);
        DrawCircle(bench.GetCenter() + new Vector2(-3f, -1f), 2.2f, new Color("d7d0b2"));
        DrawLine(bench.GetCenter(), bench.GetCenter() + new Vector2(5f, -4f),
            new Color("8e9594"), 1.5f);
    }

    private void DrawWalkways(
        IReadOnlySet<GridPosition> walkwayCells,
        Color modulate)
    {
        foreach (var position in walkwayCells)
        {
            var mask = 0;
            if (walkwayCells.Contains(position with { Y = position.Y - 1 })) mask |= 1;
            if (walkwayCells.Contains(position with { X = position.X + 1 })) mask |= 2;
            if (walkwayCells.Contains(position with { Y = position.Y + 1 })) mask |= 4;
            if (walkwayCells.Contains(position with { X = position.X - 1 })) mask |= 8;

            var cell = CellRect(position.X, position.Y).Grow(-0.5f);
            DrawTextureRectRegion(
                _walkwayAtlas,
                cell,
                WalkwaySprites.GetRegion(_walkwayAtlas, mask),
                modulate);
        }
    }

    private void DrawPrimitiveBarriers(StructureRenderCache cache)
    {
        if (cache.ConnectedCells.Count == 0 && cache.DoorLeaves.Count == 0 &&
            cache.WallTorches.Length == 0)
        {
            return;
        }
        foreach (var (position, wall) in cache.WoodenWalls)
        {
            var mask = GetCardinalConnectionMask(position, cache.ConnectedCells);
            var destination = CellRect(position.X, position.Y).Grow(-0.5f);
            var region = StructureWallSprites.GetRegion(
                _structureWallAtlas,
                StructureWallMaterial.GoblinBogwood,
                mask);
            var palette = PaletteFor(wall);
            if (palette is null)
            {
                DrawTextureRectRegion(_structureWallAtlas, destination, region);
            }
            else
            {
                DrawTextureRect(
                    _materialPaletteTextures.Get(
                        _structureWallAtlas,
                        region,
                        wall.MaterialVariant,
                        MaterialPaletteTextureProfile.CompleteSurface),
                    destination,
                    tile: false);
            }
            DrawInteriorWallFaces(
                position,
                cache.Enclosure!.GetWallSides(position).VisibleFaces,
                palette: palette);
        }

        foreach (var (position, wall) in cache.StoneWalls)
        {
            var mask = GetCardinalConnectionMask(position, cache.ConnectedCells);
            var destination = CellRect(position.X, position.Y).Grow(-0.5f);
            var region = StructureWallSprites.GetRegion(
                _structureWallAtlas,
                StructureWallMaterial.GoblinBogwood,
                mask);
            var palette = PaletteFor(wall);
            if (palette is null)
            {
                DrawTextureRectRegion(
                    _structureWallAtlas,
                    destination,
                    region,
                    new Color(0.62f, 0.7f, 0.76f));
            }
            else
            {
                DrawTextureRect(
                    _materialPaletteTextures.Get(
                        _structureWallAtlas,
                        region,
                        wall.MaterialVariant,
                        MaterialPaletteTextureProfile.CompleteSurface),
                    destination,
                    tile: false);
            }
            DrawInteriorWallFaces(
                position,
                cache.Enclosure!.GetWallSides(position).VisibleFaces,
                stone: true,
                palette: palette);
        }

        foreach (var (position, frame) in cache.DoorFrames)
        {
            DrawDoorFrame(
                position,
                frame.Orientation,
                stone: frame.Kind == WorldObjectKind.StoneDoorFrame,
                palette: PaletteFor(frame));
            if (cache.DoorLeaves.TryGetValue(position, out var leaf))
            {
                var isOpen = leaf.Parts.Single().Kind is
                    WorldObjectPartKind.OpenDoorLeaf or
                    WorldObjectPartKind.AutomaticallyOpenedDoorLeaf;
                DrawDoorLeaf(position, frame.Orientation, isOpen, PaletteFor(leaf));
            }
        }

        foreach (var torch in cache.WallTorches)
        {
            DrawWallTorch(torch.Anchor, torch.Orientation, PaletteFor(torch));
        }
    }

    private static MaterialPaletteColors? PaletteFor(WorldObjectSnapshot worldObject) =>
        worldObject.MaterialVariant == ResourceVariant.None
            ? null
            : MaterialPaletteColors.For(worldObject.MaterialVariant);

    private void DrawIllustratedHumanStructure(WorldObjectSnapshot worldObject)
    {
        var relativeLevel = _visibleLevel - worldObject.Anchor.Z;
        var sprite = (worldObject.Kind, relativeLevel) switch
        {
            (WorldObjectKind.HumanCottage, 0) => HumanStructureSprite.CottageGround,
            (WorldObjectKind.HumanCottage, 1) => HumanStructureSprite.CottageRoof,
            (WorldObjectKind.HumanBarn, 0) => HumanStructureSprite.BarnGround,
            (WorldObjectKind.HumanBarn, 1) => HumanStructureSprite.BarnRoof,
            (WorldObjectKind.HumanStorehouse, 0) => HumanStructureSprite.StorehouseGround,
            (WorldObjectKind.HumanStorehouse, 1) => HumanStructureSprite.StorehouseRoof,
            (WorldObjectKind.HumanWell, 0) => HumanStructureSprite.WellSurface,
            (WorldObjectKind.HumanWell, -1) => HumanStructureSprite.WellShaft,
            _ => (HumanStructureSprite?)null,
        };
        if (sprite is null)
        {
            return;
        }

        var positions = worldObject.GetAbsoluteParts()
            .Where(item => item.Position.Z == _visibleLevel)
            .Select(item => item.Position)
            .Distinct()
            .ToArray();
        if (positions.Length == 0)
        {
            return;
        }

        var minimumX = positions.Min(position => position.X);
        var minimumY = positions.Min(position => position.Y);
        var maximumX = positions.Max(position => position.X);
        var maximumY = positions.Max(position => position.Y);
        var footprintSize = new Vector2(
            (maximumX - minimumX + 1) * TileSize,
            (maximumY - minimumY + 1) * TileSize);
        var center = new Vector2(
            (minimumX * TileSize) + (footprintSize.X / 2f),
            (minimumY * TileSize) + (footprintSize.Y / 2f));
        // Whole-room illustrations are temporary. Rotating them also rotates furniture,
        // lighting and painted perspective, so keep them upright until structures are modular.
        DrawSetTransform(center, 0f);
        DrawTextureRectRegion(
            _humanStructureAtlas,
            new Rect2(-footprintSize / 2f, footprintSize),
            HumanStructureSprites.GetRegion(sprite.Value));
        DrawSetTransform(Vector2.Zero);
    }

    private void DrawModularHumanStructure(WorldObjectSnapshot worldObject)
    {
        var parts = worldObject.GetAbsoluteParts()
            .Where(item => item.Position.Z == _visibleLevel)
            .ToArray();
        if (parts.Length == 0)
        {
            return;
        }

        if (_visibleLevel == 1)
        {
            foreach (var position in parts
                         .Where(item => item.Part.Kind == WorldObjectPartKind.Roof)
                         .Select(item => item.Position))
            {
                var rect = CellRect(position.X, position.Y).Grow(-0.7f);
                DrawRect(rect, new Color("51483c"));
                DrawLine(rect.Position + new Vector2(0f, rect.Size.Y * 0.5f),
                    new Vector2(rect.End.X, rect.Position.Y + (rect.Size.Y * 0.5f)),
                    new Color("84745d"), 0.8f);
            }
            return;
        }

        if (_visibleLevel != 0)
        {
            return;
        }

        foreach (var position in parts
                     .Where(item => item.Part.Kind == WorldObjectPartKind.Floor)
                     .Select(item => item.Position))
        {
            var rect = CellRect(position.X, position.Y).Grow(-0.6f);
            var variation = ((position.X + position.Y) & 1) == 0 ? 0f : 0.055f;
            DrawRect(rect, new Color("917654").Lightened(variation));
            DrawRect(rect, new Color("4d3d2d"), filled: false, width: 0.55f);
        }

        var wallConnections = parts
            .Where(item => item.Part.Kind is WorldObjectPartKind.Wall or WorldObjectPartKind.Door)
            .Select(item => item.Position)
            .ToHashSet();
        foreach (var position in parts
                     .Where(item => item.Part.Kind == WorldObjectPartKind.Wall)
                     .Select(item => item.Position))
        {
            var mask = GetCardinalConnectionMask(position, wallConnections);
            DrawTextureRectRegion(
                _structureWallAtlas,
                CellRect(position.X, position.Y).Grow(-0.5f),
                StructureWallSprites.GetRegion(
                    _structureWallAtlas,
                    StructureWallMaterial.HumanOak,
                    mask));
        }

        foreach (var position in parts
                     .Where(item => item.Part.Kind == WorldObjectPartKind.Door)
                     .Select(item => item.Position))
        {
            DrawDoorFrame(position, worldObject.Orientation);
        }
    }

    private static int GetCardinalConnectionMask(
        GridPosition position,
        IReadOnlySet<GridPosition> connectedCells)
    {
        var mask = 0;
        if (connectedCells.Contains(position with { Y = position.Y - 1 })) mask |= 1;
        if (connectedCells.Contains(position with { X = position.X + 1 })) mask |= 2;
        if (connectedCells.Contains(position with { Y = position.Y + 1 })) mask |= 4;
        if (connectedCells.Contains(position with { X = position.X - 1 })) mask |= 8;
        return mask;
    }

    private void DrawDoorFrame(
        GridPosition position,
        CardinalOrientation orientation,
        bool stone = false,
        MaterialPaletteColors? palette = null)
    {
        var rect = CellRect(position.X, position.Y).Grow(-1.2f);
        var timber = palette?.Midtone ?? (stone ? new Color("9daab5") : new Color("b47d43"));
        var shadow = palette?.Edge ?? (stone ? new Color("3d4852") : new Color("4a2e19"));
        const float postSize = 4.2f;
        if (orientation is CardinalOrientation.North or CardinalOrientation.South)
        {
            DrawRect(new Rect2(rect.Position, new Vector2(postSize, rect.Size.Y)), shadow);
            DrawRect(new Rect2(rect.End.X - postSize, rect.Position.Y, postSize, rect.Size.Y), shadow);
            DrawRect(new Rect2(rect.Position + Vector2.One, new Vector2(postSize - 1f, rect.Size.Y - 2f)), timber);
            DrawRect(new Rect2(rect.End.X - postSize, rect.Position.Y + 1f,
                postSize - 1f, rect.Size.Y - 2f), timber);
            DrawLine(new Vector2(rect.Position.X + postSize, rect.GetCenter().Y),
                new Vector2(rect.End.X - postSize, rect.GetCenter().Y), timber, 1.4f);
        }
        else
        {
            DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, postSize)), shadow);
            DrawRect(new Rect2(rect.Position.X, rect.End.Y - postSize, rect.Size.X, postSize), shadow);
            DrawRect(new Rect2(rect.Position + Vector2.One, new Vector2(rect.Size.X - 2f, postSize - 1f)), timber);
            DrawRect(new Rect2(rect.Position.X + 1f, rect.End.Y - postSize,
                rect.Size.X - 2f, postSize - 1f), timber);
            DrawLine(new Vector2(rect.GetCenter().X, rect.Position.Y + postSize),
                new Vector2(rect.GetCenter().X, rect.End.Y - postSize), timber, 1.4f);
        }
    }

    private void DrawWallTorch(
        GridPosition position,
        CardinalOrientation orientation,
        MaterialPaletteColors? palette = null)
    {
        var direction = orientation switch
        {
            CardinalOrientation.North => Vector2.Up,
            CardinalOrientation.East => Vector2.Right,
            CardinalOrientation.South => Vector2.Down,
            CardinalOrientation.West => Vector2.Left,
            _ => Vector2.Up,
        };
        var wallPoint = CellCenter(position) + (direction * TileSize * 0.32f);
        var flameCenter = wallPoint + (direction * 5.5f);
        var tangent = new Vector2(-direction.Y, direction.X);
        DrawLine(wallPoint - (tangent * 3.6f), wallPoint + (tangent * 3.6f),
            palette?.Edge ?? new Color("3b2819"), 2.6f, antialiased: true);
        DrawLine(wallPoint, flameCenter - (direction * 2f),
            palette?.Midtone ?? new Color("956038"), 2.2f, antialiased: true);
        var phase = ((float)Time.GetTicksMsec() * 0.012f) +
            (position.X * 0.73f) + (position.Y * 1.17f) + (position.Z * 0.41f);
        var flicker = 0.5f + (0.5f * Mathf.Sin(phase));
        DrawCircle(flameCenter, 10f + (flicker * 2f),
            new Color(1f, 0.42f, 0.08f, 0.09f));
        DrawCircle(flameCenter, 3.2f + (flicker * 0.7f),
            new Color("ff7622"));
        DrawCircle(flameCenter - (direction * 0.8f), 1.5f,
            new Color("ffe66b"));
    }

    private void DrawInteriorWallFaces(
        GridPosition position,
        WallInteriorFacing facing,
        bool stone = false,
        MaterialPaletteColors? palette = null)
    {
        if (facing == WallInteriorFacing.None)
        {
            return;
        }

        var rect = CellRect(position.X, position.Y).Grow(-0.5f);
        if ((facing & WallInteriorFacing.North) != 0)
        {
            DrawInteriorWallFace(rect, WallInteriorFacing.North, stone, palette);
        }
        if ((facing & WallInteriorFacing.East) != 0)
        {
            DrawInteriorWallFace(rect, WallInteriorFacing.East, stone, palette);
        }
        if ((facing & WallInteriorFacing.South) != 0)
        {
            DrawInteriorWallFace(rect, WallInteriorFacing.South, stone, palette);
        }
        if ((facing & WallInteriorFacing.West) != 0)
        {
            DrawInteriorWallFace(rect, WallInteriorFacing.West, stone, palette);
        }
    }

    private void DrawInteriorWallFace(
        Rect2 rect,
        WallInteriorFacing direction,
        bool stone,
        MaterialPaletteColors? palette)
    {
        const int bands = 7;
        const float faceFraction = 0.66f;
        var light = palette?.Midtone ?? (stone
            ? new Color(0.38f, 0.42f, 0.45f, 0.96f)
            : new Color(0.43f, 0.29f, 0.17f, 0.94f));
        var dark = palette?.Edge ?? (stone
            ? new Color(0.08f, 0.095f, 0.11f, 0.99f)
            : new Color(0.13f, 0.075f, 0.04f, 0.98f));
        for (var band = 0; band < bands; band++)
        {
            var phase = (band + 0.5f) / bands;
            var color = light.Lerp(dark, phase);
            Rect2 strip;
            if (direction == WallInteriorFacing.South)
            {
                var height = rect.Size.Y * faceFraction / bands;
                strip = new Rect2(
                    rect.Position.X,
                    rect.End.Y - rect.Size.Y * faceFraction + band * height,
                    rect.Size.X,
                    height + 0.2f);
            }
            else if (direction == WallInteriorFacing.North)
            {
                var height = rect.Size.Y * faceFraction / bands;
                strip = new Rect2(
                    rect.Position.X,
                    rect.Position.Y + rect.Size.Y * faceFraction - (band + 1) * height,
                    rect.Size.X,
                    height + 0.2f);
            }
            else if (direction == WallInteriorFacing.East)
            {
                var width = rect.Size.X * faceFraction / bands;
                strip = new Rect2(
                    rect.End.X - rect.Size.X * faceFraction + band * width,
                    rect.Position.Y,
                    width + 0.2f,
                    rect.Size.Y);
            }
            else
            {
                var width = rect.Size.X * faceFraction / bands;
                strip = new Rect2(
                    rect.Position.X + rect.Size.X * faceFraction - (band + 1) * width,
                    rect.Position.Y,
                    width + 0.2f,
                    rect.Size.Y);
            }
            DrawRect(strip, color);
        }

        var rim = palette?.Highlight ?? (stone
            ? new Color(0.62f, 0.68f, 0.72f, 0.96f)
            : new Color(0.68f, 0.47f, 0.27f, 0.95f));
        switch (direction)
        {
            case WallInteriorFacing.North:
                DrawLine(
                    new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y * faceFraction),
                    new Vector2(rect.End.X, rect.Position.Y + rect.Size.Y * faceFraction),
                    rim,
                    1f);
                break;
            case WallInteriorFacing.East:
                DrawLine(
                    new Vector2(rect.End.X - rect.Size.X * faceFraction, rect.Position.Y),
                    new Vector2(rect.End.X - rect.Size.X * faceFraction, rect.End.Y),
                    rim,
                    1f);
                break;
            case WallInteriorFacing.South:
                DrawLine(
                    new Vector2(rect.Position.X, rect.End.Y - rect.Size.Y * faceFraction),
                    new Vector2(rect.End.X, rect.End.Y - rect.Size.Y * faceFraction),
                    rim,
                    1f);
                break;
            case WallInteriorFacing.West:
                DrawLine(
                    new Vector2(rect.Position.X + rect.Size.X * faceFraction, rect.Position.Y),
                    new Vector2(rect.Position.X + rect.Size.X * faceFraction, rect.End.Y),
                    rim,
                    1f);
                break;
        }
    }

    private void DrawDoorLeaf(
        GridPosition position,
        CardinalOrientation orientation,
        bool isOpen,
        MaterialPaletteColors? palette = null)
    {
        var rect = CellRect(position.X, position.Y).Grow(-5.2f);
        var timber = palette?.Midtone ?? new Color("8f5d30");
        var edge = palette?.Edge ?? new Color("402716");
        var horizontal = orientation is CardinalOrientation.North or CardinalOrientation.South;
        if (!isOpen)
        {
            var leaf = horizontal
                ? new Rect2(rect.Position, new Vector2(rect.Size.X, rect.Size.Y * 0.55f))
                : new Rect2(rect.Position, new Vector2(rect.Size.X * 0.55f, rect.Size.Y));
            leaf.Position += horizontal
                ? new Vector2(0f, rect.Size.Y * 0.225f)
                : new Vector2(rect.Size.X * 0.225f, 0f);
            DrawRect(leaf, edge);
            DrawRect(leaf.Grow(-1.2f), timber);
            var center = leaf.GetCenter();
            if (horizontal)
            {
                DrawLine(new Vector2(center.X, leaf.Position.Y + 1f),
                    new Vector2(center.X, leaf.End.Y - 1f), edge, 1f);
            }
            else
            {
                DrawLine(new Vector2(leaf.Position.X + 1f, center.Y),
                    new Vector2(leaf.End.X - 1f, center.Y), edge, 1f);
            }
            return;
        }

        var hinge = horizontal
            ? new Vector2(rect.Position.X, rect.GetCenter().Y)
            : new Vector2(rect.GetCenter().X, rect.Position.Y);
        var openedEnd = horizontal
            ? hinge + new Vector2(rect.Size.X * 0.72f, rect.Size.Y * 0.48f)
            : hinge + new Vector2(rect.Size.X * 0.48f, rect.Size.Y * 0.72f);
        DrawLine(hinge, openedEnd, edge, 4.5f);
        DrawLine(hinge, openedEnd, timber, 2.5f);
    }

    private static float RotationFor(CardinalOrientation orientation) => orientation switch
    {
        CardinalOrientation.North => Mathf.Pi,
        CardinalOrientation.East => -Mathf.Pi / 2f,
        CardinalOrientation.South => 0f,
        CardinalOrientation.West => Mathf.Pi / 2f,
        _ => 0f,
    };

    private void DrawIllustratedNaturalStructure(WorldObjectSnapshot worldObject)
    {
        var center = CellCenter(worldObject.Anchor);
        var surfaceOffset = worldObject.Anchor.Z == 0
            ? _engine.Map.GetColumnCell(worldObject.Anchor).SurfaceLevel
            : 0;
        if (worldObject.Kind == WorldObjectKind.Boulder)
        {
            if (_visibleLevel == worldObject.Anchor.Z + surfaceOffset)
            {
                var stoneVariant = StoneMaterialPolicy.VariantFor(
                    _engine.WorldSeed,
                    _engine.Map.Width,
                    worldObject.Anchor);
                DrawMaterialBoulder(center, stoneVariant);
            }
            return;
        }

        var woodVariant = WoodMaterialPolicy.VariantFor(
            _engine.WorldSeed,
            _engine.Map.Width,
            worldObject.Anchor);
        var woodModulate = TreePartSprites.GetWoodModulate(woodVariant);

        if (worldObject.Kind == WorldObjectKind.DeadTreeStump)
        {
            var stumpLevel = worldObject.Anchor.Z + surfaceOffset;
            if (_visibleLevel == stumpLevel - 1)
            {
                DrawTreePartSprite(
                    center, TreePartSprite.UndergroundRoots, 39f, woodModulate);
                return;
            }
            if (_visibleLevel == stumpLevel)
            {
                if (worldObject.Parts.Any(part =>
                        part.Kind == WorldObjectPartKind.FelledTreeRemains))
                {
                    DrawTreePartSprite(
                        center, TreePartSprite.FelledRemains, 43f, woodModulate);
                }
                DrawTreePartSprite(center, TreePartSprite.CutStump, 18f, woodModulate);
            }
            return;
        }

        if (_visibleLevel == worldObject.Anchor.Z + surfaceOffset - 1)
        {
            DrawTreePartSprite(
                center, TreePartSprite.UndergroundRoots, 39f, woodModulate);
            return;
        }

        var absoluteParts = worldObject.GetAbsoluteParts().ToArray();
        var visibleParts = absoluteParts
            .Where(item => item.Position.Z + surfaceOffset == _visibleLevel)
            .Select(item => item.Part.Kind)
            .ToArray();
        if (visibleParts.Contains(WorldObjectPartKind.TreeTrunk))
        {
            DrawStandingTrunk(center, woodVariant);
            return;
        }

        var crownLevel = absoluteParts
            .Where(item => item.Part.Kind == WorldObjectPartKind.TreeCrown)
            .Select(item => item.Position.Z + surfaceOffset)
            .DefaultIfEmpty(int.MaxValue)
            .Max();
        if (_visibleLevel < crownLevel)
        {
            return;
        }

        var levelDifference = _visibleLevel - crownLevel;
        var crownOpacity = levelDifference == 0
            ? 1f
            : Mathf.Max(0.16f, 0.32f - ((levelDifference - 1) * 0.08f));
        var crownSize = new Vector2(TileSize * 3f, TileSize * 3f);
        DrawTextureRectRegion(
            _treeCrownAtlas,
            new Rect2(center - crownSize / 2f, crownSize),
            TreeCrownSprites.GetRegion(_treeCrownAtlas, woodVariant),
            new Color(1f, 1f, 1f, crownOpacity));
    }

    private void DrawTreePartSprite(
        Vector2 center,
        TreePartSprite sprite,
        float size,
        Color modulate)
    {
        var dimensions = new Vector2(size, size);
        DrawTextureRectRegion(
            _treePartAtlas,
            new Rect2(center - dimensions / 2f, dimensions),
            TreePartSprites.GetRegion(_treePartAtlas, sprite),
            modulate);
    }

    private void DrawStandingTrunk(Vector2 center, ResourceVariant variant)
    {
        var palette = MaterialPaletteColors.For(variant);
        DrawCircle(center + new Vector2(1.2f, 1.7f), 9f, new Color(0, 0, 0, 0.42f));
        DrawCircle(center, 8.7f, palette.Edge);
        DrawCircle(center, 6.9f, palette.Highlight);
        DrawCircle(center + new Vector2(0.3f, 0.15f), 5.3f, palette.Midtone);
        DrawCircle(center + new Vector2(-0.2f, -0.15f), 4f, palette.Highlight);
        DrawCircle(center + new Vector2(0.15f, 0.1f), 2.9f, palette.Shadow);
        DrawCircle(center + new Vector2(-0.1f, -0.05f), 1.85f, palette.Midtone);
        DrawCircle(center, 0.85f, palette.Shadow);
    }

    private void DrawMaterialBoulder(Vector2 center, ResourceVariant variant)
    {
        var dimensions = new Vector2(24f, 21f);
        var visualCenter = center + new Vector2(0f, -1f);
        var destination = new Rect2(visualCenter - dimensions / 2f, dimensions);

        DrawCircle(center + new Vector2(0.8f, 2f), 9.5f, new Color(0, 0, 0, 0.45f));
        DrawTextureRect(
            _materialPaletteTextures.Get(
                _itemIconAtlas,
                GetNeutralStoneRegion(),
                variant,
                MaterialPaletteTextureProfile.CompleteSurface),
            destination,
            tile: false);
    }

    private Rect2 GetNeutralStoneRegion()
    {
        var cell = ItemIcons.GetRegion(_itemIconAtlas, ItemIcon.Stone);
        return new Rect2(
            cell.Position + new Vector2(cell.Size.X * 0.05f, cell.Size.Y * 0.17f),
            new Vector2(cell.Size.X * 0.78f, cell.Size.Y * 0.77f));
    }

    private void DrawIllustratedGoblinStructure(WorldObjectSnapshot worldObject)
    {
        var relativeLevel = _visibleLevel - worldObject.Anchor.Z;
        var sprite = (worldObject.Kind, relativeLevel) switch
        {
            (WorldObjectKind.GoblinHut, 0) => EnvironmentSprite.GoblinHutGround,
            (WorldObjectKind.GoblinHut, 1) => EnvironmentSprite.GoblinHutRoof,
            (WorldObjectKind.GoblinFieldCamp, 0) => EnvironmentSprite.FieldCampGround,
            (WorldObjectKind.GoblinFieldCamp, 1) => EnvironmentSprite.FieldCampRoof,
            _ => (EnvironmentSprite?)null,
        };
        if (sprite is null)
        {
            return;
        }

        var positions = worldObject.GetAbsoluteParts()
            .Where(item => item.Position.Z == _visibleLevel)
            .Select(item => item.Position)
            .Distinct()
            .ToArray();
        if (positions.Length == 0)
        {
            return;
        }

        var minimumX = positions.Min(position => position.X);
        var minimumY = positions.Min(position => position.Y);
        var maximumX = positions.Max(position => position.X);
        var maximumY = positions.Max(position => position.Y);
        var size = new Vector2(
            (maximumX - minimumX + 1) * TileSize,
            (maximumY - minimumY + 1) * TileSize);
        var center = new Vector2(
            (minimumX * TileSize) + (size.X / 2f),
            (minimumY * TileSize) + (size.Y / 2f));
        var rotation = worldObject.Kind == WorldObjectKind.GoblinFieldCamp
            ? 0f
            : RotationFor(worldObject.Orientation);
        var region = EnvironmentSprites.GetRegion(_environmentAtlas, sprite.Value);
        DrawSetTransform(center, rotation);
        var destination = new Rect2(-size / 2f, size);
        if (worldObject.MaterialVariant == ResourceVariant.None)
        {
            DrawTextureRectRegion(_environmentAtlas, destination, region);
        }
        else
        {
            DrawTextureRect(
                _materialPaletteTextures.Get(
                    _environmentAtlas,
                    region,
                    worldObject.MaterialVariant,
                    MaterialPaletteTextureProfile.IllustratedTimber),
                destination,
                tile: false);
        }
        DrawSetTransform(Vector2.Zero);
        if (worldObject.Kind == WorldObjectKind.GoblinFieldCamp)
        {
            DrawFieldCampRaidStatus(worldObject, center, size);
        }
    }

    private void DrawFieldCampRaidStatus(
        WorldObjectSnapshot worldObject,
        Vector2 center,
        Vector2 size)
    {
        if (worldObject.Anchor != _snapshot.RaidRallyPoint ||
            _snapshot.RaidPhase == GoblinRaidPhase.None)
        {
            return;
        }

        var color = _snapshot.RaidPhase switch
        {
            GoblinRaidPhase.Preparing => new Color("f0b84b"),
            GoblinRaidPhase.Ready => new Color("75e36b"),
            GoblinRaidPhase.Suspended => new Color("9b9a90"),
            GoblinRaidPhase.Marching => new Color("e15b45"),
            GoblinRaidPhase.Looting => new Color("d68ee8"),
            GoblinRaidPhase.Returning => new Color("66b7e8"),
            _ => new Color("f1f3e8"),
        };
        var radius = Math.Max(size.X, size.Y) * 0.56f;
        DrawArc(center, radius, 0f, Mathf.Tau, 40, color, 2.5f, true);

        var beacon = center + new Vector2(size.X * 0.42f, -size.Y * 0.42f);
        DrawCircle(beacon, 5.5f, new Color(0.03f, 0.04f, 0.03f, 0.88f));
        DrawCircle(beacon, 3.6f, color);
        if (_snapshot.RaidPhase == GoblinRaidPhase.Ready)
        {
            DrawArc(center, radius + 3.5f, 0f, Mathf.Tau, 40, color, 1.5f, true);
            DrawLine(beacon + new Vector2(-2.2f, 0f), beacon + new Vector2(-0.4f, 2f),
                new Color("182117"), 1.4f, true);
            DrawLine(beacon + new Vector2(-0.4f, 2f), beacon + new Vector2(2.5f, -2f),
                new Color("182117"), 1.4f, true);
        }
    }

    private void DrawHumanFields()
    {
        if (_visibleLevel != 0)
        {
            return;
        }

        foreach (var field in _snapshot.HumanVillage.Fields.Where(field =>
                     _snapshot.GetVisibility(field.Position, _engine.Map.Width) == CellVisibility.Visible))
        {
            var color = field.Phase switch
            {
                HumanFieldPhase.Cleared => new Color("76583a"),
                HumanFieldPhase.Sown => new Color("8a7042"),
                HumanFieldPhase.Growing => new Color("789548"),
                HumanFieldPhase.Ripe => new Color("d5b94f"),
                _ => Colors.Magenta,
            };
            var rect = CellRect(field.Position.X, field.Position.Y).Grow(-2f);
            var sprite = field.Phase switch
            {
                HumanFieldPhase.Cleared => TerrainSprite.ClearedField,
                HumanFieldPhase.Sown => TerrainSprite.SownField,
                HumanFieldPhase.Growing => TerrainSprite.GrowingField,
                HumanFieldPhase.Ripe => TerrainSprite.RipeField,
                _ => throw new ArgumentOutOfRangeException(),
            };
            DrawTextureRectRegion(
                _terrainAtlas,
                rect.Grow(2f),
                TerrainSprites.GetRegion(_terrainAtlas, sprite));
            DrawRect(rect, color with { A = 0.32f }, filled: false, width: 0.8f);
        }
    }

    private void DrawConstructionPreview()
    {
        foreach (var cell in _constructionPreview.Where(cell => cell.Z == _visibleLevel))
        {
            var color = _constructionPreviewValid
                ? new Color(0.95f, 0.75f, 0.28f, 0.7f)
                : new Color(0.92f, 0.2f, 0.2f, 0.72f);
            DrawRect(CellRect(cell.X, cell.Y).Grow(-2f), color, filled: false, width: 2f);
        }
    }

    private void DrawWorkDesignations()
    {
        foreach (var designation in _snapshot.WorkDesignations.Where(designation =>
                     designation.Target.Z == _visibleLevel))
        {
            var color = designation.Kind switch
            {
                WorkDesignationKind.GatherFood => new Color(0.55f, 0.9f, 0.28f, 0.72f),
                WorkDesignationKind.GatherReeds => new Color(0.72f, 0.86f, 0.32f, 0.78f),
                WorkDesignationKind.GatherBrushwood => new Color(0.72f, 0.46f, 0.22f, 0.78f),
                WorkDesignationKind.UprootBerryBush => new Color(0.92f, 0.3f, 0.2f, 0.82f),
                WorkDesignationKind.FellTree => new Color(0.95f, 0.72f, 0.18f, 0.86f),
                WorkDesignationKind.GatherStone => new Color(0.62f, 0.7f, 0.76f, 0.8f),
                WorkDesignationKind.QuarryBoulder => new Color(0.75f, 0.82f, 0.88f, 0.9f),
                WorkDesignationKind.MineRock => new Color(0.9f, 0.62f, 0.2f, 0.92f),
                WorkDesignationKind.CarveRampDown => new Color(0.3f, 0.66f, 0.95f, 0.94f),
                WorkDesignationKind.CarveRampUp => new Color(0.96f, 0.74f, 0.3f, 0.94f),
                WorkDesignationKind.Scout => new Color(0.38f, 0.78f, 0.94f, 0.68f),
                WorkDesignationKind.HuntAnimal => new Color(0.96f, 0.32f, 0.18f, 0.88f),
                WorkDesignationKind.CleanBlood => new Color(0.86f, 0.76f, 1f, 0.9f),
                _ => Colors.Magenta,
            };
            if (designation.IsSuspended)
            {
                color = color.Lerp(new Color(0.34f, 0.36f, 0.38f, 0.42f), 0.72f);
            }
            DrawRect(
                CellRect(designation.Target.X, designation.Target.Y).Grow(-4f),
                color,
                filled: false,
                width: designation.IsSuspended ? 0.5f : 0.7f);
        }
    }

    private void DrawWorkPreview()
    {
        var style = WorkToolCatalog.GetPreviewStyle(_workPreviewKind);
        foreach (var cell in _workPreview.Where(cell => cell.Z == _visibleLevel))
        {
            DrawRect(
                CellRect(cell.X, cell.Y).Grow(-style.Inset),
                style.Color,
                style.Filled,
                style.Width);
        }
    }

    private void DrawWorkAreaPreview()
    {
        if (_workAreaPreview is not { } area ||
            area.Start.Z != _visibleLevel || area.End.Z != _visibleLevel)
        {
            return;
        }

        var minimumX = Math.Min(area.Start.X, area.End.X);
        var maximumX = Math.Max(area.Start.X, area.End.X);
        var minimumY = Math.Min(area.Start.Y, area.End.Y);
        var maximumY = Math.Max(area.Start.Y, area.End.Y);
        var rect = new Rect2(
            minimumX * TileSize,
            minimumY * TileSize,
            (maximumX - minimumX + 1) * TileSize,
            (maximumY - minimumY + 1) * TileSize).Grow(-1.5f);
        var border = new Color(0.96f, 0.9f, 0.62f, 0.94f);

        DrawRect(rect, border with { A = 0.055f });
        DrawDashedLine(rect.Position, new Vector2(rect.End.X, rect.Position.Y), border, 1.5f, 5f);
        DrawDashedLine(new Vector2(rect.End.X, rect.Position.Y), rect.End, border, 1.5f, 5f);
        DrawDashedLine(rect.End, new Vector2(rect.Position.X, rect.End.Y), border, 1.5f, 5f);
        DrawDashedLine(new Vector2(rect.Position.X, rect.End.Y), rect.Position, border, 1.5f, 5f);
    }

    private void DrawActors()
    {
        var offsets = CreateActorOffsets();
        foreach (var group in _snapshot.Actors
                     .Where(actor => actor.Position.Z == _visibleLevel)
                     .GroupBy(actor => actor.Position))
        {
            var actors = group.OrderBy(actor => actor.Id).ToArray();
            for (var index = 0; index < actors.Length; index++)
            {
                var center = GetVisualActorPosition(actors[index]) + offsets[actors[index].Id];
                var healthRatio = (float)actors[index].Health / _engine.Definitions.MaximumHealth;
                var healthColor = new Color("b5443e").Lerp(new Color("a8d14b"), healthRatio);
                if (_selectedActorIds.Contains(actors[index].Id))
                {
                    DrawCircle(center, 6.5f, new Color("f5dc72"));
                }

                DrawArc(center, 4.8f, -Mathf.Pi / 2, Mathf.Tau - Mathf.Pi / 2, 20,
                    new Color("542e2b"), 1.8f);
                DrawArc(center, 4.8f, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * healthRatio, 20,
                    healthColor, 2.2f);
                DrawCircle(center, 3.6f, new Color("78a947"));
                DrawCircle(center + new Vector2(-1.2f, -0.6f), 0.65f, new Color("182117"));
                DrawCircle(center + new Vector2(1.2f, -0.6f), 0.65f, new Color("182117"));
                DrawCarriedResource(actors[index], center);
                DrawActorIntent(actors[index], center);
            }
        }
    }

    private void DrawCombatEffects()
    {
        foreach (var effect in _combatEffects.Where(effect => effect.Level == _visibleLevel))
        {
            var progress = Mathf.Clamp(
                (float)(effect.ElapsedSeconds / CombatEffectDurationSeconds), 0f, 1f);
            var direction = effect.To - effect.From;
            if (direction.LengthSquared() < 0.01f)
            {
                direction = Vector2.Right;
            }
            direction = direction.Normalized();

            if (effect.Ranged)
            {
                var flightProgress = Mathf.Clamp(progress / 0.72f, 0f, 1f);
                var position = effect.From.Lerp(effect.To, flightProgress);
                var arcOffset = new Vector2(0f, -Mathf.Sin(flightProgress * Mathf.Pi) * 5f);
                position += arcOffset;
                DrawLine(position - direction * 4f, position - direction * 1.5f,
                    new Color(0.72f, 0.68f, 0.58f, 0.55f), 1.2f, true);
                DrawCircle(position, 2.1f, new Color(0.12f, 0.11f, 0.1f, 0.92f));
                DrawCircle(position, 1.35f, new Color(0.72f, 0.7f, 0.64f));
            }
            else
            {
                var normal = new Vector2(-direction.Y, direction.X);
                var swing = Mathf.Sin(Mathf.Clamp(progress / 0.62f, 0f, 1f) * Mathf.Pi);
                var hand = effect.From + direction * 3f;
                var weaponTip = hand + direction * (4f + swing * 3f) + normal * (4f - swing * 8f);
                DrawLine(hand, weaponTip, new Color(0.16f, 0.11f, 0.08f), 2.5f, true);
                DrawLine(hand, weaponTip, new Color(0.78f, 0.66f, 0.42f), 1.15f, true);
            }

            if (progress >= 0.58f)
            {
                var impactProgress = (progress - 0.58f) / 0.42f;
                DrawArc(effect.To, 2f + impactProgress * 6f, 0f, Mathf.Tau, 16,
                    new Color(1f, 0.84f, 0.38f, 0.85f * (1f - impactProgress)), 1.5f, true);
            }
        }
    }

    private static bool TryGetCombatPositions(
        SimulationEvent simulationEvent,
        SimulationSnapshot snapshot,
        out Vector2 from,
        out Vector2 to,
        out int level,
        out bool ranged)
    {
        ActorSnapshot? goblin;
        HumanVillagerSnapshot? human;
        if (simulationEvent.Kind == SimulationEventKind.HumanGuardHitGoblin)
        {
            goblin = snapshot.Actors
                .Where(actor => actor.Id == simulationEvent.Target)
                .Select(actor => (ActorSnapshot?)actor)
                .FirstOrDefault();
            human = FindHumanVillager(snapshot, simulationEvent.Subject);
            ranged = false;
            if (goblin is not { } targetGoblin || human is not { } attackingHuman)
            {
                from = default;
                to = default;
                level = default;
                return false;
            }
            from = CellCenter(attackingHuman.Position);
            to = CellCenter(targetGoblin.Position);
            level = targetGoblin.Position.Z;
            return attackingHuman.Position.Z == level;
        }

        goblin = snapshot.Actors
            .Where(actor => actor.Id == simulationEvent.Subject)
            .Select(actor => (ActorSnapshot?)actor)
            .FirstOrDefault();
        human = FindHumanVillager(snapshot, simulationEvent.Target);
        if (goblin is not { } attackingGoblin || human is not { } targetHuman)
        {
            from = default;
            to = default;
            level = default;
            ranged = default;
            return false;
        }
        from = CellCenter(attackingGoblin.Position);
        to = CellCenter(targetHuman.Position);
        level = attackingGoblin.Position.Z;
        ranged = Math.Abs(attackingGoblin.Position.X - targetHuman.Position.X) +
            Math.Abs(attackingGoblin.Position.Y - targetHuman.Position.Y) > 1;
        return targetHuman.Position.Z == level;
    }

    private static HumanVillagerSnapshot? FindHumanVillager(
        SimulationSnapshot snapshot,
        EntityId entityId)
    {
        var villagerId = unchecked((int)(entityId.Value & uint.MaxValue));
        return snapshot.HumanVillage.Villagers
            .Where(villager => villager.Id == villagerId)
            .Select(villager => (HumanVillagerSnapshot?)villager)
            .FirstOrDefault();
    }

    private void PlayCombatSound(AudioStreamWav stream, ulong sequence)
    {
        var player = _combatAudioPlayers[_nextCombatAudioPlayer++ % _combatAudioPlayers.Length];
        player.Stop();
        player.Stream = stream;
        player.PitchScale = 0.94f + (sequence % 7) * 0.02f;
        player.Play();
    }

    private static AudioStreamWav CreateCombatSound(float frequency, float duration, float grit)
    {
        const int mixRate = 22_050;
        var sampleCount = Mathf.CeilToInt(duration * mixRate);
        var data = new byte[sampleCount * sizeof(short)];
        for (var index = 0; index < sampleCount; index++)
        {
            var time = (float)index / mixRate;
            var envelope = Mathf.Pow(1f - (float)index / sampleCount, 2f);
            var tone = Mathf.Sin(Mathf.Tau * frequency * time);
            var noise = Mathf.Sin(Mathf.Tau * frequency * 3.73f * time + index * 1.17f);
            var sample = (short)(Mathf.Clamp((tone * (1f - grit) + noise * grit) * envelope,
                -1f, 1f) * short.MaxValue * 0.55f);
            data[index * 2] = (byte)(sample & 0xff);
            data[index * 2 + 1] = (byte)((sample >> 8) & 0xff);
        }

        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = mixRate,
            Stereo = false,
            Data = data,
        };
    }

    private sealed class CombatEffect(Vector2 from, Vector2 to, int level, bool ranged)
    {
        public Vector2 From { get; } = from;

        public Vector2 To { get; } = to;

        public int Level { get; } = level;

        public bool Ranged { get; } = ranged;

        public double ElapsedSeconds { get; set; }
    }

    private void DrawCorpses()
    {
        foreach (var corpse in _snapshot.Corpses.Where(corpse =>
                     corpse.Position.Z == _visibleLevel &&
                     _snapshot.GetVisibility(corpse.Position, _engine.Map.Width) !=
                         CellVisibility.Unknown))
        {
            var center = CellCenter(corpse.Position);
            var color = corpse.Kind == CorpseKind.Goblin
                ? new Color("52693f")
                : new Color("765f4a");
            DrawCircle(center, 5.2f, new Color(0.05f, 0.04f, 0.03f, 0.72f));
            DrawCircle(center + new Vector2(0f, 1f), 4.1f, color);
            DrawLine(center + new Vector2(-2.2f, -1.2f), center + new Vector2(2.2f, 1.2f),
                new Color(0.16f, 0.11f, 0.09f, 0.95f), 1.3f, true);
            DrawLine(center + new Vector2(2.2f, -1.2f), center + new Vector2(-2.2f, 1.2f),
                new Color(0.16f, 0.11f, 0.09f, 0.95f), 1.3f, true);
        }
    }

    private void DrawGoblinBuds()
    {
        foreach (var bud in _snapshot.GoblinBuds.Where(bud =>
                     bud.Position.Z == _visibleLevel &&
                     _snapshot.GetVisibility(bud.Position, _engine.Map.Width) != CellVisibility.Unknown))
        {
            var center = CellCenter(bud.Position);
            var progress = 1f - (float)bud.RemainingCareTicks / bud.TotalCareTicks;
            DrawCircle(center + new Vector2(0, 2), 5.5f, new Color(0.15f, 0.23f, 0.12f, 0.9f));
            DrawCircle(center, 4.2f, bud.OriginCorpseId == EntityId.None
                ? new Color("91c95a")
                : new Color("b6c870"));
            DrawCircle(center + new Vector2(-2.5f, -3.5f), 1.5f, new Color("bfdc72"));
            DrawCircle(center + new Vector2(2.2f, -3f), 1.2f, new Color("bfdc72"));
            DrawArc(
                center,
                7f,
                -Mathf.Pi / 2,
                -Mathf.Pi / 2 + Mathf.Tau * progress,
                20,
                new Color("e4ef9a"),
                1.8f);
        }
    }

    private void DrawAnimals()
    {
        foreach (var animal in _snapshot.Animals.Where(animal =>
                     animal.Position.Z == _visibleLevel &&
                     _snapshot.GetVisibility(animal.Position, _engine.Map.Width) ==
                     CellVisibility.Visible))
        {
            var center = GetVisualAnimalPosition(animal);
            if (animal.Kind == AnimalKind.MarshHare)
            {
                DrawEllipse(center + new Vector2(0, 1), new Vector2(4.6f, 3.2f),
                    new Color("b9aa86"));
                DrawLine(center + new Vector2(-1.5f, -2), center + new Vector2(-2.2f, -6),
                    new Color("d5c9a8"), 1.8f);
                DrawLine(center + new Vector2(0.5f, -2), center + new Vector2(1.2f, -6),
                    new Color("d5c9a8"), 1.8f);
                DrawCircle(center + new Vector2(2.6f, -0.4f), 0.65f, new Color("241d18"));
            }
            else if (animal.Kind == AnimalKind.SwampBoar)
            {
                DrawEllipse(center, new Vector2(6.2f, 4.2f), new Color("665044"));
                DrawCircle(center + new Vector2(5, 1), 2.3f, new Color("7b5d4c"));
                DrawLine(center + new Vector2(6.4f, 1.8f), center + new Vector2(8, 0.5f),
                    new Color("e5d3a4"), 1.4f);
                if (animal.Activity == AnimalActivity.Threatening)
                {
                    DrawArc(center, 8f, 0, Mathf.Tau, 20, new Color("e15b45"), 1.8f);
                }
            }
            else
            {
                var size = new Vector2(TileSize, TileSize);
                DrawTextureRectRegion(
                    _undergroundFaunaAtlas,
                    new Rect2(center - (size / 2f), size),
                    UndergroundSprites.GetCaveSpiderRegion(_undergroundFaunaAtlas),
                    animal.Kind switch
                    {
                        AnimalKind.DeepCrawler => new Color("9d57bd"),
                        AnimalKind.MagmaWyrm => new Color("ef6b32"),
                        _ => Colors.White,
                    });
                if (animal.Activity == AnimalActivity.Threatening)
                {
                    DrawArc(center, 9f, 0, Mathf.Tau, 20, new Color("e15b45"), 1.8f);
                }
            }
        }
    }

    private void DrawEllipse(Vector2 center, Vector2 radius, Color color)
    {
        var points = new Vector2[20];
        for (var index = 0; index < points.Length; index++)
        {
            var angle = Mathf.Tau * index / points.Length;
            points[index] = center + new Vector2(
                Mathf.Cos(angle) * radius.X,
                Mathf.Sin(angle) * radius.Y);
        }
        DrawColoredPolygon(points, color);
    }

    private void DrawHumanCohorts()
    {
        if (_visibleLevel != 0)
        {
            return;
        }

        foreach (var villager in _snapshot.HumanVillage.Villagers.Where(villager =>
                     villager.Health > 0 &&
                     _snapshot.GetVisibility(villager.Position, _engine.Map.Width) ==
                         CellVisibility.Visible))
        {
            var center = CellCenter(villager.Position);
            var color = villager.Role switch
            {
                HumanCohortRole.Farmers => new Color("d7b54b"),
                HumanCohortRole.Workers => new Color("6ea3c7"),
                HumanCohortRole.Guards when _snapshot.HumanVillage.Hostility > 0 => new Color("d75a4a"),
                HumanCohortRole.Guards => new Color("b9c2c7"),
                _ => Colors.Magenta,
            };
            if (villager.Role == HumanCohortRole.Guards && _snapshot.HumanVillage.Hostility > 0)
            {
                DrawArc(center, 9f, 0, Mathf.Tau, 24, new Color(0.95f, 0.2f, 0.14f, 0.86f), 2f);
            }

            DrawCircle(center + new Vector2(0, 2), 4.5f, color.Darkened(0.18f));
            DrawCircle(center + new Vector2(0, -4), 3.2f, color);
            if (villager.Role == HumanCohortRole.Guards)
            {
                DrawLine(center + new Vector2(-4, -7), center + new Vector2(4, -7), new Color("704a3a"), 1.5f);
            }

            var toolIcon = villager.Role switch
            {
                HumanCohortRole.Farmers => ItemIcon.WoodenHoe,
                HumanCohortRole.Guards => ItemIcon.WoodenSpear,
                HumanCohortRole.Workers when villager.Tools.HasFlag(HumanTool.WoodenBucket) =>
                    ItemIcon.WoodenBucket,
                HumanCohortRole.Workers => ItemIcon.WoodenAxe,
                _ => ItemIcon.Unknown,
            };
            var toolCenter = center + new Vector2(8, -8);
            DrawCircle(toolCenter, 6.8f, new Color(0.05f, 0.07f, 0.06f, 0.86f));
            DrawTextureRectRegion(
                _itemIconAtlas,
                new Rect2(toolCenter - new Vector2(6, 6), new Vector2(12, 12)),
                ItemIcons.GetRegion(_itemIconAtlas, toolIcon));
        }
    }

    private void DrawNightLighting()
    {
        if (_visibleLevel != 0)
        {
            return;
        }

        var calendar = SimulationCalendar.At(_snapshot.Tick, _engine.Definitions.Clock);
        if (!calendar.IsNight)
        {
            return;
        }

        var nightTick = calendar.TickOfDay - calendar.DaylightTicks;
        var twilightTicks = Math.Min(600, calendar.NightTicks / 3);
        var fadeIn = Math.Clamp((double)nightTick / twilightTicks, 0d, 1d);
        var fadeOut = Math.Clamp((double)(calendar.NightTicks - nightTick) / twilightTicks, 0d, 1d);
        var darkness = (float)(0.34d * Math.Min(fadeIn, fadeOut));
        DrawRect(
            new Rect2(Vector2.Zero, WorldSize),
            new Color(0.025f, 0.055f, 0.12f, darkness));

        foreach (var villager in _snapshot.HumanVillage.Villagers.Where(villager =>
                     villager.Health > 0 &&
                     _snapshot.GetVisibility(villager.Position, _engine.Map.Width) ==
                         CellVisibility.Visible))
        {
            var center = CellCenter(villager.Position);
            DrawCircle(center, TileSize * 1.7f, new Color(1f, 0.63f, 0.18f, darkness * 0.16f));
            DrawCircle(center + new Vector2(5f, -5f), 1.8f, new Color(1f, 0.78f, 0.3f, 0.9f));
        }

        foreach (var torch in _snapshot.WorldObjects.Where(worldObject =>
                     worldObject.Kind == WorldObjectKind.WallTorch &&
                     worldObject.Anchor.Z == 0 &&
                     _snapshot.GetVisibility(worldObject.Anchor, _engine.Map.Width) !=
                         CellVisibility.Unknown))
        {
            var center = CellCenter(torch.Anchor);
            DrawCircle(center, TileSize * 1.9f,
                new Color(1f, 0.48f, 0.12f, darkness * 0.24f));
            DrawCircle(center, 2.3f, new Color(1f, 0.76f, 0.24f, 0.95f));
        }
    }

    private void DrawStorageAreas()
    {
        foreach (var area in _snapshot.StorageAreas.Where(area =>
                     area.Footprint.Any(cell => cell.Z == _visibleLevel)))
        {
            var color = area.LogisticsNetworkId == EntityId.None
                ? new Color(0.35f, 0.55f, 0.3f, 0.16f)
                : new Color(0.28f, 0.48f, 0.68f, 0.2f);
            foreach (var cell in area.Footprint.Where(cell =>
                         cell.Z == _visibleLevel &&
                         _snapshot.GetVisibility(cell, _engine.Map.Width) !=
                             CellVisibility.Unknown))
            {
                var rect = CellRect(cell.X, cell.Y).Grow(-1f);
                DrawRect(rect, color);
                DrawDashedLine(rect.Position, new Vector2(rect.End.X, rect.Position.Y),
                    new Color("879b68"), width: 1f, dash: 3f);
                DrawDashedLine(rect.End, new Vector2(rect.Position.X, rect.End.Y),
                    new Color("879b68"), width: 1f, dash: 3f);
            }
        }
    }

    private void DrawStorageZones()
    {
        foreach (var zone in _snapshot.StorageZones.Where(zone => zone.Position.Z == _visibleLevel))
        {
            var rect = CellRect(zone.Position.X, zone.Position.Y).Grow(-2f);
            var zoneColor = zone.AcceptedResource switch
            {
                ResourceKind.Wood => new Color(0.34f, 0.22f, 0.12f, 0.68f),
                ResourceKind.Water => new Color(0.28f, 0.18f, 0.1f, 0.92f),
                _ => new Color(0.2f, 0.32f, 0.23f, 0.62f),
            };
            DrawRect(rect, zoneColor);
            if (zone.StoredQuantity > 0 &&
                _snapshot.GetVisibility(zone.Position, _engine.Map.Width) == CellVisibility.Visible)
            {
                var fillHeight = rect.Size.Y * zone.StoredQuantity / zone.Capacity;
                DrawRect(
                    new Rect2(rect.Position.X, rect.End.Y - fillHeight, rect.Size.X, fillHeight),
                    zone.AcceptedResource == ResourceKind.Water
                        ? new Color(0.25f, 0.65f, 0.82f, 0.86f)
                        : new Color(0.84f, 0.58f, 0.24f, 0.82f));
            }

            DrawRect(rect, new Color("d8c379"), filled: false, width: 1.5f);
            if (zone.ProviderKind == StorageProviderKind.WaterBarrel)
            {
                DrawLine(
                    new Vector2(rect.Position.X, rect.GetCenter().Y),
                    new Vector2(rect.End.X, rect.GetCenter().Y),
                    new Color("d8c379"),
                    1f);
            }
            else if (zone.ProviderKind == StorageProviderKind.WoodenBox)
            {
                DrawLine(rect.Position, rect.End, new Color("d8c379"), 1f);
                DrawLine(
                    new Vector2(rect.End.X, rect.Position.Y),
                    new Vector2(rect.Position.X, rect.End.Y),
                    new Color("d8c379"),
                    1f);
            }
            else if (zone.ProviderKind == StorageProviderKind.WoodenChest)
            {
                DrawLine(
                    new Vector2(rect.Position.X, rect.GetCenter().Y - 2f),
                    new Vector2(rect.End.X, rect.GetCenter().Y - 2f),
                    new Color("d8c379"),
                    1.5f);
                DrawCircle(rect.GetCenter() + new Vector2(0f, 2f), 1.8f, new Color("39281b"));
            }
            else if (zone.ProviderKind == StorageProviderKind.WoodenBulkBin)
            {
                DrawLine(
                    new Vector2(rect.GetCenter().X - 3f, rect.Position.Y),
                    new Vector2(rect.GetCenter().X - 3f, rect.End.Y),
                    new Color("d8c379"),
                    1f);
                DrawLine(
                    new Vector2(rect.GetCenter().X + 3f, rect.Position.Y),
                    new Vector2(rect.GetCenter().X + 3f, rect.End.Y),
                    new Color("d8c379"),
                    1f);
            }
        }
    }

    private void DrawItems()
    {
        foreach (var stack in _snapshot.ItemStacks.Where(stack =>
                     stack.Location.Kind == ItemLocationKind.Ground &&
                     stack.Location.Position.Z == _visibleLevel &&
                     _snapshot.GetVisibility(stack.Location.Position, _engine.Map.Width) == CellVisibility.Visible))
        {
            var center = CellCenter(stack.Location.Position);
            var size = 11f + Math.Min(5f, stack.Quantity / 4f);
            var icon = ItemIcons.ForResource(stack.Resource);
            var isStoneCluster = icon == ItemIcon.Stone;
            var dimensions = isStoneCluster
                ? new Vector2(size, size * 0.86f)
                : new Vector2(size, size);
            var visualCenter = center + (isStoneCluster
                ? new Vector2(0f, -0.5f)
                : Vector2.Zero);
            var destination = new Rect2(visualCenter - dimensions / 2f, dimensions);
            DrawCircle(center + new Vector2(0.5f, 1f), size * 0.43f,
                new Color(0, 0, 0, 0.46f));

            if (stack.Resource is ResourceKind.Food or ResourceKind.Wood)
            {
                DrawTextureRect(
                    ResourceThumbnails.Create(
                        _itemIconAtlas,
                        _treePartAtlas,
                        _foodIconAtlas,
                        _materialPaletteTextures,
                        stack.Resource,
                        stack.FoodKind,
                        stack.Variant),
                    destination,
                    tile: false);
            }
            else if (isStoneCluster && stack.Variant != ResourceVariant.None)
            {
                DrawTextureRect(
                    _materialPaletteTextures.Get(
                        _itemIconAtlas,
                        GetNeutralStoneRegion(),
                        stack.Variant,
                        MaterialPaletteTextureProfile.CompleteSurface),
                    destination,
                    tile: false);
            }
            else
            {
                DrawTextureRectRegion(
                    _itemIconAtlas,
                    destination,
                    isStoneCluster
                        ? GetNeutralStoneRegion()
                        : ItemIcons.GetRegion(_itemIconAtlas, icon),
                    ItemIcons.TintForResource(stack.Resource));
            }
        }
    }

    private void DrawCarriedResource(ActorSnapshot actor, Vector2 actorCenter)
    {
        if (actor.CarriedStackId == EntityId.None)
        {
            return;
        }

        var stack = _snapshot.ItemStacks.FirstOrDefault(item => item.Id == actor.CarriedStackId);
        if (stack.Id == EntityId.None)
        {
            return;
        }

        var center = actorCenter + new Vector2(6.5f, -6.5f);
        var size = new Vector2(9f, 9f);
        DrawCircle(center, 5.6f, new Color(0.04f, 0.05f, 0.035f, 0.88f));
        DrawTextureRect(
            ResourceThumbnails.Create(
                _itemIconAtlas,
                _treePartAtlas,
                _foodIconAtlas,
                _materialPaletteTextures,
                stack.Resource,
                stack.FoodKind,
                stack.Variant),
            new Rect2(center - size / 2f, size),
            tile: false);
    }

    private void DrawJobTargets()
    {
        foreach (var actor in _snapshot.Actors.Where(actor =>
                     actor.Position.Z == _visibleLevel &&
                     actor.Job.Target.Z == _visibleLevel &&
                     actor.Job.Kind != ActorJobKind.None))
        {
            var from = GetVisualActorPosition(actor);
            var target = CellCenter(actor.Job.Target);
            var color = actor.Job.Kind switch
            {
                ActorJobKind.Haul => new Color(0.96f, 0.62f, 0.25f, 0.72f),
                ActorJobKind.SupplyConstruction => new Color(0.98f, 0.68f, 0.25f, 0.82f),
                ActorJobKind.BuildConstruction => new Color(0.38f, 0.82f, 0.92f, 0.82f),
                ActorJobKind.SupplyCrafting => new Color(0.78f, 0.58f, 0.34f, 0.82f),
                ActorJobKind.Craft => new Color(0.72f, 0.82f, 0.42f, 0.82f),
                ActorJobKind.Rest => new Color(0.48f, 0.72f, 0.96f, 0.72f),
                ActorJobKind.Collapsed => new Color(0.34f, 0.42f, 0.64f, 0.78f),
                ActorJobKind.Eat => new Color(0.96f, 0.38f, 0.48f, 0.76f),
                ActorJobKind.Explore => new Color(0.78f, 0.82f, 0.86f, 0.72f),
                ActorJobKind.Move => new Color(0.98f, 0.84f, 0.34f, 0.82f),
                ActorJobKind.Resupply => new Color(0.36f, 0.78f, 0.92f, 0.78f),
                ActorJobKind.CleanBlood => new Color(0.86f, 0.76f, 1f, 0.84f),
                _ => new Color(0.86f, 0.93f, 0.45f, 0.58f),
            };
            DrawDashedLine(from, target, color with { A = 0.28f }, 1f, 5f);
            DrawArc(target, 6f, 0, Mathf.Tau, 16, color, 1.5f);
        }
    }

    private void DrawConstructionSites()
    {
        foreach (var site in _snapshot.ConstructionSites.Where(site =>
                     site.Anchor.Z == _visibleLevel))
        {
            var materialRequired = site.Materials.Sum(material => material.RequiredQuantity);
            var materialDelivered = site.Materials.Sum(material => material.DeliveredQuantity);
            var materialProgress = materialRequired == 0
                ? 1f
                : (float)materialDelivered / materialRequired;
            var workProgress = site.TotalWorkTicks == 0
                ? 1f
                : 1f - ((float)site.RemainingWorkTicks / site.TotalWorkTicks);
            var color = site.HasAllMaterials
                ? new Color(0.32f, 0.82f, 0.9f, 0.82f)
                : new Color(0.98f, 0.67f, 0.24f, 0.82f);

            foreach (var position in site.Footprint.Where(position =>
                         position.Z == _visibleLevel &&
                         _snapshot.GetVisibility(position, _engine.Map.Width).IsDiscovered()))
            {
                var rect = CellRect(position.X, position.Y).Grow(-2f);
                DrawRect(rect, color with { A = 0.18f });
                DrawRect(rect, color, filled: false, width: 1.5f);
            }

            var progressRect = CellRect(site.Anchor.X, site.Anchor.Y).Grow(-3f);
            var barPosition = progressRect.Position + new Vector2(1f, progressRect.Size.Y - 5f);
            var barWidth = progressRect.Size.X - 2f;
            DrawRect(new Rect2(barPosition, new Vector2(barWidth, 2f)), new Color(0.06f, 0.07f, 0.06f, 0.88f));
            DrawRect(new Rect2(barPosition, new Vector2(barWidth * materialProgress, 2f)), new Color("e5a547"));
            DrawRect(new Rect2(barPosition + new Vector2(0f, 3f), new Vector2(barWidth, 2f)), new Color(0.06f, 0.07f, 0.06f, 0.88f));
            DrawRect(new Rect2(barPosition + new Vector2(0f, 3f), new Vector2(barWidth * workProgress, 2f)), new Color("65cadc"));
        }
    }

    private void DrawCraftingOrders()
    {
        foreach (var order in _snapshot.CraftingOrders.Where(order =>
                     order.Workshop.Z == _visibleLevel))
        {
            var required = order.Materials.Sum(material => material.RequiredQuantity);
            var delivered = order.Materials.Sum(material => material.DeliveredQuantity);
            var materialProgress = required == 0 ? 1f : (float)delivered / required;
            var workProgress = order.TotalWorkTicks == 0
                ? 1f
                : 1f - ((float)order.RemainingWorkTicks / order.TotalWorkTicks);
            var rect = CellRect(order.Workshop.X, order.Workshop.Y).Grow(-3f);
            var position = rect.Position + new Vector2(1f, 2f);
            var width = rect.Size.X - 2f;
            DrawRect(new Rect2(position, new Vector2(width, 2f)), new Color(0.06f, 0.07f, 0.06f, 0.88f));
            DrawRect(new Rect2(position, new Vector2(width * materialProgress, 2f)), new Color("d59b62"));
            DrawRect(new Rect2(position + new Vector2(0f, 3f), new Vector2(width, 2f)), new Color(0.06f, 0.07f, 0.06f, 0.88f));
            DrawRect(new Rect2(position + new Vector2(0f, 3f), new Vector2(width * workProgress, 2f)), new Color("a8cc68"));
        }
    }

    private void DrawFog()
    {
        var bounds = GetVisibleCellBounds();
        for (var y = bounds.MinimumY; y < bounds.MaximumY; y++)
        {
            for (var x = bounds.MinimumX; x < bounds.MaximumX; x++)
            {
                var position = new GridPosition(x, y, _visibleLevel);
                var visibility = GetRenderedVisibility(position);
                var color = visibility switch
                {
                    CellVisibility.Unknown => new Color(0.035f, 0.045f, 0.04f, 0.97f),
                    CellVisibility.Explored => new Color(0.04f, 0.06f, 0.05f, 0.58f),
                    _ => Colors.Transparent,
                };
                if (color.A > 0)
                {
                    DrawRect(CellRect(x, y), color);
                }
            }
        }
    }

    private CellVisibility GetRenderedVisibility(GridPosition position)
    {
        var visibility = GetColumnVisibility(position);
        if (!_engine.Map.IsHillMassPosition(position))
        {
            return visibility;
        }

        ReadOnlySpan<(int X, int Y)> offsets =
            [(0, -1), (1, 0), (0, 1), (-1, 0)];
        foreach (var offset in offsets)
        {
            var neighbor = new GridPosition(
                position.X + offset.X,
                position.Y + offset.Y,
                position.Z);
            if (!_engine.Map.IsColumnWithin(neighbor) ||
                _engine.Map.IsHillMassPosition(neighbor))
            {
                continue;
            }

            var neighborVisibility = GetColumnVisibility(neighbor);
            if (neighborVisibility > visibility)
            {
                visibility = neighborVisibility;
            }
        }

        return visibility;
    }

    private CellVisibility GetColumnVisibility(GridPosition position)
    {
        var visibility = TryGetSnapshotVisibility(position, out var directVisibility)
            ? directVisibility
            : CellVisibility.Unknown;
        var surfaceLevel = _engine.Map.GetColumnCell(position).SurfaceLevel;
        if (surfaceLevel != position.Z &&
            TryGetSnapshotVisibility(
                new GridPosition(position.X, position.Y, surfaceLevel),
                out var surfaceVisibility) &&
            surfaceVisibility > visibility)
        {
            visibility = surfaceVisibility;
        }

        return visibility;
    }

    private bool TryGetSnapshotVisibility(
        GridPosition position,
        out CellVisibility visibility)
    {
        visibility = CellVisibility.Unknown;
        if (!_engine.Map.IsColumnWithin(position) ||
            _snapshot.VisibilityLayerCellCount <= 0)
        {
            return false;
        }

        var layerCount = _snapshot.Visibility.Count / _snapshot.VisibilityLayerCellCount;
        var positiveLevelCount = layerCount - _snapshot.VisibilityNegativeLevelCount - 1;
        if (position.Z < -_snapshot.VisibilityNegativeLevelCount ||
            position.Z > positiveLevelCount)
        {
            return false;
        }

        visibility = _snapshot.GetVisibility(position, _engine.Map.Width);
        return true;
    }

    private void DrawOrderedDestination()
    {
        var selected = _snapshot.Actors.FirstOrDefault(actor => _selectedActorIds.Contains(actor.Id));
        if (selected.Id == EntityId.None ||
            selected.Job.Kind != ActorJobKind.Move ||
            selected.Job.Target.Z != _visibleLevel)
        {
            return;
        }

        var center = CellCenter(selected.Job.Target);
        var color = new Color("f5dc72");
        DrawArc(center, 7f, 0, Mathf.Tau, 20, color, 2f);
        DrawLine(center + new Vector2(-4, 0), center + new Vector2(4, 0), color, 1.5f);
        DrawLine(center + new Vector2(0, -4), center + new Vector2(0, 4), color, 1.5f);
    }

    private void DrawActorIntent(ActorSnapshot actor, Vector2 actorCenter)
    {
        if (actor.Job.Kind == ActorJobKind.None)
        {
            return;
        }

        var center = actorCenter + new Vector2(0, -13);
        var icon = actor.Job.Kind switch
        {
            ActorJobKind.Forage => UiIcon.GatherFood,
            ActorJobKind.ClearVegetation => UiIcon.UprootBush,
            ActorJobKind.FellTree => UiIcon.FellTree,
            ActorJobKind.QuarryBoulder => UiIcon.Work,
            ActorJobKind.MineRock => UiIcon.Work,
            ActorJobKind.CarveRamp => UiIcon.Work,
            ActorJobKind.TendBud => UiIcon.GatherFood,
            ActorJobKind.HuntAnimal => UiIcon.Expedition,
            ActorJobKind.LootRaid => UiIcon.FoodStorage,
            ActorJobKind.RecoverRaidCorpse => UiIcon.Expedition,
            ActorJobKind.ConsumeRaidCorpse => UiIcon.Hunger,
            ActorJobKind.CleanBlood => UiIcon.ClearOrders,
            ActorJobKind.Haul => UiIcon.FoodStorage,
            ActorJobKind.ClearConstructionSite => UiIcon.GatherBrushwood,
            ActorJobKind.SupplyConstruction => UiIcon.GatherBrushwood,
            ActorJobKind.BuildConstruction => UiIcon.Build,
            ActorJobKind.SupplyCrafting => UiIcon.GatherBrushwood,
            ActorJobKind.Craft => UiIcon.Build,
            ActorJobKind.Rest => UiIcon.FieldCamp,
            ActorJobKind.Collapsed => UiIcon.FieldCamp,
            ActorJobKind.Eat => UiIcon.Hunger,
            ActorJobKind.Explore or ActorJobKind.Move => UiIcon.Expedition,
            ActorJobKind.Resupply when actor.Job.Stage == ActorJobStage.ProvisioningWater =>
                UiIcon.Thirst,
            ActorJobKind.Resupply => UiIcon.FoodStorage,
            _ => UiIcon.Work,
        };
        DrawCircle(center, 8.2f, new Color(0.05f, 0.07f, 0.06f, 0.9f));
        DrawTextureRectRegion(
            _iconAtlas,
            new Rect2(center - new Vector2(7, 7), new Vector2(14, 14)),
            UiIcons.GetRegion(icon));

        var phaseColor = actor.Job.Phase == ActorJobPhase.Traveling
            ? new Color("f5dc72")
            : new Color("f1f3e8");
        DrawCircle(center + new Vector2(6.3f, 6.3f), 1.5f, phaseColor);
    }

    private void SynchronizeActorPositions()
    {
        var livingIds = _snapshot.Actors.Select(actor => actor.Id).ToHashSet();
        foreach (var id in _visualActorPositions.Keys.Where(id => !livingIds.Contains(id)).ToArray())
        {
            _visualActorPositions.Remove(id);
            _targetActorPositions.Remove(id);
        }

        foreach (var actor in _snapshot.Actors)
        {
            var target = CellCenter(actor.Position);
            _targetActorPositions[actor.Id] = target;
            _visualActorPositions.TryAdd(actor.Id, target);
        }
    }

    private void SynchronizeAnimalPositions()
    {
        var livingIds = _snapshot.Animals.Select(animal => animal.Id).ToHashSet();
        foreach (var id in _visualAnimalPositions.Keys.Where(id => !livingIds.Contains(id)).ToArray())
        {
            _visualAnimalPositions.Remove(id);
            _targetAnimalPositions.Remove(id);
        }

        foreach (var animal in _snapshot.Animals)
        {
            var target = CellCenter(animal.Position);
            _targetAnimalPositions[animal.Id] = target;
            _visualAnimalPositions.TryAdd(animal.Id, target);
        }
    }

    private Dictionary<EntityId, Vector2> CreateActorOffsets()
    {
        var result = new Dictionary<EntityId, Vector2>();
        foreach (var group in _snapshot.Actors.GroupBy(actor => actor.Position))
        {
            var actors = group.OrderBy(actor => actor.Id).ToArray();
            for (var index = 0; index < actors.Length; index++)
            {
                var angle = Mathf.Tau * index / actors.Length;
                result[actors[index].Id] = actors.Length == 1
                    ? Vector2.Zero
                    : new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 5f;
            }
        }

        return result;
    }

    private Vector2 GetVisualActorPosition(ActorSnapshot actor) =>
        _visualActorPositions.GetValueOrDefault(actor.Id, CellCenter(actor.Position));

    private Vector2 GetVisualAnimalPosition(AnimalSnapshot animal) =>
        _visualAnimalPositions.GetValueOrDefault(animal.Id, CellCenter(animal.Position));

    private (int MinimumX, int MinimumY, int MaximumX, int MaximumY) GetVisibleCellBounds(
        int padding = 2)
    {
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var viewportToLocal = GetGlobalTransformWithCanvas().AffineInverse();
        var first = viewportToLocal * Vector2.Zero;
        var second = viewportToLocal * viewportSize;
        var minimumX = Math.Clamp(
            Mathf.FloorToInt(Math.Min(first.X, second.X) / TileSize) - padding,
            0,
            _engine.Map.Width);
        var minimumY = Math.Clamp(
            Mathf.FloorToInt(Math.Min(first.Y, second.Y) / TileSize) - padding,
            0,
            _engine.Map.Height);
        var maximumX = Math.Clamp(
            Mathf.CeilToInt(Math.Max(first.X, second.X) / TileSize) + padding,
            0,
            _engine.Map.Width);
        var maximumY = Math.Clamp(
            Mathf.CeilToInt(Math.Max(first.Y, second.Y) / TileSize) + padding,
            0,
            _engine.Map.Height);
        return (minimumX, minimumY, maximumX, maximumY);
    }

    private bool HasVisibleAnimatedWater()
    {
        var bounds = GetVisibleCellBounds(padding: 0);
        for (var y = bounds.MinimumY; y < bounds.MaximumY; y++)
        {
            for (var x = bounds.MinimumX; x < bounds.MaximumX; x++)
            {
                var position = new GridPosition(x, y, _visibleLevel);
                if (_visibleLevel < 0)
                {
                    if (_engine.World.TryGetFluid(position, out var fluid, out _) &&
                        fluid != CellFluidKind.None)
                    {
                        return true;
                    }
                    continue;
                }

                var cell = _engine.Map.GetColumnCell(position);
                if (cell.SurfaceLevel == _visibleLevel &&
                    cell.Terrain is TerrainKind.ShallowWater or TerrainKind.DeepWater)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private IReadOnlySet<GridPosition> GetCachedCaveSolids()
    {
        var topologyVersion = _snapshotTopologyVersion;
        if (_cachedCaveSolids.TryGetValue(_visibleLevel, out var cached) &&
            cached.TopologyVersion == topologyVersion)
        {
            return cached.Solids;
        }

        var solids = new HashSet<GridPosition>();
        for (var y = 0; y < _engine.Map.Height; y++)
        {
            for (var x = 0; x < _engine.Map.Width; x++)
            {
                var position = new GridPosition(x, y, _visibleLevel);
                if (_engine.World.IsSolidCaveRock(position))
                {
                    solids.Add(position);
                }
            }
        }

        _cachedCaveSolids[_visibleLevel] = (topologyVersion, solids);
        return solids;
    }

    private StructureRenderCache GetStructureRenderCache()
    {
        if (_structureRenderCaches.TryGetValue(_visibleLevel, out var cached) &&
            cached.TopologyVersion == _snapshotTopologyVersion)
        {
            return cached;
        }

        var woodenWalls = _snapshot.WorldObjects
            .Where(worldObject => worldObject.Kind == WorldObjectKind.WoodenWall &&
                worldObject.Anchor.Z == _visibleLevel)
            .ToDictionary(worldObject => worldObject.Anchor);
        var stoneWalls = _snapshot.WorldObjects
            .Where(worldObject => worldObject.Kind == WorldObjectKind.StoneWall &&
                worldObject.Anchor.Z == _visibleLevel)
            .ToDictionary(worldObject => worldObject.Anchor);
        var doorFrames = _snapshot.WorldObjects
            .Where(worldObject => worldObject.Kind is (WorldObjectKind.WoodenDoorFrame or
                    WorldObjectKind.StoneDoorFrame) &&
                worldObject.Anchor.Z == _visibleLevel)
            .ToDictionary(worldObject => worldObject.Anchor);
        var doorLeaves = _snapshot.WorldObjects
            .Where(worldObject => worldObject.Kind == WorldObjectKind.WoodenDoorLeaf &&
                worldObject.Anchor.Z == _visibleLevel)
            .ToDictionary(worldObject => worldObject.Anchor);
        var wallTorches = _snapshot.WorldObjects
            .Where(worldObject => worldObject.Kind == WorldObjectKind.WallTorch &&
                worldObject.Anchor.Z == _visibleLevel)
            .ToArray();
        var walkwayCells = _snapshot.WorldObjects
            .Where(worldObject => worldObject.Kind == WorldObjectKind.WoodenWalkway)
            .SelectMany(worldObject => worldObject.GetAbsoluteParts())
            .Where(item => item.Position.Z == _visibleLevel &&
                item.Part.Kind == WorldObjectPartKind.Walkway)
            .Select(item => item.Position)
            .ToHashSet();
        var basaltWalkwayCells = _snapshot.WorldObjects
            .Where(worldObject => worldObject.Kind == WorldObjectKind.BasaltWalkway)
            .SelectMany(worldObject => worldObject.GetAbsoluteParts())
            .Where(item => item.Position.Z == _visibleLevel &&
                item.Part.Kind == WorldObjectPartKind.Walkway)
            .Select(item => item.Position)
            .ToHashSet();
        var connectedCells = woodenWalls.Keys.Concat(stoneWalls.Keys)
            .Concat(doorFrames.Keys)
            .ToHashSet();
        var structuralSolids = _snapshot.WorldObjects
            .Where(worldObject => worldObject.Kind is not (
                WorldObjectKind.WoodenWall or WorldObjectKind.StoneWall or
                WorldObjectKind.WoodenDoorFrame or WorldObjectKind.StoneDoorFrame or
                WorldObjectKind.WoodenDoorLeaf))
            .SelectMany(worldObject => worldObject.GetAbsoluteParts())
            .Where(item => item.Position.Z == _visibleLevel &&
                item.Part.Kind == WorldObjectPartKind.Wall)
            .Select(item => item.Position)
            .ToHashSet();
        if (_visibleLevel < 0)
        {
            structuralSolids.UnionWith(GetCachedCaveSolids());
        }

        var enclosure = connectedCells.Count == 0
            ? null
            : WallEnclosureAnalysis.Analyze(
                _engine.Map.Width,
                _engine.Map.Height,
                connectedCells,
                structuralSolids,
                _visibleLevel);
        cached = new StructureRenderCache(
            _snapshotTopologyVersion,
            walkwayCells,
            basaltWalkwayCells,
            woodenWalls,
            stoneWalls,
            doorFrames,
            doorLeaves,
            wallTorches,
            connectedCells,
            enclosure);
        _structureRenderCaches[_visibleLevel] = cached;
        return cached;
    }

    private sealed record StructureRenderCache(
        ulong TopologyVersion,
        HashSet<GridPosition> WalkwayCells,
        HashSet<GridPosition> BasaltWalkwayCells,
        Dictionary<GridPosition, WorldObjectSnapshot> WoodenWalls,
        Dictionary<GridPosition, WorldObjectSnapshot> StoneWalls,
        Dictionary<GridPosition, WorldObjectSnapshot> DoorFrames,
        Dictionary<GridPosition, WorldObjectSnapshot> DoorLeaves,
        WorldObjectSnapshot[] WallTorches,
        HashSet<GridPosition> ConnectedCells,
        WallEnclosureAnalysis? Enclosure);

    private static Rect2 CellRect(int x, int y) => new(
        x * TileSize,
        y * TileSize,
        TileSize + 0.5f,
        TileSize + 0.5f);

    private static Vector2 CellCenter(GridPosition position) => new(
        (position.X + 0.5f) * TileSize,
        (position.Y + 0.5f) * TileSize);
}
