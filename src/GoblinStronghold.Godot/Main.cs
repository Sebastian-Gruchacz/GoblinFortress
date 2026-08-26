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
    private WorldView3D _worldView3D = null!;
    private MinimapView _minimap = null!;
    private Camera2D _camera = null!;
    private Label _status = null!;
    private Label _clock = null!;
    private Label _seasonName = null!;
    private SeasonCycleView _seasonProgress = null!;
    private Label _inspector = null!;
    private PopupPanel _buildMenu = null!;
    private PopupPanel _workMenu = null!;
    private PopupPanel _statisticsMenu = null!;
    private GridContainer _buildMenuGrid = null!;
    private GridContainer _workMenuGrid = null!;
    private GridContainer _statisticsMenuGrid = null!;
    private Texture2D _iconAtlas = null!;
    private Texture2D _itemIconAtlas = null!;
    private Texture2D _pickaxeIcon = null!;
    private Window _goblinDetails = null!;
    private Label _goblinDetailsText = null!;
    private ProgressBar _healthBar = null!;
    private ProgressBar _hungerBar = null!;
    private ProgressBar _thirstBar = null!;
    private ProgressBar _fatigueBar = null!;
    private HBoxContainer _inventoryIcons = null!;
    private string _inventorySignature = string.Empty;
    private Window _storedResourcesWindow = null!;
    private Label _storedResourcesSummary = null!;
    private CheckButton _storedResourcesDetailed = null!;
    private GridContainer _storedResourcesGrid = null!;
    private string _storedResourcesSignature = string.Empty;
    private Window _looseResourcesWindow = null!;
    private Label _looseResourcesSummary = null!;
    private CheckButton _looseResourcesDetailed = null!;
    private GridContainer _looseResourcesGrid = null!;
    private string _looseResourcesSignature = string.Empty;
    private Window _goblinRosterWindow = null!;
    private VBoxContainer _goblinRosterRows = null!;
    private string _goblinRosterSignature = string.Empty;
    private Window _statisticsWindow = null!;
    private Label _statisticsText = null!;
    private Window _raidWindow = null!;
    private Label _raidSummary = null!;
    private VBoxContainer _raidRows = null!;
    private Button _raidStartButton = null!;
    private readonly HashSet<EntityId> _raidDraftIds = [];
    private bool _updatingRaidSelection;
    private int _speed = 1;
    private int _visibleLevel;
    private double _accumulator;
    private ulong _commandSequence = 1;
    private EntityId _selectedActorId = EntityId.None;
    private BuildMode _buildMode;
    private bool _isDraggingLinearBuild;
    private GridPosition _linearBuildStart;
    private WorkMode _workMode;
    private bool _isDraggingWorkArea;
    private GridPosition _workAreaStart;
    private bool _isMoveMode;
    private bool _isPanningCamera;
    private float _rightDragDistance;
    private Window _storageDetails = null!;
    private Label _storageSummary = null!;
    private Control _storageMineralFilters = null!;
    private CheckButton _storageSandstone = null!;
    private CheckButton _storageGranite = null!;
    private CheckButton _storageCoal = null!;
    private CheckButton _storageIronOre = null!;
    private CheckButton _storagePullLoose = null!;
    private SpinBox _storageTarget = null!;
    private OptionButton _storagePriority = null!;
    private OptionButton _resourcePriority = null!;
    private OptionButton _storageHauler = null!;
    private readonly List<EntityId> _storageHaulerActorIds = [];
    private OptionButton _storageSource = null!;
    private readonly List<EntityId> _storageSourceZoneIds = [];
    private EntityId _selectedStorageId = EntityId.None;
    private bool _storageSettingsDirty;
    private bool _updatingStorageControls;
    private Window _constructionDetails = null!;
    private Label _constructionSummary = null!;
    private OptionButton _constructionPriority = null!;
    private EntityId _selectedConstructionId = EntityId.None;
    private GameSaveStore _saveStore = null!;
    private SimulationTick _nextAutosaveTick;
    private Control _mainMenu = null!;
    private Button _resumeGameButton = null!;
    private Button _newGameButton = null!;
    private Button _loadMenuButton = null!;
    private bool _hasActiveSession;
    private int _speedBeforeMenu = 1;
    private Button _viewModeButton = null!;
    private Control _cameraModePanel = null!;
    private Button _cameraAngleButton = null!;
    private bool _use3DView;

    private double SecondsPerTick =>
        _engine.Definitions.Clock.RealSecondsPerTickAtNormalSpeed;

    private enum BuildMode
    {
        None,
        FoodStorage,
        Walkway,
        WoodStorage,
        StoneStorage,
        FieldCamp,
        WoodenWall,
        StoneWall,
        WoodenDoorFrame,
        StoneDoorFrame,
        WoodenDoor,
        WallTorch,
    }

    private enum WorkMode
    {
        None,
        GatherFood,
        GatherBrushwood,
        GatherStone,
        UprootBerryBushes,
        FellTrees,
        QuarryBoulders,
        MineRock,
        Clear,
    }

    public override void _Ready()
    {
        _saveStore = new GameSaveStore(ProjectSettings.GlobalizePath("user://saves"));
        _engine = CreateNewEngine(InitialSeed);
        var map = _engine.Map;

        _worldView = GetNode<WorldView>("WorldView");
        _worldView3D = GetNode<WorldView3D>("WorldView3D");
        _minimap = GetNode<MinimapView>("Interface/RightHud/MinimapFrame/Minimap");
        _camera = GetNode<Camera2D>("Camera2D");
        _status = GetNode<Label>("Interface/TopBar/Controls/Status");
        _clock = GetNode<Label>("Interface/Calendar/Controls/Clock");
        _seasonName = GetNode<Label>("Interface/Calendar/Controls/SeasonName");
        _seasonProgress = GetNode<SeasonCycleView>("Interface/Calendar/Controls/Season");
        _inspector = GetNode<Label>("Interface/Inspector/Text");
        _buildMenu = GetNode<PopupPanel>("BuildMenu");
        _workMenu = GetNode<PopupPanel>("WorkMenu");
        _statisticsMenu = GetNode<PopupPanel>("StatisticsMenu");
        _buildMenuGrid = GetNode<GridContainer>("BuildMenu/Margin/Grid");
        _workMenuGrid = GetNode<GridContainer>("WorkMenu/Margin/Grid");
        _statisticsMenuGrid = GetNode<GridContainer>("StatisticsMenu/Margin/Grid");
        _mainMenu = GetNode<Control>("Interface/MainMenu");
        _resumeGameButton = GetNode<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/Resume");
        _newGameButton = GetNode<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/NewGame");
        _loadMenuButton = GetNode<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/LoadGame");
        _viewModeButton = GetNode<Button>("Interface/RightHud/SessionPanel/Controls/ViewMode");
        _cameraModePanel = GetNode<Control>("Interface/RightHud/CameraPanel");
        _cameraAngleButton = GetNode<Button>("Interface/RightHud/CameraPanel/Controls/Angle");
        _iconAtlas = UiIcons.LoadAtlas();
        _itemIconAtlas = ItemIcons.LoadAtlas();
        _pickaxeIcon = GD.Load<Texture2D>("res://Assets/UI/primitive-pickaxe-v1.svg");
        _goblinDetails = GetNode<Window>("GoblinDetails");
        _goblinDetailsText = GetNode<Label>("GoblinDetails/Scroll/Content/Text");
        _inventoryIcons = GetNode<HBoxContainer>("GoblinDetails/Scroll/Content/Inventory");
        _storedResourcesWindow = GetNode<Window>("StoredResourcesWindow");
        _storedResourcesSummary = GetNode<Label>("StoredResourcesWindow/Margin/Content/Summary");
        _storedResourcesDetailed = GetNode<CheckButton>(
            "StoredResourcesWindow/Margin/Content/Detailed");
        _storedResourcesGrid = GetNode<GridContainer>("StoredResourcesWindow/Margin/Content/Grid");
        _looseResourcesWindow = GetNode<Window>("LooseResourcesWindow");
        _looseResourcesSummary = GetNode<Label>("LooseResourcesWindow/Margin/Content/Summary");
        _looseResourcesDetailed = GetNode<CheckButton>(
            "LooseResourcesWindow/Margin/Content/Detailed");
        _looseResourcesGrid = GetNode<GridContainer>("LooseResourcesWindow/Margin/Content/Grid");
        _goblinRosterWindow = GetNode<Window>("GoblinRosterWindow");
        _goblinRosterRows = GetNode<VBoxContainer>("GoblinRosterWindow/Scroll/Rows");
        _statisticsWindow = GetNode<Window>("StatisticsWindow");
        _statisticsText = GetNode<Label>("StatisticsWindow/Margin/Text");
        GetViewport().GuiEmbedSubwindows = true;
        CreateTileButton(_buildMenuGrid, _buildMenu, UiIcon.FoodStorage,
            "Skład żywności\nKoszt: 2 drewna", () => SelectBuildMode((long)BuildMode.FoodStorage));
        CreateTileButton(_buildMenuGrid, _buildMenu, UiIcon.WoodStorage,
            "Skład drewna\nKoszt: 2 drewna", () => SelectBuildMode((long)BuildMode.WoodStorage));
        CreateItemTileButton(_buildMenuGrid, _buildMenu, ItemIcon.Stone,
            "Skład kamienia i urobku\nKoszt: 2 drewna",
            () => SelectBuildMode((long)BuildMode.StoneStorage));
        CreateTileButton(_buildMenuGrid, _buildMenu, UiIcon.Walkway,
            "Pomost\nKoszt: 1 drewno za segment", () => SelectBuildMode((long)BuildMode.Walkway));
        CreateTileButton(_buildMenuGrid, _buildMenu, UiIcon.FieldCamp,
            "Obozowisko wypadowe\nKoszt: 6 drewna", () => SelectBuildMode((long)BuildMode.FieldCamp));
        CreateTileButton(_buildMenuGrid, _buildMenu, UiIcon.WoodenWall,
            "Drewniana ściana\nKoszt: 2 drewna", () => SelectBuildMode((long)BuildMode.WoodenWall));
        CreateItemTileButton(_buildMenuGrid, _buildMenu, ItemIcon.Stone,
            "Kamienny mur\nKoszt: 2 jednostki kamienia • wymaga kilofa",
            () => SelectBuildMode((long)BuildMode.StoneWall));
        CreateTileButton(_buildMenuGrid, _buildMenu, UiIcon.WoodenDoorFrame,
            "Drewniana ościeżnica\nKoszt: 1 drewno", () => SelectBuildMode((long)BuildMode.WoodenDoorFrame));
        CreateItemTileButton(_buildMenuGrid, _buildMenu, ItemIcon.Stone,
            "Kamienna ościeżnica\nKoszt: 1 kamień • wymaga kilofa",
            () => SelectBuildMode((long)BuildMode.StoneDoorFrame));
        CreateTileButton(_buildMenuGrid, _buildMenu, UiIcon.WoodenDoor,
            "Drewniane drzwi\nKoszt: 1 drewno", () => SelectBuildMode((long)BuildMode.WoodenDoor));
        CreateTextureTileButton(_buildMenuGrid, _buildMenu, CreateWallTorchIcon(),
            "Pochodnia ścienna\nKoszt: 1 drewno • wskaż ścianę",
            () => SelectBuildMode((long)BuildMode.WallTorch));
        CreateTileButton(_workMenuGrid, _workMenu, UiIcon.GatherFood,
            "Zbierz żywność\nJagody, grzyby, korzonki i ryby", () => SelectWorkMode((long)WorkMode.GatherFood));
        CreateTileButton(_workMenuGrid, _workMenu, UiIcon.GatherBrushwood,
            "Zbierz chrust\nPrzeciągnij obszar", () => SelectWorkMode((long)WorkMode.GatherBrushwood));
        CreateItemTileButton(_workMenuGrid, _workMenu, ItemIcon.Stone,
            "Zbierz kamienie i urobek\nPrzeciągnij obszar",
            () => SelectWorkMode((long)WorkMode.GatherStone));
        CreateTileButton(_workMenuGrid, _workMenu, UiIcon.UprootBush,
            "Wykarcz krzaki\nTrwale usuwa źródła jagód", () => SelectWorkMode((long)WorkMode.UprootBerryBushes));
        CreateTileButton(_workMenuGrid, _workMenu, UiIcon.FellTree,
            "Wyrąb drzew i pni\nWymaga goblina z siekierą", () => SelectWorkMode((long)WorkMode.FellTrees));
        CreateTextureTileButton(_workMenuGrid, _workMenu, _pickaxeIcon,
            "Rozbij głazy\nWymaga goblina z kilofem", () => SelectWorkMode((long)WorkMode.QuarryBoulders));
        CreateTextureTileButton(_workMenuGrid, _workMenu, _pickaxeIcon,
            "Kop w skale\nWymaga goblina z kilofem", () => SelectWorkMode((long)WorkMode.MineRock));
        CreateTileButton(_workMenuGrid, _workMenu, UiIcon.ClearOrders,
            "Usuń zlecenia\nPrzeciągnij obszar", () => SelectWorkMode((long)WorkMode.Clear));
        CreateItemTileButton(
            _statisticsMenuGrid,
            _statisticsMenu,
            ItemIcon.Cargo,
            "Łączne zapasy w magazynach",
            ShowStoredResources);
        CreateItemTileButton(
            _statisticsMenuGrid,
            _statisticsMenu,
            ItemIcon.Wood,
            "Znane towary leżące na ziemi",
            ShowLooseResources);
        CreateTileButton(
            _statisticsMenuGrid,
            _statisticsMenu,
            UiIcon.Health,
            "Lista goblinów",
            ShowGoblinRoster);
        CreateTextTileButton(
            _statisticsMenuGrid,
            _statisticsMenu,
            "Σ",
            "Statystyki plemienia",
            ShowStatistics);
        CreateNeedIndicators();
        _goblinDetails.CloseRequested += _goblinDetails.Hide;
        _goblinDetails.GetNode<Control>("Scroll").GuiInput += inputEvent =>
            CloseWindowOnSecondaryInput(inputEvent, _goblinDetails);
        ConfigureOverviewWindow(
            _storedResourcesWindow,
            _storedResourcesWindow.GetNode<Control>("Margin"));
        ConfigureOverviewWindow(
            _looseResourcesWindow,
            _looseResourcesWindow.GetNode<Control>("Margin"));
        ConfigureOverviewWindow(
            _goblinRosterWindow,
            _goblinRosterWindow.GetNode<Control>("Scroll"));
        ConfigureOverviewWindow(
            _statisticsWindow,
            _statisticsWindow.GetNode<Control>("Margin"));
        _storedResourcesDetailed.Toggled += _ =>
        {
            _storedResourcesSignature = string.Empty;
            UpdateStoredResources(_engine.CreateSnapshot(), force: true);
        };
        _looseResourcesDetailed.Toggled += _ =>
        {
            _looseResourcesSignature = string.Empty;
            UpdateLooseResources(_engine.CreateSnapshot(), force: true);
        };
        _storageDetails = GetNode<Window>("StorageDetails");
        _storageSummary = GetNode<Label>("StorageDetails/Margin/Controls/Summary");
        _storageMineralFilters = GetNode<Control>(
            "StorageDetails/Margin/Controls/MineralFilters");
        _storageSandstone = GetNode<CheckButton>(
            "StorageDetails/Margin/Controls/MineralFilters/Choices/Sandstone");
        _storageGranite = GetNode<CheckButton>(
            "StorageDetails/Margin/Controls/MineralFilters/Choices/Granite");
        _storageCoal = GetNode<CheckButton>(
            "StorageDetails/Margin/Controls/MineralFilters/Choices/Coal");
        _storageIronOre = GetNode<CheckButton>(
            "StorageDetails/Margin/Controls/MineralFilters/Choices/IronOre");
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
        _constructionDetails = GetNode<Window>("ConstructionDetails");
        _constructionSummary = GetNode<Label>("ConstructionDetails/Margin/Controls/Summary");
        _constructionPriority = GetNode<OptionButton>(
            "ConstructionDetails/Margin/Controls/PriorityRow/Priority");
        foreach (var priority in Enum.GetValues<StoragePriority>())
        {
            _constructionPriority.AddItem(DescribeStoragePriority(priority));
        }
        _constructionDetails.CloseRequested += _constructionDetails.Hide;
        _constructionDetails.GetNode<Control>("Margin").GuiInput += inputEvent =>
            CloseWindowOnSecondaryInput(inputEvent, _constructionDetails);
        _buildMenu.GetNode<Control>("Margin").GuiInput += inputEvent =>
            CloseWindowOnSecondaryInput(inputEvent, _buildMenu);
        _workMenu.GetNode<Control>("Margin").GuiInput += inputEvent =>
            CloseWindowOnSecondaryInput(inputEvent, _workMenu);
        _statisticsMenu.GetNode<Control>("Margin").GuiInput += inputEvent =>
            CloseWindowOnSecondaryInput(inputEvent, _statisticsMenu);
        _storagePullLoose.Toggled += enabled =>
        {
            _storageTarget.Editable = enabled;
            MarkStorageSettingsDirty();
        };
        _storageTarget.ValueChanged += _ => MarkStorageSettingsDirty();
        _storagePriority.ItemSelected += _ => MarkStorageSettingsDirty();
        _resourcePriority.ItemSelected += _ => MarkStorageSettingsDirty();
        _storageHauler.ItemSelected += _ => MarkStorageSettingsDirty();
        _storageSource.ItemSelected += _ => MarkStorageSettingsDirty();
        _storageSandstone.Toggled += _ => MarkStorageSettingsDirty();
        _storageGranite.Toggled += _ => MarkStorageSettingsDirty();
        _storageCoal.Toggled += _ => MarkStorageSettingsDirty();
        _storageIronOre.Toggled += _ => MarkStorageSettingsDirty();
        GetNode<Button>("StorageDetails/Margin/Controls/Apply").Pressed += ApplyStorageSettings;
        GetNode<Button>("ConstructionDetails/Margin/Controls/Apply").Pressed +=
            ApplyConstructionSettings;
        GetNode<Button>("Interface/RightHud/SessionPanel/Controls/Menu").Pressed += ShowMainMenu;
        GetNode<Button>("Interface/RightHud/SessionPanel/Controls/SaveGame").Pressed += SaveGame;
        _viewModeButton.Pressed += ToggleWorldView;
        GetNode<Button>("Interface/RightHud/CameraPanel/Controls/RotateLeft").Pressed +=
            () => Rotate3DCamera(-1);
        _cameraAngleButton.Pressed += Toggle3DCameraAngle;
        GetNode<Button>("Interface/RightHud/CameraPanel/Controls/RotateRight").Pressed +=
            () => Rotate3DCamera(1);
        _resumeGameButton.Pressed += ResumeGame;
        _newGameButton.Pressed += StartNewGame;
        _loadMenuButton.Pressed += LoadGame;
        GetNode<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/Quit").Pressed += () => GetTree().Quit();
        _worldView.SetWorld(_engine);
        _worldView3D.SetWorld(_engine);
        _worldView3D.SetActive(false);
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
        ConfigureActionButton("Move", UiIcon.Expedition, "Rozkaż wybranemu goblinowi przejść we wskazane miejsce");
        ConfigureActionButton("Raid", UiIcon.Expedition, "Przygotuj najazd na wieś");
        var statisticsButton = GetToolbarButton("Statistics");
        statisticsButton.FocusMode = Control.FocusModeEnum.None;
        statisticsButton.TooltipText = "Zestawienia i statystyki";
        GetToolbarButton("Build").Pressed += ShowBuildMenu;
        GetToolbarButton("Work").Pressed += ShowWorkMenu;
        GetToolbarButton("Move").Pressed += SelectMoveMode;
        GetToolbarButton("Raid").Pressed += ShowRaidWindow;
        statisticsButton.Pressed += ShowStatisticsMenu;
        CreateRaidWindow();
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
            if (_use3DView)
            {
                _worldView3D.Refresh(snapshot);
            }
            else
            {
                _worldView.Refresh(snapshot);
            }
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
            case InputEventKey key when key.Pressed && !key.Echo && key.Keycode == Key.F3:
                ToggleWorldView();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey key when key.Pressed && !key.Echo && key.Keycode == Key.F4 &&
                _use3DView:
                Toggle3DCameraAngle();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey key when key.Pressed && !key.Echo && key.Keycode == Key.Q &&
                _use3DView:
                Rotate3DCamera(-1);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey key when key.Pressed && !key.Echo && key.Keycode == Key.E &&
                _use3DView:
                Rotate3DCamera(1);
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
                if (_buildMode != BuildMode.None || _workMode != WorkMode.None || _isMoveMode)
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
                if (_use3DView)
                {
                    _worldView3D.PanScreenDelta(mouse.Relative, GetViewport().GetVisibleRect().Size);
                }
                else
                {
                    _camera.Position -= mouse.Relative / _camera.Zoom;
                }
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
                else if (_isMoveMode)
                {
                    IssueMoveOrder(mouse.Position);
                }
                else
                {
                    InspectWorld(mouse.Position);
                }
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } mouse
                when _isDraggingLinearBuild:
                FinishLinearConstruction(mouse.Position);
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } mouse
                when _isDraggingWorkArea:
                FinishWorkArea(mouse.Position);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                if (_buildMode != BuildMode.None || _workMode != WorkMode.None || _isMoveMode)
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
        _selectedStorageId = EntityId.None;
        _selectedConstructionId = EntityId.None;
        _storageDetails.Hide();
        _constructionDetails.Hide();
        _storedResourcesWindow.Hide();
        _looseResourcesWindow.Hide();
        _goblinRosterWindow.Hide();
        _statisticsWindow.Hide();
        _raidWindow.Hide();
        _raidDraftIds.Clear();
        _storedResourcesSignature = string.Empty;
        _looseResourcesSignature = string.Empty;
        _goblinRosterSignature = string.Empty;
        _engine = engine;
        _commandSequence = engine.NextAvailableCommandSequence;
        _accumulator = 0;
        _visibleLevel = 0;
        _worldView.SetWorld(engine);
        _worldView.SetVisibleLevel(0);
        _worldView.SetSimulationSpeed(_speed, SecondsPerTick);
        _worldView3D.SetWorld(engine);
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

    private void ConfigureOverviewWindow(Window window, Control content)
    {
        window.CloseRequested += window.Hide;
        content.GuiInput += inputEvent => CloseWindowOnSecondaryInput(inputEvent, window);
    }

    private void ClearSelection()
    {
        SelectActor(EntityId.None);
        _selectedStorageId = EntityId.None;
        _selectedConstructionId = EntityId.None;
        _storageDetails.Hide();
        _constructionDetails.Hide();
        _inspector.Text = "Zaznaczenie wyczyszczone. PPM przeciągnięty przesuwa mapę.";
    }

    private void ShowBuildMenu()
    {
        if (_visibleLevel > 0)
        {
            _inspector.Text = "Budowanie ponad powierzchnią wymaga blueprintu podpartej konstrukcji.";
            return;
        }

        ShowToolbarMenu(_buildMenu, "Build");
    }

    private void ShowWorkMenu()
    {
        if (_visibleLevel > 0)
        {
            _inspector.Text = "Zlecenia pracy ponad powierzchnią nie są jeszcze dostępne.";
            return;
        }

        ShowToolbarMenu(_workMenu, "Work");
    }

    private void SelectMoveMode()
    {
        if (_selectedActorId == EntityId.None)
        {
            _inspector.Text = "Najpierw wybierz goblina, któremu chcesz wydać rozkaz marszu.";
            return;
        }

        CancelBuildMode(clearInspector: false);
        CancelWorkMode(clearInspector: false);
        _isMoveMode = true;
        _inspector.Text = "Ruch: wskaż odkryte, dostępne pole na dowolnym poziomie • Page Up / Page Down zmienia warstwę • Esc anuluje";
    }

    private void IssueMoveOrder(Vector2 screenPosition)
    {
        var destination = ScreenToVisibleCell(screenPosition);
        var snapshot = _engine.CreateSnapshot();
        if (!IsBuildableLayerCell(destination) ||
            !snapshot.GetVisibility(destination, _engine.Map.Width).IsDiscovered() ||
            !_engine.World.IsTerrainReachable(destination))
        {
            _inspector.Text = "Cel marszu musi być odkrytym, dostępnym polem.";
            return;
        }

        _engine.QueueCommand(SimulationCommand.Move(
            _engine.CurrentTick.Next(),
            _commandSequence++,
            _selectedActorId,
            destination));
        _isMoveMode = false;
        _inspector.Text = $"Wydano rozkaz marszu do {destination}." +
            (_speed == 0 ? " Zostanie wykonany po wznowieniu czasu." : string.Empty);
    }

    private void ShowStatisticsMenu() => ShowToolbarMenu(_statisticsMenu, "Statistics");

    private void ShowToolbarMenu(PopupPanel menu, string buttonName)
    {
        foreach (var candidate in new[] { _buildMenu, _workMenu, _statisticsMenu })
        {
            if (candidate != menu)
            {
                candidate.Hide();
            }
        }

        menu.Popup();
        var buttonRect = GetToolbarButton(buttonName).GetGlobalRect();
        var menuSize = (Vector2)menu.Size;
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var maximumX = Math.Max(8f, viewportSize.X - menuSize.X - 8f);
        var maximumY = Math.Max(8f, viewportSize.Y - menuSize.Y - 8f);
        var position = new Vector2(
            Math.Clamp(
                buttonRect.Position.X + ((buttonRect.Size.X - menuSize.X) / 2f),
                8f,
                maximumX),
            Math.Clamp(buttonRect.Position.Y - menuSize.Y - 8f, 8f, maximumY));
        menu.Position = new Vector2I(
            Mathf.RoundToInt(position.X),
            Mathf.RoundToInt(position.Y));
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

    private void CreateItemTileButton(
        GridContainer grid,
        PopupPanel menu,
        ItemIcon icon,
        string tooltip,
        Action action)
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(68, 68),
            Icon = ItemIcons.CreateTexture(_itemIconAtlas, icon),
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

    private static void CreateTextureTileButton(
        GridContainer grid,
        PopupPanel menu,
        Texture2D texture,
        string tooltip,
        Action action)
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(68, 68),
            Icon = texture,
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

    private static Texture2D CreateWallTorchIcon()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
              <path d="M28 58 L36 58 L35 28 L29 28 Z" fill="#8b5a2b" stroke="#3d2818" stroke-width="3"/>
              <path d="M23 31 Q32 36 41 31 L39 24 L25 24 Z" fill="#5a3a22" stroke="#2c1c12" stroke-width="2"/>
              <path d="M32 25 C19 19 25 7 34 3 C32 10 44 12 39 22 C37 26 34 27 32 25 Z" fill="#ff7a18" stroke="#7d2b0b" stroke-width="2"/>
              <path d="M32 22 C27 18 30 12 34 9 C34 14 38 16 35 21 C34 23 33 23 32 22 Z" fill="#ffe45b"/>
            </svg>
            """;
        var image = new Image();
        if (image.LoadSvgFromString(svg) != Error.Ok)
        {
            throw new InvalidOperationException("Cannot create the wall-torch icon.");
        }
        return ImageTexture.CreateFromImage(image);
    }

    private static void CreateTextTileButton(
        GridContainer grid,
        PopupPanel menu,
        string text,
        string tooltip,
        Action action)
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(68, 68),
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = tooltip,
        };
        button.AddThemeFontSizeOverride("font_size", 30);
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
        _isMoveMode = false;
        _buildMode = mode;
        _isDraggingLinearBuild = false;
        _worldView.SetConstructionPreview([]);
        _inspector.Text = _buildMode switch
        {
            BuildMode.FoodStorage => "Budowa składu żywności: wskaż pole LPM • koszt 2 drewna • Esc anuluje",
            BuildMode.WoodStorage => "Budowa składu drewna: wskaż pole LPM • koszt 2 drewna • Esc anuluje",
            BuildMode.StoneStorage => "Budowa składu kamienia: wskaż pole LPM • koszt 2 drewna • Esc anuluje",
            BuildMode.Walkway => "Budowa pomostu: przeciągnij LPM od początku do końca • 1 drewno/segment • Esc anuluje",
            BuildMode.FieldCamp => "Obozowisko 2×2: wskaż lewy górny narożnik przy płytkiej wodzie • koszt 6 drewna • zawiera skład prowiantu",
            BuildMode.WoodenWall => "Budowa drewnianej ściany: przeciągnij LPM od początku do końca • 2 drewna/segment • blokuje przejście",
            BuildMode.StoneWall => "Budowa kamiennego muru: przeciągnij LPM od początku do końca • 2 jednostki kamienia/segment • wymaga kilofa",
            BuildMode.WoodenDoorFrame => "Budowa drewnianej ościeżnicy: wskaż pole LPM • koszt 1 drewna • może zastąpić gotową ścianę",
            BuildMode.StoneDoorFrame => "Budowa kamiennej ościeżnicy: wskaż pole LPM • koszt 1 kamienia • wymaga kilofa • może zastąpić gotowy kamienny mur",
            BuildMode.WoodenDoor => "Budowa drewnianych drzwi: wskaż gotową ościeżnicę LPM • koszt 1 drewna • po budowie kliknij skrzydło, aby je otworzyć",
            BuildMode.WallTorch => "Budowa pochodni: wskaż odkrytą ścianę LPM • koszt 1 drewna • strona montażu wynika z wnętrza i sąsiedztwa",
            _ => _inspector.Text,
        };
    }

    private void SelectWorkMode(long id)
    {
        var mode = (WorkMode)id;
        var availableUnderground = mode is WorkMode.GatherBrushwood or
            WorkMode.GatherStone or WorkMode.MineRock or WorkMode.Clear;
        if (_visibleLevel != 0 && !(_visibleLevel < 0 && availableUnderground))
        {
            _inspector.Text = _visibleLevel < 0
                ? "W jaskini można zbierać luźne drewno i urobek, kopać oraz usuwać zlecenia."
                : "Na tej warstwie nie ma jeszcze pasujących zleceń pracy.";
            return;
        }

        CancelBuildMode(clearInspector: false);
        _isMoveMode = false;
        _workMode = mode;
        _isDraggingWorkArea = false;
        _worldView.SetWorkPreview(default, []);
        _inspector.Text = _workMode switch
        {
            WorkMode.GatherFood => "Praca: przeciągnij obszar zbierania żywności • Esc anuluje",
            WorkMode.GatherBrushwood => "Praca: przeciągnij obszar zbierania chrustu • Esc anuluje",
            WorkMode.GatherStone => "Praca: przeciągnij obszar zbierania małych kamieni • Esc anuluje",
            WorkMode.UprootBerryBushes => "Praca: przeciągnij obszar karczowania krzaków • usuwa je trwale • Esc anuluje",
            WorkMode.FellTrees => "Praca: przeciągnij obszar wyrębu • pozostaną konkretne drzewa i martwe pnie • Esc anuluje",
            WorkMode.QuarryBoulders => "Praca: przeciągnij obszar wydobycia • pozostaną konkretne głazy • wymaga kilofa • Esc anuluje",
            WorkMode.MineRock => "Praca: przeciągnij obszar kopania • pozostaną możliwe do wydobycia ściany • wymaga kilofa • Esc anuluje",
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

        if (_buildMode is BuildMode.FoodStorage or BuildMode.WoodStorage or BuildMode.StoneStorage)
        {
            var resource = _buildMode switch
            {
                BuildMode.FoodStorage => ResourceKind.Food,
                BuildMode.WoodStorage => ResourceKind.Wood,
                BuildMode.StoneStorage => ResourceKind.Stone,
                _ => throw new InvalidOperationException(),
            };
            CreateStorage(
                cell,
                resource);
            CancelBuildMode(clearInspector: false);
            return;
        }

        if (_buildMode is BuildMode.WoodenDoorFrame or BuildMode.StoneDoorFrame or
            BuildMode.WoodenDoor or BuildMode.WallTorch)
        {
            var snapshot = _engine.CreateSnapshot();
            if (!snapshot.GetVisibility(cell, _engine.Map.Width).IsDiscovered())
            {
                _inspector.Text = "Konstrukcja musi stanąć na odkrytym polu.";
                CancelBuildMode(clearInspector: false);
                return;
            }

            var command = _buildMode switch
            {
                BuildMode.WoodenDoorFrame => SimulationCommand.BuildWoodenDoorFrame(
                    _engine.CurrentTick.Next(), _commandSequence++, cell),
                BuildMode.StoneDoorFrame => SimulationCommand.BuildStoneDoorFrame(
                    _engine.CurrentTick.Next(), _commandSequence++, cell),
                BuildMode.WallTorch => SimulationCommand.BuildWallTorch(
                    _engine.CurrentTick.Next(), _commandSequence++, cell),
                _ => SimulationCommand.BuildWoodenDoor(
                    _engine.CurrentTick.Next(), _commandSequence++, cell),
            };
            _engine.QueueCommand(command);
            _inspector.Text = _buildMode switch
            {
                BuildMode.WoodenDoorFrame =>
                    "Zlecono przechodnią drewnianą ościeżnicę • koszt 1 drewna",
                BuildMode.StoneDoorFrame =>
                    "Zlecono przechodnią kamienną ościeżnicę • koszt 1 kamienia",
                BuildMode.WallTorch =>
                    "Zlecono pochodnię ścienną • koszt 1 drewna",
                _ => "Zlecono zamknięte drewniane drzwi w ościeżnicy • koszt 1 drewna",
            };
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

        _linearBuildStart = cell;
        _isDraggingLinearBuild = true;
        UpdateBuildPreview(screenPosition);
    }

    private void FinishLinearConstruction(Vector2 screenPosition)
    {
        var end = ScreenToVisibleCell(screenPosition);
        _isDraggingLinearBuild = false;
        if (!IsBuildableLayerCell(end) || end.Z != _linearBuildStart.Z ||
            (_buildMode == BuildMode.Walkway && end.Z != 0))
        {
            _inspector.Text = _buildMode == BuildMode.Walkway && end.Z != 0
                ? "Pomost jest obecnie blueprintem powierzchniowym."
                : "Cała konstrukcja musi leżeć na jednym dostępnym poziomie.";
            CancelBuildMode(clearInspector: false);
            return;
        }

        var cells = SimulationCommand.GetLinearCells(_linearBuildStart, end);
        var snapshot = _engine.CreateSnapshot();
        if (cells.Any(cell =>
                !snapshot.GetVisibility(cell, _engine.Map.Width).IsDiscovered()))
        {
            _inspector.Text = "Cała liniowa konstrukcja musi przebiegać przez odkryty teren.";
            CancelBuildMode(clearInspector: false);
            return;
        }

        var command = _buildMode switch
        {
            BuildMode.WoodenWall => SimulationCommand.BuildWoodenWall(
                _engine.CurrentTick.Next(), _commandSequence++, _linearBuildStart, end),
            BuildMode.StoneWall => SimulationCommand.BuildStoneWall(
                _engine.CurrentTick.Next(), _commandSequence++, _linearBuildStart, end),
            _ => SimulationCommand.BuildWalkway(
                _engine.CurrentTick.Next(), _commandSequence++, _linearBuildStart, end),
        };
        _engine.QueueCommand(command);
        _inspector.Text = _buildMode switch
        {
            BuildMode.WoodenWall =>
                $"Zlecono drewnianą ścianę: {cells.Count} segmentów • koszt {cells.Count * 2} drewna",
            BuildMode.StoneWall =>
                $"Zlecono kamienny mur: {cells.Count} segmentów • koszt {cells.Count * 2} jednostek kamienia",
            _ => $"Zlecono pomost: {cells.Count} segmentów • koszt {cells.Count} drewna",
        };
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
            BuildMode.Walkway or BuildMode.WoodenWall or BuildMode.StoneWall
                when _isDraggingLinearBuild =>
                SimulationCommand.GetLinearCells(_linearBuildStart, cell),
            BuildMode.FieldCamp => GetAreaCells(cell, cell with { X = cell.X + 1, Y = cell.Y + 1 }),
            _ => new[] { cell },
        };
        _worldView.SetConstructionPreview(cells);
        if (_isDraggingLinearBuild)
        {
            _inspector.Text = _buildMode switch
            {
                BuildMode.WoodenWall =>
                    $"Drewniana ściana: {cells.Count} segmentów • koszt {cells.Count * 2} drewna",
                BuildMode.StoneWall =>
                    $"Kamienny mur: {cells.Count} segmentów • koszt {cells.Count * 2} jednostek kamienia",
                _ => $"Pomost: {cells.Count} segmentów • koszt {cells.Count} drewna",
            };
        }
    }

    private void CancelBuildMode(bool clearInspector = true)
    {
        var wasActive = _buildMode != BuildMode.None;
        _buildMode = BuildMode.None;
        _isDraggingLinearBuild = false;
        _worldView.SetConstructionPreview([]);
        if (clearInspector && wasActive)
        {
            _inspector.Text = "Tryb budowy anulowany.";
        }
    }

    private void BeginWorkArea(Vector2 screenPosition)
    {
        var cell = ScreenToVisibleCell(screenPosition);
        if (!IsBuildableLayerCell(cell))
        {
            return;
        }

        _workAreaStart = cell;
        _isDraggingWorkArea = true;
        UpdateWorkPreview(screenPosition);
    }

    private void UpdateWorkPreview(Vector2 screenPosition)
    {
        var cell = ScreenToVisibleCell(screenPosition);
        if (!IsBuildableLayerCell(cell))
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
        var end = ScreenToVisibleCell(screenPosition);
        _isDraggingWorkArea = false;
        if (!IsBuildableLayerCell(end))
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
            WorkMode.GatherStone => SimulationCommand.DesignateWork(
                executeAt,
                _commandSequence++,
                _workAreaStart,
                end,
                ResourceKind.Stone),
            WorkMode.UprootBerryBushes => SimulationCommand.DesignateWork(
                executeAt,
                _commandSequence++,
                _workAreaStart,
                end,
                ResourceKind.Vegetation),
            WorkMode.FellTrees => SimulationCommand.DesignateTreeFelling(
                executeAt,
                _commandSequence++,
                _workAreaStart,
                end),
            WorkMode.QuarryBoulders => SimulationCommand.DesignateBoulderQuarrying(
                executeAt,
                _commandSequence++,
                _workAreaStart,
                end),
            WorkMode.MineRock => SimulationCommand.DesignateRockMining(
                executeAt,
                _commandSequence++,
                _workAreaStart,
                end),
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
        var hadActiveTool = _buildMode != BuildMode.None || _workMode != WorkMode.None ||
            _isMoveMode;
        CancelBuildMode(clearInspector: false);
        CancelWorkMode(clearInspector: false);
        _isMoveMode = false;
        if (hadActiveTool)
        {
            _inspector.Text = "Aktywne narzędzie anulowane.";
        }
    }

    private static WorkDesignationKind ToDesignationKind(WorkMode mode) => mode switch
    {
        WorkMode.GatherFood => WorkDesignationKind.GatherFood,
        WorkMode.GatherBrushwood => WorkDesignationKind.GatherBrushwood,
        WorkMode.GatherStone => WorkDesignationKind.GatherStone,
        WorkMode.UprootBerryBushes => WorkDesignationKind.UprootBerryBush,
        WorkMode.FellTrees => WorkDesignationKind.FellTree,
        WorkMode.QuarryBoulders => WorkDesignationKind.QuarryBoulder,
        WorkMode.MineRock => WorkDesignationKind.MineRock,
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
            : _engine.World.IsTerrainTraversable(cell);
        var discovered = snapshot.GetVisibility(cell, _engine.Map.Width).IsDiscovered();
        if (!terrainAvailable || !discovered)
        {
            _inspector.Text = $"{cell} • tu nie można wyznaczyć składu.";
            return;
        }

        var command = resource switch
        {
            ResourceKind.Food => SimulationCommand.BuildFoodStorage(
                _engine.CurrentTick.Next(), _commandSequence++, cell),
            ResourceKind.Wood => SimulationCommand.BuildWoodStorage(
                _engine.CurrentTick.Next(), _commandSequence++, cell),
            ResourceKind.Stone => SimulationCommand.BuildStoneStorage(
                _engine.CurrentTick.Next(), _commandSequence++, cell),
            _ => throw new ArgumentOutOfRangeException(nameof(resource)),
        };
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
                SimulationEventKind.StorageMineralFilterConfigured or
                SimulationEventKind.ResourcePriorityConfigured);
        if (workEvent.Kind == SimulationEventKind.WorkDesignationCreated)
        {
            _inspector.Text = (WorkDesignationKind)workEvent.Amount switch
            {
                WorkDesignationKind.GatherFood => "Dispatcher dodał wskazane źródło żywności do zebrania.",
                WorkDesignationKind.GatherBrushwood => "Dispatcher dodał wskazany stos chrustu do transportu.",
                WorkDesignationKind.GatherStone => "Dispatcher dodał wskazany stos kamieni do transportu.",
                WorkDesignationKind.UprootBerryBush => "Dispatcher dodał krzak do trwałego wykarczowania.",
                WorkDesignationKind.FellTree => "Dispatcher dodał drzewo lub martwy pień do wyrębu.",
                WorkDesignationKind.QuarryBoulder => "Dispatcher dodał głaz do rozbicia kilofem.",
                WorkDesignationKind.MineRock => "Dispatcher dodał ścianę jaskini do wykopania.",
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
                 SimulationEventKind.StorageMineralFilterConfigured or
                 SimulationEventKind.ResourcePriorityConfigured)
        {
            var configuredId = workEvent.Kind == SimulationEventKind.ResourcePriorityConfigured
                ? _selectedStorageId
                : workEvent.Target;
            var configured = _engine.CreateSnapshot().StorageZones
                .FirstOrDefault(zone => zone.Id == configuredId);
            if (configured.Id != EntityId.None && configured.Id == _selectedStorageId)
            {
                _storageSettingsDirty = false;
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
                SimulationEventKind.ConstructionPriorityConfigured or
                SimulationEventKind.ConstructionCompleted ||
            (item.Kind == SimulationEventKind.CommandRejected &&
             item.Amount == (int)SimulationCommandKind.Build));
        if (constructionEvent.Kind == SimulationEventKind.CommandRejected)
        {
            _inspector.Text = "Nie można wyznaczyć placu budowy: teren jest niedostępny albo zajęty.";
        }
        else if (constructionEvent.Kind is SimulationEventKind.ConstructionOrdered or
                 SimulationEventKind.ConstructionMaterialDelivered or
                 SimulationEventKind.ConstructionPriorityConfigured)
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
            var material = constructionEvent.Construction is ConstructionKind.StoneWall or
                ConstructionKind.StoneDoorFrame
                ? "kamienia"
                : "drewna";
            _inspector.Text = constructionEvent.Construction is not null
                ? $"Budowa {DescribeConstruction(constructionEvent.Construction.Value)} ukończona • " +
                  $"zużyto {constructionEvent.Amount} {material}"
                : zone.Id != EntityId.None
                    ? $"Skład {DescribeResource(zone.AcceptedResource)} ukończony • " +
                      $"zużyto {constructionEvent.Amount} {material}"
                    : "Budowa ukończona.";
        }

        if (_selectedConstructionId != EntityId.None &&
            constructionEvent.Kind is SimulationEventKind.ConstructionMaterialDelivered or
                SimulationEventKind.ConstructionPriorityConfigured or
                SimulationEventKind.ConstructionCompleted)
        {
            var selectedConstruction = _engine.CreateSnapshot().ConstructionSites
                .FirstOrDefault(site => site.Id == _selectedConstructionId);
            if (selectedConstruction is null)
            {
                _selectedConstructionId = EntityId.None;
                _constructionDetails.Hide();
            }
            else
            {
                UpdateConstructionDetails(selectedConstruction);
            }
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
                if (!snapshot.GetVisibility(levelPosition, _engine.Map.Width).IsDiscovered())
                {
                    SelectActor(EntityId.None);
                    _inspector.Text = $"{levelPosition} • nieznany teren";
                    return;
                }

                var actor = snapshot.Actors.FirstOrDefault(item => item.Position == levelPosition);
                var zone = snapshot.StorageZones.FirstOrDefault(item => item.Position == levelPosition);
                var construction = snapshot.ConstructionSites.FirstOrDefault(item =>
                    item.Footprint.Contains(levelPosition));
                if (actor.Id != EntityId.None)
                {
                    SelectActor(actor.Id);
                    _inspector.Text = $"{actor.Name} • {levelPosition} • {DescribeJob(actor.Job)}";
                    return;
                }
                if (zone.Id != EntityId.None)
                {
                    SelectActor(EntityId.None);
                    ShowStorageDetails(zone);
                    return;
                }
                if (construction is not null)
                {
                    SelectActor(EntityId.None);
                    ShowConstructionDetails(construction);
                    return;
                }

                var caveCell = _engine.Map.GetCaveCell(levelPosition);
                var passages = _engine.Map.VerticalPassages
                    .Where(passage => passage.Upper == levelPosition || passage.Lower == levelPosition)
                    .Select(passage => passage.Kind == VerticalPassageKind.CaveMouth
                        ? "wejście na powierzchnię"
                        : "pochylnia między poziomami")
                    .ToArray();
                SelectActor(EntityId.None);
                var excavated = _engine.World.ExcavatedCaveCells.Contains(levelPosition);
                var caveKind = excavated
                    ? "wykopany korytarz"
                    : DescribeCaveKind(caveCell.Kind);
                _inspector.Text = $"{levelPosition} • {DescribeCaveRock(caveCell.Rock)} • " +
                    caveKind + (excavated ? string.Empty : DescribeMineralDeposit(caveCell.Deposit)) +
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
        if (objects.Any(item => item.Kind == WorldObjectKind.WoodenDoorLeaf) &&
            _engine.World.TryGetWoodenDoorState(cell, out var isDoorOpen))
        {
            _engine.QueueCommand(SimulationCommand.ToggleWoodenDoor(
                _engine.CurrentTick.Next(), _commandSequence++, cell));
            SelectActor(EntityId.None);
            _inspector.Text = isDoorOpen
                ? $"{cell} • polecenie zamknięcia drzwi" +
                  (_speed == 0 ? " • zostanie wykonane po wznowieniu czasu" : string.Empty)
                : $"{cell} • polecenie otwarcia drzwi" +
                  (_speed == 0 ? " • zostanie wykonane po wznowieniu czasu" : string.Empty);
            return;
        }
        SelectActor(actors.OrderBy(actor => actor.Id).FirstOrDefault().Id);
        if (actors.Length == 0 && zones.Length > 0)
        {
            ShowStorageDetails(zones[0]);
        }
        else if (actors.Length == 0 && constructionSites.Length > 0)
        {
            ShowConstructionDetails(constructionSites[0]);
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
        if (_use3DView)
        {
            return _worldView3D.ScreenToCell(screenPosition);
        }

        var worldPosition = _camera.GetScreenCenterPosition() +
            ((screenPosition - GetViewport().GetVisibleRect().Size / 2f) / _camera.Zoom);
        return _worldView.WorldToCell(worldPosition);
    }

    private GridPosition ScreenToVisibleCell(Vector2 screenPosition) =>
        ScreenToCell(screenPosition) with { Z = _visibleLevel };

    private static string DescribeStack(ItemStackSnapshot stack) =>
        $"{(stack.Resource == ResourceKind.Food
            ? DescribeFood(stack.FoodKind)
            : stack.Variant != ResourceVariant.None
                ? DescribeResourceVariant(stack.Variant)
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
        ResourceKind.Coal => "węgla",
        ResourceKind.Ore => "rudy",
        ResourceKind.Bone => "kości",
        ResourceKind.Vegetation => "roślinności",
        _ => "towarów",
    };

    private static string DescribeResourceVariant(ResourceVariant variant) => variant switch
    {
        ResourceVariant.OakWood => "drewno dębowe",
        ResourceVariant.ChestnutWood => "drewno kasztanowca",
        ResourceVariant.BirchWood => "drewno brzozowe",
        ResourceVariant.WalnutWood => "drewno orzechowe",
        ResourceVariant.AppleWood => "drewno jabłoni",
        ResourceVariant.PineWood => "drewno sosnowe",
        ResourceVariant.Sandstone => "piaskowiec",
        ResourceVariant.Granite => "granit",
        ResourceVariant.IronOre => "ruda żelaza",
        _ => "towar",
    };

    private static string DescribeMineralDeposit(MineralDepositKind deposit) => deposit switch
    {
        MineralDepositKind.Coal => " • żyła węgla",
        MineralDepositKind.IronOre => " • żyła rudy żelaza",
        _ => string.Empty,
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
        ActorJobKind.FellTree when job.Phase == ActorJobPhase.Traveling =>
            $"idzie do wyrębu → {job.Target}",
        ActorJobKind.FellTree => $"rąbie drzewo lub pień ({job.RemainingWorkTicks})",
        ActorJobKind.QuarryBoulder when job.Phase == ActorJobPhase.Traveling =>
            $"idzie rozbić głaz → {job.Target}",
        ActorJobKind.QuarryBoulder => $"rozbija głaz kilofem ({job.RemainingWorkTicks})",
        ActorJobKind.MineRock when job.Phase == ActorJobPhase.Traveling =>
            $"idzie kopać w skale → {job.Target}",
        ActorJobKind.MineRock => $"kopie w skale ({job.RemainingWorkTicks})",
        _ => "bez zadania",
    };

    private string DescribeConstructionSite(ConstructionSiteSnapshot site)
    {
        var materials = string.Join(", ", site.Materials.Select(material =>
            $"{DescribeResource(material.Resource)} {material.DeliveredQuantity}/{material.RequiredQuantity}"));
        var workDone = site.TotalWorkTicks - site.RemainingWorkTicks;
        var readiness = DescribeConstructionReadiness(
            _engine.InspectConstructionReadiness(site.Id));
        return $"plac budowy {DescribeConstruction(site.Kind)} • " +
            $"priorytet: {DescribeStoragePriority(site.Priority)} • materiały: {materials} • " +
            $"praca {workDone}/{site.TotalWorkTicks} • {readiness}";
    }

    private static string DescribeConstructionReadiness(
        ConstructionReadinessDiagnostic diagnostic) => diagnostic.State switch
    {
        ConstructionReadinessState.NoAvailableMaterials =>
            $"wstrzymana: brak wolnego materiału ({diagnostic.MatchingSourceCount} źródeł)",
        ConstructionReadinessState.NoAvailableSupplier =>
            "wstrzymana: brak goblina mogącego dostarczyć materiały",
        ConstructionReadinessState.NoReachableMaterialSource =>
            $"wstrzymana: dostępny materiał ({diagnostic.AvailableMaterialQuantity}) jest nieosiągalny",
        ConstructionReadinessState.WaitingForSupplier =>
            $"oczekuje na dostawcę; dostępny materiał: {diagnostic.AvailableMaterialQuantity}",
        ConstructionReadinessState.MaterialsInTransit =>
            $"materiały w drodze: {diagnostic.InTransitQuantity}",
        ConstructionReadinessState.NoCapableBuilder =>
            "wstrzymana: brak budowniczego z wymaganą wiedzą, umiejętnością lub narzędziem",
        ConstructionReadinessState.NoReachableBuilder =>
            "wstrzymana: żaden zdolny budowniczy nie ma dojścia",
        ConstructionReadinessState.WaitingForBuilder =>
            $"gotowa; oczekuje na budowniczego ({diagnostic.CapableBuilderCount} zdolnych)",
        ConstructionReadinessState.Building => "budowa trwa",
        _ => "stan budowy nieznany",
    };

    private static string DescribeConstruction(ConstructionKind kind) => kind switch
    {
        ConstructionKind.FoodStorage => "składu żywności",
        ConstructionKind.WoodStorage => "składu drewna",
        ConstructionKind.StoneStorage => "składu kamienia",
        ConstructionKind.WoodenWalkway => "pomostu",
        ConstructionKind.GoblinFieldCamp => "obozu wypadowego",
        ConstructionKind.WoodenWall => "drewnianej ściany",
        ConstructionKind.StoneWall => "kamiennego muru",
        ConstructionKind.WoodenDoorFrame => "drewnianej ościeżnicy",
        ConstructionKind.StoneDoorFrame => "kamiennej ościeżnicy",
        ConstructionKind.WoodenDoor => "drewnianych drzwi",
        ConstructionKind.WallTorch => "pochodni ściennej",
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

    private void ShowStoredResources()
    {
        UpdateStoredResources(_engine.CreateSnapshot(), force: true);
        _storedResourcesWindow.Popup();
    }

    private void ShowLooseResources()
    {
        UpdateLooseResources(_engine.CreateSnapshot(), force: true);
        _looseResourcesWindow.Popup();
    }

    private void ShowGoblinRoster()
    {
        UpdateGoblinRoster(_engine.CreateSnapshot(), force: true);
        _goblinRosterWindow.Popup();
    }

    private void ShowStatistics()
    {
        UpdateStatistics(_engine.CreateSnapshot());
        _statisticsWindow.Popup();
    }

    private void UpdateOverviewWindows(SimulationSnapshot snapshot)
    {
        if (_storedResourcesWindow.Visible)
        {
            UpdateStoredResources(snapshot);
        }
        if (_looseResourcesWindow.Visible)
        {
            UpdateLooseResources(snapshot);
        }
        if (_goblinRosterWindow.Visible)
        {
            UpdateGoblinRoster(snapshot);
        }
        if (_statisticsWindow.Visible)
        {
            UpdateStatistics(snapshot);
        }
        if (_raidWindow.Visible)
        {
            UpdateRaidWindowSummary(snapshot);
        }
    }

    private void UpdateStoredResources(SimulationSnapshot snapshot, bool force = false)
    {
        var signature = string.Join('|', snapshot.ResourceInventory.Select(item =>
            $"{(int)item.Resource}:{item.StoredQuantity}")) +
            CreateResourceBreakdownSignature(snapshot, ItemLocationKind.StorageZone) +
            $"|detailed:{_storedResourcesDetailed.ButtonPressed}";
        if (!force && signature == _storedResourcesSignature)
        {
            return;
        }

        _storedResourcesSignature = signature;
        var total = snapshot.ResourceInventory.Sum(item => item.StoredQuantity);
        _storedResourcesSummary.Text =
            $"Fizyczna zawartość {snapshot.StorageZones.Count} magazynów • razem {total:N0} szt.";
        RebuildResourceGrid(
            _storedResourcesGrid,
            snapshot,
            item => item.StoredQuantity,
            ItemLocationKind.StorageZone,
            "w magazynach",
            _storedResourcesDetailed.ButtonPressed);
    }

    private void UpdateLooseResources(SimulationSnapshot snapshot, bool force = false)
    {
        var signature = string.Join('|', snapshot.ResourceInventory.Select(item =>
            $"{(int)item.Resource}:{item.KnownLooseQuantity}")) +
            CreateResourceBreakdownSignature(snapshot, ItemLocationKind.Ground) +
            $"|detailed:{_looseResourcesDetailed.ButtonPressed}";
        if (!force && signature == _looseResourcesSignature)
        {
            return;
        }

        _looseResourcesSignature = signature;
        var total = snapshot.ResourceInventory.Sum(item => item.KnownLooseQuantity);
        _looseResourcesSummary.Text =
            $"Znane towary leżące na ziemi • razem {total:N0} szt. • osobna pula dla tragarzy.";
        RebuildResourceGrid(
            _looseResourcesGrid,
            snapshot,
            item => item.KnownLooseQuantity,
            ItemLocationKind.Ground,
            "na ziemi",
            _looseResourcesDetailed.ButtonPressed);
    }

    private void RebuildResourceGrid(
        GridContainer grid,
        SimulationSnapshot snapshot,
        Func<ResourceInventorySnapshot, int> quantitySelector,
        ItemLocationKind locationKind,
        string location,
        bool detailed)
    {
        foreach (var child in grid.GetChildren())
        {
            child.QueueFree();
        }

        void AddTile(ResourceKind resource, int quantity, string tooltip)
        {
            var tile = new PanelContainer
            {
                CustomMinimumSize = new Vector2(70, 92),
                TooltipText = tooltip,
            };
            var content = new VBoxContainer
            {
                Alignment = BoxContainer.AlignmentMode.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            var icon = new TextureRect
            {
                CustomMinimumSize = new Vector2(58, 58),
                Texture = ItemIcons.CreateTexture(_itemIconAtlas, ItemIcons.ForResource(resource)),
                SelfModulate = ItemIcons.TintForResource(resource),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            var quantityLabel = new Label
            {
                Text = quantity.ToString("N0"),
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            content.AddChild(icon);
            content.AddChild(quantityLabel);
            tile.AddChild(content);
            grid.AddChild(tile);
        }

        if (detailed)
        {
            foreach (var group in GetVisibleResourceStacks(snapshot, locationKind)
                .GroupBy(stack => (stack.Resource, stack.FoodKind, stack.Variant))
                .OrderBy(group => group.Key.Resource)
                .ThenBy(group => group.Key.FoodKind)
                .ThenBy(group => group.Key.Variant))
            {
                var quantity = group.Sum(stack => stack.Quantity);
                var name = group.Key.Resource == ResourceKind.Food
                    ? DescribeFood(group.Key.FoodKind)
                    : group.Key.Variant != ResourceVariant.None
                        ? DescribeResourceVariant(group.Key.Variant)
                        : DescribeResource(group.Key.Resource);
                AddTile(group.Key.Resource, quantity, $"{name}: {quantity:N0} szt. {location}");
            }
            return;
        }

        foreach (var item in snapshot.ResourceInventory.OrderBy(item => item.Resource))
        {
            var quantity = quantitySelector(item);
            AddTile(
                item.Resource,
                quantity,
                DescribeResourceOverviewTooltip(
                    snapshot,
                    item,
                    quantity,
                    locationKind,
                    location));
        }
    }

    private string CreateResourceBreakdownSignature(
        SimulationSnapshot snapshot,
        ItemLocationKind locationKind) =>
        string.Concat(
            "|types:",
            string.Join(',', GetVisibleResourceStacks(snapshot, locationKind)
                .GroupBy(stack => (stack.Resource, stack.FoodKind, stack.Variant))
                .OrderBy(group => group.Key)
                .Select(group =>
                    $"{(int)group.Key.Resource}:{(int)group.Key.FoodKind}:" +
                    $"{(int)group.Key.Variant}:{group.Sum(stack => stack.Quantity)}")));

    private string DescribeResourceOverviewTooltip(
        SimulationSnapshot snapshot,
        ResourceInventorySnapshot item,
        int quantity,
        ItemLocationKind locationKind,
        string location)
    {
        var tooltip = $"{DescribeResource(item.Resource)}: {quantity:N0} szt. {location}";
        var breakdown = GetVisibleResourceStacks(snapshot, locationKind)
            .Where(stack => stack.Resource == item.Resource)
            .GroupBy(stack => (stack.FoodKind, stack.Variant))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var name = item.Resource == ResourceKind.Food
                    ? DescribeFood(group.Key.FoodKind)
                    : group.Key.Variant != ResourceVariant.None
                        ? DescribeResourceVariant(group.Key.Variant)
                        : DescribeResource(item.Resource);
                return $"{name}: {group.Sum(stack => stack.Quantity):N0}";
            })
            .ToArray();
        return breakdown.Length == 0
            ? tooltip
            : $"{tooltip}\n{string.Join(", ", breakdown)}";
    }

    private IEnumerable<ItemStackSnapshot> GetVisibleResourceStacks(
        SimulationSnapshot snapshot,
        ItemLocationKind locationKind) =>
        snapshot.ItemStacks.Where(stack =>
            stack.Location.Kind == locationKind &&
            (locationKind != ItemLocationKind.Ground ||
             snapshot.GetVisibility(stack.Location.Position, _engine.Map.Width).IsDiscovered()));

    private void UpdateGoblinRoster(SimulationSnapshot snapshot, bool force = false)
    {
        var signature = string.Join('|', snapshot.Actors.Select(actor =>
            $"{actor.Id.Value}:{actor.Name}:{actor.Health}:{(int)actor.Job.Kind}:{(int)actor.Job.Phase}"));
        if (!force && signature == _goblinRosterSignature)
        {
            return;
        }

        _goblinRosterSignature = signature;
        foreach (var child in _goblinRosterRows.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var actor in snapshot.Actors.OrderBy(actor => actor.Id))
        {
            var row = new HBoxContainer
            {
                CustomMinimumSize = new Vector2(0, 38),
                TooltipText = DescribeJob(actor.Job),
            };
            var name = new Button
            {
                Text = actor.Name,
                Alignment = HorizontalAlignment.Left,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                FocusMode = Control.FocusModeEnum.None,
                TooltipText = $"Pokaż {actor.Name} na mapie",
            };
            var actorId = actor.Id;
            name.Pressed += () => FocusGoblinFromRoster(actorId);
            var health = new Label
            {
                CustomMinimumSize = new Vector2(145, 0),
                Text = $"zdrowie {actor.Health:N0}/{_engine.Definitions.MaximumHealth:N0}",
                VerticalAlignment = VerticalAlignment.Center,
            };
            var job = new Label
            {
                CustomMinimumSize = new Vector2(54, 0),
                Text = DescribeJobSymbol(actor.Job.Kind),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TooltipText = DescribeJob(actor.Job),
            };
            row.AddChild(name);
            row.AddChild(health);
            row.AddChild(job);
            _goblinRosterRows.AddChild(row);
        }
    }

    private void FocusGoblinFromRoster(EntityId actorId)
    {
        var actor = _engine.CreateSnapshot().Actors.FirstOrDefault(actor => actor.Id == actorId);
        if (actor.Id == EntityId.None)
        {
            return;
        }

        _goblinRosterWindow.Hide();
        if (!_use3DView && _visibleLevel != actor.Position.Z)
        {
            _visibleLevel = actor.Position.Z;
            _worldView.SetVisibleLevel(_visibleLevel);
            UpdateLayerToolAvailability();
        }
        CenterCameraOn(actor.Position);
        SelectActor(actor.Id);
        UpdateStatus();
    }

    private static string DescribeJobSymbol(ActorJobKind kind) => kind switch
    {
        ActorJobKind.Forage => "⌕",
        ActorJobKind.Haul => "⇢",
        ActorJobKind.Rest => "Zz",
        ActorJobKind.Eat => "●",
        ActorJobKind.Explore => "⌖",
        ActorJobKind.Move => "→",
        ActorJobKind.Resupply => "↺",
        ActorJobKind.ClearVegetation => "×",
        ActorJobKind.SupplyConstruction => "⇥",
        ActorJobKind.BuildConstruction => "⚒",
        ActorJobKind.Collapsed => "!",
        ActorJobKind.FellTree => "♣",
        ActorJobKind.QuarryBoulder => "◆",
        ActorJobKind.MineRock => "⛏",
        _ => "·",
    };

    private void UpdateStatistics(SimulationSnapshot snapshot)
    {
        var metrics = _engine.GetMetrics();
        var navigation = metrics.Navigation;
        var cacheHitRate = navigation.Requests == 0
            ? 0
            : navigation.CacheHits * 100d / navigation.Requests;
        var averageTickMilliseconds = metrics.TicksExecuted == 0
            ? 0
            : metrics.TotalTickDuration.TotalMilliseconds / metrics.TicksExecuted;
        var explored = snapshot.Visibility.Count(state => state != CellVisibility.Unknown);
        var stored = snapshot.ResourceInventory.Sum(item => item.StoredQuantity);
        var loose = snapshot.ResourceInventory.Sum(item => item.KnownLooseQuantity);
        _statisticsText.Text =
            $"Plemię: {snapshot.Actors.Count}\n" +
            $"Magazyny: {snapshot.StorageZones.Count} • towary {stored:N0}\n" +
            $"Znane luźne towary: {loose:N0}\n" +
            $"Budowy: {snapshot.ConstructionSites.Count}\n" +
            $"Zlecenia terenowe: {snapshot.WorkDesignations.Count}\n" +
            $"Odkryta mapa: {explored:N0}/{snapshot.Visibility.Count:N0}\n\n" +
            $"Ticki: {metrics.TicksExecuted:N0}\n" +
            $"Ostatni tick: {metrics.LastTickDuration.TotalMilliseconds:N3} ms\n" +
            $"Średni tick: {averageTickMilliseconds:N3} ms\n" +
            $"Aktywne stacki: {metrics.ItemStacks:N0}\n" +
            $"Obiekty świata: {metrics.WorldObjects:N0}\n" +
            $"Ścieżki: {navigation.Searches:N0}/{navigation.Requests:N0} wyszukań " +
            $"• cache {cacheHitRate:N1}% ({navigation.CachedRoutes:N0})";
    }

    private void SelectActor(EntityId actorId)
    {
        _selectedActorId = actorId;
        _worldView.SetSelectedActor(actorId);
        _worldView3D.SetSelectedActor(actorId);
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
        _selectedConstructionId = EntityId.None;
        _constructionDetails.Hide();
        _selectedStorageId = zone.Id;
        _storageSettingsDirty = false;
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
        var mineralFilterDescription = zone.AcceptedResource == ResourceKind.Stone
            ? $"Przyjmowany urobek: {DescribeMineralFilter(zone.MineralFilter)}.\n"
            : string.Empty;
        _storageSummary.Text = $"Skład {DescribeResource(zone.AcceptedResource)}\n" +
            $"Stan: {zone.StoredQuantity}/{zone.Capacity}\n" +
            (zone.SeparatesItemTypes
                ? $"Sloty rodzajowe: {zone.UsedTypeSlots}/{zone.TypeSlotCount}, " +
                  $"stos do {zone.StackCapacity} szt.\n"
                : string.Empty) +
            (contents.Length == 0 ? "Zawartość: pusty\n" :
                $"Zawartość: {string.Join(", ", contents)}\n") +
            mineralFilterDescription +
            (zone.DesiredQuantity == 0
                ? "Automatyczne dostawy wyłączone.\n"
                : $"Żądanie dostawy do {zone.DesiredQuantity} szt.\n") +
            $"Status dostaw: {DescribeStorageDelivery(delivery, assignedHauler)}\n" +
            $"Transport: {haulerDescription}.\n" +
            $"Źródło: {sourceDescription}.\n" +
            $"Priorytet lokalny: {DescribeStoragePriority(zone.Priority)}.\n" +
            $"Priorytet {DescribeResource(zone.AcceptedResource)} w plemieniu: " +
            $"{DescribeStoragePriority(globalPriority)}.";
        if (_storageSettingsDirty)
        {
            return;
        }

        _updatingStorageControls = true;
        try
        {
            _storageMineralFilters.Visible = zone.AcceptedResource == ResourceKind.Stone;
            _storageSandstone.ButtonPressed = zone.MineralFilter.HasFlag(
                MineralStorageFilter.Sandstone);
            _storageGranite.ButtonPressed = zone.MineralFilter.HasFlag(
                MineralStorageFilter.Granite);
            _storageCoal.ButtonPressed = zone.MineralFilter.HasFlag(MineralStorageFilter.Coal);
            _storageIronOre.ButtonPressed = zone.MineralFilter.HasFlag(
                MineralStorageFilter.IronOre);
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
        finally
        {
            _updatingStorageControls = false;
        }
    }

    private void MarkStorageSettingsDirty()
    {
        if (!_updatingStorageControls)
        {
            _storageSettingsDirty = true;
        }
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
        var mineralFilter = MineralStorageFilter.None;
        if (_storageSandstone.ButtonPressed)
        {
            mineralFilter |= MineralStorageFilter.Sandstone;
        }
        if (_storageGranite.ButtonPressed)
        {
            mineralFilter |= MineralStorageFilter.Granite;
        }
        if (_storageCoal.ButtonPressed)
        {
            mineralFilter |= MineralStorageFilter.Coal;
        }
        if (_storageIronOre.ButtonPressed)
        {
            mineralFilter |= MineralStorageFilter.IronOre;
        }
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
        if (zone.AcceptedResource == ResourceKind.Stone)
        {
            _engine.QueueCommand(SimulationCommand.ConfigureStorageMineralFilter(
                executeAt,
                _commandSequence++,
                zone.Id,
                mineralFilter));
        }
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

    private static string DescribeMineralFilter(MineralStorageFilter filter)
    {
        if (filter == MineralStorageFilter.None)
        {
            return "nic (dotychczasowa zawartość pozostaje)";
        }
        if (filter == MineralStorageFilter.All)
        {
            return "wszystkie rodzaje";
        }

        var names = new List<string>(4);
        if (filter.HasFlag(MineralStorageFilter.Sandstone))
        {
            names.Add("piaskowiec");
        }
        if (filter.HasFlag(MineralStorageFilter.Granite))
        {
            names.Add("granit");
        }
        if (filter.HasFlag(MineralStorageFilter.Coal))
        {
            names.Add("węgiel");
        }
        if (filter.HasFlag(MineralStorageFilter.IronOre))
        {
            names.Add("ruda żelaza");
        }

        return string.Join(", ", names);
    }

    private void ShowConstructionDetails(ConstructionSiteSnapshot site)
    {
        _selectedStorageId = EntityId.None;
        _storageDetails.Hide();
        _selectedConstructionId = site.Id;
        UpdateConstructionDetails(site);
        _constructionDetails.Popup();
    }

    private void UpdateConstructionDetails(ConstructionSiteSnapshot site)
    {
        _constructionSummary.Text = DescribeConstructionSite(site);
        _constructionPriority.Select((int)site.Priority);
    }

    private void ApplyConstructionSettings()
    {
        var site = _engine.CreateSnapshot().ConstructionSites
            .FirstOrDefault(item => item.Id == _selectedConstructionId);
        if (site is null)
        {
            _selectedConstructionId = EntityId.None;
            _constructionDetails.Hide();
            return;
        }

        var priority = Enum.IsDefined((StoragePriority)_constructionPriority.Selected)
            ? (StoragePriority)_constructionPriority.Selected
            : StoragePriority.Normal;
        _engine.QueueCommand(SimulationCommand.ConfigureConstructionPriority(
            _engine.CurrentTick.Next(),
            _commandSequence++,
            site.Id,
            priority));
        _inspector.Text = $"Plac budowy {site.Id}: ustaw priorytet " +
            $"{DescribeStoragePriority(priority)}" +
            (_speed == 0 ? " po wznowieniu czasu." : ".");
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
            .AppendLine($"Preferencje pracy: zbieractwo {DescribeWorkPreference(actor.WorkPreferences.Foraging)}, " +
                $"transport {DescribeWorkPreference(actor.WorkPreferences.Hauling)}, " +
                $"budowanie {DescribeWorkPreference(actor.WorkPreferences.Building)}")
            .AppendLine($"Znane cechy: {DescribeTraits(actor.KnownTraits)}")
            .AppendLine($"Służba logistyczna: " +
                (logisticsDuty.Length == 0 ? "brak przydziału" : string.Join(", ", logisticsDuty)))
            .AppendLine();
        text.AppendLine("Plan działań:");
        if (actor.Plan.Count == 0)
        {
            text.AppendLine("— brak kolejnych zamiarów");
        }
        else
        {
            for (var index = 0; index < actor.Plan.Count; index++)
            {
                text.AppendLine($"{index + 1}. {DescribePlanEntry(actor.Plan[index])}");
            }
        }
        text.AppendLine()
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

    private static string DescribePlanEntry(ActorPlanEntrySnapshot entry)
    {
        var action = entry.Kind switch
        {
            ActorPlanIntentKind.CurrentJob => $"kontynuuje: {DescribeJobKind(entry.JobKind)}",
            ActorPlanIntentKind.Eat => "zje niesioną rację",
            ActorPlanIntentKind.FindFood => "poszuka posiłku",
            ActorPlanIntentKind.Drink => "napije się z bukłaka",
            ActorPlanIntentKind.RefillWater => "poszuka wody",
            ActorPlanIntentKind.Rest => "pójdzie odpocząć",
            ActorPlanIntentKind.ResumeSuspendedJob =>
                $"wróci do: {DescribeJobKind(entry.JobKind)} → {entry.Target}",
            _ => "nieznany zamiar",
        };
        return $"{action}  [nacisk {entry.Priority}]";
    }

    private static string DescribeJobKind(ActorJobKind kind) => kind switch
    {
        ActorJobKind.Forage => "zbierania",
        ActorJobKind.Haul => "transportu",
        ActorJobKind.Rest => "odpoczynku",
        ActorJobKind.Eat => "jedzenia",
        ActorJobKind.Explore => "zwiadu",
        ActorJobKind.Move => "marszu",
        ActorJobKind.Resupply => "uzupełniania zapasów",
        ActorJobKind.ClearVegetation => "karczowania",
        ActorJobKind.SupplyConstruction => "dostawy na budowę",
        ActorJobKind.BuildConstruction => "budowy",
        ActorJobKind.Collapsed => "przymusowego snu",
        ActorJobKind.FellTree => "wyrębu",
        ActorJobKind.QuarryBoulder => "wydobycia kamienia",
        ActorJobKind.MineRock => "kopania w skale",
        _ => "bezczynności",
    };

    private static string DescribeWorkPreference(int preference) => preference switch
    {
        -2 => "unika",
        -1 => "nie lubi",
        0 => "obojętne",
        1 => "lubi",
        2 => "uwielbia",
        _ => "nieznane",
    };

    private static void UpdateNeedBar(ProgressBar bar, int value, int maximum, string name)
    {
        bar.Value = value;
        bar.TooltipText = $"{name}: {value:N0} / {maximum:N0}";
    }

    private void UpdateInventoryIcons(ActorSnapshot actor, ItemStackSnapshot? cargo)
    {
        var signature = $"{(int)actor.Equipment}:{string.Join(',', actor.PersonalFoodKinds)}:" +
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
        if (actor.Equipment.HasFlag(PersonalEquipment.WoodenAxe))
        {
            AddInventoryIcon(ItemIcon.WoodenAxe, "Drewniana siekiera • narzędzie do wyrębu");
        }
        if (actor.Equipment.HasFlag(PersonalEquipment.PrimitivePickaxe))
        {
            AddInventoryIcon(_pickaxeIcon, "Prymitywny kilof • narzędzie do rozbijania głazów");
        }
        AddInventoryIcon(
            ItemIcon.Food,
            actor.PersonalFood == 0
                ? "Osobiste racje żywności • pusto"
                : "Osobiste racje • " + string.Join(", ", actor.PersonalFoodKinds
                    .GroupBy(kind => kind)
                    .Select(group =>
                        $"{DescribeFood(group.Key)} ×{group.Count()} " +
                        $"(sytość {_engine.Definitions.Food.GetSatiety(group.Key):N0})")),
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
        => AddInventoryIcon(ItemIcons.CreateTexture(_itemIconAtlas, icon), tooltip, quantity);

    private void AddInventoryIcon(Texture2D texture, string tooltip, int? quantity = null)
    {
        var slot = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(48, 54),
            TooltipText = tooltip,
        };
        var image = new TextureRect
        {
            CustomMinimumSize = new Vector2(44, 40),
            Texture = texture,
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

    private void ToggleWorldView()
    {
        CancelActiveTool();
        SelectActor(EntityId.None);
        _use3DView = !_use3DView;
        _worldView.Visible = !_use3DView;
        _camera.Enabled = !_use3DView;
        _worldView3D.SetActive(_use3DView);
        _cameraModePanel.Visible = _use3DView;
        if (_use3DView)
        {
            _visibleLevel = 0;
            _worldView.SetVisibleLevel(0);
            _worldView3D.Refresh(_engine.CreateSnapshot());
            _viewModeButton.Text = "2D";
            _viewModeButton.TooltipText = "Wróć do stabilnego renderera 2D • F3";
            Update3DCameraControls();
            _inspector.Text = "Prototyp 3D • chunkowe meshe terenu 16×16 • prawdziwe rampy i klify • " +
                $"woda jako osobna powierzchnia • {_worldView3D.TerrainMeshCount} meshów terenu/wody + " +
                $"{_worldView3D.StructureMeshCount} wspólny mesh konstrukcji • dachy ukryte dla czytelności wnętrz.";
        }
        else
        {
            _worldView.Refresh(_engine.CreateSnapshot());
            _viewModeButton.Text = "3D";
            _viewModeButton.TooltipText = "Włącz prototypowy renderer 3D • F3";
            _inspector.Text = "Renderer 2D przywrócony.";
        }

        UpdateLayerToolAvailability();
        ConstrainCameraToMap();
        UpdateStatus();
    }

    private void Toggle3DCameraAngle()
    {
        if (!_use3DView)
        {
            return;
        }

        _worldView3D.ToggleCameraAngle();
        Update3DCameraControls();
        ConstrainCameraToMap();
        _inspector.Text = _worldView3D.CurrentCameraAngle == WorldView3D.CameraAngle.TopDown
            ? "Kamera 3D: widok całkowicie z góry. Q/E obraca mapę skokowo o 90°."
            : "Kamera 3D: widok ukośny. Q/E obraca kamerę skokowo o 90°.";
    }

    private void Rotate3DCamera(int quarterTurns)
    {
        if (!_use3DView)
        {
            return;
        }

        _worldView3D.RotateCamera(quarterTurns);
        Update3DCameraControls();
        ConstrainCameraToMap();
        _inspector.Text = $"Kamera 3D obrócona • {_worldView3D.CameraQuarterTurns * 90}°.";
    }

    private void Update3DCameraControls()
    {
        _cameraAngleButton.Text = _worldView3D.CurrentCameraAngle == WorldView3D.CameraAngle.TopDown
            ? "Z góry"
            : "Ukośnie";
    }

    private void MoveCamera(double delta)
    {
        var direction = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
        if (Input.IsKeyPressed(Key.A)) direction.X -= 1;
        if (Input.IsKeyPressed(Key.D)) direction.X += 1;
        if (Input.IsKeyPressed(Key.W)) direction.Y -= 1;
        if (Input.IsKeyPressed(Key.S)) direction.Y += 1;
        if (_use3DView)
        {
            _worldView3D.Pan(direction.Normalized(), delta);
        }
        else
        {
            _camera.Position += direction.Normalized() * (float)(520 * delta / _camera.Zoom.X);
        }
        ConstrainCameraToMap();
    }

    private void CenterCameraOn(GridPosition position)
    {
        if (_use3DView)
        {
            _worldView3D.CenterOn(position);
        }
        else
        {
            _camera.Position = _worldView.CellToWorld(position);
        }
        ConstrainCameraToMap();
    }

    private void ChangeCameraZoom(float factor)
    {
        if (_use3DView)
        {
            _worldView3D.ChangeZoom(factor);
            ConstrainCameraToMap();
            return;
        }

        var minimumZoom = GetMinimumCameraZoom();
        var maximumZoom = Math.Max(3.5f, minimumZoom);
        var zoom = Math.Clamp(_camera.Zoom.X * factor, minimumZoom, maximumZoom);
        _camera.Zoom = Vector2.One * zoom;
        ConstrainCameraToMap();
    }

    private void ConstrainCameraToMap()
    {
        if (_use3DView)
        {
            _worldView3D.ConstrainCamera();
            var cameraView = _worldView3D.GetNormalizedCameraView(GetViewport().GetVisibleRect().Size);
            _minimap.SetCameraView(cameraView.Center, cameraView.Size);
            return;
        }

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

    private void CreateRaidWindow()
    {
        _raidWindow = new Window
        {
            Title = "Oddział wyprawy",
            Size = new Vector2I(500, 560),
            MinSize = new Vector2I(420, 420),
            Unresizable = false,
        };
        _raidWindow.CloseRequested += _raidWindow.Hide;
        AddChild(_raidWindow);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        _raidWindow.AddChild(margin);

        var content = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddThemeConstantOverride("separation", 10);
        margin.AddChild(content);

        _raidSummary = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        content.AddChild(_raidSummary);
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddChild(scroll);
        _raidRows = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _raidRows.AddThemeConstantOverride("separation", 5);
        scroll.AddChild(_raidRows);

        var buttons = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
        };
        buttons.AddThemeConstantOverride("separation", 8);
        content.AddChild(buttons);
        var cancel = new Button { Text = "Zamknij" };
        cancel.Pressed += _raidWindow.Hide;
        buttons.AddChild(cancel);
        _raidStartButton = new Button { Text = "Rozpocznij przygotowania" };
        _raidStartButton.Pressed += StartSelectedRaid;
        buttons.AddChild(_raidStartButton);
    }

    private void ShowRaidWindow()
    {
        var snapshot = _engine.CreateSnapshot();
        _raidDraftIds.Clear();
        if (snapshot.RaidPartyIds.Count > 0)
        {
            _raidDraftIds.UnionWith(snapshot.RaidPartyIds);
        }
        else
        {
            _raidDraftIds.UnionWith(snapshot.Actors
                .Where(actor => actor.Health > 0)
                .OrderBy(actor => actor.Id)
                .Take(SimulationDefinitions.FieldCampCapacity)
                .Select(actor => actor.Id));
        }

        foreach (var child in _raidRows.GetChildren())
        {
            child.QueueFree();
        }
        var selectionLocked = snapshot.RaidPhase != GoblinRaidPhase.None ||
            snapshot.HumanVillage.GoblinAttackOrdered;
        foreach (var actor in snapshot.Actors.OrderBy(actor => actor.Id))
        {
            var check = new CheckButton
            {
                Text = $"{actor.Name} • zdrowie {actor.Health} • wałówka " +
                    $"{actor.PersonalFood}/{_engine.Definitions.PersonalFoodCapacity} • bukłak " +
                    $"{actor.PersonalWater}/{_engine.Definitions.PersonalWaterCapacity} • " +
                    $"głód {actor.Hunger}, pragnienie {actor.Thirst}, zmęczenie {actor.Fatigue}",
                ButtonPressed = _raidDraftIds.Contains(actor.Id),
                Disabled = selectionLocked || actor.Health <= 0,
                TooltipText = DescribeJob(actor.Job),
            };
            var actorId = actor.Id;
            check.Toggled += enabled => ToggleRaidDraftMember(actorId, check, enabled);
            _raidRows.AddChild(check);
        }

        UpdateRaidWindowSummary(snapshot);
        _raidWindow.PopupCentered();
    }

    private void ToggleRaidDraftMember(EntityId actorId, CheckButton check, bool enabled)
    {
        if (_updatingRaidSelection)
        {
            return;
        }
        if (enabled && _raidDraftIds.Count >= SimulationDefinitions.FieldCampCapacity)
        {
            _updatingRaidSelection = true;
            check.ButtonPressed = false;
            _updatingRaidSelection = false;
            _inspector.Text = $"Oddział może liczyć maksymalnie {SimulationDefinitions.FieldCampCapacity} goblinów.";
            return;
        }

        if (enabled)
        {
            _raidDraftIds.Add(actorId);
        }
        else
        {
            _raidDraftIds.Remove(actorId);
        }
        UpdateRaidWindowSummary(_engine.CreateSnapshot());
    }

    private void UpdateRaidWindowSummary(SimulationSnapshot snapshot)
    {
        var hasCamp = snapshot.WorldObjects.Any(item =>
            item.Kind == WorldObjectKind.GoblinFieldCamp &&
            item.Owner == WorldObjectOwner.GoblinTribe);
        var phase = snapshot.RaidPhase switch
        {
            GoblinRaidPhase.Preparing => $"Przygotowanie w punkcie {snapshot.RaidRallyPoint}.",
            GoblinRaidPhase.Marching => "Oddział maszeruje na wieś.",
            _ => "Wybierz od 1 do 5 goblinów. Wyruszą po zebraniu się w obozie i uzupełnieniu zapasów.",
        };
        var blockers = snapshot.RaidPhase == GoblinRaidPhase.Preparing
            ? DescribeRaidBlockers(snapshot)
            : string.Empty;
        _raidSummary.Text = $"{phase}\nWybrano: {_raidDraftIds.Count}/{SimulationDefinitions.FieldCampCapacity}." +
            (hasCamp ? string.Empty : "\nBrak ukończonego obozowiska z drogą do wsi.") +
            blockers;
        _raidStartButton.Disabled = snapshot.RaidPhase != GoblinRaidPhase.None ||
            snapshot.HumanVillage.GoblinAttackOrdered || !hasCamp || _raidDraftIds.Count == 0;
    }

    private string DescribeRaidBlockers(SimulationSnapshot snapshot)
    {
        var selected = snapshot.RaidPartyIds.ToHashSet();
        var lines = snapshot.Actors
            .Where(actor => selected.Contains(actor.Id) && actor.Health > 0)
            .OrderBy(actor => actor.Id)
            .Select(actor =>
            {
                var reasons = new List<string>();
                if (actor.Position != snapshot.RaidRallyPoint)
                {
                    reasons.Add("idzie do obozu");
                }
                if (actor.CarriedStackId != EntityId.None)
                {
                    reasons.Add("odkłada ładunek");
                }
                if (actor.PersonalFood < _engine.Definitions.PersonalFoodCapacity)
                {
                    reasons.Add("uzupełnia wałówkę");
                }
                if (actor.PersonalWater < _engine.Definitions.PersonalWaterCapacity)
                {
                    reasons.Add("napełnia bukłak");
                }
                if (actor.Hunger >= _engine.Definitions.FoodSeekThreshold)
                {
                    reasons.Add("je");
                }
                if (actor.Thirst >= _engine.Definitions.DrinkThreshold)
                {
                    reasons.Add("pije");
                }
                if (actor.Fatigue >= _engine.Definitions.RestThreshold)
                {
                    reasons.Add("odpoczywa");
                }
                if (reasons.Count == 0 && actor.Job.Kind != ActorJobKind.None)
                {
                    reasons.Add("kończy przygotowania");
                }
                return reasons.Count == 0
                    ? null
                    : $"\n• {actor.Name}: {string.Join(", ", reasons)}";
            })
            .Where(line => line is not null);
        var details = string.Concat(lines);
        return details.Length == 0 ? "\nOddział jest gotowy do wymarszu." : "\nWymarsz czeka na:" + details;
    }

    private void StartSelectedRaid()
    {
        var snapshot = _engine.CreateSnapshot();
        if (_raidDraftIds.Count == 0 || snapshot.RaidPhase != GoblinRaidPhase.None ||
            snapshot.HumanVillage.GoblinAttackOrdered)
        {
            return;
        }

        var executeAt = _engine.CurrentTick.Next();
        foreach (var actor in snapshot.Actors.Where(actor => actor.Health > 0).OrderBy(actor => actor.Id))
        {
            _engine.QueueCommand(SimulationCommand.ConfigureRaidMember(
                executeAt,
                _commandSequence++,
                actor.Id,
                _raidDraftIds.Contains(actor.Id)));
        }
        _engine.QueueCommand(SimulationCommand.AttackHumanVillage(executeAt, _commandSequence++));
        _raidWindow.Hide();
        _inspector.Text = $"Wyznaczono {_raidDraftIds.Count} goblinów do najazdu. " +
            "Najpierw zbiorą się i uzupełnią zapasy w najbliższym obozowisku." +
            (_speed == 0 ? " Polecenie ruszy po wznowieniu czasu." : string.Empty);
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
        if (_use3DView)
        {
            _inspector.Text = "Prototyp 3D pokazuje obecnie całą powierzchnię wraz z wysokościami. " +
                "Przekrój jaskiń zostanie podłączony w kolejnej iteracji renderera.";
            return;
        }

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

        CancelBuildMode(clearInspector: false);
        CancelWorkMode(clearInspector: false);
        _visibleLevel = next;
        _worldView.SetVisibleLevel(next);
        UpdateLayerToolAvailability();
        _inspector.Text = _isMoveMode
            ? $"Widoczna warstwa z={next}. Wskaż odkryty cel marszu dla wybranego goblina."
            : $"Widoczna warstwa mapy: z={next}. Page Up / Page Down zmienia poziom.";
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
            (_visibleLevel < 0 && mode is BuildMode.FoodStorage or BuildMode.WoodStorage or
                BuildMode.StoneStorage or BuildMode.WoodenWall or BuildMode.StoneWall or
                BuildMode.WoodenDoorFrame or BuildMode.StoneDoorFrame or BuildMode.WoodenDoor or
                BuildMode.WallTorch))
        {
            return true;
        }

        CancelBuildMode(clearInspector: false);
        _buildMenu.Hide();
        _inspector.Text = _visibleLevel < 0
            ? "W jaskini można planować składy, ściany, mury, ościeżnice, drzwi i pochodnie. " +
              "Pomost i obozowisko pozostają blueprintami powierzchniowymi."
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
        var build = GetToolbarButton("Build");
        var work = GetToolbarButton("Work");
        build.Disabled = _visibleLevel > 0;
        work.Disabled = _visibleLevel > 0;
        build.TooltipText = _visibleLevel switch
        {
            0 => "Budowanie",
            < 0 => "Budowanie pod ziemią • składy, drewniane ściany i drzwi",
            _ => "Budowanie niedostępne: brak blueprintów konstrukcji nadziemnych",
        };
        work.TooltipText = _visibleLevel switch
        {
            0 => "Zlecenia pracy",
            < 0 => "Zlecenia podziemne • zbieranie urobku, kopanie i czyszczenie",
            _ => "Zlecenia pracy niedostępne na tej warstwie",
        };
    }

    private void UpdateStatus()
    {
        var snapshot = _engine.CreateSnapshot();
        UpdateCalendar(snapshot);
        UpdateOverviewWindows(snapshot);
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
        if (_use3DView)
        {
            _status.Text += "  •  RENDERER 3D: PROTOTYP";
        }
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
