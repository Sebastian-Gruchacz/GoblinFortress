using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using System.Text;

namespace GoblinStronghold.GodotClient;

public partial class Main : Node
{
    private static readonly WorldSeed InitialSeed = new(0x474F424C494EUL);
    private SimulationEngine _engine = null!;
    private WorldView _worldView = null!;
    private MinimapView _minimap = null!;
    private Camera2D _camera = null!;
    private Label _status = null!;
    private Label _clock = null!;
    private Label _seasonName = null!;
    private SeasonCycleView _seasonProgress = null!;
    private Label _inspector = null!;
    private PopupPanel _buildMenu = null!;
    private PopupPanel _workMenu = null!;
    private GridContainer _buildMenuGrid = null!;
    private GridContainer _workMenuGrid = null!;
    private Texture2D _iconAtlas = null!;
    private Texture2D _itemIconAtlas = null!;
    private Window _goblinDetails = null!;
    private Label _goblinDetailsText = null!;
    private ProgressBar _healthBar = null!;
    private ProgressBar _hungerBar = null!;
    private ProgressBar _thirstBar = null!;
    private ProgressBar _fatigueBar = null!;
    private HBoxContainer _inventoryIcons = null!;
    private string _inventorySignature = string.Empty;
    private int _speed = 1;
    private int _visibleLevel;
    private double _accumulator;
    private ulong _commandSequence = 1;
    private EntityId _selectedActorId = EntityId.None;
    private BuildMode _buildMode;
    private bool _isDraggingWalkway;
    private GridPosition _walkwayStart;
    private WorkMode _workMode;
    private bool _isDraggingWorkArea;
    private GridPosition _workAreaStart;
    private bool _isPanningCamera;
    private float _rightDragDistance;
    private Window _storageDetails = null!;
    private Label _storageSummary = null!;
    private CheckButton _storagePullLoose = null!;
    private SpinBox _storageTarget = null!;
    private OptionButton _storagePriority = null!;
    private OptionButton _resourcePriority = null!;
    private OptionButton _storageHauler = null!;
    private readonly List<EntityId> _storageHaulerActorIds = [];
    private OptionButton _storageSource = null!;
    private readonly List<EntityId> _storageSourceZoneIds = [];
    private EntityId _selectedStorageId = EntityId.None;
    private GameSaveStore _saveStore = null!;
    private SimulationTick _nextAutosaveTick;
    private Control _mainMenu = null!;
    private Button _resumeGameButton = null!;
    private Button _newGameButton = null!;
    private Button _loadMenuButton = null!;
    private bool _hasActiveSession;
    private int _speedBeforeMenu = 1;

    private double SecondsPerTick =>
        _engine.Definitions.Clock.RealSecondsPerTickAtNormalSpeed;

    private enum BuildMode
    {
        None,
        FoodStorage,
        Walkway,
        WoodStorage,
        FieldCamp,
    }

    private enum WorkMode
    {
        None,
        GatherFood,
        GatherBrushwood,
        UprootBerryBushes,
        Clear,
    }

    public override void _Ready()
    {
        _saveStore = new GameSaveStore(ProjectSettings.GlobalizePath("user://saves"));
        _engine = CreateNewEngine(InitialSeed);
        var map = _engine.Map;

        _worldView = GetNode<WorldView>("WorldView");
        _minimap = GetNode<MinimapView>("Interface/RightHud/MinimapFrame/Minimap");
        _camera = GetNode<Camera2D>("Camera2D");
        _status = GetNode<Label>("Interface/TopBar/Controls/Status");
        _clock = GetNode<Label>("Interface/Calendar/Controls/Clock");
        _seasonName = GetNode<Label>("Interface/Calendar/Controls/SeasonName");
        _seasonProgress = GetNode<SeasonCycleView>("Interface/Calendar/Controls/Season");
        _inspector = GetNode<Label>("Interface/Inspector/Text");
        _buildMenu = GetNode<PopupPanel>("BuildMenu");
        _workMenu = GetNode<PopupPanel>("WorkMenu");
        _buildMenuGrid = GetNode<GridContainer>("BuildMenu/Margin/Grid");
        _workMenuGrid = GetNode<GridContainer>("WorkMenu/Margin/Grid");
        _mainMenu = GetNode<Control>("Interface/MainMenu");
        _resumeGameButton = GetNode<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/Resume");
        _newGameButton = GetNode<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/NewGame");
        _loadMenuButton = GetNode<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/LoadGame");
        _iconAtlas = UiIcons.LoadAtlas();
        _itemIconAtlas = ItemIcons.LoadAtlas();
        _goblinDetails = GetNode<Window>("GoblinDetails");
        _goblinDetailsText = GetNode<Label>("GoblinDetails/Scroll/Content/Text");
        _inventoryIcons = GetNode<HBoxContainer>("GoblinDetails/Scroll/Content/Inventory");
        GetViewport().GuiEmbedSubwindows = true;
        CreateTileButton(_buildMenuGrid, _buildMenu, UiIcon.FoodStorage,
            "Skład żywności\nKoszt: 2 drewna", () => SelectBuildMode((long)BuildMode.FoodStorage));
        CreateTileButton(_buildMenuGrid, _buildMenu, UiIcon.GatherBrushwood,
            "Skład drewna\nKoszt: 2 drewna", () => SelectBuildMode((long)BuildMode.WoodStorage));
        CreateTileButton(_buildMenuGrid, _buildMenu, UiIcon.Walkway,
            "Pomost\nKoszt: 1 drewno za segment", () => SelectBuildMode((long)BuildMode.Walkway));
        CreateTileButton(_buildMenuGrid, _buildMenu, UiIcon.FieldCamp,
            "Obozowisko wypadowe\nKoszt: 6 drewna", () => SelectBuildMode((long)BuildMode.FieldCamp));
        CreateTileButton(_workMenuGrid, _workMenu, UiIcon.GatherFood,
            "Zbierz żywność\nJagody, grzyby, korzonki i ryby", () => SelectWorkMode((long)WorkMode.GatherFood));
        CreateTileButton(_workMenuGrid, _workMenu, UiIcon.GatherBrushwood,
            "Zbierz chrust\nPrzeciągnij obszar", () => SelectWorkMode((long)WorkMode.GatherBrushwood));
        CreateTileButton(_workMenuGrid, _workMenu, UiIcon.GatherBrushwood,
            "Wykarcz krzaki\nTrwale usuwa źródła jagód", () => SelectWorkMode((long)WorkMode.UprootBerryBushes));
        CreateTileButton(_workMenuGrid, _workMenu, UiIcon.ClearOrders,
            "Usuń zlecenia\nPrzeciągnij obszar", () => SelectWorkMode((long)WorkMode.Clear));
        CreateNeedIndicators();
        _goblinDetails.CloseRequested += _goblinDetails.Hide;
        _goblinDetails.GetNode<Control>("Scroll").GuiInput += inputEvent =>
            CloseWindowOnSecondaryInput(inputEvent, _goblinDetails);
        _storageDetails = GetNode<Window>("StorageDetails");
        _storageSummary = GetNode<Label>("StorageDetails/Margin/Controls/Summary");
        _storagePullLoose = GetNode<CheckButton>("StorageDetails/Margin/Controls/PullLoose");
        _storageTarget = GetNode<SpinBox>("StorageDetails/Margin/Controls/TargetRow/Target");
        _storagePriority = GetNode<OptionButton>("StorageDetails/Margin/Controls/PriorityRow/Priority");
        _resourcePriority = GetNode<OptionButton>("StorageDetails/Margin/Controls/GlobalPriorityRow/Priority");
        _storageHauler = GetNode<OptionButton>("StorageDetails/Margin/Controls/HaulerRow/Hauler");
        _storageSource = GetNode<OptionButton>("StorageDetails/Margin/Controls/SourceRow/Source");
        foreach (var priority in Enum.GetValues<StoragePriority>())
        {
            _storagePriority.AddItem(DescribeStoragePriority(priority));
            _resourcePriority.AddItem(DescribeStoragePriority(priority));
        }
        _storageDetails.CloseRequested += _storageDetails.Hide;
        _storageDetails.GetNode<Control>("Margin").GuiInput += inputEvent =>
            CloseWindowOnSecondaryInput(inputEvent, _storageDetails);
        _buildMenu.GetNode<Control>("Margin").GuiInput += inputEvent =>
            CloseWindowOnSecondaryInput(inputEvent, _buildMenu);
        _workMenu.GetNode<Control>("Margin").GuiInput += inputEvent =>
            CloseWindowOnSecondaryInput(inputEvent, _workMenu);
        _storagePullLoose.Toggled += enabled => _storageTarget.Editable = enabled;
        GetNode<Button>("StorageDetails/Margin/Controls/Apply").Pressed += ApplyStorageSettings;
        GetNode<Button>("Interface/RightHud/SessionPanel/Controls/Menu").Pressed += ShowMainMenu;
        GetNode<Button>("Interface/RightHud/SessionPanel/Controls/SaveGame").Pressed += SaveGame;
        _resumeGameButton.Pressed += ResumeGame;
        _newGameButton.Pressed += StartNewGame;
        _loadMenuButton.Pressed += LoadGame;
        GetNode<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/Quit").Pressed += () => GetTree().Quit();
        _worldView.SetWorld(_engine);
        _minimap.SetWorld(_engine);
        _worldView.SetSimulationSpeed(_speed, SecondsPerTick);
        _minimap.NavigationRequested += CenterCameraOn;
        GetViewport().SizeChanged += ConstrainCameraToMap;
        _camera.Position = _worldView.CellToWorld(map.GoblinSpawn);
        ConstrainCameraToMap();

        BindButton("Pause", 0);
        BindButton("Speed1", 1);
        BindButton("Speed2", 2);
        BindButton("Speed4", 4);
        BindButton("Speed8", 8);
        ConfigureActionButton("Pause", UiIcon.Pause, "Pauza • ~ lub Spacja");
        ConfigureActionButton("Speed1", UiIcon.Play, "Normalna prędkość • klawisz 1 • 1×");
        ConfigureActionButton("Speed2", UiIcon.Faster, "Przyspieszenie • klawisz 2 • 2×");
        ConfigureActionButton("Speed4", UiIcon.Fastest, "Przyspieszenie • klawisz 3 • 4×");
        ConfigureActionButton("Speed8", UiIcon.Fastest, "Maksymalne przyspieszenie • klawisz 4 • 8×");
        GetToolbarButton("Speed8").Icon = UiIcons.LoadSpeed8Texture();
        ConfigureActionButton("Build", UiIcon.Build, "Budowanie");
        ConfigureActionButton("Work", UiIcon.Work, "Zlecenia pracy");
        ConfigureActionButton("Raid", UiIcon.Expedition, "Przygotuj najazd na wieś");
        GetToolbarButton("Build").Pressed += () =>
            ShowBuildMenu(GetViewport().GetMousePosition());
        GetToolbarButton("Work").Pressed += () =>
            ShowWorkMenu(GetViewport().GetMousePosition());
        GetToolbarButton("Raid").Pressed += OrderVillageRaid;
        UpdateSpeedButtons();
        UpdateLayerToolAvailability();
        ScheduleNextAutosave();
        UpdateStatus();
        ShowMainMenu();
    }

