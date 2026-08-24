using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.GodotClient;

public partial class Main : Node
{
    private const double SecondsPerTick = 0.1;
    private readonly WorldSeed _seed = new(0x474F424C494EUL);
    private SimulationEngine _engine = null!;
    private WorldView _worldView = null!;
    private Camera2D _camera = null!;
    private Label _status = null!;
    private Label _inspector = null!;
    private int _speed = 1;
    private double _accumulator;
    private ulong _commandSequence = 1;
    private EntityId _selectedActorId = EntityId.None;

    public override void _Ready()
    {
        var map = SwampMapGenerator.Generate(_seed, 64, 64);
        _engine = SimulationEngine.Create(
            _seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 8,
            initialFoodStock: 16);
        _engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1),
            _commandSequence++,
            map.GoblinSpawn,
            ResourceKind.Food,
            capacity: 64));

        _worldView = GetNode<WorldView>("WorldView");
        _camera = GetNode<Camera2D>("Camera2D");
        _status = GetNode<Label>("Interface/TopBar/Controls/Status");
        _inspector = GetNode<Label>("Interface/Inspector/Text");
        _worldView.SetWorld(_engine);
        _worldView.SetSimulationSpeed(_speed, SecondsPerTick);
        _camera.Position = _worldView.CellToWorld(map.GoblinSpawn);

        BindButton("Pause", 0);
        BindButton("Speed1", 1);
        BindButton("Speed2", 2);
        BindButton("Speed4", 4);
        BindButton("Speed8", 8);
        UpdateStatus();
    }

    public override void _Process(double delta)
    {
        MoveCamera(delta);
        if (_speed == 0)
        {
            return;
        }

        _accumulator += delta * _speed;
        var changed = false;
        while (_accumulator >= SecondsPerTick)
        {
            _engine.AdvanceTicks(1);
            _accumulator -= SecondsPerTick;
            changed = true;
        }

        if (changed)
        {
            HandleEvents(_engine.DrainEvents());
            _worldView.Refresh(_engine.CreateSnapshot());
            UpdateStatus();
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case InputEventKey key when key.Pressed && key.Keycode == Key.Space:
                SetSpeed(_speed == 0 ? 1 : 0);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _camera.Zoom = (_camera.Zoom * 1.15f).Clamp(
                    new Vector2(0.35f, 0.35f),
                    new Vector2(3.5f, 3.5f));
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _camera.Zoom = (_camera.Zoom / 1.15f).Clamp(
                    new Vector2(0.35f, 0.35f),
                    new Vector2(3.5f, 3.5f));
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouse:
                InspectWorld(mouse.Position);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } mouse:
                HandleSecondaryAction(mouse.Position);
                break;
        }
    }

    private void HandleSecondaryAction(Vector2 screenPosition)
    {
        var snapshot = _engine.CreateSnapshot();
        if (_selectedActorId != EntityId.None &&
            snapshot.Actors.Any(actor => actor.Id == _selectedActorId))
        {
            OrderSelectedActorToMove(screenPosition);
            return;
        }

        SelectActor(EntityId.None);
        CreateFoodStorage(screenPosition);
    }

    private void OrderSelectedActorToMove(Vector2 screenPosition)
    {
        var destination = ScreenToCell(screenPosition);
        if (!_engine.Map.IsWithin(destination))
        {
            return;
        }

        _engine.QueueCommand(SimulationCommand.Move(
            _engine.CurrentTick.Next(),
            _commandSequence++,
            _selectedActorId,
            destination));
        _inspector.Text = $"{_selectedActorId} • rozkaz marszu → {destination}";
    }

    private void CreateFoodStorage(Vector2 screenPosition)
    {
        var cell = ScreenToCell(screenPosition);
        if (!_engine.Map.IsWithin(cell))
        {
            return;
        }

        var snapshot = _engine.CreateSnapshot();
        if (!_engine.World.IsSurfaceTraversable(cell) ||
            snapshot.GetVisibility(cell, _engine.Map.Width) == CellVisibility.Unknown)
        {
            return;
        }

        _engine.QueueCommand(SimulationCommand.CreateStorageZone(
            _engine.CurrentTick.Next(),
            _commandSequence++,
            cell,
            ResourceKind.Food,
            capacity: 64));
        _inspector.Text = $"{cell} • zlecono skład żywności 0/64";
    }

    private void HandleEvents(IReadOnlyList<SimulationEvent> events)
    {
        var selectedEvent = events.LastOrDefault(item =>
            item.Subject == _selectedActorId &&
            (item.Kind == SimulationEventKind.MoveCompleted ||
             (item.Kind == SimulationEventKind.CommandRejected &&
              item.Amount == (int)SimulationCommandKind.Move)));
        if (selectedEvent.Kind == SimulationEventKind.CommandRejected &&
            selectedEvent.Amount == (int)SimulationCommandKind.Move)
        {
            _inspector.Text = $"{selectedEvent.Subject} • cel marszu jest niedostępny";
        }
        else if (selectedEvent.Kind == SimulationEventKind.MoveCompleted)
        {
            _inspector.Text = $"{selectedEvent.Subject} • dotarł do celu marszu";
        }
    }

    private void InspectWorld(Vector2 screenPosition)
    {
        var cell = ScreenToCell(screenPosition);
        if (!_engine.Map.IsWithin(cell))
        {
            return;
        }

        var snapshot = _engine.CreateSnapshot();
        var visibility = snapshot.GetVisibility(cell, _engine.Map.Width);
        if (visibility == CellVisibility.Unknown)
        {
            SelectActor(EntityId.None);
            _inspector.Text = $"{cell} • nieznany teren";
            return;
        }

        var terrain = _engine.Map.GetCell(cell);
        if (visibility == CellVisibility.Explored)
        {
            SelectActor(EntityId.None);
            _inspector.Text = $"{cell} • odkryte, obecnie niewidoczne • {terrain.Terrain}";
            return;
        }

        var plant = _engine.World.GetPlantPatch(cell);
        var objects = _engine.World.GetWorldObjectsAt(cell);
        var actors = snapshot.Actors.Where(actor => actor.Position == cell).ToArray();
        var humanCohorts = snapshot.HumanVillage.Cohorts
            .Where(cohort => cohort.Population > 0 && cohort.Position == cell)
            .ToArray();
        var groundStacks = snapshot.ItemStacks.Where(stack =>
            stack.Location.Kind == ItemLocationKind.Ground &&
            stack.Location.Position == cell).ToArray();
        var carriedStacks = actors
            .Where(actor => actor.CarriedStackId != EntityId.None)
            .Select(actor => snapshot.ItemStacks.Single(stack => stack.Id == actor.CarriedStackId))
            .ToArray();
        var zones = snapshot.StorageZones.Where(zone => zone.Position == cell).ToArray();
        SelectActor(actors.OrderBy(actor => actor.Id).FirstOrDefault().Id);

        _inspector.Text = $"{cell} • {terrain.Terrain} • wilgoć {terrain.Moisture} • żyzność {terrain.Fertility}" +
            (plant is null ? string.Empty : $" • jagody {plant.Value.Biomass}/{plant.Value.Capacity}") +
            (objects.Count == 0 ? string.Empty : $" • {string.Join(", ", objects.Select(item => item.Kind))}") +
            (humanCohorts.Length == 0
                ? string.Empty
                : $" • ludzie: {string.Join(", ", humanCohorts.Select(DescribeCohort))}") +
            (!humanCohorts.Any(cohort => cohort.Role == HumanCohortRole.Guards)
                ? string.Empty
                : $" • alarm {snapshot.HumanVillage.Hostility}/100, siła straży " +
                  $"{snapshot.HumanVillage.GuardHitPoints}/{snapshot.HumanVillage.MaximumGuardHitPoints}") +
            (cell != snapshot.HumanVillage.Anchor &&
             !objects.Any(item => item.Owner == WorldObjectOwner.HumanVillage)
                ? string.Empty
                : $" • wieś: {snapshot.HumanVillage.Population} osób, żywność {snapshot.HumanVillage.FoodStock}, " +
                  $"drewno {snapshot.HumanVillage.WoodStock}, towary {snapshot.HumanVillage.GoodsStock}") +
            (zones.Length == 0
                ? string.Empty
                : $" • skład: {string.Join(", ", zones.Select(zone => $"{zone.AcceptedResource} {zone.StoredQuantity}/{zone.Capacity}"))}") +
            (actors.Length == 0
                ? string.Empty
                : $" • gobliny ×{actors.Length}, głód {actors.Min(actor => actor.Hunger)}–{actors.Max(actor => actor.Hunger)}" +
                  $", zmęczenie {actors.Min(actor => actor.Fatigue)}–{actors.Max(actor => actor.Fatigue)}" +
                  $", pragnienie {actors.Min(actor => actor.Thirst)}–{actors.Max(actor => actor.Thirst)}" +
                  $", zdrowie {actors.Min(actor => actor.Health)}–{actors.Max(actor => actor.Health)}" +
                  $", racje {actors.Sum(actor => actor.PersonalFood)} jedz./{actors.Sum(actor => actor.PersonalWater)} wody" +
                  $" • {string.Join(", ", actors.Select(actor => DescribeJob(actor.Job)))}") +
            (groundStacks.Length == 0
                ? string.Empty
                : $" • na ziemi: {string.Join(", ", groundStacks.Select(DescribeStack))}") +
            (carriedStacks.Length == 0
                ? string.Empty
                : $" • w kieszeniach: {string.Join(", ", carriedStacks.Select(DescribeStack))}");
    }

    private GridPosition ScreenToCell(Vector2 screenPosition)
    {
        var worldPosition = _camera.GetScreenCenterPosition() +
            ((screenPosition - GetViewport().GetVisibleRect().Size / 2f) / _camera.Zoom);
        return _worldView.WorldToCell(worldPosition);
    }

    private static string DescribeStack(ItemStackSnapshot stack) =>
        $"{stack.Resource} ×{stack.Quantity}";

    private static string DescribeCohort(HumanCohortSnapshot cohort) =>
        $"{cohort.Role switch
        {
            HumanCohortRole.Farmers => "rolnicy",
            HumanCohortRole.Workers => "robotnicy",
            HumanCohortRole.Guards => "strażnicy",
            _ => "nieznani",
        }} ×{cohort.Population}";

    private static string DescribeJob(ActorJobSnapshot job) => job.Kind switch
    {
        ActorJobKind.Forage when job.Phase == ActorJobPhase.Traveling => $"idzie po jagody → {job.Target}",
        ActorJobKind.Forage when job.Phase == ActorJobPhase.Working => $"zbiera ({job.RemainingWorkTicks})",
        ActorJobKind.Haul when job.Stage == ActorJobStage.Collecting && job.Phase == ActorJobPhase.Traveling =>
            $"idzie po ładunek ×{job.ReservedQuantity}",
        ActorJobKind.Haul when job.Stage == ActorJobStage.Collecting =>
            $"ładuje ×{job.ReservedQuantity} ({job.RemainingWorkTicks})",
        ActorJobKind.Haul when job.Stage == ActorJobStage.Delivering && job.Phase == ActorJobPhase.Traveling =>
            $"niesie ×{job.ReservedQuantity}",
        ActorJobKind.Haul when job.Stage == ActorJobStage.Delivering =>
            $"rozładowuje ×{job.ReservedQuantity} ({job.RemainingWorkTicks})",
        ActorJobKind.Rest when job.Phase == ActorJobPhase.Traveling => $"idzie odpocząć → {job.Target}",
        ActorJobKind.Rest => $"odpoczywa ({job.RemainingWorkTicks})",
        ActorJobKind.Eat when job.Phase == ActorJobPhase.Traveling => $"idzie coś zjeść → {job.Target}",
        ActorJobKind.Eat => $"je ({job.RemainingWorkTicks})",
        ActorJobKind.Explore => $"odkrywa teren → {job.Target}",
        ActorJobKind.Move => $"wykonuje rozkaz marszu → {job.Target}",
        ActorJobKind.Resupply when job.Stage == ActorJobStage.ProvisioningFood &&
            job.Phase == ActorJobPhase.Traveling => $"idzie po prowiant → {job.Target}",
        ActorJobKind.Resupply when job.Stage == ActorJobStage.ProvisioningFood =>
            $"pakuje rację żywności ({job.RemainingWorkTicks})",
        ActorJobKind.Resupply when job.Stage == ActorJobStage.ProvisioningWater &&
            job.Phase == ActorJobPhase.Traveling => $"idzie napełnić wodę → {job.Target}",
        ActorJobKind.Resupply when job.Stage == ActorJobStage.ProvisioningWater =>
            $"napełnia bukłak ({job.RemainingWorkTicks})",
        _ => "bez zadania",
    };

    private void SelectActor(EntityId actorId)
    {
        _selectedActorId = actorId;
        _worldView.SetSelectedActor(actorId);
    }

    private void MoveCamera(double delta)
    {
        var direction = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
        if (Input.IsKeyPressed(Key.A)) direction.X -= 1;
        if (Input.IsKeyPressed(Key.D)) direction.X += 1;
        if (Input.IsKeyPressed(Key.W)) direction.Y -= 1;
        if (Input.IsKeyPressed(Key.S)) direction.Y += 1;
        _camera.Position += direction.Normalized() * (float)(520 * delta / _camera.Zoom.X);
    }

    private void BindButton(string name, int speed) =>
        GetNode<Button>($"Interface/TopBar/Controls/{name}").Pressed += () => SetSpeed(speed);

    private void SetSpeed(int speed)
    {
        _speed = speed;
        _worldView.SetSimulationSpeed(speed, SecondsPerTick);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var snapshot = _engine.CreateSnapshot();
        if (_selectedActorId != EntityId.None &&
            snapshot.Actors.All(actor => actor.Id != _selectedActorId))
        {
            SelectActor(EntityId.None);
        }

        var traveling = snapshot.Actors.Count(actor => actor.Job.Phase == ActorJobPhase.Traveling);
        var working = snapshot.Actors.Count(actor => actor.Job.Phase == ActorJobPhase.Working);
        var haulers = snapshot.Actors.Count(actor => actor.Job.Kind == ActorJobKind.Haul);
        var resting = snapshot.Actors.Count(actor => actor.Job.Kind == ActorJobKind.Rest);
        var eating = snapshot.Actors.Count(actor => actor.Job.Kind == ActorJobKind.Eat);
        var resupplying = snapshot.Actors.Count(actor => actor.Job.Kind == ActorJobKind.Resupply);
        var explored = snapshot.Visibility.Count(state => state != CellVisibility.Unknown);
        var villageVisibility = snapshot.GetVisibility(snapshot.HumanVillage.Anchor, _engine.Map.Width);
        var storedFood = snapshot.ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Food &&
                stack.Location.Kind == ItemLocationKind.StorageZone)
            .Sum(stack => stack.Quantity);
        var personalFood = snapshot.Actors.Sum(actor => actor.PersonalFood);
        var personalWater = snapshot.Actors.Sum(actor => actor.PersonalWater);
        _status.Text = $"Tick {snapshot.Tick.Value:N0}  •  {(_speed == 0 ? "PAUZA" : $"{_speed}×")}  •  plemię {snapshot.Actors.Count}" +
            $"  •  żywność {snapshot.FoodStock}" +
            $" (skł. {storedFood}, racje {personalFood}/{personalWater})" +
            $"  •  odkryte {explored}/{snapshot.Visibility.Count}" +
            $"  •  transport {haulers}  •  w drodze {traveling}  •  pracuje {working}";
        if (resting > 0)
        {
            _status.Text += $"  •  odpoczywa {resting}";
        }
        if (eating > 0)
        {
            _status.Text += $"  •  je {eating}";
        }
        if (resupplying > 0)
        {
            _status.Text += $"  •  pobiera zapasy {resupplying}";
        }
        if (_selectedActorId != EntityId.None)
        {
            _status.Text += $"  •  wybrany {_selectedActorId}";
        }
        if (villageVisibility == CellVisibility.Visible)
        {
            _status.Text += $"  •  wieś {snapshot.HumanVillage.Population} osób, zapasy " +
                $"{snapshot.HumanVillage.FoodStock}/{snapshot.HumanVillage.WoodStock}/{snapshot.HumanVillage.GoodsStock}" +
                $"  •  alarm {snapshot.HumanVillage.Hostility}/100";
        }
        else if (villageVisibility == CellVisibility.Explored)
        {
            _status.Text += "  •  wieś odkryta";
        }
    }
}
