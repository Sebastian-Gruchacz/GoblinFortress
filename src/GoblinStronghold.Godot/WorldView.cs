using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.GodotClient;

public partial class WorldView : Node2D
{
    private const float TileSize = 20f;
    private readonly Dictionary<EntityId, Vector2> _visualActorPositions = [];
    private readonly Dictionary<EntityId, Vector2> _targetActorPositions = [];
    private SimulationEngine _engine = null!;
    private SimulationSnapshot _snapshot = null!;
    private EntityId _selectedActorId = EntityId.None;
    private int _simulationSpeed = 1;
    private double _secondsPerTick = 0.1;
    private IReadOnlyList<GridPosition> _constructionPreview = [];
    private WorkDesignationKind _workPreviewKind;
    private IReadOnlyList<GridPosition> _workPreview = [];
    private Texture2D _iconAtlas = null!;
    private Texture2D _itemIconAtlas = null!;
    private Texture2D _environmentAtlas = null!;
    private int _visibleLevel;

    public int VisibleLevel => _visibleLevel;

    public override void _Ready()
    {
        _iconAtlas = UiIcons.LoadAtlas();
        _itemIconAtlas = ItemIcons.LoadAtlas();
        _environmentAtlas = EnvironmentSprites.LoadAtlas();
    }

    public void SetWorld(SimulationEngine engine)
    {
        _engine = engine;
        Refresh(engine.CreateSnapshot());
    }