    public override void _Process(double delta)
    {
        if (_mainMenu.Visible)
        {
            return;
        }

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
            var snapshot = _engine.CreateSnapshot();
            _worldView.Refresh(snapshot);
            _minimap.Refresh(snapshot);
            UpdateStatus();
            TryAutosave();
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (_mainMenu.Visible)
        {
            if (inputEvent is InputEventKey { Pressed: true, Echo: false } menuKey)
            {
                if (menuKey.AltPressed && menuKey.Keycode is Key.Enter or Key.KpEnter)
                {
                    ToggleFullscreen();
                }
                else if (menuKey.Keycode == Key.Escape && _hasActiveSession)
                {
                    ResumeGame();
                }
                else if (menuKey.Keycode == Key.F9)
                {
                    LoadGame();
                }
                else if (menuKey.CtrlPressed && menuKey.Keycode == Key.N)
                {
                    StartNewGame();
                }
            }

            GetViewport().SetInputAsHandled();
            return;
        }

        switch (inputEvent)
        {
            case InputEventKey key when key.Pressed && !key.Echo && key.AltPressed &&
                key.Keycode is Key.Enter or Key.KpEnter:
                ToggleFullscreen();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey key when key.Pressed && !key.Echo && key.CtrlPressed &&
                key.Keycode == Key.N:
                ShowMainMenu();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey key when key.Pressed && !key.Echo && key.Keycode == Key.F5:
                SaveGame();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey key when key.Pressed && !key.Echo && key.Keycode == Key.F9:
                LoadGame();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey key when key.Pressed && key.Keycode == Key.Space:
                SetSpeed(_speed == 0 ? 1 : 0);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey key when key.Pressed && !key.Echo &&
                TryResolveSpeedShortcut(key.Keycode, out var shortcutSpeed):
                SetSpeed(shortcutSpeed);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey key when key.Pressed && key.Keycode == Key.Pageup:
                ChangeVisibleLevel(1);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey key when key.Pressed && key.Keycode == Key.Pagedown:
                ChangeVisibleLevel(-1);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey key when key.Pressed && key.Keycode == Key.Escape:
                if (_buildMode != BuildMode.None || _workMode != WorkMode.None)
                {
                    CancelActiveTool();
                }
                else
                {
                    ShowMainMenu();
                }
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                ChangeCameraZoom(1.15f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                ChangeCameraZoom(1f / 1.15f);
                break;
            case InputEventMouseMotion mouse when _isPanningCamera:
                _camera.Position -= mouse.Relative / _camera.Zoom;
                ConstrainCameraToMap();
                _rightDragDistance += mouse.Relative.Length();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseMotion mouse when _buildMode != BuildMode.None:
                UpdateBuildPreview(mouse.Position);
                break;
            case InputEventMouseMotion mouse when _workMode != WorkMode.None:
                UpdateWorkPreview(mouse.Position);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouse:
                if (_buildMode != BuildMode.None)
                {
                    BeginConstruction(mouse.Position);
                }
                else if (_workMode != WorkMode.None)
                {
                    BeginWorkArea(mouse.Position);
                }
                else
                {
                    InspectWorld(mouse.Position);
                }
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } mouse
                when _isDraggingWalkway:
                FinishWalkway(mouse.Position);
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } mouse
                when _isDraggingWorkArea:
                FinishWorkArea(mouse.Position);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                if (_buildMode != BuildMode.None || _workMode != WorkMode.None)
                {
                    CancelActiveTool();
                }
                else
                {
                    _isPanningCamera = true;
                    _rightDragDistance = 0f;
                }
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right }:
                if (_isPanningCamera && _rightDragDistance < 4f)
                {
                    ClearSelection();
                }
                _isPanningCamera = false;
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void ToggleFullscreen()
    {
        var currentMode = DisplayServer.WindowGetMode();
        var isFullscreen = currentMode is DisplayServer.WindowMode.Fullscreen or
            DisplayServer.WindowMode.ExclusiveFullscreen;
        DisplayServer.WindowSetMode(isFullscreen
            ? DisplayServer.WindowMode.Windowed
            : DisplayServer.WindowMode.Fullscreen);
        _inspector.Text = isFullscreen
            ? "Tryb okienkowy • Alt+Enter przełącza pełny ekran."
            : "Pełny ekran w aktualnej rozdzielczości monitora • Alt+Enter wraca do okna.";
    }

