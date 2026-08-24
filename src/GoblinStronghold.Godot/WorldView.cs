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

    public void SetSelectedActor(EntityId actorId)
    {
        _selectedActorId = actorId;
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
        DrawStructures();
        DrawHumanCohorts();
        DrawStorageZones();
        DrawItems();
        DrawJobTargets();
        DrawActors();
        DrawFog();
        DrawOrderedDestination();
    }

    private void DrawTerrain()
    {
        for (var y = 0; y < _engine.Map.Height; y++)
        {
            for (var x = 0; x < _engine.Map.Width; x++)
            {
                var cell = _engine.Map.GetCell(new GridPosition(x, y));
                var color = cell.Terrain switch
                {
                    TerrainKind.SolidGround => new Color("718b55"),
                    TerrainKind.Mud => new Color("596b46"),
                    TerrainKind.ShallowWater => new Color("4d8790"),
                    TerrainKind.DeepWater => new Color("315d73"),
                    _ => Colors.Magenta,
                };
                DrawRect(CellRect(x, y), color);
            }
        }
    }

    private void DrawPlants()
    {
        foreach (var plant in _snapshot.PlantPatches.Where(item => item.Biomass > 0))
        {
            var center = CellCenter(plant.Position);
            var radius = 2.5f + (4f * plant.Biomass / plant.Capacity);
            DrawCircle(center, radius, new Color("8fbd43"));
            DrawCircle(center + new Vector2(2, -1), 1.6f, new Color("b84c72"));
        }
    }

    private void DrawStructures()
    {
        foreach (var worldObject in _snapshot.WorldObjects)
        {
            var baseColor = worldObject.Owner == WorldObjectOwner.GoblinTribe
                ? new Color("745b3b")
                : new Color("c08b55");
            foreach (var (position, part) in worldObject.GetAbsoluteParts().Where(item => item.Position.Z == 0))
            {
                var color = part.Kind switch
                {
                    WorldObjectPartKind.Floor => baseColor.Darkened(0.18f),
                    WorldObjectPartKind.Door => new Color("e3c06c"),
                    WorldObjectPartKind.WellRim => new Color("9ca4a1"),
                    _ => baseColor,
                };
                DrawRect(CellRect(position.X, position.Y).Grow(-1.5f), color);
            }
        }
    }

    private void DrawActors()
    {
        var offsets = CreateActorOffsets();
        foreach (var group in _snapshot.Actors.GroupBy(actor => actor.Position))
        {
            var actors = group.OrderBy(actor => actor.Id).ToArray();
            for (var index = 0; index < actors.Length; index++)
            {
                var center = GetVisualActorPosition(actors[index]) + offsets[actors[index].Id];
                var healthRatio = (float)actors[index].Health / _engine.Definitions.MaximumHealth;
                var actorColor = new Color("b5443e").Lerp(new Color("a8d14b"), healthRatio);
                if (actors[index].Id == _selectedActorId)
                {
                    DrawCircle(center, 5.5f, new Color("f5dc72"));
                }

                DrawCircle(center, 3.6f, actorColor);
                DrawCircle(center + new Vector2(-1.2f, -0.6f), 0.65f, new Color("182117"));
                DrawCircle(center + new Vector2(1.2f, -0.6f), 0.65f, new Color("182117"));
                DrawActorIntent(actors[index], center);
            }
        }
    }

    private void DrawHumanCohorts()
    {
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
        }
    }

    private void DrawStorageZones()
    {
        foreach (var zone in _snapshot.StorageZones)
        {
            var rect = CellRect(zone.Position.X, zone.Position.Y).Grow(-2f);
            DrawRect(rect, new Color(0.2f, 0.32f, 0.23f, 0.62f));
            if (zone.StoredQuantity > 0)
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
        foreach (var stack in _snapshot.ItemStacks.Where(stack =>
                     stack.Location.Kind == ItemLocationKind.Ground))
        {
            var radius = 2f + Math.Min(3f, stack.Quantity / 4f);
            DrawCircle(CellCenter(stack.Location.Position), radius, new Color("e0a340"));
        }
    }

    private void DrawJobTargets()
    {
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
        DrawCircle(center, 5.5f, new Color(0.05f, 0.07f, 0.06f, 0.86f));
        switch (actor.Job.Kind)
        {
            case ActorJobKind.Forage:
                DrawCircle(center + new Vector2(-2, 1), 1.7f, new Color("8fbd43"));
                DrawCircle(center + new Vector2(1.5f, 1), 1.7f, new Color("b84c72"));
                break;
            case ActorJobKind.Haul:
                DrawRect(new Rect2(center - new Vector2(2.8f, 2.4f), new Vector2(5.6f, 4.8f)), new Color("df983e"));
                DrawLine(center + new Vector2(-3, -3), center + new Vector2(3, -3), new Color("f4cf70"), 1.2f);
                if (actor.Job.Stage == ActorJobStage.Collecting)
                {
                    DrawLine(center + new Vector2(0, -1), center + new Vector2(0, 3), new Color("5a351e"), 1.2f);
                    DrawLine(center + new Vector2(0, 3), center + new Vector2(-2, 1), new Color("5a351e"), 1.2f);
                }
                else
                {
                    DrawLine(center + new Vector2(-2, 0), center + new Vector2(3, 0), new Color("5a351e"), 1.2f);
                    DrawLine(center + new Vector2(3, 0), center + new Vector2(1, -2), new Color("5a351e"), 1.2f);
                }

                break;
            case ActorJobKind.Rest:
                DrawLine(center + new Vector2(-2, -2), center + new Vector2(2, -2), new Color("8fc4f2"), 1.4f);
                DrawLine(center + new Vector2(2, -2), center + new Vector2(-2, 2), new Color("8fc4f2"), 1.4f);
                DrawLine(center + new Vector2(-2, 2), center + new Vector2(2, 2), new Color("8fc4f2"), 1.4f);
                break;
            case ActorJobKind.Eat:
                DrawCircle(center, 2.6f, new Color("d65d55"));
                DrawLine(center + new Vector2(0, -3), center + new Vector2(2, -4), new Color("8fbd43"), 1.4f);
                break;
            case ActorJobKind.Explore:
                DrawArc(center + new Vector2(-1, -1), 2.4f, 0, Mathf.Tau, 12, new Color("d8e1e5"), 1.3f);
                DrawLine(center + new Vector2(1, 1), center + new Vector2(4, 4), new Color("d8e1e5"), 1.5f);
                break;
            case ActorJobKind.Move:
                DrawLine(center + new Vector2(-3, 2), center + new Vector2(3, -2), new Color("f5dc72"), 1.7f);
                DrawLine(center + new Vector2(3, -2), center + new Vector2(0, -3), new Color("f5dc72"), 1.7f);
                DrawLine(center + new Vector2(3, -2), center + new Vector2(2, 1), new Color("f5dc72"), 1.7f);
                break;
            case ActorJobKind.Resupply when actor.Job.Stage == ActorJobStage.ProvisioningWater:
                DrawCircle(center + new Vector2(0, 1), 2.5f, new Color("52a9d8"));
                DrawLine(center + new Vector2(0, -4), center + new Vector2(-2, 0), new Color("78c8ed"), 1.6f);
                break;
            case ActorJobKind.Resupply:
                DrawRect(new Rect2(center - new Vector2(2.5f, 2.5f), new Vector2(5, 5)), new Color("d7b54b"));
                DrawCircle(center, 1.3f, new Color("b84c72"));
                break;
        }

        var phaseColor = actor.Job.Phase == ActorJobPhase.Traveling
            ? new Color("f5dc72")
            : new Color("f1f3e8");
        DrawCircle(center + new Vector2(4.5f, 4.5f), 1.25f, phaseColor);
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