    public void Refresh(SimulationSnapshot snapshot)
    {
        _snapshot = snapshot;
        SynchronizeActorPositions();
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

    public void SetSelectedActor(EntityId actorId)
    {
        _selectedActorId = actorId;
        QueueRedraw();
    }

    public void SetConstructionPreview(IReadOnlyList<GridPosition> cells)
    {
        _constructionPreview = cells;
        QueueRedraw();
    }

    public void SetWorkPreview(WorkDesignationKind kind, IReadOnlyList<GridPosition> cells)
    {
        _workPreviewKind = kind;
        _workPreview = cells;
        QueueRedraw();
    }

    public GridPosition WorldToCell(Vector2 position) => new(
        Mathf.FloorToInt(position.X / TileSize),
        Mathf.FloorToInt(position.Y / TileSize));

    public Vector2 CellToWorld(GridPosition position) => CellCenter(position);

    public override void _Process(double delta)
    {
        if (_engine is null || _simulationSpeed == 0 || _visualActorPositions.Count == 0)
        {
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

        if (changed)
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
        DrawPlants();
        DrawHumanFields();
        DrawStructures();
        DrawHumanCohorts();
        DrawStorageZones();
        DrawItems();
        DrawJobTargets();
        DrawActors();
        DrawFog();
        DrawWorkDesignations();
        DrawWorkPreview();
        DrawOrderedDestination();
        DrawConstructionPreview();
    }

    private void DrawTerrain()
    {
        for (var y = 0; y < _engine.Map.Height; y++)
        {
            for (var x = 0; x < _engine.Map.Width; x++)
            {
                var cell = _engine.Map.GetCell(new GridPosition(x, y));
                var color = _visibleLevel == 0
                    ? cell.Terrain switch
                    {
                        TerrainKind.SolidGround => new Color("718b55"),
                        TerrainKind.Mud => new Color("596b46"),
                        TerrainKind.ShallowWater => new Color("4d8790"),
                        TerrainKind.DeepWater => new Color("315d73"),
                        _ => Colors.Magenta,
                    }
                    : cell.FloorLevel == _visibleLevel
                        ? new Color("35443d")
                        : new Color("101719");
                DrawRect(CellRect(x, y), color);
            }
        }
    }

    private void DrawPlants()
    {
        if (_visibleLevel != 0)
        {
            return;
        }

        foreach (var plant in _snapshot.PlantPatches.Where(item =>
                     item.Biomass > 0 || item.Kind == PlantKind.BerryBush))
        {
            var center = CellCenter(plant.Position);
            var sprite = plant.Kind switch
            {
                PlantKind.BerryBush when plant.Biomass > 0 =>
                    EnvironmentSprite.FruitingBerryBush,
                PlantKind.BerryBush => EnvironmentSprite.BareBerryBush,
                PlantKind.MushroomCluster => EnvironmentSprite.MushroomCluster,
                PlantKind.EdibleRoots => EnvironmentSprite.EdibleRoots,
                PlantKind.FishShoal => EnvironmentSprite.FishShoal,
                _ => throw new ArgumentOutOfRangeException(),
            };
            var size = plant.Kind == PlantKind.FishShoal ? 25f : 22f;
            DrawTextureRectRegion(
                _environmentAtlas,
                new Rect2(center - new Vector2(size / 2f, size / 2f), new Vector2(size, size)),
                EnvironmentSprites.GetRegion(_environmentAtlas, sprite));
        }
    }

    private void DrawStructures()
    {
        foreach (var worldObject in _snapshot.WorldObjects)
        {
            if (worldObject.Kind is WorldObjectKind.GoblinHut or WorldObjectKind.GoblinFieldCamp)
            {
                DrawIllustratedGoblinStructure(worldObject);
                continue;
            }

            var baseColor = worldObject.Kind == WorldObjectKind.WoodenWalkway
                ? new Color("b8894c")
                : worldObject.Owner == WorldObjectOwner.GoblinTribe
                ? new Color("745b3b")
                : new Color("c08b55");
            foreach (var (position, part) in worldObject.GetAbsoluteParts().Where(item =>
                         item.Position.Z == _visibleLevel))
            {
                var color = part.Kind switch
                {
                    WorldObjectPartKind.Floor => baseColor.Darkened(0.18f),
                    WorldObjectPartKind.Door => new Color("e3c06c"),
                    WorldObjectPartKind.WellRim => new Color("9ca4a1"),
                    WorldObjectPartKind.Walkway => new Color("b8894c"),
                    _ => baseColor,
                };
                DrawRect(
                    CellRect(position.X, position.Y).Grow(
                        part.Kind == WorldObjectPartKind.Walkway ? -4f : -1.5f),
                    color);
            }
        }
    }

    private void DrawIllustratedGoblinStructure(WorldObjectSnapshot worldObject)
    {
        var sprite = (worldObject.Kind, _visibleLevel) switch
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
            : worldObject.Orientation switch
            {
                CardinalOrientation.North => Mathf.Pi,
                CardinalOrientation.East => -Mathf.Pi / 2f,
                CardinalOrientation.South => 0f,
                CardinalOrientation.West => Mathf.Pi / 2f,
                _ => 0f,
            };
        DrawSetTransform(center, rotation);
        DrawTextureRectRegion(
            _environmentAtlas,
            new Rect2(-size / 2f, size),
            EnvironmentSprites.GetRegion(_environmentAtlas, sprite.Value));
        DrawSetTransform(Vector2.Zero);
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
                HumanFieldPhase.Cleared => EnvironmentSprite.ClearedField,
                HumanFieldPhase.Sown => EnvironmentSprite.SownField,
                HumanFieldPhase.Growing => EnvironmentSprite.GrowingField,
                HumanFieldPhase.Ripe => EnvironmentSprite.RipeField,
                _ => throw new ArgumentOutOfRangeException(),
            };
            DrawTextureRectRegion(
                _environmentAtlas,
                rect.Grow(2f),
                EnvironmentSprites.GetRegion(_environmentAtlas, sprite));
            DrawRect(rect, color with { A = 0.32f }, filled: false, width: 0.8f);
        }
    }

    private void DrawConstructionPreview()
    {
        if (_visibleLevel != 0)
        {
            return;
        }

        foreach (var cell in _constructionPreview)
        {
            var valid = _engine.Map.IsWithin(cell) &&
                _snapshot.GetVisibility(cell, _engine.Map.Width) != CellVisibility.Unknown;
            var color = valid
                ? new Color(0.95f, 0.75f, 0.28f, 0.7f)
                : new Color(0.92f, 0.2f, 0.2f, 0.72f);
            DrawRect(CellRect(cell.X, cell.Y).Grow(-2f), color, filled: false, width: 2f);
        }
    }