    private SimulationEngine CreateNewEngine(WorldSeed seed)
    {
        var map = SwampMapGenerator.Generate(seed, 64, 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 8,
            initialFoodStock: 16,
            scatterInitialBrushwood: true,
            debugSettings: SimulationDebugSettings.ForCurrentBuild);
        _commandSequence = 1;
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1),
            _commandSequence++,
            map.GoblinSpawn,
            ResourceKind.Food,
            capacity: engine.Definitions.Storage.SmallFoodCapacity));
        return engine;
    }

    private void StartNewGame()
    {
        try
        {
            var protectedPreviousSession = _hasActiveSession;
            if (protectedPreviousSession)
            {
                SaveAutosave();
            }

            var seedBytes = Guid.NewGuid().ToByteArray();
            var seed = new WorldSeed(BitConverter.ToUInt64(seedBytes, 0));
            ReplaceEngine(CreateNewEngine(seed));
            _hasActiveSession = true;
            CloseMainMenu();
            _inspector.Text = $"Nowa gra • seed {seed.Value:X16}" +
                (protectedPreviousSession ? " • poprzednia sesja zabezpieczona autozapisem." : ".");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _inspector.Text = $"Nie udało się rozpocząć nowej gry: {exception.Message}";
        }
    }

    private void SaveGame()
    {
        if (!_hasActiveSession)
        {
            return;
        }

        try
        {
            _saveStore.SaveQuick(_engine.Save());
            _inspector.Text = $"Gra zapisana • tick {_engine.CurrentTick.Value:N0} • quicksave.json";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _inspector.Text = $"Zapis nie powiódł się: {exception.Message}";
        }
    }

    private void LoadGame()
    {
        foreach (var candidate in _saveStore.LoadNewestFirst())
        {
            try
            {
                var loaded = SimulationEngine.Load(
                    candidate.Json,
                    SimulationDefinitions.Foundation,
                    SimulationDebugSettings.ForCurrentBuild);
                ReplaceEngine(loaded);
                _hasActiveSession = true;
                CloseMainMenu();
                _inspector.Text = $"Gra wczytana • tick {_engine.CurrentTick.Value:N0} • {Path.GetFileName(candidate.Path)}";
                return;
            }
            catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException)
            {
                // Try an older rotating slot if the newest save is incompatible or damaged.
            }
        }

        _inspector.Text = "Nie znaleziono zgodnego zapisu do wczytania.";
    }

    private void ShowMainMenu()
    {
        if (_mainMenu.Visible)
        {
            return;
        }

        CancelActiveTool();
        _speedBeforeMenu = _speed;
        SetSpeed(0);
        _resumeGameButton.Visible = _hasActiveSession;
        _loadMenuButton.Disabled = !_saveStore.HasAnySave;
        _mainMenu.Show();
        (_hasActiveSession ? _resumeGameButton : _newGameButton).GrabFocus();
    }

    private void ResumeGame()
    {
        if (!_hasActiveSession)
        {
            return;
        }

        CloseMainMenu();
    }

    private void CloseMainMenu()
    {
        if (!_mainMenu.Visible)
        {
            return;
        }

        _mainMenu.Hide();
        SetSpeed(_speedBeforeMenu);
    }

    private void TryAutosave()
    {
        if (_engine.CurrentTick.Value < _nextAutosaveTick.Value)
        {
            return;
        }

        try
        {
            SaveAutosave();
            _inspector.Text = $"Autozapis ukończony • początek dnia " +
                $"{SimulationCalendar.At(_engine.CurrentTick, _engine.Definitions.Clock).AbsoluteDay + 1}.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _inspector.Text = $"Autozapis nie powiódł się: {exception.Message}";
        }
        finally
        {
            ScheduleNextAutosave();
        }
    }

    private void SaveAutosave()
    {
        _saveStore.SaveAuto(_engine.Save());
    }

    private void ScheduleNextAutosave() => _nextAutosaveTick =
        SimulationCalendar.NextDayStart(_engine.CurrentTick, _engine.Definitions.Clock);

    private void ReplaceEngine(SimulationEngine engine)
    {
        CancelActiveTool();
        SelectActor(EntityId.None);
        _storageDetails.Hide();
        _engine = engine;
        _commandSequence = engine.NextAvailableCommandSequence;
        _accumulator = 0;
        _visibleLevel = 0;
        _worldView.SetWorld(engine);
        _worldView.SetVisibleLevel(0);
        _worldView.SetSimulationSpeed(_speed, SecondsPerTick);
        _minimap.SetWorld(engine);
        _camera.Position = _worldView.CellToWorld(engine.Map.GoblinSpawn);
        UpdateLayerToolAvailability();
        ScheduleNextAutosave();
        ConstrainCameraToMap();
        UpdateStatus();
    }

    private void CloseWindowOnSecondaryInput(InputEvent inputEvent, Window window)
    {
        if (inputEvent is not InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Right,
            })
        {
            return;
        }

        window.Hide();
        GetViewport().SetInputAsHandled();
    }

    private void ClearSelection()
    {
        SelectActor(EntityId.None);
        _selectedStorageId = EntityId.None;
        _storageDetails.Hide();
        _inspector.Text = "Zaznaczenie wyczyszczone. PPM przeciągnięty przesuwa mapę.";
    }

    private void ShowBuildMenu(Vector2 screenPosition)
    {
        if (_visibleLevel > 0)
        {
            _inspector.Text = "Budowanie ponad powierzchnią wymaga blueprintu podpartej konstrukcji.";
            return;
        }

        _buildMenu.Position = new Vector2I((int)screenPosition.X, (int)screenPosition.Y);
        _buildMenu.Popup();
    }

    private void ShowWorkMenu(Vector2 screenPosition)
    {
        if (!EnsureSurfaceToolAvailable("Zlecenia pracy"))
        {
            return;
        }

        _workMenu.Position = new Vector2I((int)screenPosition.X, (int)screenPosition.Y);
        _workMenu.Popup();
    }

    private void ConfigureActionButton(string name, UiIcon icon, string tooltip)
    {
        var button = GetToolbarButton(name);
        button.Text = string.Empty;
        button.Icon = UiIcons.CreateTexture(_iconAtlas, icon);
        button.ExpandIcon = true;
        button.FocusMode = Control.FocusModeEnum.None;
        button.TooltipText = tooltip;
    }

    private void CreateTileButton(
        GridContainer grid,
        PopupPanel menu,
        UiIcon icon,
        string tooltip,
        Action action)
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(68, 68),
            Icon = UiIcons.CreateTexture(_iconAtlas, icon),
            ExpandIcon = true,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = tooltip,
        };
        button.Pressed += () =>
        {
            menu.Hide();
            action();
        };
        grid.AddChild(button);
    }

    private void CreateNeedIndicators()
    {
        var grid = GetNode<GridContainer>("GoblinDetails/Scroll/Content/Needs");
        _healthBar = CreateNeedIndicator(
            grid, UiIcon.Health, "Zdrowie", _engine.Definitions.MaximumHealth);
        _hungerBar = CreateNeedIndicator(
            grid, UiIcon.Hunger, "Nasycenie", _engine.Definitions.MaximumHunger);
        _thirstBar = CreateNeedIndicator(
            grid, UiIcon.Thirst, "Nawodnienie", _engine.Definitions.MaximumThirst);
        _fatigueBar = CreateNeedIndicator(
            grid, UiIcon.FieldCamp, "Wytrzymałość", _engine.Definitions.MaximumFatigue);
    }

    private ProgressBar CreateNeedIndicator(
        GridContainer grid,
        UiIcon icon,
        string tooltip,
        double maximum)
    {
        var image = new TextureRect
        {
            CustomMinimumSize = new Vector2(30, 30),
            Texture = UiIcons.CreateTexture(_iconAtlas, icon),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TooltipText = tooltip,
        };
        var bar = new ProgressBar
        {
            CustomMinimumSize = new Vector2(330, 30),
            MaxValue = maximum,
            ShowPercentage = true,
            TooltipText = tooltip,
        };
        grid.AddChild(image);
        grid.AddChild(bar);
        return bar;
    }

    private void SelectBuildMode(long id)
    {
        var mode = (BuildMode)id;
        if (!EnsureBuildModeAvailable(mode))
        {
            return;
        }

        CancelWorkMode(clearInspector: false);
        _buildMode = mode;
        _isDraggingWalkway = false;
        _worldView.SetConstructionPreview([]);
        _inspector.Text = _buildMode switch
        {
            BuildMode.FoodStorage => "Budowa składu żywności: wskaż pole LPM • koszt 2 drewna • Esc anuluje",
            BuildMode.WoodStorage => "Budowa składu drewna: wskaż pole LPM • koszt 2 drewna • Esc anuluje",
            BuildMode.Walkway => "Budowa pomostu: przeciągnij LPM od początku do końca • 1 drewno/segment • Esc anuluje",
            BuildMode.FieldCamp => "Obozowisko 2×2: wskaż lewy górny narożnik przy płytkiej wodzie • koszt 6 drewna • zawiera skład prowiantu",
            _ => _inspector.Text,
        };
    }

    private void SelectWorkMode(long id)
    {
        if (!EnsureSurfaceToolAvailable("Zlecenia pracy"))
        {
            return;
        }

        CancelBuildMode(clearInspector: false);
        _workMode = (WorkMode)id;
        _isDraggingWorkArea = false;
        _worldView.SetWorkPreview(default, []);
        _inspector.Text = _workMode switch
        {
            WorkMode.GatherFood => "Praca: przeciągnij obszar zbierania żywności • Esc anuluje",
            WorkMode.GatherBrushwood => "Praca: przeciągnij obszar zbierania chrustu • Esc anuluje",
            WorkMode.UprootBerryBushes => "Praca: przeciągnij obszar karczowania krzaków • usuwa je trwale • Esc anuluje",
            WorkMode.Clear => "Praca: przeciągnij obszar usuwania zleceń • Esc anuluje",
            _ => _inspector.Text,
        };
    }

    private void BeginConstruction(Vector2 screenPosition)
    {
        if (!EnsureBuildModeAvailable(_buildMode))
        {
            return;
        }

        var cell = ScreenToVisibleCell(screenPosition);
        if (!IsBuildableLayerCell(cell))
        {
            return;
        }

        if (_buildMode is BuildMode.FoodStorage or BuildMode.WoodStorage)
        {
            CreateStorage(
                cell,
                _buildMode == BuildMode.FoodStorage ? ResourceKind.Food : ResourceKind.Wood);
            CancelBuildMode(clearInspector: false);
            return;
        }

        if (_buildMode == BuildMode.FieldCamp)
        {
            var cells = GetAreaCells(cell, cell with { X = cell.X + 1, Y = cell.Y + 1 });
            var snapshot = _engine.CreateSnapshot();
            if (cells.Any(item =>
                    !_engine.Map.IsWithin(item) ||
                    !snapshot.GetVisibility(item, _engine.Map.Width).IsDiscovered()))
            {
                _inspector.Text = "Całe obozowisko musi mieścić się na odkrytym terenie.";
                CancelBuildMode(clearInspector: false);
                return;
            }
            _engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
                _engine.CurrentTick.Next(), _commandSequence++, cell));
            _inspector.Text = "Zlecono obozowisko 2×2 • koszt 6 drewna • skład prowiantu do 48, cel zależny od liczebności plemienia";
            CancelBuildMode(clearInspector: false);
            return;
        }

        _walkwayStart = cell;
        _isDraggingWalkway = true;
        UpdateBuildPreview(screenPosition);
    }

    private void FinishWalkway(Vector2 screenPosition)
    {
        var end = ScreenToVisibleCell(screenPosition);
        _isDraggingWalkway = false;
        if (!_engine.Map.IsWithin(end))
        {
            CancelBuildMode();
            return;
        }

        var cells = SimulationCommand.GetWalkwayCells(_walkwayStart, end);
        var snapshot = _engine.CreateSnapshot();
        if (cells.Any(cell =>
                !snapshot.GetVisibility(cell, _engine.Map.Width).IsDiscovered()))
        {
            _inspector.Text = "Pomost musi przebiegać przez odkryty teren.";
            CancelBuildMode(clearInspector: false);
            return;
        }

        _engine.QueueCommand(SimulationCommand.BuildWalkway(
            _engine.CurrentTick.Next(),
            _commandSequence++,
            _walkwayStart,
            end));
        _inspector.Text = $"Zlecono pomost: {cells.Count} segmentów • koszt {cells.Count} drewna";
        CancelBuildMode(clearInspector: false);
    }

    private void UpdateBuildPreview(Vector2 screenPosition)
    {
        var cell = ScreenToVisibleCell(screenPosition);
        if (!IsBuildableLayerCell(cell))
        {
            _worldView.SetConstructionPreview([]);
            return;
        }

        var cells = _buildMode switch
        {
            BuildMode.Walkway when _isDraggingWalkway =>
                SimulationCommand.GetWalkwayCells(_walkwayStart, cell),
            BuildMode.FieldCamp => GetAreaCells(cell, cell with { X = cell.X + 1, Y = cell.Y + 1 }),
            _ => new[] { cell },
        };
        _worldView.SetConstructionPreview(cells);
        if (_isDraggingWalkway)
        {
            _inspector.Text = $"Pomost: {cells.Count} segmentów • koszt {cells.Count} drewna";
        }
    }

    private void CancelBuildMode(bool clearInspector = true)
    {
        var wasActive = _buildMode != BuildMode.None;
        _buildMode = BuildMode.None;
        _isDraggingWalkway = false;
        _worldView.SetConstructionPreview([]);
        if (clearInspector && wasActive)
        {
            _inspector.Text = "Tryb budowy anulowany.";
        }
    }

    private void BeginWorkArea(Vector2 screenPosition)
    {
        if (!EnsureSurfaceToolAvailable("Zlecenia pracy"))
        {
            return;
        }

        var cell = ScreenToCell(screenPosition);
        if (!_engine.Map.IsWithin(cell))
        {
            return;
        }

        _workAreaStart = cell;
        _isDraggingWorkArea = true;
        UpdateWorkPreview(screenPosition);
    }

    private void UpdateWorkPreview(Vector2 screenPosition)
    {
        var cell = ScreenToCell(screenPosition);
        if (!_engine.Map.IsWithin(cell))
        {
            _worldView.SetWorkPreview(default, []);
            return;
        }

        var cells = _isDraggingWorkArea
            ? GetAreaCells(_workAreaStart, cell)
            : new[] { cell };
        var snapshot = _engine.CreateSnapshot();
        cells = cells.Where(position =>
            snapshot.GetVisibility(position, _engine.Map.Width).IsDiscovered()).ToArray();
        _worldView.SetWorkPreview(ToDesignationKind(_workMode), cells);
        if (_isDraggingWorkArea)
        {
            _inspector.Text = $"Zaznaczanie pracy: {cells.Count} pól; po zatwierdzeniu pozostaną tylko pasujące obiekty.";
        }
    }

    private void FinishWorkArea(Vector2 screenPosition)
    {
        var end = ScreenToCell(screenPosition);
        _isDraggingWorkArea = false;
        if (!_engine.Map.IsWithin(end))
        {
            CancelWorkMode();
            return;
        }

        var executeAt = _engine.CurrentTick.Next();
        var command = _workMode switch
        {
            WorkMode.GatherFood => SimulationCommand.DesignateWork(
                executeAt,
                _commandSequence++,
                _workAreaStart,
                end,
                ResourceKind.Food),
            WorkMode.GatherBrushwood => SimulationCommand.DesignateWork(
                executeAt,
                _commandSequence++,
                _workAreaStart,
                end,
                ResourceKind.Wood),
            WorkMode.UprootBerryBushes => SimulationCommand.DesignateWork(
                executeAt,
                _commandSequence++,
                _workAreaStart,
                end,
                ResourceKind.Vegetation),
            WorkMode.Clear => SimulationCommand.ClearWorkDesignations(
                executeAt,
                _commandSequence++,
                _workAreaStart,
                end),
            _ => default,
        };
        if (command.Kind == default)
        {
            return;
        }

        _engine.QueueCommand(command);
        _inspector.Text = _workMode == WorkMode.Clear
            ? "Zlecono usunięcie celów pracy z zaznaczenia."
            : "Zlecono wskazanie pasujących obiektów; cele pojawią się po następnym ticku.";
        CancelWorkMode(clearInspector: false);
    }

    private void CancelWorkMode(bool clearInspector = true)
    {
        var wasActive = _workMode != WorkMode.None;
        _workMode = WorkMode.None;
        _isDraggingWorkArea = false;
        _worldView.SetWorkPreview(default, []);
        if (clearInspector && wasActive)
        {
            _inspector.Text = "Narzędzie obszaru pracy anulowane.";
        }
    }

    private void CancelActiveTool()
    {
        var hadActiveTool = _buildMode != BuildMode.None || _workMode != WorkMode.None;
        CancelBuildMode(clearInspector: false);
        CancelWorkMode(clearInspector: false);
        if (hadActiveTool)
        {
            _inspector.Text = "Aktywne narzędzie anulowane.";
        }
    }

    private static WorkDesignationKind ToDesignationKind(WorkMode mode) => mode switch
    {
        WorkMode.GatherFood => WorkDesignationKind.GatherFood,
        WorkMode.GatherBrushwood => WorkDesignationKind.GatherBrushwood,
        WorkMode.UprootBerryBushes => WorkDesignationKind.UprootBerryBush,
        _ => default,
    };

    private static IReadOnlyList<GridPosition> GetAreaCells(GridPosition first, GridPosition second)
    {
        var minimumX = Math.Min(first.X, second.X);
        var maximumX = Math.Max(first.X, second.X);
        var minimumY = Math.Min(first.Y, second.Y);
        var maximumY = Math.Max(first.Y, second.Y);
        var cells = new List<GridPosition>((maximumX - minimumX + 1) * (maximumY - minimumY + 1));
        for (var y = minimumY; y <= maximumY; y++)
        {
            for (var x = minimumX; x <= maximumX; x++)
            {
                cells.Add(new GridPosition(x, y, first.Z));
            }
        }

        return cells;
    }

    private void CreateStorage(GridPosition cell, ResourceKind resource)
    {
        var snapshot = _engine.CreateSnapshot();
        var terrainAvailable = cell.Z == 0
            ? _engine.World.IsSurfaceTraversable(cell)
            : _engine.Map.IsTerrainTraversable(cell);
        var discovered = cell.Z < 0 ||
            snapshot.GetVisibility(cell, _engine.Map.Width).IsDiscovered();
        if (!terrainAvailable || !discovered)
        {
            _inspector.Text = $"{cell} • tu nie można wyznaczyć składu.";
            return;
        }

        var command = resource == ResourceKind.Food
            ? SimulationCommand.BuildFoodStorage(_engine.CurrentTick.Next(), _commandSequence++, cell)
            : SimulationCommand.BuildWoodStorage(_engine.CurrentTick.Next(), _commandSequence++, cell);
        _engine.QueueCommand(command);
        var capacity = resource == ResourceKind.Food
            ? _engine.Definitions.Storage.SmallFoodCapacity
            : 64;
        _inspector.Text = $"{cell} • wyznaczono plac pod skład {DescribeResource(resource)} 0/{capacity} • blueprint żąda 2 drewna";
    }

    private void HandleEvents(IReadOnlyList<SimulationEvent> events)
    {
        var workEvent = events.LastOrDefault(item =>
            item.Kind is SimulationEventKind.WorkDesignationCreated or
                SimulationEventKind.WorkDesignationRemoved or
                SimulationEventKind.StoragePullConfigured or
                SimulationEventKind.StorageHaulerConfigured or
                SimulationEventKind.StorageSourceConfigured or
                SimulationEventKind.StoragePriorityConfigured or
                SimulationEventKind.ResourcePriorityConfigured);
        if (workEvent.Kind == SimulationEventKind.WorkDesignationCreated)
        {
            _inspector.Text = (WorkDesignationKind)workEvent.Amount switch
            {
                WorkDesignationKind.GatherFood => "Dispatcher dodał wskazane źródło żywności do zebrania.",
                WorkDesignationKind.GatherBrushwood => "Dispatcher dodał wskazany stos chrustu do transportu.",
                WorkDesignationKind.UprootBerryBush => "Dispatcher dodał krzak do trwałego wykarczowania.",
                _ => "Dispatcher dodał cel pracy.",
            };
        }
        else if (workEvent.Kind == SimulationEventKind.WorkDesignationRemoved)
        {
            _inspector.Text = "Cel pracy został zakończony lub usunięty.";
        }
        else if (workEvent.Kind is SimulationEventKind.StoragePullConfigured or
                 SimulationEventKind.StorageHaulerConfigured or
                 SimulationEventKind.StorageSourceConfigured or
                 SimulationEventKind.StoragePriorityConfigured or
                 SimulationEventKind.ResourcePriorityConfigured)
        {
            var configuredId = workEvent.Kind == SimulationEventKind.ResourcePriorityConfigured
                ? _selectedStorageId
                : workEvent.Target;
            var configured = _engine.CreateSnapshot().StorageZones
                .FirstOrDefault(zone => zone.Id == configuredId);
            if (configured.Id != EntityId.None && configured.Id == _selectedStorageId)
            {
                UpdateStorageDetails(configured);
            }
        }

        if (_selectedStorageId != EntityId.None &&
            events.Any(item => item.Kind is
                SimulationEventKind.ItemPickedUp or SimulationEventKind.ItemStored))
        {
            var selectedStorage = _engine.CreateSnapshot().StorageZones
                .FirstOrDefault(zone => zone.Id == _selectedStorageId);
            if (selectedStorage.Id != EntityId.None)
            {
                UpdateStorageDetails(selectedStorage);
            }
        }

        var constructionEvent = events.LastOrDefault(item =>
            item.Kind is SimulationEventKind.ConstructionOrdered or
                SimulationEventKind.ConstructionMaterialDelivered or
                SimulationEventKind.ConstructionCompleted ||
            (item.Kind == SimulationEventKind.CommandRejected &&
             item.Amount == (int)SimulationCommandKind.Build));
        if (constructionEvent.Kind == SimulationEventKind.CommandRejected)
        {
            _inspector.Text = "Nie można wyznaczyć placu budowy: teren jest niedostępny albo zajęty.";
        }
        else if (constructionEvent.Kind is SimulationEventKind.ConstructionOrdered or
                 SimulationEventKind.ConstructionMaterialDelivered)
        {
            var site = _engine.CreateSnapshot().ConstructionSites
                .FirstOrDefault(item => item.Id == constructionEvent.Target);
            if (site is not null)
            {
                _inspector.Text = DescribeConstructionSite(site);
            }
        }
        else if (constructionEvent.Kind == SimulationEventKind.ConstructionCompleted)
        {
            var snapshot = _engine.CreateSnapshot();
            var zone = snapshot.StorageZones
                .FirstOrDefault(item => item.Id == constructionEvent.Target);
            var completedCamp = zone.Id != EntityId.None && snapshot.WorldObjects.Any(item =>
                item.Kind == WorldObjectKind.GoblinFieldCamp && item.Anchor == zone.Position);
            _inspector.Text = constructionEvent.Target == EntityId.None
                ? $"Pomost ukończony • zużyto {constructionEvent.Amount} drewna"
                : completedCamp
                    ? $"Obóz wypadowy ukończony • zużyto {constructionEvent.Amount} drewna"
                : $"Skład {DescribeResource(zone.AcceptedResource)} ukończony • " +
                  $"zużyto {constructionEvent.Amount} drewna";
        }

        var selectedEvent = events.LastOrDefault(item =>
            item.Subject == _selectedActorId &&
            (item.Kind == SimulationEventKind.MoveCompleted ||
             (item.Kind == SimulationEventKind.CommandRejected &&
              item.Amount == (int)SimulationCommandKind.Move)));
        var selectedHit = events.LastOrDefault(item =>
            item.Target == _selectedActorId &&
            item.Kind == SimulationEventKind.HumanGuardHitGoblin);
        if (selectedHit.Kind == SimulationEventKind.HumanGuardHitGoblin)
        {
            _inspector.Text = $"{selectedHit.Target} • trafiony przez straż • −{selectedHit.Amount} zdrowia";
        }
        else if (selectedEvent.Kind == SimulationEventKind.CommandRejected &&
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
        if (_visibleLevel != 0)
        {
            var levelPosition = cell with { Z = _visibleLevel };
            if (_visibleLevel < 0 && _engine.Map.IsCavePosition(levelPosition))
            {
                var caveCell = _engine.Map.GetCaveCell(levelPosition);
                var passages = _engine.Map.VerticalPassages
                    .Where(passage => passage.Upper == levelPosition || passage.Lower == levelPosition)
                    .Select(passage => passage.Kind == VerticalPassageKind.CaveMouth
                        ? "wejście na powierzchnię"
                        : "pochylnia między poziomami")
                    .ToArray();
                SelectActor(EntityId.None);
                _inspector.Text = $"{levelPosition} • {DescribeCaveRock(caveCell.Rock)} • " +
                    $"{DescribeCaveKind(caveCell.Kind)}" +
                    (passages.Length == 0 ? string.Empty : $" • {string.Join(", ", passages)}");
                return;
            }

            var parts = snapshot.WorldObjects
                .SelectMany(worldObject => worldObject.GetAbsoluteParts()
                    .Where(part => part.Position == levelPosition)
                    .Select(part => $"{worldObject.Kind}: {part.Part.Kind}"))
                .ToArray();
            var mapCell = _engine.Map.GetCell(cell);
            SelectActor(EntityId.None);
            _inspector.Text = $"{levelPosition} • warstwa z={_visibleLevel}" +
                (mapCell.FloorLevel == _visibleLevel ? " • dno terenu" : " • pusta przestrzeń") +
                (parts.Length == 0 ? string.Empty : $" • {string.Join(", ", parts)}");
            return;
        }

        var visibility = snapshot.GetVisibility(cell, _engine.Map.Width);
        if (!visibility.IsDiscovered())
        {
            SelectActor(EntityId.None);
            _inspector.Text = $"{cell} • nieznany teren";
            return;
        }

        var terrain = _engine.Map.GetCell(cell);
        var plant = _engine.World.GetPlantPatch(cell);
        var objects = _engine.World.GetWorldObjectsAt(cell);
        var actors = snapshot.Actors.Where(actor => actor.Position == cell).ToArray();
        var humanCohorts = snapshot.HumanVillage.Cohorts
            .Where(cohort => cohort.Population > 0 && cohort.Position == cell)
            .ToArray();
        var humanFields = snapshot.HumanVillage.Fields.Where(field => field.Position == cell).ToArray();
        var groundStacks = snapshot.ItemStacks.Where(stack =>
            stack.Location.Kind == ItemLocationKind.Ground &&
            stack.Location.Position == cell).ToArray();
        var carriedStacks = actors
            .Where(actor => actor.CarriedStackId != EntityId.None)
            .Select(actor => snapshot.ItemStacks.Single(stack => stack.Id == actor.CarriedStackId))
            .ToArray();
        var zones = snapshot.StorageZones.Where(zone => zone.Position == cell).ToArray();
        var constructionSites = snapshot.ConstructionSites
            .Where(site => site.Footprint.Contains(cell))
            .ToArray();
        SelectActor(actors.OrderBy(actor => actor.Id).FirstOrDefault().Id);
        if (actors.Length == 0 && zones.Length > 0)
        {
            ShowStorageDetails(zones[0]);
        }

        _inspector.Text = $"{cell}" +
            (visibility == CellVisibility.Explored ? " • odkryte, obecnie niewidoczne" : string.Empty) +
            $" • {terrain.Terrain}{DescribeWaterDepth(terrain)} • wilgoć {terrain.Moisture} • żyzność {terrain.Fertility}" +
            (plant is null
                ? string.Empty
                : $" • {DescribeFoodSource(plant.Value.Kind)} {plant.Value.Biomass}/{plant.Value.Capacity}") +
            (objects.Count == 0 ? string.Empty : $" • {string.Join(", ", objects.Select(item => item.Kind))}") +
            (humanCohorts.Length == 0
                ? string.Empty
                : $" • ludzie: {string.Join(", ", humanCohorts.Select(DescribeCohort))}") +
            (humanFields.Length == 0
                ? string.Empty
                : $" • pole: {string.Join(", ", humanFields.Select(field => $"{DescribeField(field.Phase)} {field.GrowthDays}/120 dni"))}") +
            (!humanCohorts.Any(cohort => cohort.Role == HumanCohortRole.Guards)
                ? string.Empty
                : $" • alarm {snapshot.HumanVillage.Hostility}/100, siła straży " +
                  $"{snapshot.HumanVillage.GuardHitPoints}/{snapshot.HumanVillage.MaximumGuardHitPoints}") +
            (cell != snapshot.HumanVillage.Anchor &&
             !objects.Any(item => item.Owner == WorldObjectOwner.HumanVillage)
                ? string.Empty
                : $" • wieś: {snapshot.HumanVillage.Population} osób, żywność {snapshot.HumanVillage.FoodStock}, " +
                  $"woda {snapshot.HumanVillage.WaterStock}, drewno {snapshot.HumanVillage.WoodStock}, " +
                  $"pola {snapshot.HumanVillage.Fields.Count}/{snapshot.HumanVillage.PlannedFieldCount}, " +
                  $"spichlerze {snapshot.HumanVillage.StorehouseCount}") +
            (zones.Length == 0
                ? string.Empty
                : $" • skład: {string.Join(", ", zones.Select(zone => $"{DescribeResource(zone.AcceptedResource)} {zone.StoredQuantity}/{zone.Capacity}"))}") +
            (constructionSites.Length == 0
                ? string.Empty
                : $" • {string.Join(" • ", constructionSites.Select(DescribeConstructionSite))}") +
            (actors.Length == 0
                ? string.Empty
                : $" • gobliny ×{actors.Length}, nasycenie " +
                  $"{actors.Min(actor => _engine.Definitions.MaximumHunger - actor.Hunger)}–" +
                  $"{actors.Max(actor => _engine.Definitions.MaximumHunger - actor.Hunger)}" +
                  $", wytrzymałość {actors.Min(actor => _engine.Definitions.MaximumFatigue - actor.Fatigue)}–" +
                  $"{actors.Max(actor => _engine.Definitions.MaximumFatigue - actor.Fatigue)}" +
                  $", nawodnienie {actors.Min(actor => _engine.Definitions.MaximumThirst - actor.Thirst)}–" +
                  $"{actors.Max(actor => _engine.Definitions.MaximumThirst - actor.Thirst)}" +
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

    private static string DescribeCaveRock(RockKind rock) => rock switch
    {
        RockKind.Sandstone => "piaskowiec",
        RockKind.Granite => "granit",
        _ => rock.ToString(),
    };

    private static string DescribeCaveKind(CaveCellKind kind) => kind switch
    {
        CaveCellKind.SolidRock => "lita skała",
        CaveCellKind.Floor => "podłoga jaskini • przejście dostępne",
        CaveCellKind.Ramp => "naturalna pochylnia • przejście dostępne",
        _ => kind.ToString(),
    };

    private GridPosition ScreenToCell(Vector2 screenPosition)
    {
        var worldPosition = _camera.GetScreenCenterPosition() +
            ((screenPosition - GetViewport().GetVisibleRect().Size / 2f) / _camera.Zoom);
        return _worldView.WorldToCell(worldPosition);
    }

    private GridPosition ScreenToVisibleCell(Vector2 screenPosition) =>
        ScreenToCell(screenPosition) with { Z = _visibleLevel };

    private static string DescribeStack(ItemStackSnapshot stack) =>
        $"{(stack.Resource == ResourceKind.Food
            ? DescribeFood(stack.FoodKind)
            : DescribeResource(stack.Resource))} ×{stack.Quantity}";

    private static string DescribeFood(FoodKind food) => food switch
    {
        FoodKind.DriedRations => "suszone racje",
        FoodKind.Berries => "jagody",
        FoodKind.Mushrooms => "grzyby",
        FoodKind.EdibleRoots => "jadalne korzonki",
        FoodKind.Fish => "ryby",
        _ => "żywność",
    };

    private static string DescribeResource(ResourceKind resource) => resource switch
    {
        ResourceKind.Food => "żywności",
        ResourceKind.Wood => "drewna",
        ResourceKind.Reeds => "sitowia",
        ResourceKind.Stone => "kamienia",
        ResourceKind.Bone => "kości",
        ResourceKind.Vegetation => "roślinności",
        _ => "towarów",
    };

    private static string DescribeStoragePriority(StoragePriority priority) => priority switch
    {
        StoragePriority.Low => "Niski",
        StoragePriority.Normal => "Normalny",
        StoragePriority.High => "Wysoki",
        StoragePriority.Urgent => "Pilny",
        _ => "Nieznany",
    };

    private static string DescribeCohort(HumanCohortSnapshot cohort) =>
        $"{cohort.Role switch
        {
            HumanCohortRole.Farmers => "rolnicy",
            HumanCohortRole.Workers => "robotnicy",
            HumanCohortRole.Guards => "strażnicy",
            _ => "nieznani",
        }} ×{cohort.Population} • {DescribeHumanTask(cohort.Task)} • um. {cohort.SkillLevel} • {cohort.Tools}";

    private static string DescribeHumanTask(HumanCohortTask task) => task switch
    {
        HumanCohortTask.WorkFields => "pracują na polach",
        HumanCohortTask.DrawWater => "czerpią wodę",
        HumanCohortTask.ClearLand => "karczują pod pola",
        HumanCohortTask.GatherBerries => "szukają jagód",
        HumanCohortTask.BuildStorehouse => "budują spichlerz",
        HumanCohortTask.Guard => "strzegą wsi",
        HumanCohortTask.Flee => "uciekają",
        _ => "trzymają się wsi",
    };

    private static string DescribeField(HumanFieldPhase phase) => phase switch
    {
        HumanFieldPhase.Cleared => "oczyszczone",
        HumanFieldPhase.Sown => "obsiane",
        HumanFieldPhase.Growing => "rośnie",
        HumanFieldPhase.Ripe => "do zbioru",
        _ => "nieznane",
    };

    private static string DescribeJob(ActorJobSnapshot job) => job.Kind switch
    {
        ActorJobKind.Forage when job.Phase == ActorJobPhase.Traveling => $"idzie po żywność → {job.Target}",
        ActorJobKind.Forage when job.Phase == ActorJobPhase.Working => $"zbiera ({job.RemainingWorkTicks})",
        ActorJobKind.Haul when job.Stage == ActorJobStage.Collecting && job.Phase == ActorJobPhase.Traveling =>
            $"idzie po ładunek ×{job.ReservedQuantity}",
        ActorJobKind.Haul when job.Stage == ActorJobStage.Collecting =>
            $"ładuje ×{job.ReservedQuantity} ({job.RemainingWorkTicks})",
        ActorJobKind.Haul when job.Stage == ActorJobStage.Delivering && job.Phase == ActorJobPhase.Traveling =>
            $"niesie ×{job.ReservedQuantity}",
        ActorJobKind.Haul when job.Stage == ActorJobStage.Delivering =>
            $"rozładowuje ×{job.ReservedQuantity} ({job.RemainingWorkTicks})",
        ActorJobKind.SupplyConstruction when job.Stage == ActorJobStage.Collecting &&
            job.Phase == ActorJobPhase.Traveling => $"idzie po materiał budowlany ×{job.ReservedQuantity}",
        ActorJobKind.SupplyConstruction when job.Stage == ActorJobStage.Collecting =>
            $"pobiera materiał budowlany ×{job.ReservedQuantity} ({job.RemainingWorkTicks})",
        ActorJobKind.SupplyConstruction when job.Phase == ActorJobPhase.Traveling =>
            $"niesie materiał na budowę ×{job.ReservedQuantity}",
        ActorJobKind.SupplyConstruction =>
            $"składa materiał na budowie ({job.RemainingWorkTicks})",
        ActorJobKind.BuildConstruction when job.Phase == ActorJobPhase.Traveling =>
            $"idzie budować → {job.Target}",
        ActorJobKind.BuildConstruction => $"buduje ({job.RemainingWorkTicks})",
        ActorJobKind.Rest when job.Phase == ActorJobPhase.Traveling => $"idzie odpocząć → {job.Target}",
        ActorJobKind.Rest => $"odpoczywa ({job.RemainingWorkTicks})",
        ActorJobKind.Collapsed => $"padł ze zmęczenia i śpi na ziemi ({job.RemainingWorkTicks})",
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
        ActorJobKind.ClearVegetation when job.Phase == ActorJobPhase.Traveling =>
            $"idzie wykarczować krzak → {job.Target}",
        ActorJobKind.ClearVegetation => $"karczuje krzak ({job.RemainingWorkTicks})",
        _ => "bez zadania",
    };

    private static string DescribeConstructionSite(ConstructionSiteSnapshot site)
    {
        var materials = string.Join(", ", site.Materials.Select(material =>
            $"{DescribeResource(material.Resource)} {material.DeliveredQuantity}/{material.RequiredQuantity}"));
        var workDone = site.TotalWorkTicks - site.RemainingWorkTicks;
        return $"plac budowy {DescribeConstruction(site.Kind)} • materiały: {materials} • praca {workDone}/{site.TotalWorkTicks}";
    }

    private static string DescribeConstruction(ConstructionKind kind) => kind switch
    {
        ConstructionKind.FoodStorage => "składu żywności",
        ConstructionKind.WoodStorage => "składu drewna",
        ConstructionKind.WoodenWalkway => "pomostu",
        ConstructionKind.GoblinFieldCamp => "obozu wypadowego",
        _ => "konstrukcji",
    };

    private static string DescribeFoodSource(PlantKind kind) => kind switch
    {
        PlantKind.BerryBush => "jagody",
        PlantKind.MushroomCluster => "grzyby",
        PlantKind.EdibleRoots => "korzonki",
        PlantKind.FishShoal => "ryby",
        _ => "żywność",
    };

    private static string DescribeWaterDepth(MapCell cell) => cell.Terrain switch
    {
        TerrainKind.ShallowWater => " • bród, dno z=0",
        TerrainKind.DeepWater => $" • głębokość ≥{cell.WaterDepthLevels}, dno z={cell.FloorLevel}",
        _ => string.Empty,
    };

    private void SelectActor(EntityId actorId)
    {
        _selectedActorId = actorId;
        _worldView.SetSelectedActor(actorId);
        if (actorId == EntityId.None)
        {
            _goblinDetails.Hide();
            return;
        }

        UpdateGoblinDetails(_engine.CreateSnapshot());
        _storageDetails.Hide();
        _goblinDetails.Popup();
    }

    private void ShowStorageDetails(StorageZoneSnapshot zone)
    {
        _selectedStorageId = zone.Id;
        UpdateStorageDetails(zone);
        _storageDetails.Popup();
    }

    private void UpdateStorageDetails(StorageZoneSnapshot zone)
    {
        var snapshot = _engine.CreateSnapshot();
        var delivery = _engine.InspectStorageDelivery(zone.Id);
        var contents = snapshot.ItemStacks
            .Where(stack =>
                stack.Location.Kind == ItemLocationKind.StorageZone &&
                stack.Location.OwnerId == zone.Id)
            .OrderBy(stack => stack.FoodKind)
            .ThenBy(stack => stack.Id)
            .Select(DescribeStack)
            .ToArray();
        var assignedHauler = snapshot.Actors.FirstOrDefault(actor =>
            actor.Id == zone.AssignedHaulerId);
        var haulerDescription = assignedHauler.Id == EntityId.None
            ? "publiczny dispatcher"
            : $"{assignedHauler.Name} ({assignedHauler.Id})";
        var sourceZone = snapshot.StorageZones.FirstOrDefault(candidate =>
            candidate.Id == zone.SourceStorageZoneId);
        var sourceDescription = sourceZone.Id == EntityId.None
            ? "teren i nadwyżki dowolnych składów"
            : $"skład {sourceZone.Id} przy {sourceZone.Position}";
        var globalPriority = snapshot.ResourcePriorities
            .Single(priority => priority.Resource == zone.AcceptedResource)
            .Priority;
        _storageSummary.Text = $"Skład {DescribeResource(zone.AcceptedResource)}\n" +
            $"Stan: {zone.StoredQuantity}/{zone.Capacity}\n" +
            (zone.TypeSlotCount > 0
                ? $"Sloty rodzajowe: {zone.UsedTypeSlots}/{zone.TypeSlotCount}, " +
                  $"stos do {zone.StackCapacity} szt.\n"
                : string.Empty) +
            (contents.Length == 0 ? "Zawartość: pusty\n" :
                $"Zawartość: {string.Join(", ", contents)}\n") +
            (zone.DesiredQuantity == 0
                ? "Automatyczne dostawy wyłączone.\n"
                : $"Żądanie dostawy do {zone.DesiredQuantity} szt.\n") +
            $"Status dostaw: {DescribeStorageDelivery(delivery, assignedHauler)}\n" +
            $"Transport: {haulerDescription}.\n" +
            $"Źródło: {sourceDescription}.\n" +
            $"Priorytet lokalny: {DescribeStoragePriority(zone.Priority)}.\n" +
            $"Priorytet {DescribeResource(zone.AcceptedResource)} w plemieniu: " +
            $"{DescribeStoragePriority(globalPriority)}.";
        _storagePullLoose.ButtonPressed = zone.DesiredQuantity > 0;
        _storageTarget.MaxValue = zone.Capacity;
        _storageTarget.Value = zone.DesiredQuantity > 0
            ? zone.DesiredQuantity
            : zone.Capacity;
        _storageTarget.Editable = zone.DesiredQuantity > 0;
        _storagePriority.Select((int)zone.Priority);
        _resourcePriority.Select((int)globalPriority);

        _storageHauler.Clear();
        _storageHaulerActorIds.Clear();
        _storageHauler.AddItem("Dowolny wolny goblin");
        _storageHaulerActorIds.Add(EntityId.None);
        foreach (var actor in snapshot.Actors.OrderBy(actor => actor.Id))
        {
            _storageHauler.AddItem($"{actor.Name} ({actor.Id})");
            _storageHaulerActorIds.Add(actor.Id);
        }

        var selectedHaulerIndex = _storageHaulerActorIds.IndexOf(zone.AssignedHaulerId);
        _storageHauler.Select(Math.Max(0, selectedHaulerIndex));

        _storageSource.Clear();
        _storageSourceZoneIds.Clear();
        _storageSource.AddItem("Dowolne źródło");
        _storageSourceZoneIds.Add(EntityId.None);
        foreach (var candidate in snapshot.StorageZones
                     .Where(candidate => candidate.Id != zone.Id &&
                         candidate.AcceptedResource == zone.AcceptedResource)
                     .OrderBy(candidate => candidate.Id))
        {
            _storageSource.AddItem($"Skład {candidate.Id} • {candidate.Position}");
            _storageSourceZoneIds.Add(candidate.Id);
        }

        var selectedSourceIndex = _storageSourceZoneIds.IndexOf(zone.SourceStorageZoneId);
        _storageSource.Select(Math.Max(0, selectedSourceIndex));
    }

    private static string DescribeStorageDelivery(
        StorageDeliveryDiagnostic delivery,
        ActorSnapshot assignedHauler) => delivery.State switch
    {
        StorageDeliveryState.Disabled => "wyłączone",
        StorageDeliveryState.Satisfied => "cel osiągnięty",
        StorageDeliveryState.InTransit =>
            $"w drodze {delivery.InTransitQuantity} szt. (brakuje {delivery.RequestedQuantity})",
        StorageDeliveryState.NoAllowedSource => "brak dozwolonego źródła z tym zasobem",
        StorageDeliveryState.NoSurplus =>
            $"źródła istnieją, ale nie mają nadwyżki (brakuje {delivery.RequestedQuantity})",
        StorageDeliveryState.DestinationBlocked =>
            "brak wolnego slotu dla dostępnego rodzaju zasobu",
        StorageDeliveryState.NoReachableSource =>
            $"nadwyżka {delivery.AvailableSourceQuantity} szt. istnieje, ale nie ma drogi",
        StorageDeliveryState.NoAvailableHauler => "brak goblina mogącego obsłużyć dostawę",
        StorageDeliveryState.AssignedHaulerBusy => assignedHauler.Id == EntityId.None
            ? "przypisany tragarz jest zajęty"
            : $"{assignedHauler.Name} jest zajęty: {DescribeJob(assignedHauler.Job)}",
        StorageDeliveryState.WaitingForHauler =>
            $"oczekuje na tragarza; dostępne {delivery.AvailableSourceQuantity} szt.",
        _ => "nieznany",
    };

    private void ApplyStorageSettings()
    {
        var snapshot = _engine.CreateSnapshot();
        var zone = snapshot.StorageZones.FirstOrDefault(item => item.Id == _selectedStorageId);
        if (zone.Id == EntityId.None)
        {
            _storageDetails.Hide();
            return;
        }

        var desired = _storagePullLoose.ButtonPressed
            ? Math.Clamp((int)Math.Round(_storageTarget.Value), 1, zone.Capacity)
            : 0;
        var selectedHaulerIndex = _storageHauler.Selected;
        var assignedHaulerId = selectedHaulerIndex >= 0 &&
            selectedHaulerIndex < _storageHaulerActorIds.Count
                ? _storageHaulerActorIds[selectedHaulerIndex]
                : EntityId.None;
        var selectedSourceIndex = _storageSource.Selected;
        var sourceZoneId = selectedSourceIndex >= 0 &&
            selectedSourceIndex < _storageSourceZoneIds.Count
                ? _storageSourceZoneIds[selectedSourceIndex]
                : EntityId.None;
        var priority = Enum.IsDefined((StoragePriority)_storagePriority.Selected)
            ? (StoragePriority)_storagePriority.Selected
            : StoragePriority.Normal;
        var globalPriority = Enum.IsDefined((StoragePriority)_resourcePriority.Selected)
            ? (StoragePriority)_resourcePriority.Selected
            : StoragePriority.Normal;
        var executeAt = _engine.CurrentTick.Next();
        _engine.QueueCommand(SimulationCommand.ConfigureStoragePull(
            executeAt,
            _commandSequence++,
            zone.Id,
            desired));
        _engine.QueueCommand(SimulationCommand.ConfigureStorageHauler(
            executeAt,
            _commandSequence++,
            zone.Id,
            assignedHaulerId));
        _engine.QueueCommand(SimulationCommand.ConfigureStorageSource(
            executeAt,
            _commandSequence++,
            zone.Id,
            sourceZoneId));
        _engine.QueueCommand(SimulationCommand.ConfigureStoragePriority(
            executeAt,
            _commandSequence++,
            zone.Id,
            priority));
        _engine.QueueCommand(SimulationCommand.ConfigureResourcePriority(
            executeAt,
            _commandSequence++,
            zone.AcceptedResource,
            globalPriority));
        var haulerDescription = assignedHaulerId == EntityId.None
            ? "dowolny wolny goblin"
            : snapshot.Actors.First(actor => actor.Id == assignedHaulerId).Name;
        var sourceDescription = sourceZoneId == EntityId.None
            ? "dowolne źródło"
            : $"skład {sourceZoneId}";
        _inspector.Text = desired == 0
            ? $"Skład {zone.Id}: wyłączono automatyczne dostawy; transport: {haulerDescription}; źródło: {sourceDescription}; priorytet lokalny: {DescribeStoragePriority(priority)}; globalny: {DescribeStoragePriority(globalPriority)}."
            : $"Skład {zone.Id}: żądaj zasobów do {desired}; transport: {haulerDescription}; źródło: {sourceDescription}; priorytet lokalny: {DescribeStoragePriority(priority)}; globalny: {DescribeStoragePriority(globalPriority)}.";
    }

    private void UpdateGoblinDetails(SimulationSnapshot snapshot)
    {
        var actor = snapshot.Actors.FirstOrDefault(item => item.Id == _selectedActorId);
        if (actor.Id == EntityId.None)
        {
            return;
        }

        UpdateNeedBar(_healthBar, actor.Health, _engine.Definitions.MaximumHealth, "Zdrowie");
        UpdateNeedBar(
            _hungerBar,
            _engine.Definitions.MaximumHunger - actor.Hunger,
            _engine.Definitions.MaximumHunger,
            "Nasycenie");
        UpdateNeedBar(
            _thirstBar,
            _engine.Definitions.MaximumThirst - actor.Thirst,
            _engine.Definitions.MaximumThirst,
            "Nawodnienie");
        UpdateNeedBar(
            _fatigueBar,
            _engine.Definitions.MaximumFatigue - actor.Fatigue,
            _engine.Definitions.MaximumFatigue,
            "Wytrzymałość");
        _healthBar.TooltipText += " • obecnie brak naturalnej regeneracji";
        _hungerBar.TooltipText += " • obrażenia z głodu zaczynają się poniżej 500";
        _thirstBar.TooltipText += " • obrażenia z odwodnienia zaczynają się poniżej 500";

        var cargo = actor.CarriedStackId == EntityId.None
            ? (ItemStackSnapshot?)null
            : snapshot.ItemStacks.FirstOrDefault(stack => stack.Id == actor.CarriedStackId);
        var logisticsDuty = snapshot.StorageZones
            .Where(zone => zone.AssignedHaulerId == actor.Id)
            .OrderByDescending(zone => zone.Priority)
            .ThenBy(zone => zone.Id)
            .Select(zone =>
                $"skład {zone.Id} ({DescribeResource(zone.AcceptedResource)}, " +
                $"{DescribeStoragePriority(zone.Priority)})")
            .ToArray();
        UpdateInventoryIcons(actor, cargo);
        var text = new StringBuilder()
            .AppendLine($"{actor.Name}  [#{actor.Id}]")
            .AppendLine($"Pozycja: {actor.Position}")
            .AppendLine()
            .AppendLine($"Znane umiejętności: {DescribeSkills(actor.KnownSkills)}")
            .AppendLine($"Doświadczenie: {DescribeExperience(actor.Experience)}")
            .AppendLine($"Znane cechy: {DescribeTraits(actor.KnownTraits)}")
            .AppendLine($"Służba logistyczna: " +
                (logisticsDuty.Length == 0 ? "brak przydziału" : string.Join(", ", logisticsDuty)))
            .AppendLine()
            .AppendLine("Aktualne zadanie:")
            .AppendLine(DescribeJob(actor.Job))
            .AppendLine($"Faza: {actor.Job.Phase} • etap: {actor.Job.Stage}")
            .AppendLine($"Cel: {actor.Job.Target} • pozostała trasa: {actor.Job.RemainingRouteSteps} pól")
            .AppendLine($"Pozostała praca: {actor.Job.RemainingWorkTicks} ticków")
            .AppendLine($"Źródło: {actor.Job.SourceStackId} • skład docelowy: {actor.Job.DestinationZoneId}")
            .Append($"Rezerwacja ładunku: {actor.Job.ReservedQuantity}");
        _goblinDetails.Title = actor.Name;
        _goblinDetailsText.Text = text.ToString();
    }

    private static void UpdateNeedBar(ProgressBar bar, int value, int maximum, string name)
    {
        bar.Value = value;
        bar.TooltipText = $"{name}: {value:N0} / {maximum:N0}";
    }

    private void UpdateInventoryIcons(ActorSnapshot actor, ItemStackSnapshot? cargo)
    {
        var signature = $"{(int)actor.Equipment}:{actor.PersonalFood}:{(int)actor.PersonalFoodKind}:" +
            $"{actor.PersonalWater}:" +
            (cargo is null ? "none" : $"{cargo.Value.Id}:{cargo.Value.Resource}:{cargo.Value.Quantity}");
        if (_inventorySignature == signature)
        {
            return;
        }

        _inventorySignature = signature;
        foreach (var child in _inventoryIcons.GetChildren())
        {
            _inventoryIcons.RemoveChild(child);
            child.QueueFree();
        }

        if (actor.Equipment.HasFlag(PersonalEquipment.RagClothes))
        {
            AddInventoryIcon(ItemIcon.RagClothes, "Łachmany • ubranie osobiste");
        }
        if (actor.Equipment.HasFlag(PersonalEquipment.BoneKnife))
        {
            AddInventoryIcon(ItemIcon.BoneKnife, "Kościany nóż • broń i narzędzie osobiste");
        }
        AddInventoryIcon(
            ItemIcon.Food,
            actor.PersonalFood == 0
                ? "Osobiste racje żywności • pusto"
                : $"Osobiste racje • {DescribeFood(actor.PersonalFoodKind)} • " +
                  $"sytość {_engine.Definitions.Food.GetSatiety(actor.PersonalFoodKind):N0} / porcję",
            actor.PersonalFood);
        if (actor.Equipment.HasFlag(PersonalEquipment.PrimitiveWaterskin))
        {
            AddWaterskinIcon(actor.PersonalWater);
        }
        if (cargo is not null)
        {
            AddInventoryIcon(
                ItemIcons.ForResource(cargo.Value.Resource),
                $"Ładunek roboczy • {DescribeStack(cargo.Value)}",
                cargo.Value.Quantity);
        }
    }

    private void AddInventoryIcon(ItemIcon icon, string tooltip, int? quantity = null)
    {
        var slot = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(48, 54),
            TooltipText = tooltip,
        };
        var image = new TextureRect
        {
            CustomMinimumSize = new Vector2(44, 40),
            Texture = ItemIcons.CreateTexture(_itemIconAtlas, icon),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TooltipText = tooltip,
        };
        slot.AddChild(image);
        if (quantity is not null)
        {
            slot.AddChild(new Label
            {
                Text = $"×{quantity.Value}",
                HorizontalAlignment = HorizontalAlignment.Center,
                TooltipText = tooltip,
            });
        }
        _inventoryIcons.AddChild(slot);
    }

    private void AddWaterskinIcon(int water)
    {
        var tooltip = $"Prymitywny bukłak • woda {water}/{_engine.Definitions.PersonalWaterCapacity} " +
            "umownych porcji • obecnie jedno picie zużywa jedną porcję";
        var slot = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(62, 44),
            TooltipText = tooltip,
        };
        slot.AddChild(new TextureRect
        {
            CustomMinimumSize = new Vector2(44, 40),
            Texture = ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.PrimitiveWaterskin),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TooltipText = tooltip,
        });
        slot.AddChild(new ProgressBar
        {
            CustomMinimumSize = new Vector2(12, 40),
            MaxValue = _engine.Definitions.PersonalWaterCapacity,
            Value = water,
            FillMode = (int)ProgressBar.FillModeEnum.BottomToTop,
            ShowPercentage = false,
            TooltipText = tooltip,
        });
        _inventoryIcons.AddChild(slot);
    }

    private static string DescribeExperience(GoblinExperienceSnapshot experience) =>
        $"zbieractwo poz. {GoblinExperienceSnapshot.GetLevel(experience.Foraging)} " +
        $"({GoblinExperienceSnapshot.GetProgressToNextLevel(experience.Foraging)}/100), " +
        $"transport poz. {GoblinExperienceSnapshot.GetLevel(experience.Hauling)} " +
        $"({GoblinExperienceSnapshot.GetProgressToNextLevel(experience.Hauling)}/100), " +
        $"budowanie poz. {GoblinExperienceSnapshot.GetLevel(experience.Building)} " +
        $"({GoblinExperienceSnapshot.GetProgressToNextLevel(experience.Building)}/100)";

    private static string DescribeSkills(GoblinSkill skills) => string.Join(", ", new[]
    {
        skills.HasFlag(GoblinSkill.Foraging) ? "zbieractwo" : null,
        skills.HasFlag(GoblinSkill.Hauling) ? "transport" : null,
        skills.HasFlag(GoblinSkill.Survival) ? "przetrwanie" : null,
        skills.HasFlag(GoblinSkill.Scouting) ? "zwiad" : null,
        skills.HasFlag(GoblinSkill.Building) ? "budowanie" : null,
    }.Where(item => item is not null));

    private static string DescribeTraits(GoblinTrait traits) => string.Join(", ", new[]
    {
        traits.HasFlag(GoblinTrait.Stubborn) ? "uparty" : null,
        traits.HasFlag(GoblinTrait.Curious) ? "ciekawski" : null,
        traits.HasFlag(GoblinTrait.Hardy) ? "wytrzymały" : null,
        traits.HasFlag(GoblinTrait.Gluttonous) ? "żarłoczny" : null,
        traits.HasFlag(GoblinTrait.Nimble) ? "zwinny" : null,
    }.Where(item => item is not null));

    private void MoveCamera(double delta)
    {
        var direction = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
        if (Input.IsKeyPressed(Key.A)) direction.X -= 1;
        if (Input.IsKeyPressed(Key.D)) direction.X += 1;
        if (Input.IsKeyPressed(Key.W)) direction.Y -= 1;
        if (Input.IsKeyPressed(Key.S)) direction.Y += 1;
        _camera.Position += direction.Normalized() * (float)(520 * delta / _camera.Zoom.X);
        ConstrainCameraToMap();
    }

    private void CenterCameraOn(GridPosition position)
    {
        _camera.Position = _worldView.CellToWorld(position);
        ConstrainCameraToMap();
    }

    private void ChangeCameraZoom(float factor)
    {
        var minimumZoom = GetMinimumCameraZoom();
        var maximumZoom = Math.Max(3.5f, minimumZoom);
        var zoom = Math.Clamp(_camera.Zoom.X * factor, minimumZoom, maximumZoom);
        _camera.Zoom = Vector2.One * zoom;
        ConstrainCameraToMap();
    }

    private void ConstrainCameraToMap()
    {
        var worldSize = _worldView.WorldSize;
        var viewportSize = GetViewport().GetVisibleRect().Size;
        if (worldSize.X <= 0f || worldSize.Y <= 0f || viewportSize.X <= 0f || viewportSize.Y <= 0f)
        {
            return;
        }

        var minimumZoom = GetMinimumCameraZoom();
        if (_camera.Zoom.X < minimumZoom)
        {
            _camera.Zoom = Vector2.One * minimumZoom;
        }

        var visibleWorldSize = viewportSize / _camera.Zoom;
        var halfView = visibleWorldSize / 2f;
        _camera.Position = new Vector2(
            ConstrainCameraAxis(_camera.Position.X, halfView.X, worldSize.X),
            ConstrainCameraAxis(_camera.Position.Y, halfView.Y, worldSize.Y));
        _minimap.SetCameraView(_camera.Position / worldSize, visibleWorldSize / worldSize);
    }

    private float GetMinimumCameraZoom()
    {
        var worldSize = _worldView.WorldSize;
        var viewportSize = GetViewport().GetVisibleRect().Size;
        return worldSize.X <= 0f || worldSize.Y <= 0f
            ? 1f
            : Math.Max(viewportSize.X / worldSize.X, viewportSize.Y / worldSize.Y);
    }

    private static float ConstrainCameraAxis(float center, float halfView, float worldExtent) =>
        halfView * 2f >= worldExtent
            ? worldExtent / 2f
            : Math.Clamp(center, halfView, worldExtent - halfView);

    private void BindButton(string name, int speed) =>
        GetToolbarButton(name).Pressed += () => SetSpeed(speed);

    private Button GetToolbarButton(string name)
    {
        var parent = name is "Pause" or "Speed1" or "Speed2" or "Speed4" or "Speed8"
            ? "Interface/RightHud/SpeedPanel/Controls"
            : "Interface/ActionBar/Controls";
        return GetNode<Button>($"{parent}/{name}");
    }

    private void OrderVillageRaid()
    {
        if (_engine.CreateSnapshot().HumanVillage.GoblinAttackOrdered)
        {
            return;
        }
        _engine.QueueCommand(SimulationCommand.AttackHumanVillage(
            new SimulationTick(_engine.CurrentTick.Value + 1), _commandSequence++));
    }

    private void SetSpeed(int speed)
    {
        if (speed is not (0 or 1 or 2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(nameof(speed));
        }

        _speed = speed;
        _worldView.SetSimulationSpeed(speed, SecondsPerTick);
        UpdateSpeedButtons();
        UpdateStatus();
    }

    private static bool TryResolveSpeedShortcut(Key key, out int speed)
    {
        speed = key switch
        {
            Key.Quoteleft or Key.Key0 or Key.Kp0 => 0,
            Key.Key1 or Key.Kp1 => 1,
            Key.Key2 or Key.Kp2 => 2,
            Key.Key3 or Key.Kp3 => 4,
            Key.Key4 or Key.Kp4 => 8,
            _ => -1,
        };
        return speed >= 0;
    }

    private void UpdateSpeedButtons()
    {
        var states = new (string Name, int Speed, Color SelectedColor)[]
        {
            ("Pause", 0, new Color("ff4d57")),
            ("Speed1", 1, new Color("8bcf72")),
            ("Speed2", 2, new Color("63e77a")),
            ("Speed4", 4, new Color("3af28a")),
            ("Speed8", 8, new Color("55ffad")),
        };
        foreach (var state in states)
        {
            var button = GetToolbarButton(state.Name);
            button.ToggleMode = true;
            button.ButtonPressed = state.Speed == _speed;
            button.SelfModulate = state.Speed == _speed
                ? state.SelectedColor
                : new Color("aab2b5");
        }
    }

    private void ChangeVisibleLevel(int delta)
    {
        var snapshot = _engine.CreateSnapshot();
        var minimumSurfaceFloor = Enumerable.Range(0, _engine.Map.CellCount)
            .Select(index => _engine.Map.GetCell(new GridPosition(
                index % _engine.Map.Width,
                index / _engine.Map.Width)).FloorLevel)
            .Min(level => (int)level);
        var minimumLevel = Math.Min(minimumSurfaceFloor, _engine.Map.DeepestCaveLevel);
        var maximumLevel = Math.Max(0, snapshot.WorldObjects
            .SelectMany(worldObject => worldObject.GetAbsoluteParts())
            .Select(part => part.Position.Z)
            .DefaultIfEmpty(0)
            .Max());
        var next = Math.Clamp(_visibleLevel + delta, minimumLevel, maximumLevel);
        if (next == _visibleLevel)
        {
            return;
        }

        CancelActiveTool();
        SelectActor(EntityId.None);
        _visibleLevel = next;
        _worldView.SetVisibleLevel(next);
        UpdateLayerToolAvailability();
        _inspector.Text = $"Widoczna warstwa mapy: z={next}. Page Up / Page Down zmienia poziom.";
        UpdateStatus();
    }

    private bool EnsureSurfaceToolAvailable(string toolName)
    {
        if (_visibleLevel == 0)
        {
            return true;
        }

        CancelActiveTool();
        _buildMenu.Hide();
        _workMenu.Hide();
        _inspector.Text = $"{toolName} jest obecnie dostępne tylko na powierzchni (z=0). " +
            "Podziemne konstrukcje i prace dostaną osobne blueprinty.";
        return false;
    }

    private bool EnsureBuildModeAvailable(BuildMode mode)
    {
        if (_visibleLevel == 0 ||
            (_visibleLevel < 0 && mode is BuildMode.FoodStorage or BuildMode.WoodStorage))
        {
            return true;
        }

        CancelBuildMode(clearInspector: false);
        _buildMenu.Hide();
        _inspector.Text = _visibleLevel < 0
            ? "W jaskini można obecnie planować składy żywności i drewna. " +
              "Pomost oraz obozowisko są blueprintami powierzchniowymi."
            : "Budowanie ponad powierzchnią wymaga blueprintu podpartej konstrukcji.";
        return false;
    }

    private bool IsBuildableLayerCell(GridPosition position) => position.Z switch
    {
        0 => _engine.Map.IsWithin(position),
        < 0 => _engine.Map.IsCavePosition(position),
        _ => false,
    };

    private void UpdateLayerToolAvailability()
    {
        var surface = _visibleLevel == 0;
        var build = GetToolbarButton("Build");
        var work = GetToolbarButton("Work");
        build.Disabled = _visibleLevel > 0;
        work.Disabled = !surface;
        build.TooltipText = _visibleLevel switch
        {
            0 => "Budowanie",
            < 0 => "Budowanie pod ziemią • obecnie dostępne składy żywności i drewna",
            _ => "Budowanie niedostępne: brak blueprintów konstrukcji nadziemnych",
        };
        work.TooltipText = surface
            ? "Zlecenia pracy"
            : "Zlecenia pracy niedostępne: widoczna warstwa nie jest powierzchnią z=0";
    }

    private void UpdateStatus()
    {
        var snapshot = _engine.CreateSnapshot();
        UpdateCalendar(snapshot);
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
        var constructionWorkers = snapshot.Actors.Count(actor =>
            actor.Job.Kind is ActorJobKind.SupplyConstruction or ActorJobKind.BuildConstruction);
        var explored = snapshot.Visibility.Count(state => state != CellVisibility.Unknown);
        var villageVisibility = snapshot.GetVisibility(snapshot.HumanVillage.Anchor, _engine.Map.Width);
        var storedFood = snapshot.ItemStacks
            .Where(stack => stack.Resource == ResourceKind.Food &&
                stack.Location.Kind == ItemLocationKind.StorageZone)
            .Sum(stack => stack.Quantity);
        var personalFood = snapshot.Actors.Sum(actor => actor.PersonalFood);
        var personalWater = snapshot.Actors.Sum(actor => actor.PersonalWater);
        var wood = snapshot.ItemStacks
            .Where(stack =>
                stack.Resource == ResourceKind.Wood &&
                (stack.Location.Kind != ItemLocationKind.Ground ||
                 snapshot.GetVisibility(stack.Location.Position, _engine.Map.Width) == CellVisibility.Visible))
            .Sum(stack => stack.Quantity);
        _status.Text = $"Tick {snapshot.Tick.Value:N0}  •  z={_visibleLevel}  •  plemię {snapshot.Actors.Count}" +
            $"  •  żywność {snapshot.FoodStock}" +
            $" (skł. {storedFood}, racje {personalFood}/{personalWater})" +
            $"  •  drewno {wood}" +
            $"  •  odkryte {explored}/{snapshot.Visibility.Count}" +
            $"  •  cele pracy {snapshot.WorkDesignations.Count}" +
            $"  •  budowy {snapshot.ConstructionSites.Count} ({constructionWorkers} gobl.)" +
            $"  •  transport {haulers}  •  w drodze {traveling}  •  pracuje {working}";
        if (_engine.DebugSettings.RevealFogFromNonPlayerUnits)
        {
            _status.Text += "  •  DEBUG: widok obcych jednostek";
        }
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
            UpdateGoblinDetails(snapshot);
        }
        if (villageVisibility == CellVisibility.Visible)
        {
            _status.Text += $"  •  wieś {snapshot.HumanVillage.Population} osób, zapasy " +
                $"ziarno {snapshot.HumanVillage.FoodStock}/{snapshot.HumanVillage.FoodCapacity}, " +
                $"woda {snapshot.HumanVillage.WaterStock}, drewno {snapshot.HumanVillage.WoodStock}" +
                $"  •  pola {snapshot.HumanVillage.Fields.Count}/{snapshot.HumanVillage.PlannedFieldCount}" +
                $"  •  alarm {snapshot.HumanVillage.Hostility}/100" +
                (snapshot.HumanVillage.GoblinAttackOrdered ? "  •  NAJAZD" : string.Empty);
        }
        if (snapshot.RaidPhase == GoblinRaidPhase.Preparing)
        {
            _status.Text += $"  •  najazd: przygotowanie w {snapshot.RaidRallyPoint}";
        }
        else if (snapshot.RaidPhase == GoblinRaidPhase.Marching)
        {
            _status.Text += "  •  najazd: wymarsz";
        }
        else if (villageVisibility == CellVisibility.Explored)
        {
            _status.Text += "  •  wieś odkryta";
        }
    }

    private void UpdateCalendar(SimulationSnapshot snapshot)
    {
        var calendar = SimulationCalendar.At(snapshot.Tick, _engine.Definitions.Clock);
        var seasonName = calendar.Season switch
        {
            SeasonKind.Spring => "Wiosna",
            SeasonKind.Summer => "Lato",
            SeasonKind.Autumn => "Jesień",
            SeasonKind.Winter => "Zima",
            _ => throw new ArgumentOutOfRangeException(),
        };
        _clock.Text =
            $"{calendar.Hour:00}:{calendar.Minute:00}:{calendar.Second:00} • dzień {calendar.DayOfSeason}";
        _clock.TooltipText = calendar.IsNight
            ? "Noc • gobliny widzą słabiej, ludzie korzystają z latarni"
            : "Dzień";
        _seasonName.Text = seasonName;
        var season = _engine.Definitions.Clock.Climate.GetSeason(calendar.Season);
        _seasonProgress.SetCalendar(_engine.Definitions.Clock.Climate, calendar);
        _seasonProgress.TooltipText =
            $"{seasonName} • dzień {calendar.DayOfSeason}/{season.Days} • strefa {_engine.Definitions.Clock.Climate.Id}";
    }
}