    private void DrawWorkDesignations()
    {
        if (_visibleLevel != 0)
        {
            return;
        }

        foreach (var designation in _snapshot.WorkDesignations)
        {
            var color = designation.Kind switch
            {
                WorkDesignationKind.GatherFood => new Color(0.55f, 0.9f, 0.28f, 0.72f),
                WorkDesignationKind.GatherBrushwood => new Color(0.72f, 0.46f, 0.22f, 0.78f),
                WorkDesignationKind.UprootBerryBush => new Color(0.92f, 0.3f, 0.2f, 0.82f),
                _ => Colors.Magenta,
            };
            DrawRect(
                CellRect(designation.Target.X, designation.Target.Y).Grow(-4f),
                color,
                filled: false,
                width: 0.7f);
        }
    }

    private void DrawWorkPreview()
    {
        if (_visibleLevel != 0)
        {
            return;
        }

        var color = _workPreviewKind switch
        {
            WorkDesignationKind.GatherFood => new Color(0.65f, 1f, 0.3f, 0.9f),
            WorkDesignationKind.GatherBrushwood => new Color(0.9f, 0.58f, 0.25f, 0.9f),
            WorkDesignationKind.UprootBerryBush => new Color(1f, 0.32f, 0.2f, 0.92f),
            _ => new Color(0.95f, 0.28f, 0.24f, 0.9f),
        };
        foreach (var cell in _workPreview)
        {
            DrawRect(CellRect(cell.X, cell.Y).Grow(-1.5f), color, filled: false, width: 2f);
        }
    }

    private void DrawActors()
    {
        if (_visibleLevel != 0)
        {
            return;
        }

        var offsets = CreateActorOffsets();
        foreach (var group in _snapshot.Actors.GroupBy(actor => actor.Position))
        {
            var actors = group.OrderBy(actor => actor.Id).ToArray();
            for (var index = 0; index < actors.Length; index++)
            {
                var center = GetVisualActorPosition(actors[index]) + offsets[actors[index].Id];
                var healthRatio = (float)actors[index].Health / _engine.Definitions.MaximumHealth;
                var healthColor = new Color("b5443e").Lerp(new Color("a8d14b"), healthRatio);
                if (actors[index].Id == _selectedActorId)
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
                DrawActorIntent(actors[index], center);
            }
        }
    }

    private void DrawHumanCohorts()
    {
        if (_visibleLevel != 0)
        {
            return;
        }

        foreach (var cohort in _snapshot.HumanVillage.Cohorts.Where(cohort =>
                     cohort.Population > 0 &&
                     _snapshot.GetVisibility(cohort.Position, _engine.Map.Width) == CellVisibility.Visible))
        {
            var center = CellCenter(cohort.Position);
            var color = cohort.Role switch
            {
                HumanCohortRole.Farmers => new Color("d7b54b"),
                HumanCohortRole.Workers => new Color("6ea3c7"),
                HumanCohortRole.Guards when _snapshot.HumanVillage.Hostility > 0 => new Color("d75a4a"),
                HumanCohortRole.Guards => new Color("b9c2c7"),
                _ => Colors.Magenta,
            };
            if (cohort.Role == HumanCohortRole.Guards && _snapshot.HumanVillage.Hostility > 0)
            {
                DrawArc(center, 9f, 0, Mathf.Tau, 24, new Color(0.95f, 0.2f, 0.14f, 0.86f), 2f);
            }

            DrawCircle(center + new Vector2(0, 2), 4.5f, color.Darkened(0.18f));
            DrawCircle(center + new Vector2(0, -4), 3.2f, color);
            if (cohort.Role == HumanCohortRole.Guards)
            {
                DrawLine(center + new Vector2(-4, -7), center + new Vector2(4, -7), new Color("704a3a"), 1.5f);
            }

            for (var index = 0; index < cohort.Population; index++)
            {
                var angle = Mathf.Tau * index / cohort.Population;
                DrawCircle(
                    center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 7f,
                    1.15f,
                    color.Lightened(0.2f));
            }

            var toolIcon = cohort.Role switch
            {
                HumanCohortRole.Farmers => ItemIcon.WoodenHoe,
                HumanCohortRole.Guards => ItemIcon.WoodenSpear,
                HumanCohortRole.Workers when cohort.Task == HumanCohortTask.DrawWater =>
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

    private void DrawStorageZones()
    {
        if (_visibleLevel != 0)
        {
            return;
        }

        foreach (var zone in _snapshot.StorageZones)
        {
            var rect = CellRect(zone.Position.X, zone.Position.Y).Grow(-2f);
            var zoneColor = zone.AcceptedResource == ResourceKind.Wood
                ? new Color(0.34f, 0.22f, 0.12f, 0.68f)
                : new Color(0.2f, 0.32f, 0.23f, 0.62f);
            DrawRect(rect, zoneColor);
            if (zone.StoredQuantity > 0 &&
                _snapshot.GetVisibility(zone.Position, _engine.Map.Width) == CellVisibility.Visible)
            {
                var fillHeight = rect.Size.Y * zone.StoredQuantity / zone.Capacity;
                DrawRect(
                    new Rect2(rect.Position.X, rect.End.Y - fillHeight, rect.Size.X, fillHeight),
                    new Color(0.84f, 0.58f, 0.24f, 0.82f));
            }

            DrawRect(rect, new Color("d8c379"), filled: false, width: 1.5f);
        }
    }

    private void DrawItems()
    {
        if (_visibleLevel != 0)
        {
            return;
        }

        foreach (var stack in _snapshot.ItemStacks.Where(stack =>
                     stack.Location.Kind == ItemLocationKind.Ground &&
                     _snapshot.GetVisibility(stack.Location.Position, _engine.Map.Width) == CellVisibility.Visible))
        {
            var center = CellCenter(stack.Location.Position);
            var size = 11f + Math.Min(5f, stack.Quantity / 4f);
            DrawCircle(center + new Vector2(1, 1), size * 0.46f, new Color(0, 0, 0, 0.46f));
            DrawTextureRectRegion(
                _itemIconAtlas,
                new Rect2(center - new Vector2(size / 2, size / 2), new Vector2(size, size)),
                ItemIcons.GetRegion(_itemIconAtlas, ItemIcons.ForResource(stack.Resource)));
        }
    }

    private void DrawJobTargets()
    {
        if (_visibleLevel != 0)
        {
            return;
        }

        foreach (var actor in _snapshot.Actors.Where(actor => actor.Job.Kind != ActorJobKind.None))
        {
            var from = GetVisualActorPosition(actor);
            var target = CellCenter(actor.Job.Target);
            var color = actor.Job.Kind switch
            {
                ActorJobKind.Haul => new Color(0.96f, 0.62f, 0.25f, 0.72f),
                ActorJobKind.Rest => new Color(0.48f, 0.72f, 0.96f, 0.72f),
                ActorJobKind.Eat => new Color(0.96f, 0.38f, 0.48f, 0.76f),
                ActorJobKind.Explore => new Color(0.78f, 0.82f, 0.86f, 0.72f),
                ActorJobKind.Move => new Color(0.98f, 0.84f, 0.34f, 0.82f),
                ActorJobKind.Resupply => new Color(0.36f, 0.78f, 0.92f, 0.78f),
                _ => new Color(0.86f, 0.93f, 0.45f, 0.58f),
            };
            DrawDashedLine(from, target, color with { A = 0.28f }, 1f, 5f);
            DrawArc(target, 6f, 0, Mathf.Tau, 16, color, 1.5f);
        }
    }

    private void DrawFog()
    {
        for (var y = 0; y < _engine.Map.Height; y++)
        {
            for (var x = 0; x < _engine.Map.Width; x++)
            {
                var visibility = _snapshot.GetVisibility(
                    new GridPosition(x, y),
                    _engine.Map.Width);
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

    private void DrawOrderedDestination()
    {
        if (_visibleLevel != 0)
        {
            return;
        }

        var selected = _snapshot.Actors.FirstOrDefault(actor => actor.Id == _selectedActorId);
        if (selected.Id == EntityId.None || selected.Job.Kind != ActorJobKind.Move)
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
            ActorJobKind.ClearVegetation => UiIcon.GatherBrushwood,
            ActorJobKind.Haul => UiIcon.FoodStorage,
            ActorJobKind.Rest => UiIcon.FieldCamp,
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

    private static Rect2 CellRect(int x, int y) => new(
        x * TileSize,
        y * TileSize,
        TileSize + 0.5f,
        TileSize + 0.5f);

    private static Vector2 CellCenter(GridPosition position) => new(
        (position.X + 0.5f) * TileSize,
        (position.Y + 0.5f) * TileSize);
}
