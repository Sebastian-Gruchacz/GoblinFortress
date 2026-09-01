using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Construction;
using GoblinStronghold.Simulation.Localization;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using GoblinStronghold.Simulation.Terrain;
using GoblinStronghold.Simulation.Workshops;
using GoblinStronghold.GodotClient.UI.Actors;
using GoblinStronghold.GodotClient.UI.Animals;
using GoblinStronghold.GodotClient.UI.MainMenu;
using GoblinStronghold.GodotClient.UI.WorldPlanning;
using GoblinStronghold.GodotClient.Application.Profiles;
using GoblinStronghold.GodotClient.Platform.Steam;
using System.Text;

namespace GoblinStronghold.GodotClient;

public partial class Main : Node
{
    private const int MaximumSimulationTicksPerFrame = 8;
    private const double MaximumSimulationMillisecondsPerFrame = 8d;
    private const double PresentationRefreshIntervalSeconds = 1d / 5d;
    private const double FpsRefreshIntervalSeconds = 0.25d;
    private const double MinimumAutosaveIntervalSeconds = 10d * 60d;
    private const string CameraPanLeftAction = "goblin_camera_left";
    private const string CameraPanRightAction = "goblin_camera_right";
    private const string CameraPanUpAction = "goblin_camera_up";
    private const string CameraPanDownAction = "goblin_camera_down";
    private static readonly Color GoblinEntitySelectorColor = new("a8f05c");
    private SimulationEngine _engine = null!;
    private SimulationSnapshot _latestSnapshot = null!;
    private WorldView _worldView = null!;
    private WorldView3D _worldView3D = null!;
    private MinimapView _minimap = null!;
    private Camera2D _camera = null!;
    private Label _fpsCounter = null!;
    private Label _status = null!;
    private Label _clock = null!;
    private Label _seasonName = null!;
    private SeasonCycleView _seasonProgress = null!;
    private Label _inspector = null!;
    private PopupPanel _managementMenu = null!;
    private PopupPanel _buildMenu = null!;
    private PopupPanel _advancedBuildMenu = null!;
    private PopupPanel _terrainMenu = null!;
    private PopupPanel _workMenu = null!;
    private PopupPanel _statisticsMenu = null!;
    private GridContainer _managementMenuGrid = null!;
    private GridContainer _buildMenuGrid = null!;
    private WorldPlanningMenuController _constructionPlanningMenu = null!;
    private GridContainer _advancedBuildMenuGrid = null!;
    private WorldPlanningMenuController _advancedConstructionPlanningMenu = null!;
    private GridContainer _terrainMenuGrid = null!;
    private WorldPlanningMenuController _terrainPlanningMenu = null!;
    private GridContainer _workMenuGrid = null!;
    private WorldPlanningMenuController _workPlanningMenu = null!;
    private GridContainer _statisticsMenuGrid = null!;
    private Texture2D _iconAtlas = null!;
    private Texture2D _itemIconAtlas = null!;
    private Texture2D _constructionIconAtlas = null!;
    private Texture2D _treePartAtlas = null!;
    private Texture2D? _foodIconAtlas;
    private readonly MaterialPaletteTextureCache _resourceThumbnailTextures = new();
    private Texture2D _pickaxeIcon = null!;
    private Texture2D _commandingHandIcon = null!;
    private Texture2D _woodenBarrelIcon = null!;
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
    private Button _raidAutoAssignButton = null!;
    private Button _raidStartButton = null!;
    private OptionButton _raidEngagement = null!;
    private OptionButton _raidCorpseHandling = null!;
    private readonly Dictionary<RaidDirective, CheckButton> _raidDirectiveChecks = [];
    private readonly Dictionary<EntityId, CheckButton> _raidMemberChecks = [];
    private readonly HashSet<EntityId> _raidDraftIds = [];
    private bool _updatingRaidSelection;
    private GridPosition? _raidDraftRallyPoint;
    private bool _isRaidTargetMode;
    private int _raidTargetRadius = SimulationEngine.DefaultRaidTargetRadius;
    private PopupMenu _worldContextMenu = null!;
    private PopupPanel _entitySelectorMenu = null!;
    private VBoxContainer _entitySelectorRows = null!;
    private ContextEntityTarget _contextEntityTarget;
    private Vector2 _contextMenuScreenPosition;
    private ConfirmationDialog _constructionRemovalDialog = null!;
    private GridPosition? _contextCampAnchor;
    private EntityId _contextCorpseId = EntityId.None;
    private ConstructionRemovalTarget _contextRemovalTarget;
    private EntityId _contextRemovalEntityId = EntityId.None;
    private GridPosition _contextRemovalPosition;
    private Window _plannerWindow = null!;
    private VBoxContainer _plannerRows = null!;
    private Label _plannerSummary = null!;
    private string _plannerSignature = string.Empty;
    private Window _logisticsWindow = null!;
    private VBoxContainer _logisticsRows = null!;
    private Label _logisticsSummary = null!;
    private string _logisticsSignature = string.Empty;
    private EntityId _resizingStorageAreaId = EntityId.None;
    private EntityId _replacingWorkOrderId = EntityId.None;
    private StoragePriority? _replacementWorkPriority;
    private bool _replacementWorkSuspended;
    private int _speed = 1;
    private int _visibleLevel;
    private double _accumulator;
    private double _presentationRefreshElapsed;
    private double _fpsRefreshElapsed;
    private ulong _commandSequence = 1;
    private EntityId _selectedActorId = EntityId.None;
    private readonly HashSet<EntityId> _selectedActorIds = [];
    private BuildMode _buildMode;
    private PopupMenu _constructionMaterialMenu = null!;
    private ConstructionMaterialGroup _pendingMaterialGroup;
    private ResourceVariant _selectedConstructionMaterial;
    private GameSessionPreferences _sessionPreferences = new();
    private bool _isDraggingLinearBuild;
    private GridPosition _linearBuildStart;
    private WorkMode _workMode;
    private bool _isDraggingWorkArea;
    private GridPosition _workAreaStart;
    private UnitOrderMode _unitOrderMode;
    private PopupMenu _unitOrderMenu = null!;
    private int _unitOrderRadius = SimulationEngine.DefaultRaidTargetRadius;
    private readonly List<GridPosition> _patrolDraftPoints = [];
    private bool _isPanningCamera;
    private float _rightDragDistance;
    private Window _storageDetails = null!;
    private Label _storageSummary = null!;
    private Label _storageContentsLabel = null!;
    private GridContainer _storageContentsGrid = null!;
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
    private Window _workshopDetails = null!;
    private Label _workshopSummary = null!;
    private GridPosition? _selectedWorkshop;
    private readonly Dictionary<CraftingRecipeKind, Control> _workshopRecipeRows = [];
    private readonly Dictionary<CraftingRecipeKind, Button> _workshopRepeatButtons = [];
    private bool _updatingWorkshopRepeatButtons;
    private GameSaveStore _saveStore = null!;
    private SimulationTick _nextAutosaveTick;
    private double _autosaveElapsedRealSeconds;
    private Control _mainMenu = null!;
    private Button _resumeGameButton = null!;
    private Button _newGameButton = null!;
    private NewGameSetupWindow _newGameSetupWindow = null!;
    private Button _loadMenuButton = null!;
    private Button _chooseSaveButton = null!;
    private Window _recoveryWindow = null!;
    private AcceptDialog _loadFailureDialog = null!;
    private VBoxContainer _recoveryRows = null!;
    private Label _recoverySummary = null!;
    private Window _optionsWindow = null!;
    private VBoxContainer _shortcutRows = null!;
    private ShortcutSettings _shortcutSettings = null!;
    private Theme _gameUiTheme = null!;
    private readonly Dictionary<GameShortcutId, Action> _shortcutActions = [];
    private readonly Dictionary<GameShortcutId, Button> _shortcutBindingButtons = [];
    private readonly Dictionary<GameShortcutId, (Button Button, string Tooltip)> _shortcutTiles = [];
    private GameShortcutId? _capturedShortcut;
    private GameShortcutId? _activeShortcutMenu;
    private ulong _shortcutMenuExpiresAt;
    private AudioStreamPlayer _titleMusic = null!;
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
        BasaltWalkway,
        WoodStorage,
        StoneStorage,
        EquipmentStorage,
        MaterialsStorage,
        WaterBarrel,
        WoodenBox,
        WoodenChest,
        WoodenBulkBin,
        StorageArea,
        FieldCamp,
        WoodenWall,
        StoneWall,
        WoodenFloor,
        StoneFloor,
        WoodenRamp,
        StoneRamp,
        WoodenDoorFrame,
        StoneDoorFrame,
        WoodenDoor,
        WallTorch,
        PrimitiveWorkshop,
        Bloomery,
        SmeltingFurnace,
        CrucibleFurnace,
        GoblinHut,
    }

    private enum ConstructionMaterialGroup
    {
        Walkway,
        StoneWalkway,
        Wall,
        Floor,
        Ramp,
        DoorFrame,
        Door,
    }

    private enum WorkMode
    {
        None,
        GatherFood,
        GatherReeds,
        GatherBrushwood,
        GatherStone,
        UprootBerryBushes,
        FellTrees,
        QuarryBoulders,
        MineRock,
        CarveRampDown,
        CarveRampUp,
        HuntAnimals,
        Scout,
        CleanBlood,
        Clear,
    }

    private enum UnitOrderMode
    {
        None,
        Move,
        AttackArea,
        HuntArea,
        Patrol,
    }

    private enum UnitOrderAction
    {
        Move = 1,
        AttackArea = 2,
        HuntArea = 3,
        Patrol = 4,
    }

    private enum WorldContextAction
    {
        EditRaid = 1,
        ToggleRaidPreparation = 2,
        SelectRaidTarget = 3,
        LaunchRaid = 4,
        SelectCampOccupants = 5,
        OpenCampStorage = 6,
        CancelConstruction = 7,
        DismantleConstruction = 8,
        OpenEntityDetails = 9,
        OrderGoblinFlee = 10,
        OrderGoblinSleep = 11,
        SuspendGoblinDispatcher = 12,
        PickUpItem = 13,
        EquipItem = 14,
        PrioritizeItemHauling = 15,
        LootCorpse = 100,
        ConsumeCorpse = 101,
        RecoverCorpse = 102,
        RecoverAndBudCorpse = 103,
        BudCorpseInPlace = 104,
        ClearCorpseDirectives = 105,
    }

    private enum ContextEntityKind
    {
        None,
        Goblin,
        ConstructionSite,
        WorldObject,
        StorageZone,
        Corpse,
        Animal,
        HumanVillager,
        ItemStack,
    }

    private readonly record struct ContextEntityTarget(
        ContextEntityKind Kind,
        ulong Id,
        GridPosition Position);

    private readonly record struct ContextEntityChoice(
        ContextEntityTarget Target,
        string Label,
        int Section,
        Color? TextColorOverride = null);

    private enum ConstructionRemovalTarget
    {
        None,
        PendingConstruction,
        WorldObject,
        StorageZone,
    }

    public override void _Ready()
    {
        LoadLocalContentPacks();
        _localeSettings = new LocaleSettings(
            ProjectSettings.GlobalizePath("user://settings/locale.json"),
            SteamLocaleProvider.TryGetCurrentGameLanguage);
        _currentLocale = _localeSettings.Locale;
        _saveStore = new GameSaveStore(
            ProjectSettings.GlobalizePath("user://saves"),
            SimulationSaveFormat.CurrentVersion);
        _shortcutSettings = new ShortcutSettings(
            ProjectSettings.GlobalizePath("user://settings/shortcuts.json"));
        ApplyCameraShortcutBindings();
        _gameUiTheme = GameUiTheme.Create();

        _worldView = GetNode<WorldView>("WorldView");
        _worldView3D = GetNode<WorldView3D>("WorldView3D");
        _minimap = GetNode<MinimapView>("Interface/RightHud/MinimapFrame/Minimap");
        _camera = GetNode<Camera2D>("Camera2D");
        _fpsCounter = GetNode<Label>("Interface/FpsPanel/FpsCounter");
        _status = GetNode<Label>("Interface/StatusBar/Status");
        _clock = GetNode<Label>("Interface/Calendar/Controls/Clock");
        _seasonName = GetNode<Label>("Interface/Calendar/Controls/SeasonName");
        _seasonProgress = GetNode<SeasonCycleView>("Interface/Calendar/Controls/Season");
        _inspector = GetNode<Label>("Interface/Inspector/Text");
        _managementMenu = GetNode<PopupPanel>("ManagementMenu");
        _buildMenu = GetNode<PopupPanel>("BuildMenu");
        _advancedBuildMenu = GetNode<PopupPanel>("AdvancedBuildMenu");
        _terrainMenu = GetNode<PopupPanel>("TerrainMenu");
        _workMenu = GetNode<PopupPanel>("WorkMenu");
        _statisticsMenu = GetNode<PopupPanel>("StatisticsMenu");
        _managementMenuGrid = GetNode<GridContainer>("ManagementMenu/Margin/Grid");
        _buildMenuGrid = GetNode<GridContainer>("BuildMenu/Margin/Grid");
        _advancedBuildMenuGrid = GetNode<GridContainer>("AdvancedBuildMenu/Margin/Grid");
        _terrainMenuGrid = GetNode<GridContainer>("TerrainMenu/Margin/Grid");
        _workMenuGrid = GetNode<GridContainer>("WorkMenu/Margin/Grid");
        _statisticsMenuGrid = GetNode<GridContainer>("StatisticsMenu/Margin/Grid");
        _mainMenu = GetNode<Control>("Interface/MainMenu");
        _resumeGameButton = GetNode<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/Resume");
        _newGameButton = GetNode<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/NewGame");
        _loadMenuButton = GetNode<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/LoadGame");
        _chooseSaveButton = GetNode<Button>(
            "Interface/MainMenu/Center/Panel/Margin/Controls/ChooseSave");
        var titleSplash = GetNode<Label>("Interface/MainMenu/Center/Panel/Margin/Controls/Subtitle");
        titleSplash.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        titleSplash.Text = TitleSplashCatalog.Pick(
            _currentLocale,
            Ui("main-menu", "splash-fallback"));
        _titleMusic = GetNode<AudioStreamPlayer>("TitleMusic");
        _titleMusic.Finished += ReplayTitleMusic;
        _viewModeButton = GetNode<Button>("Interface/RightHud/SessionPanel/Controls/ViewMode");
        _cameraModePanel = GetNode<Control>("Interface/RightHud/CameraPanel");
        _cameraAngleButton = GetNode<Button>("Interface/RightHud/CameraPanel/Controls/Angle");
        _iconAtlas = UiIcons.LoadAtlas();
        _itemIconAtlas = ItemIcons.LoadAtlas();
        _constructionIconAtlas = ConstructionIcons.LoadAtlas();
        _treePartAtlas = TreePartSprites.LoadAtlas();
        _foodIconAtlas = ResourceThumbnails.TryLoadFoodAtlas();
        _pickaxeIcon = GD.Load<Texture2D>("res://Assets/UI/primitive-pickaxe-v1.svg");
        _commandingHandIcon = GD.Load<Texture2D>("res://Assets/UI/commanding-hand-v1.svg");
        _woodenBarrelIcon = GD.Load<Texture2D>("res://Assets/UI/wooden-barrel-v1.svg");
        _goblinDetails = GetNode<Window>("GoblinDetails");
        _goblinDetailsText = GetNode<Label>("GoblinDetails/Scroll/Content/Text");
        _inventoryIcons = GetNode<HBoxContainer>("GoblinDetails/Scroll/Content/Inventory");
        _storedResourcesWindow = GetNode<Window>("StoredResourcesWindow");
        _storedResourcesSummary = GetNode<Label>("StoredResourcesWindow/Margin/Content/Summary");
        _storedResourcesDetailed = GetNode<CheckButton>(
            "StoredResourcesWindow/Margin/Content/Detailed");
        _storedResourcesGrid = GetNodeOrNull<GridContainer>(
            "StoredResourcesWindow/Margin/Content/Scroll/Grid") ??
            GetNode<GridContainer>("StoredResourcesWindow/Margin/Content/Grid");
        _storedResourcesGrid.Columns = 3;
        _looseResourcesWindow = GetNode<Window>("LooseResourcesWindow");
        _looseResourcesSummary = GetNode<Label>("LooseResourcesWindow/Margin/Content/Summary");
        _looseResourcesDetailed = GetNode<CheckButton>(
            "LooseResourcesWindow/Margin/Content/Detailed");
        _looseResourcesGrid = GetNodeOrNull<GridContainer>(
            "LooseResourcesWindow/Margin/Content/Scroll/Grid") ??
            GetNode<GridContainer>("LooseResourcesWindow/Margin/Content/Grid");
        _looseResourcesGrid.Columns = 3;
        _goblinRosterWindow = GetNode<Window>("GoblinRosterWindow");
        _goblinRosterRows = GetNode<VBoxContainer>("GoblinRosterWindow/Scroll/Rows");
        _statisticsWindow = GetNode<Window>("StatisticsWindow");
        _statisticsText = GetNode<Label>("StatisticsWindow/Margin/Content/Text");
        GetViewport().GuiEmbedSubwindows = true;
        CreateTextureTileButton(
            _managementMenuGrid,
            _managementMenu,
            _commandingHandIcon,
            Ui("action-tiles", "tribe-planner"),
            ShowPlanner,
            GameShortcutId.ShowPlanner);
        CreateTextureTileButton(
            _managementMenuGrid,
            _managementMenu,
            CreateStorageIcon(ItemIcon.Cargo),
            Ui("action-tiles", "logistics"),
            ShowLogistics);
        CreateWorldPlanningMenus();
        CreateWorldPlanningTools();
        CreateWorkOrderMenus();
        CreateWorkOrderTools();
        CreateTextureTileButton(
            _statisticsMenuGrid,
            _statisticsMenu,
            CreateStoredResourcesOverviewIcon(),
            Ui("action-tiles", "stored-resources"),
            ShowStoredResources);
        CreateTextureTileButton(
            _statisticsMenuGrid,
            _statisticsMenu,
            CreateLooseResourcesOverviewIcon(),
            Ui("action-tiles", "loose-resources"),
            ShowLooseResources);
        CreateTileButton(
            _statisticsMenuGrid,
            _statisticsMenu,
            UiIcon.Health,
            Ui("action-tiles", "goblin-roster"),
            ShowGoblinRoster);
        CreateTextTileButton(
            _statisticsMenuGrid,
            _statisticsMenu,
            "Σ",
            Ui("action-tiles", "tribal-statistics"),
            ShowStatistics);
        CreateNeedIndicators();
        _goblinDetails.CloseRequested += _goblinDetails.Hide;
        ConfigureOverviewWindow(
            _storedResourcesWindow);
        ConfigureOverviewWindow(
            _looseResourcesWindow);
        ConfigureOverviewWindow(
            _goblinRosterWindow);
        ConfigureOverviewWindow(
            _statisticsWindow);
        _storedResourcesDetailed.Toggled += _ =>
        {
            _storedResourcesSignature = string.Empty;
            UpdateStoredResources(_latestSnapshot, force: true);
        };
        _looseResourcesDetailed.Toggled += _ =>
        {
            _looseResourcesSignature = string.Empty;
            UpdateLooseResources(_latestSnapshot, force: true);
        };
        _storageDetails = GetNode<Window>("StorageDetails");
        _storageSummary = GetNode<Label>("StorageDetails/Margin/Controls/Summary");
        var storageControls = GetNode<VBoxContainer>("StorageDetails/Margin/Controls");
        _storageContentsLabel = storageControls.GetNodeOrNull<Label>("ContentsLabel") ??
            new Label
            {
                Name = "ContentsLabel",
                Text = Ui("windows", "contents-empty"),
            };
        if (_storageContentsLabel.GetParent() is null)
        {
            storageControls.AddChild(_storageContentsLabel);
            storageControls.MoveChild(_storageContentsLabel, _storageSummary.GetIndex() + 1);
        }

        _storageContentsGrid = storageControls.GetNodeOrNull<GridContainer>("ContentsGrid") ??
            new GridContainer
            {
                Name = "ContentsGrid",
                Columns = 3,
            };
        if (_storageContentsGrid.GetParent() is null)
        {
            storageControls.AddChild(_storageContentsGrid);
            storageControls.MoveChild(_storageContentsGrid, _storageContentsLabel.GetIndex() + 1);
        }
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
        _constructionDetails = GetNode<Window>("ConstructionDetails");
        _constructionSummary = GetNode<Label>("ConstructionDetails/Margin/Controls/Summary");
        _constructionPriority = GetNode<OptionButton>(
            "ConstructionDetails/Margin/Controls/PriorityRow/Priority");
        foreach (var priority in Enum.GetValues<StoragePriority>())
        {
            _constructionPriority.AddItem(DescribeStoragePriority(priority));
        }
        _constructionDetails.CloseRequested += _constructionDetails.Hide;
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
        _newGameButton.Pressed += ShowNewGameSetup;
        _loadMenuButton.Pressed += LoadGame;
        GetNode<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/Quit").Pressed += () => GetTree().Quit();
        _worldView.Hide();
        _camera.Enabled = false;
        _worldView3D.SetActive(false);
        _minimap.NavigationRequested += CenterCameraOn;
        GetViewport().SizeChanged += HandleViewportSizeChanged;

        BindButton("Pause", 0);
        BindButton("Speed1", 1);
        BindButton("Speed2", 2);
        BindButton("Speed4", 4);
        BindButton("Speed8", 8);
        ConfigureActionButton("Pause", UiIcon.Pause, Ui("toolbar", "pause"));
        ConfigureActionButton("Speed1", UiIcon.Play, Ui("toolbar", "speed-1"));
        ConfigureActionButton("Speed2", UiIcon.Faster, Ui("toolbar", "speed-2"));
        ConfigureActionButton("Speed4", UiIcon.Fastest, Ui("toolbar", "speed-4"));
        ConfigureActionButton("Speed8", UiIcon.Fastest, Ui("toolbar", "speed-8"));
        GetToolbarButton("Speed8").Icon = UiIcons.LoadSpeed8Texture();
        ConfigureActionButton("Management", UiIcon.Work, Ui("toolbar", "management"));
        GetToolbarButton("Management").Icon = _commandingHandIcon;
        ConfigureActionButton("Build", UiIcon.Build, Ui("toolbar", "build"));
        GetToolbarButton("Build").Icon = PlanningToolIcons.CreateBasicConstructionIcon(
            UiIcons.CreateTexture(_iconAtlas, UiIcon.Build));
        ConfigureActionButton(
            "AdvancedBuild", UiIcon.Build, Ui("toolbar", "advanced-build"));
        GetToolbarButton("AdvancedBuild").Icon =
            PlanningToolIcons.CreateAdvancedConstructionIcon(
                UiIcons.CreateTexture(_iconAtlas, UiIcon.Build));
        ConfigureActionButton("Terrain", UiIcon.Build, Ui("toolbar", "terrain"));
        GetToolbarButton("Terrain").Icon =
            CreateBadgedIcon(_pickaxeIcon, CreateMineBadgeIcon());
        ConfigureActionButton("Work", UiIcon.Work, Ui("toolbar", "work"));
        ConfigureActionButton("Move", UiIcon.Expedition, Ui("toolbar", "unit-orders"));
        var statisticsButton = GetToolbarButton("Statistics");
        statisticsButton.FocusMode = Control.FocusModeEnum.None;
        statisticsButton.TooltipText = Ui("toolbar", "statistics");
        GetToolbarButton("Management").Pressed += ShowManagementMenu;
        GetToolbarButton("Build").Pressed += ShowBuildMenu;
        GetToolbarButton("AdvancedBuild").Pressed += ShowAdvancedBuildMenu;
        GetToolbarButton("Terrain").Pressed += ShowTerrainMenu;
        GetToolbarButton("Work").Pressed += ShowWorkMenu;
        GetToolbarButton("Move").Pressed += ShowUnitOrderMenu;
        GetToolbarButton("Raid").Hide();
        statisticsButton.Pressed += ShowStatisticsMenu;
        var levelUpButton = GetNode<Button>(
            "Interface/ActionBar/Controls/LevelControls/LevelUp");
        var levelDownButton = GetNode<Button>(
            "Interface/ActionBar/Controls/LevelControls/LevelDown");
        levelUpButton.Pressed += () => ChangeVisibleLevel(1);
        levelDownButton.Pressed += () => ChangeVisibleLevel(-1);
        RegisterShortcutAction(GameShortcutId.OpenManagement, ShowManagementMenu);
        RegisterShortcutAction(GameShortcutId.OpenConstruction, ShowBuildMenu);
        RegisterShortcutAction(GameShortcutId.OpenTerrain, ShowTerrainMenu);
        RegisterShortcutAction(GameShortcutId.OpenWork, ShowWorkMenu);
        RegisterShortcutAction(GameShortcutId.OpenStatistics, ShowStatisticsMenu);
        RegisterShortcutAction(GameShortcutId.OpenUnitOrders, ShowUnitOrderMenu);
        RegisterShortcutAction(GameShortcutId.CameraLevelUp, () => ChangeVisibleLevel(1));
        RegisterShortcutAction(GameShortcutId.CameraLevelDown, () => ChangeVisibleLevel(-1));
        RegisterShortcutAction(GameShortcutId.MoveSelectedUnits,
            () => SelectUnitOrderMode(UnitOrderMode.Move));
        RegisterShortcutAction(GameShortcutId.AttackArea,
            () => SelectUnitOrderMode(UnitOrderMode.AttackArea));
        RegisterShortcutAction(GameShortcutId.HuntArea,
            () => SelectUnitOrderMode(UnitOrderMode.HuntArea));
        RegisterShortcutAction(GameShortcutId.Patrol,
            () => SelectUnitOrderMode(UnitOrderMode.Patrol));
        RegisterShortcutTile(
            GameShortcutId.OpenManagement,
            GetToolbarButton("Management"),
            Ui("toolbar", "management"));
        RegisterShortcutTile(
            GameShortcutId.OpenConstruction,
            GetToolbarButton("Build"),
            Ui("toolbar", "build"));
        RegisterShortcutTile(
            null,
            GetToolbarButton("AdvancedBuild"),
            Ui("toolbar", "advanced-build"));
        RegisterShortcutTile(
            GameShortcutId.OpenTerrain,
            GetToolbarButton("Terrain"),
            Ui("toolbar", "terrain"));
        RegisterShortcutTile(
            GameShortcutId.OpenWork,
            GetToolbarButton("Work"),
            Ui("toolbar", "work"));
        RegisterShortcutTile(
            GameShortcutId.OpenStatistics,
            statisticsButton,
            Ui("toolbar", "statistics"));
        RegisterShortcutTile(
            GameShortcutId.OpenUnitOrders,
            GetToolbarButton("Move"),
            Ui("toolbar", "selected-unit-orders"));
        RegisterShortcutTile(
            GameShortcutId.CameraLevelUp,
            levelUpButton,
            Ui("toolbar", "level-up"));
        RegisterShortcutTile(
            GameShortcutId.CameraLevelDown,
            levelDownButton,
            Ui("toolbar", "level-down"));
        UpdateLevelButtonLabels();
        CreatePlannerWindow();
        CreateLogisticsWindow();
        CreateRaidWindow();
        CreateWorkshopWindow();
        CreateWorldContextMenu();
        CreateUnitOrderMenu();
        CreateConstructionMaterialMenu();
        CreateOptionsWindow();
        CreateModManagerWindow();
        CreateRecoveryWindow();
        _newGameSetupWindow = new NewGameSetupWindow(
            Ui,
            () => GameProfileName.CreateDefault(
                SteamPlayerIdentityProvider.TryGetPersonaName(),
                System.Environment.UserName,
                Ui("new-game", "default-profile-owner"),
                DateTimeOffset.Now));
        _newGameSetupWindow.StartRequested += StartNewGame;
        AddChild(_newGameSetupWindow);
        ApplyGameThemeToWindows();
        ApplyStaticTranslations();
        UpdateSpeedButtons();
        ShowMainMenu();
    }

    public override void _ExitTree() => _resourceThumbnailTextures.Dispose();

    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        if ((_capturedShortcut is not null || _activeShortcutMenu is not null) &&
            TryHandleShortcutInput(key))
        {
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (!_hasActiveSession || _mainMenu.Visible)
        {
            return;
        }

        UpdateFpsCounter(delta);
        MoveCamera(delta);
        if (_speed == 0)
        {
            return;
        }

        _autosaveElapsedRealSeconds += delta;
        _presentationRefreshElapsed += delta;
        var maximumBacklogSeconds = SecondsPerTick * MaximumSimulationTicksPerFrame;
        _accumulator = Math.Min(
            _accumulator + delta * _speed,
            maximumBacklogSeconds);
        var changed = false;
        var ticksAdvanced = 0;
        var simulationStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        while (_accumulator >= SecondsPerTick &&
               ticksAdvanced < MaximumSimulationTicksPerFrame)
        {
            _engine.AdvanceTicks(1);
            _accumulator -= SecondsPerTick;
            ticksAdvanced++;
            changed = true;
            if (System.Diagnostics.Stopwatch.GetElapsedTime(simulationStartedAt).TotalMilliseconds >=
                MaximumSimulationMillisecondsPerFrame)
            {
                break;
            }
        }

        if (changed && _presentationRefreshElapsed >= PresentationRefreshIntervalSeconds)
        {
            var events = _engine.DrainEvents();
            var snapshot = _engine.CreatePresentationSnapshot();
            _latestSnapshot = snapshot;
            HandleEvents(events, snapshot);
            if (_use3DView)
            {
                _worldView3D.Refresh(snapshot);
            }
            else
            {
                _worldView.Refresh(snapshot);
            }
            _minimap.Refresh(snapshot);
            UpdateStatus(snapshot);
            _presentationRefreshElapsed %= PresentationRefreshIntervalSeconds;
        }
        if (changed)
        {
            TryAutosave();
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Echo: false } shortcutKey &&
            TryHandleShortcutInput(shortcutKey))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (TryDismissTopmostWindow(inputEvent))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

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
                    ShowNewGameSetup();
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
            case InputEventKey key when key.Pressed && !key.Echo && key.Keycode == Key.F1:
                ShowSelectedGoblinDetails();
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
            case InputEventKey key when key.Pressed && key.Keycode == Key.Escape:
                if (_buildMode != BuildMode.None || _workMode != WorkMode.None ||
                    _unitOrderMode != UnitOrderMode.None ||
                    _isRaidTargetMode)
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
                if (_isRaidTargetMode)
                {
                    ChangeRaidTargetRadius(1);
                }
                else if (_unitOrderMode is UnitOrderMode.AttackArea or UnitOrderMode.HuntArea)
                {
                    ChangeUnitOrderRadius(1);
                }
                else
                {
                    ChangeCameraZoom(1.15f);
                }
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                if (_isRaidTargetMode)
                {
                    ChangeRaidTargetRadius(-1);
                }
                else if (_unitOrderMode is UnitOrderMode.AttackArea or UnitOrderMode.HuntArea)
                {
                    ChangeUnitOrderRadius(-1);
                }
                else
                {
                    ChangeCameraZoom(1f / 1.15f);
                }
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
            case InputEventMouseMotion mouse when _isRaidTargetMode:
                UpdateRaidTargetPreview(mouse.Position);
                break;
            case InputEventMouseMotion mouse when _unitOrderMode is UnitOrderMode.AttackArea or
                UnitOrderMode.HuntArea:
                UpdateUnitOrderPreview(mouse.Position);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouse:
                if (_isRaidTargetMode)
                {
                    FinishRaidTargetSelection(mouse.Position);
                }
                else if (_buildMode != BuildMode.None)
                {
                    BeginConstruction(mouse.Position);
                }
                else if (_workMode != WorkMode.None)
                {
                    BeginWorkArea(mouse.Position);
                }
                else if (_unitOrderMode != UnitOrderMode.None)
                {
                    IssueUnitOrder(mouse);
                }
                else if (_selectedActorIds.Count > 0 && TryIssuePassageMove(mouse.Position))
                {
                    // A selected party treats a discovered stair, ramp or cave mouth as a contextual move.
                }
                else
                {
                    InspectWorld(mouse.Position, mouse.ShiftPressed, mouse.CtrlPressed);
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
                if (_buildMode != BuildMode.None || _workMode != WorkMode.None ||
                    _unitOrderMode != UnitOrderMode.None ||
                    _isRaidTargetMode)
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
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right } mouse:
                if (_isPanningCamera && _rightDragDistance < 4f)
                {
                    if (!TryShowWorldContextMenu(mouse.Position))
                    {
                        ClearSelection();
                    }
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

    private SimulationEngine CreateNewEngine(WorldSeed seed, int mapDimension)
    {
        var map = SwampMapGenerator.Generate(
            seed,
            mapDimension,
            mapDimension);
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

    private void ShowNewGameSetup() => _newGameSetupWindow.ShowSetup();

    private void StartNewGame(NewGameSetup setup)
    {
        try
        {
            var protectedPreviousSession = _hasActiveSession;
            if (protectedPreviousSession)
            {
                SaveAutosave();
            }

            _sessionPreferences = new GameSessionPreferences(setup.ProfileName);
            ReplaceEngine(CreateNewEngine(setup.Seed, setup.MapDimension));
            _hasActiveSession = true;
            _newGameSetupWindow.Hide();
            CloseMainMenu();
            _inspector.Text = UiFormat(
                "new-game",
                protectedPreviousSession ? "started-autosaved" : "started",
                setup.ProfileName,
                setup.Seed.Value,
                setup.MapDimension);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _inspector.Text = UiFormat("new-game", "failed", exception.Message);
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
            var receipt = _saveStore.SaveQuick(CreateSaveJson());
            _inspector.Text = $"Gra zapisana i zweryfikowana • " +
                $"tick {_engine.CurrentTick.Value:N0} • {Path.GetFileName(receipt.Path)} • " +
                $"{receipt.ByteCount:N0} B" +
                (receipt.BackupCreated ? " • poprzedni zapis zachowany" : string.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _inspector.Text = $"Zapis nie powiódł się: {exception.Message}";
        }
    }

    private void LoadGame()
    {
        string? preferredFailure = null;
        foreach (var candidate in _saveStore.LoadLatestProgressFirst())
        {
            if (TryLoadGameCandidate(candidate.Path, candidate.Json, out var failure))
            {
                return;
            }
            preferredFailure ??= $"{Path.GetFileName(candidate.Path)}: {failure}";
            // Try rotating autosave recovery points if the preferred save cannot load.
        }

        _inspector.Text = preferredFailure is null
            ? Ui("save-load", "no-save-to-load")
            : UiFormat("save-load", "no-compatible-save", preferredFailure);
        ShowLoadFailure(_inspector.Text);
    }

    private void LoadSpecificGame(string path)
    {
        var candidate = _saveStore.LoadLatestProgressFirst()
            .FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.Path, path));
        if (candidate.Path is null)
        {
            _recoverySummary.Text = Ui("save-load", "save-point-missing");
            ShowLoadFailure(_recoverySummary.Text);
            return;
        }

        if (!TryLoadGameCandidate(candidate.Path, candidate.Json, out var failure))
        {
            _recoverySummary.Text = UiFormat(
                "save-load", "specific-load-failed", Path.GetFileName(path), failure);
            ShowLoadFailure(_recoverySummary.Text);
        }
    }

    private bool TryLoadGameCandidate(string path, string json, out string failure)
    {
        try
        {
            var loaded = SimulationEngine.Load(
                json,
                SimulationDefinitions.Foundation,
                SimulationDebugSettings.ForCurrentBuild);
            var loadedPreferences = GameSessionPreferences.FromSave(json);
            var protectedCurrentSession = _hasActiveSession;
            if (protectedCurrentSession)
            {
                _saveStore.SaveBeforeLoad(CreateSaveJson(), excludedPath: path);
            }
            _sessionPreferences = loadedPreferences;
            ReplaceEngine(loaded);
            _hasActiveSession = true;
            _recoveryWindow.Hide();
            CloseMainMenu();
            _inspector.Text = UiFormat("save-load", "game-loaded",
                    _engine.CurrentTick.Value, Path.GetFileName(path)) +
                (protectedCurrentSession
                    ? Ui("save-load", "previous-session-preserved")
                    : string.Empty);
            failure = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or
            System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            failure = exception.Message;
            return false;
        }
    }

    private void ShowMainMenu()
    {
        if (_mainMenu.Visible)
        {
            return;
        }

        CancelActiveTool();
        if (_hasActiveSession)
        {
            _speedBeforeMenu = _speed;
            SetSpeed(0);
        }
        _workshopDetails.Hide();
        _resumeGameButton.Visible = _hasActiveSession;
        RefreshLoadMenuButton();
        _mainMenu.Show();
        (_hasActiveSession ? _resumeGameButton : _newGameButton).GrabFocus();
    }

    private void RefreshLoadMenuButton()
    {
        var candidates = _saveStore.InspectCandidates();
        _loadMenuButton.Disabled = candidates.Count == 0;
        _chooseSaveButton.Disabled = candidates.Count == 0;
        if (candidates.Count == 0)
        {
            _loadMenuButton.Text = Ui("save-load", "no-saves");
            _loadMenuButton.TooltipText = Ui("save-load", "no-save-points");
            _chooseSaveButton.TooltipText = Ui("save-load", "no-save-points");
            return;
        }

        var selected = candidates[0];
        var selectedName = selected.ProfileName ??
            Path.GetFileNameWithoutExtension(selected.Path);
        _loadMenuButton.Text = selected.CurrentTick is { } tick
            ? UiFormat("save-load", "load-selected-tick", selectedName, tick)
            : UiFormat("save-load", "load-selected", selectedName);
        _loadMenuButton.TooltipText = Ui("save-load", "selected-save") + "\n" +
            string.Join("\n", candidates.Select((candidate, index) =>
                $"{index + 1}. {DescribeSaveCandidate(candidate)}"));
        _chooseSaveButton.TooltipText = Ui("save-load", "open-all-saves");
    }

    private string DescribeSaveCandidate(GameSaveSummary candidate)
    {
        var fileName = Path.GetFileName(candidate.Path);
        var savedAt = candidate.LastWriteTimeUtc.ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm:ss");
        if (!candidate.HasReadableHeader)
        {
            return UiFormat("save-load", "unreadable-save", fileName, savedAt);
        }

        return candidate.ProfileName is { } profileName
            ? UiFormat("save-load", "save-summary-profile",
                fileName,
                profileName,
                candidate.CurrentTick!.Value,
                candidate.LowestSavedZ!.Value,
                candidate.WorldSeed!.Value,
                savedAt)
            : UiFormat("save-load", "save-summary",
                fileName,
                candidate.CurrentTick!.Value,
                candidate.LowestSavedZ!.Value,
                candidate.WorldSeed!.Value,
                savedAt);
    }

    private void CreateRecoveryWindow()
    {
        _loadFailureDialog = new AcceptDialog
        {
            Title = Ui("save-load", "load-failed-title"),
            OkButtonText = "OK",
        };
        AddChild(_loadFailureDialog);

        _recoveryWindow = new Window
        {
            Title = Ui("save-load", "save-points-title"),
            Size = new Vector2I(760, 500),
            MinSize = new Vector2I(560, 360),
            Visible = false,
        };
        _recoveryWindow.CloseRequested += _recoveryWindow.Hide;
        AddChild(_recoveryWindow);

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "top", "right", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", 14);
        }
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _recoveryWindow.AddChild(margin);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 10);
        margin.AddChild(content);
        content.AddChild(new Label
        {
            Text = Ui("save-load", "recovery-help"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        _recoverySummary = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _recoverySummary.AddThemeColorOverride("font_color", GameUiTheme.MutedText);
        content.AddChild(_recoverySummary);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddChild(scroll);
        _recoveryRows = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _recoveryRows.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_recoveryRows);

        var close = new Button { Text = Ui("common", "close") };
        close.Pressed += _recoveryWindow.Hide;
        content.AddChild(close);
        _chooseSaveButton.Pressed += ShowRecoveryWindow;
    }

    private void ShowLoadFailure(string message)
    {
        _loadFailureDialog.DialogText = message;
        _loadFailureDialog.PopupCentered();
    }

    private void ShowRecoveryWindow()
    {
        foreach (var child in _recoveryRows.GetChildren())
        {
            _recoveryRows.RemoveChild(child);
            child.QueueFree();
        }

        var candidates = _saveStore.InspectCandidates();
        _recoverySummary.Text = candidates.Count == 0
            ? Ui("save-load", "no-save-points")
            : UiFormat("save-load", "available-save-points", candidates.Count);
        Button? firstEnabled = null;
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var button = new Button
            {
                Text = (index == 0 ? Ui("save-load", "default-prefix") : string.Empty) +
                    DescribeSaveCandidate(candidate),
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(0, 44),
                Disabled = !candidate.HasReadableHeader,
                TooltipText = candidate.HasReadableHeader
                    ? Ui("save-load", "load-exact")
                    : Ui("save-load", "damaged-header"),
            };
            var path = candidate.Path;
            button.Pressed += () => LoadSpecificGame(path);
            _recoveryRows.AddChild(button);
            firstEnabled ??= button.Disabled ? null : button;
        }

        _recoveryWindow.PopupCentered();
        firstEnabled?.GrabFocus();
    }

    private void CreateOptionsWindow()
    {
        _optionsWindow = new Window
        {
            Title = Ui("options", "title"),
            Size = new Vector2I(650, 680),
            MinSize = new Vector2I(520, 420),
            Visible = false,
        };
        _optionsWindow.CloseRequested += CloseOptions;
        AddChild(_optionsWindow);

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "top", "right", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", 14);
        }
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _optionsWindow.AddChild(margin);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 10);
        margin.AddChild(content);
        content.AddChild(new Label
        {
            Text = Ui("options", "language"),
            ThemeTypeVariation = "HeaderSmall",
        });
        var language = new OptionButton();
        foreach (var locale in TranslationCatalog.SupportedLocales)
        {
            language.AddItem(TranslationCatalog.GetLocaleDisplayName(
                _currentLocale,
                locale));
            language.SetItemMetadata(language.ItemCount - 1, locale);
            if (locale == _currentLocale)
            {
                language.Select(language.ItemCount - 1);
            }
        }
        language.ItemSelected += index =>
        {
            var locale = language.GetItemMetadata((int)index).AsString();
            _localeSettings.Set(locale);
        };
        content.AddChild(language);
        content.AddChild(new Label
        {
            Text = Ui("options", "language-restart"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        content.AddChild(new HSeparator());
        content.AddChild(new Label
        {
            Text = Ui("options", "shortcuts"),
            ThemeTypeVariation = "HeaderSmall",
        });
        content.AddChild(new Label
        {
            Text = Ui("options", "shortcuts-help"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddChild(scroll);
        var shortcutMargin = new MarginContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        shortcutMargin.AddThemeConstantOverride("margin_right", 18);
        scroll.AddChild(shortcutMargin);
        _shortcutRows = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _shortcutRows.AddThemeConstantOverride("separation", 5);
        shortcutMargin.AddChild(_shortcutRows);
        RebuildShortcutRows();

        content.AddChild(new HSeparator());
        content.AddChild(new Label
        {
            Text = Ui("options", "about"),
            ThemeTypeVariation = "HeaderSmall",
        });
        content.AddChild(new Label
        {
            Text = Ui("options", "credits"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        var musicNotice = new Label
        {
            Text = Ui("options", "music-notice"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        musicNotice.AddThemeColorOverride("font_color", GameUiTheme.MutedText);
        musicNotice.AddThemeFontSizeOverride("font_size", 10);
        content.AddChild(musicNotice);

        var close = new Button { Text = Ui("common", "close") };
        close.Pressed += CloseOptions;
        content.AddChild(close);

        var optionsButton = GetNode<Button>(
            "Interface/MainMenu/Center/Panel/Margin/Controls/Options");
        optionsButton.Pressed += ShowOptions;
    }

    private void ShowOptions()
    {
        _capturedShortcut = null;
        RebuildShortcutRows();
        _optionsWindow.PopupCentered();
    }

    private void CloseOptions()
    {
        _capturedShortcut = null;
        _optionsWindow.Hide();
    }

    private void RebuildShortcutRows()
    {
        foreach (var child in _shortcutRows.GetChildren())
        {
            child.QueueFree();
        }
        _shortcutBindingButtons.Clear();

        string? currentSection = null;
        foreach (var definition in ShortcutSettings.Definitions)
        {
            if (definition.Section != currentSection)
            {
                currentSection = definition.Section;
                var section = new Label
                {
                    Text = Ui("shortcut-sections", ShortcutSectionKey(currentSection)),
                };
                section.AddThemeColorOverride("font_color", GameUiTheme.Accent);
                section.AddThemeFontSizeOverride("font_size", 17);
                _shortcutRows.AddChild(section);
            }

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.AddChild(new Label
            {
                Text = (definition.Parent is null ? string.Empty : "    ") +
                    Ui("shortcuts", definition.Id.ToString()),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var binding = new Button
            {
                Text = _shortcutSettings.Describe(definition.Id),
                CustomMinimumSize = new Vector2(150, 34),
            };
            var shortcutId = definition.Id;
            binding.Pressed += () => BeginShortcutCapture(shortcutId);
            row.AddChild(binding);
            _shortcutBindingButtons[shortcutId] = binding;
            _shortcutRows.AddChild(row);
        }
    }

    private void BeginShortcutCapture(GameShortcutId shortcut)
    {
        if (_capturedShortcut is { } previous &&
            _shortcutBindingButtons.TryGetValue(previous, out var previousButton))
        {
            previousButton.Text = _shortcutSettings.Describe(previous);
        }
        _capturedShortcut = shortcut;
        _shortcutBindingButtons[shortcut].Text = Ui("options", "press-key");
    }

    private bool TryHandleShortcutInput(InputEventKey key)
    {
        if (_capturedShortcut is { } captured)
        {
            if (key.Keycode == Key.Escape)
            {
                _shortcutBindingButtons[captured].Text = _shortcutSettings.Describe(captured);
                _capturedShortcut = null;
                return true;
            }

            var stroke = new ShortcutStroke(
                key.Keycode,
                key.CtrlPressed,
                key.AltPressed,
                key.ShiftPressed);
            if (FindShortcutConflict(captured, stroke) is { } conflict)
            {
                _shortcutBindingButtons[captured].Text =
                    string.Format(
                        Ui("options", "shortcut-conflict"),
                        Ui("shortcuts", conflict.ToString()));
                return true;
            }

            _shortcutSettings.Set(captured, stroke);
            _capturedShortcut = null;
            ApplyCameraShortcutBindings();
            RefreshShortcutTooltips();
            UpdateLevelButtonLabels();
            UpdateUnitOrderMenuLabels();
            RebuildShortcutRows();
            return true;
        }

        if (_mainMenu.Visible)
        {
            return false;
        }

        var now = Time.GetTicksMsec();
        if (_activeShortcutMenu is { } activeParent && now <= _shortcutMenuExpiresAt)
        {
            var child = ShortcutSettings.Definitions.FirstOrDefault(definition =>
                definition.Parent == activeParent &&
                _shortcutSettings[definition.Id].Matches(key));
            if (child is not null && _shortcutActions.TryGetValue(child.Id, out var childAction))
            {
                HideShortcutMenu(activeParent);
                _activeShortcutMenu = null;
                childAction();
                return true;
            }
        }
        _activeShortcutMenu = null;

        var parent = ShortcutSettings.Definitions.FirstOrDefault(definition =>
            definition.Parent is null &&
            _shortcutSettings[definition.Id].Matches(key));
        if (parent is null || !_shortcutActions.TryGetValue(parent.Id, out var parentAction))
        {
            return false;
        }

        parentAction();
        _activeShortcutMenu = parent.Id;
        _shortcutMenuExpiresAt = now + 3000;
        return true;
    }

    private static string ShortcutSectionKey(string section) => section switch
    {
        "Menu główne" => "main-menu",
        "Kamera" => "camera",
        "Zarządzanie" => "management",
        "Rozkazy jednostek" => "unit-orders",
        "Konstrukcje" => "construction",
        "Prace i obszary" => "work",
        _ => section,
    };

    private GameShortcutId? FindShortcutConflict(GameShortcutId changed, ShortcutStroke stroke)
    {
        var changedDefinition = ShortcutSettings.Definitions.First(item => item.Id == changed);
        return ShortcutSettings.Definitions
            .Where(item => item.Id != changed && item.Parent == changedDefinition.Parent)
            .Where(item => _shortcutSettings[item.Id] == stroke)
            .Select(item => (GameShortcutId?)item.Id)
            .FirstOrDefault();
    }

    private void HideShortcutMenu(GameShortcutId parent)
    {
        switch (parent)
        {
            case GameShortcutId.OpenManagement:
                _managementMenu.Hide();
                break;
            case GameShortcutId.OpenConstruction:
                _buildMenu.Hide();
                break;
            case GameShortcutId.OpenTerrain:
                _terrainMenu.Hide();
                break;
            case GameShortcutId.OpenWork:
                _workMenu.Hide();
                break;
            case GameShortcutId.OpenStatistics:
                _statisticsMenu.Hide();
                break;
            case GameShortcutId.OpenUnitOrders:
                _unitOrderMenu.Hide();
                break;
        }
    }

    private void RegisterShortcutAction(GameShortcutId shortcut, Action action) =>
        _shortcutActions[shortcut] = action;

    private void RegisterShortcutAction(GameShortcutId? shortcut, Action action)
    {
        if (shortcut is { } id)
        {
            RegisterShortcutAction(id, action);
        }
    }

    private void RegisterShortcutTile(
        GameShortcutId shortcut,
        Button button,
        string tooltip)
    {
        _shortcutTiles[shortcut] = (button, tooltip);
        button.TooltipText = $"{tooltip}\n{UiFormat(
            "toolbar", "shortcut", _shortcutSettings.Describe(shortcut))}";
    }

    private void RegisterShortcutTile(
        GameShortcutId? shortcut,
        Button button,
        string tooltip)
    {
        if (shortcut is { } id)
        {
            RegisterShortcutTile(id, button, tooltip);
        }
    }

    private void UpdateFpsCounter(double delta)
    {
        _fpsRefreshElapsed += delta;
        if (_fpsRefreshElapsed < FpsRefreshIntervalSeconds)
        {
            return;
        }

        _fpsRefreshElapsed %= FpsRefreshIntervalSeconds;
        _fpsCounter.Text = $"FPS: {Engine.GetFramesPerSecond():0}";
    }

    private void RefreshShortcutTooltips()
    {
        foreach (var (shortcut, tile) in _shortcutTiles)
        {
            tile.Button.TooltipText =
                $"{tile.Tooltip}\n{UiFormat(
                    "toolbar", "shortcut", _shortcutSettings.Describe(shortcut))}";
        }
    }

    private void ApplyGameThemeToWindows() => ApplyGameThemeToWindows(this);

    private void ApplyGameThemeToWindows(Node node)
    {
        if (node is Control control)
        {
            control.Theme = _gameUiTheme;
        }
        else if (node is Window window)
        {
            window.Theme = _gameUiTheme;
        }
        foreach (var child in node.GetChildren())
        {
            ApplyGameThemeToWindows(child);
        }
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

        _recoveryWindow.Hide();
        _newGameSetupWindow.Hide();
        _mainMenu.Hide();
        FadeOutTitleMusic();
        SetSpeed(_speedBeforeMenu);
    }

    private void ReplayTitleMusic()
    {
        if (!_hasActiveSession && _mainMenu.Visible)
        {
            _titleMusic.Play();
        }
    }

    private void FadeOutTitleMusic()
    {
        if (!_titleMusic.Playing)
        {
            return;
        }

        var tween = CreateTween();
        tween.TweenProperty(_titleMusic, "volume_db", -40.0, 1.5);
        tween.TweenCallback(Callable.From(() => _titleMusic.Stop()));
    }

    private void TryAutosave()
    {
        if (_engine.CurrentTick.Value < _nextAutosaveTick.Value ||
            _autosaveElapsedRealSeconds < MinimumAutosaveIntervalSeconds)
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
        _saveStore.SaveAuto(CreateSaveJson());
        _autosaveElapsedRealSeconds = 0;
    }

    private string CreateSaveJson() => _sessionPreferences.AddToSave(_engine.Save());

    private void ScheduleNextAutosave() => _nextAutosaveTick =
        SimulationCalendar.NextDayStart(_engine.CurrentTick, _engine.Definitions.Clock);

    private void ReplaceEngine(SimulationEngine engine)
    {
        CancelActiveTool();
        SelectActor(EntityId.None);
        _selectedStorageId = EntityId.None;
        _selectedConstructionId = EntityId.None;
        _selectedWorkshop = null;
        _storageDetails.Hide();
        _constructionDetails.Hide();
        _workshopDetails.Hide();
        _storedResourcesWindow.Hide();
        _looseResourcesWindow.Hide();
        _goblinRosterWindow.Hide();
        _statisticsWindow.Hide();
        _raidWindow.Hide();
        _worldContextMenu.Hide();
        _raidDraftIds.Clear();
        _raidDraftRallyPoint = null;
        _contextCampAnchor = null;
        _storedResourcesSignature = string.Empty;
        _looseResourcesSignature = string.Empty;
        _goblinRosterSignature = string.Empty;
        _engine = engine;
        _latestSnapshot = engine.CreatePresentationSnapshot();
        _commandSequence = engine.NextAvailableCommandSequence;
        _accumulator = 0;
        _presentationRefreshElapsed = 0;
        _autosaveElapsedRealSeconds = 0;
        _visibleLevel = 0;
        _worldView.SetWorld(engine);
        _worldView.SetVisibleLevel(0);
        _worldView.SetSimulationSpeed(_speed, SecondsPerTick);
        _worldView3D.SetWorld(engine);
        _minimap.SetWorld(engine);
        _minimap.SetVisibleLevel(0);
        _worldView.Visible = !_use3DView;
        _camera.Enabled = !_use3DView;
        _worldView3D.SetActive(_use3DView);
        _camera.Position = _worldView.CellToWorld(engine.Map.GoblinSpawn);
        UpdateLayerToolAvailability();
        ScheduleNextAutosave();
        ConstrainCameraToMap();
        UpdateStatus(_latestSnapshot);
    }

    private bool TryDismissTopmostWindow(InputEvent inputEvent)
    {
        var window = GetChildren()
            .OfType<Window>()
            .Where(candidate => candidate.Visible)
            .OrderByDescending(candidate => candidate.HasFocus())
            .ThenByDescending(candidate => candidate.GetIndex())
            .FirstOrDefault();
        if (window is null)
        {
            return false;
        }

        var dismiss = inputEvent is InputEventKey
            {
                Pressed: true,
                Echo: false,
                Keycode: Key.Escape,
            };
        if (!dismiss && inputEvent is InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Right,
            } mouse)
        {
            var decoratedBounds = new Rect2(
                new Vector2(window.Position.X, window.Position.Y - GameUiTheme.WindowTitleHeight),
                new Vector2(window.Size.X, window.Size.Y + GameUiTheme.WindowTitleHeight));
            dismiss = !decoratedBounds.HasPoint(mouse.Position);
        }

        if (!dismiss)
        {
            return false;
        }

        if (window == _optionsWindow)
        {
            CloseOptions();
        }
        else
        {
            window.Hide();
        }
        return true;
    }

    private static void ConfigureOverviewWindow(Window window)
    {
        window.CloseRequested += window.Hide;
    }

    private void ClearSelection()
    {
        SelectActor(EntityId.None);
        _selectedStorageId = EntityId.None;
        _selectedConstructionId = EntityId.None;
        _selectedWorkshop = null;
        _storageDetails.Hide();
        _constructionDetails.Hide();
        _workshopDetails.Hide();
        _inspector.Text = "Zaznaczenie wyczyszczone. PPM przeciągnięty przesuwa mapę.";
    }

    // Keep interaction aligned with the frame the player sees. Creating a full snapshot here
    // would also hash every materialized level and noticeably stall clicks on deep maps.
    private SimulationSnapshot GetDisplayedSnapshot() => _latestSnapshot;

    private void ShowBuildMenu()
    {
        ShowToolbarMenu(_buildMenu, "Build");
    }

    private void ShowAdvancedBuildMenu()
    {
        ShowToolbarMenu(_advancedBuildMenu, "AdvancedBuild");
    }

    private void ShowTerrainMenu()
    {
        ShowToolbarMenu(_terrainMenu, "Terrain");
    }

    private void CreateWorldPlanningMenus()
    {
        _constructionPlanningMenu = new WorldPlanningMenuController(
            this,
            _buildMenu,
            _buildMenuGrid,
            CreateTextureTileButton,
            menu => ShowToolbarMenu(menu, "Build"));
        _advancedConstructionPlanningMenu = new WorldPlanningMenuController(
            this,
            _advancedBuildMenu,
            _advancedBuildMenuGrid,
            CreateTextureTileButton,
            menu => ShowToolbarMenu(menu, "AdvancedBuild"));
        _terrainPlanningMenu = new WorldPlanningMenuController(
            this,
            _terrainMenu,
            _terrainMenuGrid,
            CreateTextureTileButton,
            menu => ShowToolbarMenu(menu, "Terrain"));
    }

    private void CreateWorldPlanningTools()
    {
        void AddBasic(
            Texture2D icon,
            string tooltipKey,
            Action action,
            GameShortcutId? shortcut = null) => _constructionPlanningMenu.AddRootTool(
                icon,
                Ui("action-tiles", tooltipKey),
                action,
                shortcut);

        void AddAdvanced(
            Texture2D icon,
            string tooltipKey,
            Action action) => _advancedConstructionPlanningMenu.AddRootTool(
                icon,
                Ui("action-tiles", tooltipKey),
                action);

        AddBasic(CreateStorageIcon(ItemIcon.Food), "food-storage",
            () => SelectBuildMode((long)BuildMode.FoodStorage),
            GameShortcutId.BuildFoodStorage);
        AddBasic(CreateStorageIcon(ItemIcon.Wood), "wood-storage",
            () => SelectBuildMode((long)BuildMode.WoodStorage),
            GameShortcutId.BuildWoodStorage);
        AddBasic(CreateStorageIcon(ItemIcon.Stone), "stone-storage",
            () => SelectBuildMode((long)BuildMode.StoneStorage),
            GameShortcutId.BuildStoneStorage);
        AddBasic(CreateStorageIcon(ItemIcon.Cargo),
            "equipment-storage", () => SelectBuildMode((long)BuildMode.EquipmentStorage),
            GameShortcutId.BuildEquipmentStorage);
        AddBasic(CreateStorageIcon(ItemIcon.Reeds),
            "materials-storage", () => SelectBuildMode((long)BuildMode.MaterialsStorage),
            GameShortcutId.BuildMaterialsStorage);
        AddBasic(_woodenBarrelIcon, "water-barrel",
            () => SelectBuildMode((long)BuildMode.WaterBarrel));
        AddBasic(PlanningToolIcons.CreateWoodenBoxIcon(), "wooden-box",
            () => SelectBuildMode((long)BuildMode.WoodenBox));
        AddBasic(PlanningToolIcons.CreateWoodenChestIcon(), "wooden-chest",
            () => SelectBuildMode((long)BuildMode.WoodenChest));
        AddBasic(PlanningToolIcons.CreateBulkBinIcon(), "bulk-bin",
            () => SelectBuildMode((long)BuildMode.WoodenBulkBin));
        AddBasic(
            PlanningToolIcons.CreateStorageAreaIcon(),
            "storage-area",
            () => SelectBuildMode((long)BuildMode.StorageArea));

        AddBasic(UiIcons.CreateTexture(_iconAtlas, UiIcon.FieldCamp), "field-camp",
            () => SelectBuildMode((long)BuildMode.FieldCamp),
            GameShortcutId.BuildFieldCamp);
        AddBasic(CreateGoblinHutIcon(), "goblin-hut",
            () => SelectBuildMode((long)BuildMode.GoblinHut),
            GameShortcutId.BuildGoblinHut);
        AddBasic(CreatePrimitiveWorkshopIcon(), "primitive-workshop",
            () => SelectBuildMode((long)BuildMode.PrimitiveWorkshop),
            GameShortcutId.BuildPrimitiveWorkshop);
        _constructionPlanningMenu.AddDisabledRootTool(
            PlanningToolIcons.CreateDryingRackIcon(),
            Ui("action-tiles", "drying-rack-coming-soon"));
        _constructionPlanningMenu.AddDisabledRootTool(
            PlanningToolIcons.CreateCookingFireIcon(),
            Ui("action-tiles", "cooking-fire-coming-soon"));

        AddBasic(
            ConstructionIcons.CreateTexture(_constructionIconAtlas, ConstructionIcon.StoneWall),
            "wall",
            () => ChooseConstructionMaterial(ConstructionMaterialGroup.Wall),
            GameShortcutId.BuildWoodenWall);
        AddBasic(
            ConstructionIcons.CreateTexture(_constructionIconAtlas, ConstructionIcon.WoodenFloor),
            "floor",
            () => ChooseConstructionMaterial(ConstructionMaterialGroup.Floor));
        AddBasic(
            ConstructionIcons.CreateTexture(
                _constructionIconAtlas,
                ConstructionIcon.WoodenDoorFrame),
            "door-frame",
            () => ChooseConstructionMaterial(ConstructionMaterialGroup.DoorFrame));
        AddBasic(
            ConstructionIcons.CreateTexture(_constructionIconAtlas, ConstructionIcon.WoodenDoor),
            "door",
            () => ChooseConstructionMaterial(ConstructionMaterialGroup.Door),
            GameShortcutId.BuildWoodenDoor);
        _constructionPlanningMenu.AddRootSpacer();
        AddBasic(
            ConstructionIcons.CreateTexture(_constructionIconAtlas, ConstructionIcon.WallTorch),
            "wall-torch",
            () => SelectBuildMode((long)BuildMode.WallTorch));

        AddAdvanced(CreateFurnaceIcon(WorkshopKind.Bloomery), "bloomery",
            () => SelectBuildMode((long)BuildMode.Bloomery));
        AddAdvanced(CreateFurnaceIcon(WorkshopKind.SmeltingFurnace), "smelting-furnace",
            () => SelectBuildMode((long)BuildMode.SmeltingFurnace));
        AddAdvanced(CreateFurnaceIcon(WorkshopKind.CrucibleFurnace), "crucible-furnace",
            () => SelectBuildMode((long)BuildMode.CrucibleFurnace));

        _terrainPlanningMenu.AddRootTool(
            CreateBadgedIcon(_pickaxeIcon, CreateMineBadgeIcon()),
            Ui("action-tiles", "mine-rock"),
            () => SelectWorkMode((long)WorkMode.MineRock),
            GameShortcutId.MineRock);
        _terrainPlanningMenu.AddRootTool(
            CreateBadgedIcon(_pickaxeIcon, CreateDirectionBadgeIcon(upward: false)),
            Ui("action-tiles", "carve-ramp-down"),
            () => SelectWorkMode((long)WorkMode.CarveRampDown),
            GameShortcutId.CarveRampDown);
        _terrainPlanningMenu.AddRootTool(
            CreateBadgedIcon(_pickaxeIcon, CreateDirectionBadgeIcon(upward: true)),
            Ui("action-tiles", "carve-ramp-up"),
            () => SelectWorkMode((long)WorkMode.CarveRampUp),
            GameShortcutId.CarveRampUp);
        _terrainPlanningMenu.AddRootSpacer();
        _terrainPlanningMenu.AddRootTool(
            PlanningToolIcons.CreateWoodenWalkwayIcon(),
            Ui("action-tiles", "walkway"),
            () => ChooseConstructionMaterial(ConstructionMaterialGroup.Walkway),
            GameShortcutId.BuildWalkway);
        _terrainPlanningMenu.AddRootTool(
            PlanningToolIcons.CreateBasaltWalkwayIcon(),
            Ui("action-tiles", "basalt-walkway"),
            () => ChooseConstructionMaterial(ConstructionMaterialGroup.StoneWalkway));
        _terrainPlanningMenu.AddDisabledRootTool(
            PlanningToolIcons.CreatePathIcon(),
            Ui("action-tiles", "path-coming-soon"));
        _terrainPlanningMenu.AddDisabledRootTool(
            PlanningToolIcons.CreateRoadIcon(),
            Ui("action-tiles", "road-coming-soon"));
        _terrainPlanningMenu.AddDisabledRootTool(
            PlanningToolIcons.CreateRaiseTerrainIcon(),
            Ui("action-tiles", "raise-terrain-coming-soon"));
        _terrainPlanningMenu.AddDisabledRootTool(
            PlanningToolIcons.CreateLevelTerrainIcon(),
            Ui("action-tiles", "level-terrain-coming-soon"));
        _terrainPlanningMenu.AddRootTool(
            PlanningToolIcons.CreateRampIcon(),
            Ui("action-tiles", "ramp"),
            () => ChooseConstructionMaterial(ConstructionMaterialGroup.Ramp));
        _terrainPlanningMenu.AddRootSpacer();

        RegisterShortcutAction(
            GameShortcutId.BuildStoneWall,
            () => ChooseConstructionMaterial(ConstructionMaterialGroup.Wall));
    }

    private void CreateWorkOrderMenus()
    {
        _workPlanningMenu = new WorldPlanningMenuController(
            this,
            _workMenu,
            _workMenuGrid,
            CreateTextureTileButton,
            menu => ShowToolbarMenu(menu, "Work"));
    }

    private void CreateWorkOrderTools()
    {
        void Add(
            Texture2D icon,
            string tooltipKey,
            Action action,
            GameShortcutId? shortcut = null) => _workPlanningMenu.AddRootTool(
                icon, Ui("action-tiles", tooltipKey), action, shortcut);

        Add(CreateGatherIcon(ItemIcon.Food), "gather-food",
            () => SelectWorkMode((long)WorkMode.GatherFood), GameShortcutId.GatherFood);
        Add(CreateGatherIcon(ItemIcon.Reeds), "gather-reeds",
            () => SelectWorkMode((long)WorkMode.GatherReeds), GameShortcutId.GatherReeds);
        Add(CreateGatherIcon(UiIcons.CreateTexture(_iconAtlas, UiIcon.GatherBrushwood)),
            "gather-brushwood", () => SelectWorkMode((long)WorkMode.GatherBrushwood),
            GameShortcutId.GatherBrushwood);
        Add(CreateGatherIcon(ItemIcon.Stone), "gather-stone",
            () => SelectWorkMode((long)WorkMode.GatherStone), GameShortcutId.GatherStone);

        Add(UiIcons.CreateTexture(_iconAtlas, UiIcon.UprootBush),
            "uproot-bushes", () => SelectWorkMode((long)WorkMode.UprootBerryBushes),
            GameShortcutId.UprootBushes);
        Add(ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.WoodenAxe),
            "fell-trees", () => SelectWorkMode((long)WorkMode.FellTrees),
            GameShortcutId.FellTrees);
        Add(
            CreateBadgedIcon(_pickaxeIcon, ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Stone)),
            "quarry-boulders", () => SelectWorkMode((long)WorkMode.QuarryBoulders),
            GameShortcutId.QuarryBoulders);
        _workPlanningMenu.AddRootSpacer();

        Add(PlanningToolIcons.CreateHuntDesignationIcon(CreateSlingIcon()), "hunt-animals",
            () => SelectWorkMode((long)WorkMode.HuntAnimals), GameShortcutId.HuntAnimals);
        Add(_commandingHandIcon, "attack-area",
            () => SelectUnitOrderMode(UnitOrderMode.AttackArea), GameShortcutId.AttackArea);
        Add(PlanningToolIcons.CreateHuntAreaIcon(CreateSlingIcon()), "hunt-area",
            () => SelectUnitOrderMode(UnitOrderMode.HuntArea), GameShortcutId.HuntArea);
        Add(PlanningToolIcons.CreatePatrolIcon(), "patrol",
            () => SelectUnitOrderMode(UnitOrderMode.Patrol), GameShortcutId.Patrol);

        Add(PlanningToolIcons.CreateScoutIcon(), "scout",
            () => SelectWorkMode((long)WorkMode.Scout), GameShortcutId.Scout);
        Add(CreateCleanBloodIcon(), "clean-blood",
            () => SelectWorkMode((long)WorkMode.CleanBlood), GameShortcutId.CleanBlood);
        Add(UiIcons.CreateTexture(_iconAtlas, UiIcon.ClearOrders),
            "clear-orders", () => SelectWorkMode((long)WorkMode.Clear),
            GameShortcutId.ClearOrders);
        _workPlanningMenu.AddRootSpacer();
    }

    private void ShowManagementMenu() => ShowToolbarMenu(_managementMenu, "Management");

    private void ShowWorkMenu()
    {
        ShowToolbarMenu(_workMenu, "Work");
    }

    private void CreateUnitOrderMenu()
    {
        _unitOrderMenu = new PopupMenu { MinSize = new Vector2I(210, 0) };
        _unitOrderMenu.AddItem(string.Empty, (int)UnitOrderAction.Move);
        _unitOrderMenu.AddItem(string.Empty, (int)UnitOrderAction.AttackArea);
        _unitOrderMenu.AddItem(string.Empty, (int)UnitOrderAction.HuntArea);
        _unitOrderMenu.AddItem(string.Empty, (int)UnitOrderAction.Patrol);
        _unitOrderMenu.IdPressed += action =>
        {
            _activeShortcutMenu = null;
            SelectUnitOrderMode((UnitOrderMode)(int)action);
        };
        AddChild(_unitOrderMenu);
        UpdateUnitOrderMenuLabels();
    }

    private void CreateConstructionMaterialMenu()
    {
        _constructionMaterialMenu = new PopupMenu { MinSize = new Vector2I(240, 0) };
        _constructionMaterialMenu.IdPressed += id =>
        {
            _selectedConstructionMaterial = (ResourceVariant)(int)id;
            _sessionPreferences.SetConstructionMaterial(
                _pendingMaterialGroup.ToString(),
                _selectedConstructionMaterial);
            SelectBuildMode((long)ResolveMaterialBuildMode(
                _pendingMaterialGroup,
                _selectedConstructionMaterial));
        };
        AddChild(_constructionMaterialMenu);
    }

    private void ChooseConstructionMaterial(ConstructionMaterialGroup group)
    {
        _pendingMaterialGroup = group;
        _constructionMaterialMenu.Clear();
        var snapshot = _latestSnapshot;
        var allowedTypes = group switch
        {
            ConstructionMaterialGroup.Walkway or ConstructionMaterialGroup.Door =>
                new[] { MaterialType.Wood },
            ConstructionMaterialGroup.StoneWalkway => new[] { MaterialType.Stone },
            _ => new[] { MaterialType.Wood, MaterialType.Stone },
        };
        var options = MaterialCatalog.Supporting(MaterialUse.Construction)
            .Where(material => allowedTypes.Contains(material.MaterialType) &&
                material.Variant is not null)
            .Select(material => new
            {
                Material = material,
                Variant = material.Variant!.Value,
                StoredQuantity = snapshot.ItemStacks
                    .Where(stack => stack.Location.Kind == ItemLocationKind.StorageZone &&
                        stack.Resource == material.ResourceKind &&
                        stack.Variant == material.Variant.Value)
                    .Sum(stack => stack.Quantity),
            })
            .OrderBy(item => DescribeResourceVariant(item.Variant),
                StringComparer.CurrentCulture)
            .ToArray();
        if (options.Length == 0)
        {
            _inspector.Text = Ui("construction-feedback", "no-construction-materials");
            return;
        }

        var selected = _sessionPreferences.TryGetConstructionMaterial(
                group.ToString(), out var remembered) &&
            options.Any(item => item.Variant == remembered)
                ? remembered
                : options.OrderByDescending(item => item.StoredQuantity)
                    .ThenBy(item => DescribeResourceVariant(item.Variant),
                        StringComparer.CurrentCulture)
                    .First().Variant;
        _selectedConstructionMaterial = selected;
        foreach (var option in options)
        {
            _constructionMaterialMenu.AddItem(
                UiFormat(
                    "material-selection",
                    "option",
                    DescribeResourceVariant(option.Variant),
                    option.StoredQuantity),
                (int)option.Variant);
            var index = _constructionMaterialMenu.ItemCount - 1;
            _constructionMaterialMenu.SetItemAsRadioCheckable(index, true);
            _constructionMaterialMenu.SetItemChecked(index, option.Variant == selected);
        }

        SelectBuildMode((long)ResolveMaterialBuildMode(group, selected));

        _constructionMaterialMenu.Position = new Vector2I(
            Mathf.RoundToInt(GetViewport().GetMousePosition().X),
            Mathf.RoundToInt(GetViewport().GetMousePosition().Y));
        _constructionMaterialMenu.Popup();
    }

    private static BuildMode ResolveMaterialBuildMode(
        ConstructionMaterialGroup group,
        ResourceVariant variant)
    {
        var materialType = MaterialCatalog.Get(variant).MaterialType;
        return (group, materialType) switch
        {
            (ConstructionMaterialGroup.Walkway, MaterialType.Wood) => BuildMode.Walkway,
            (ConstructionMaterialGroup.StoneWalkway, MaterialType.Stone) =>
                BuildMode.BasaltWalkway,
            (ConstructionMaterialGroup.Wall, MaterialType.Wood) => BuildMode.WoodenWall,
            (ConstructionMaterialGroup.Wall, MaterialType.Stone) => BuildMode.StoneWall,
            (ConstructionMaterialGroup.Floor, MaterialType.Wood) => BuildMode.WoodenFloor,
            (ConstructionMaterialGroup.Floor, MaterialType.Stone) => BuildMode.StoneFloor,
            (ConstructionMaterialGroup.Ramp, MaterialType.Wood) => BuildMode.WoodenRamp,
            (ConstructionMaterialGroup.Ramp, MaterialType.Stone) => BuildMode.StoneRamp,
            (ConstructionMaterialGroup.DoorFrame, MaterialType.Wood) =>
                BuildMode.WoodenDoorFrame,
            (ConstructionMaterialGroup.DoorFrame, MaterialType.Stone) =>
                BuildMode.StoneDoorFrame,
            (ConstructionMaterialGroup.Door, MaterialType.Wood) => BuildMode.WoodenDoor,
            _ => throw new InvalidOperationException(
                $"Material '{variant}' cannot be used for '{group}'."),
        };
    }

    private void UpdateUnitOrderMenuLabels()
    {
        if (_unitOrderMenu is null)
        {
            return;
        }

        _unitOrderMenu.SetItemText(
            _unitOrderMenu.GetItemIndex((int)UnitOrderAction.Move),
            $"Marsz [{_shortcutSettings[GameShortcutId.MoveSelectedUnits]}]");
        _unitOrderMenu.SetItemText(
            _unitOrderMenu.GetItemIndex((int)UnitOrderAction.AttackArea),
            $"Atakuj obszar [{_shortcutSettings[GameShortcutId.AttackArea]}]");
        _unitOrderMenu.SetItemText(
            _unitOrderMenu.GetItemIndex((int)UnitOrderAction.HuntArea),
            $"Poluj w obszarze [{_shortcutSettings[GameShortcutId.HuntArea]}]");
        _unitOrderMenu.SetItemText(
            _unitOrderMenu.GetItemIndex((int)UnitOrderAction.Patrol),
            $"Patrol [{_shortcutSettings[GameShortcutId.Patrol]}]");
    }

    private void ShowUnitOrderMenu()
    {
        var speedButton = GetToolbarButton("Speed8");
        var rect = speedButton.GetGlobalRect();
        _unitOrderMenu.Position = new Vector2I(
            Mathf.RoundToInt(rect.Position.X),
            Mathf.RoundToInt(rect.End.Y + 4));
        _unitOrderMenu.Popup();
    }

    private void SelectUnitOrderMode(UnitOrderMode mode)
    {
        if (_selectedActorIds.Count == 0)
        {
            _inspector.Text = "Najpierw wybierz goblina albo grupę goblinów. Shift+LPM rozszerza zaznaczenie.";
            return;
        }

        CancelBuildMode(clearInspector: false);
        CancelWorkMode(clearInspector: false);
        _isRaidTargetMode = false;
        _worldView.SetRaidTargetPreview(null, 0);
        _unitOrderMode = mode;
        _patrolDraftPoints.Clear();
        if (mode is UnitOrderMode.AttackArea or UnitOrderMode.HuntArea)
        {
            _unitOrderRadius = SimulationEngine.DefaultRaidTargetRadius;
            UpdateUnitOrderPreview(GetViewport().GetMousePosition());
        }
        _inspector.Text = mode switch
        {
            UnitOrderMode.Move => "Marsz (M): wskaż odkryte, dostępne pole.",
            UnitOrderMode.AttackArea => "Atak (A): wskaż centrum poszukiwania wrogów • kółko zmienia promień.",
            UnitOrderMode.HuntArea => "Polowanie (H): wskaż centrum łowiska • kółko zmienia promień.",
            UnitOrderMode.Patrol => "Patrol (P): wskaż cel; Ctrl+LPM dodaje kolejne punkty, zwykłe LPM kończy trasę.",
            _ => string.Empty,
        };
    }

    private void IssueMoveOrder(Vector2 screenPosition)
    {
        IssueMoveOrder(ScreenToVisibleCell(screenPosition));
    }

    private void IssueMoveOrder(GridPosition clickedDestination)
    {
        var snapshot = _latestSnapshot;
        if (!IsBuildableLayerCell(clickedDestination) ||
            !snapshot.GetVisibility(clickedDestination, _engine.Map.Width).IsDiscovered())
        {
            _inspector.Text = "Cel marszu musi być odkrytym, dostępnym polem.";
            return;
        }

        var executeAt = _engine.CurrentTick.Next();
        var ordered = 0;
        var usedPassage = false;
        foreach (var actor in snapshot.Actors
                     .Where(actor => _selectedActorIds.Contains(actor.Id) && actor.Health > 0)
                     .OrderBy(actor => actor.Id))
        {
            var destination = ResolveContextualMoveDestination(
                clickedDestination,
                actor.Position,
                out var actorUsesPassage);
            if ((!actorUsesPassage &&
                 !snapshot.GetVisibility(destination, _engine.Map.Width).IsDiscovered()) ||
                !_engine.World.IsTerrainReachable(destination))
            {
                continue;
            }
            _engine.QueueCommand(SimulationCommand.Move(
                executeAt,
                _commandSequence++,
                actor.Id,
                destination));
            ordered++;
            usedPassage |= actorUsesPassage || destination.Z != actor.Position.Z;
        }
        if (ordered == 0)
        {
            _inspector.Text = "Żaden zaznaczony goblin nie może dotrzeć do wskazanego przejścia.";
            return;
        }
        _unitOrderMode = UnitOrderMode.None;
        _inspector.Text = ordered == 1
            ? $"Wydano rozkaz marszu do {clickedDestination}" +
              (usedPassage ? " przez przejście między poziomami." : ".")
            : $"Wydano {ordered} goblinom rozkaz zbiórki przy {clickedDestination}" +
              (usedPassage ? " z użyciem przejścia między poziomami." : ".");
        _inspector.Text +=
            (_speed == 0 ? " Zostanie wykonany po wznowieniu czasu." : string.Empty);
    }

    private void IssueUnitOrder(InputEventMouseButton mouse)
    {
        switch (_unitOrderMode)
        {
            case UnitOrderMode.Move:
                IssueMoveOrder(mouse.Position);
                break;
            case UnitOrderMode.AttackArea:
            case UnitOrderMode.HuntArea:
                IssueAreaUnitOrder(mouse.Position);
                break;
            case UnitOrderMode.Patrol:
                AddPatrolPoint(mouse.Position, mouse.CtrlPressed);
                break;
        }
    }

    private void IssueAreaUnitOrder(Vector2 screenPosition)
    {
        var center = ScreenToVisibleCell(screenPosition);
        var snapshot = _latestSnapshot;
        if (!_engine.Visibility.Get(center).IsDiscovered())
        {
            _inspector.Text = "Obszar rozkazu musi leżeć na rozpoznanym terenie.";
            return;
        }

        var executeAt = _engine.CurrentTick.Next();
        var ordered = 0;
        foreach (var actor in snapshot.Actors
                     .Where(actor => _selectedActorIds.Contains(actor.Id) && actor.Health > 0)
                     .OrderBy(actor => actor.Id))
        {
            _engine.QueueCommand(_unitOrderMode == UnitOrderMode.AttackArea
                ? SimulationCommand.OrderAttackArea(
                    executeAt, _commandSequence++, actor.Id, center, _unitOrderRadius)
                : SimulationCommand.OrderHuntArea(
                    executeAt, _commandSequence++, actor.Id, center, _unitOrderRadius));
            ordered++;
        }

        var name = _unitOrderMode == UnitOrderMode.AttackArea ? "atak" : "polowanie";
        _unitOrderMode = UnitOrderMode.None;
        _worldView.SetRaidTargetPreview(null, 0);
        _inspector.Text = $"Wydano {ordered} goblinom rozkaz: {name} w promieniu " +
            $"{_unitOrderRadius} od {center}.";
    }

    private void AddPatrolPoint(Vector2 screenPosition, bool keepAdding)
    {
        var point = ScreenToVisibleCell(screenPosition);
        var snapshot = _latestSnapshot;
        if (!_engine.Visibility.Get(point).IsDiscovered() ||
            !_engine.World.IsTerrainReachable(point))
        {
            _inspector.Text = "Punkt patrolu musi być odkryty i dostępny.";
            return;
        }
        if (_patrolDraftPoints.Count == 0 || _patrolDraftPoints[^1] != point)
        {
            _patrolDraftPoints.Add(point);
        }
        if (keepAdding)
        {
            _inspector.Text = $"Patrol: dodano punkt {_patrolDraftPoints.Count}. " +
                "Ctrl+LPM dodaje następny; zwykłe LPM kończy trasę.";
            return;
        }

        var executeAt = _engine.CurrentTick.Next();
        var ordered = 0;
        foreach (var actor in snapshot.Actors
                     .Where(actor => _selectedActorIds.Contains(actor.Id) && actor.Health > 0)
                     .OrderBy(actor => actor.Id))
        {
            for (var index = 0; index < _patrolDraftPoints.Count; index++)
            {
                _engine.QueueCommand(SimulationCommand.OrderPatrol(
                    executeAt,
                    _commandSequence++,
                    actor.Id,
                    _patrolDraftPoints[index],
                    append: index > 0));
            }
            ordered++;
        }
        _unitOrderMode = UnitOrderMode.None;
        _inspector.Text = $"Wydano {ordered} goblinom patrol przez " +
            $"{_patrolDraftPoints.Count + 1} punktów (łącznie z pozycją startową).";
        _patrolDraftPoints.Clear();
    }

    private void UpdateUnitOrderPreview(Vector2 screenPosition) =>
        _worldView.SetRaidTargetPreview(ScreenToVisibleCell(screenPosition), _unitOrderRadius);

    private void ChangeUnitOrderRadius(int delta)
    {
        _unitOrderRadius = Math.Clamp(
            _unitOrderRadius + delta,
            SimulationEngine.MinimumRaidTargetRadius,
            SimulationEngine.MaximumRaidTargetRadius);
        UpdateUnitOrderPreview(GetViewport().GetMousePosition());
        _inspector.Text = $"Promień obszaru rozkazu: {_unitOrderRadius}.";
    }

    private bool TryIssuePassageMove(Vector2 screenPosition)
    {
        var clicked = ScreenToVisibleCell(screenPosition);
        if (!_engine.Visibility.Get(clicked).IsDiscovered() ||
            !_engine.World.CreateVerticalPassageSnapshot().Any(passage =>
                passage.Upper == clicked || passage.Lower == clicked))
        {
            return false;
        }

        IssueMoveOrder(clicked);
        return true;
    }

    private GridPosition ResolveContextualMoveDestination(
        GridPosition clicked,
        GridPosition actorPosition,
        out bool usesPassage)
    {
        foreach (var passage in _engine.World.CreateVerticalPassageSnapshot())
        {
            if (passage.Upper == clicked)
            {
                usesPassage = true;
                return actorPosition.Z == passage.Upper.Z ? passage.Lower : passage.Upper;
            }
            if (passage.Lower == clicked)
            {
                usesPassage = true;
                return actorPosition.Z == passage.Lower.Z ? passage.Upper : passage.Lower;
            }
        }

        usesPassage = false;
        return clicked;
    }

    private void ShowStatisticsMenu() => ShowToolbarMenu(_statisticsMenu, "Statistics");

    private void ShowToolbarMenu(PopupPanel menu, string buttonName)
    {
        foreach (var candidate in new[]
                 {
                     _managementMenu, _buildMenu, _advancedBuildMenu, _terrainMenu,
                     _workMenu, _statisticsMenu,
                 }.Concat(_constructionPlanningMenu.Submenus)
                 .Concat(_advancedConstructionPlanningMenu.Submenus)
                 .Concat(_terrainPlanningMenu.Submenus)
                 .Concat(_workPlanningMenu.Submenus))
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
        Action action,
        GameShortcutId? shortcut = null)
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
        RegisterShortcutAction(shortcut, action);
        RegisterShortcutTile(shortcut, button, tooltip);
    }

    private Button CreateTextureTileButton(
        GridContainer grid,
        PopupPanel menu,
        Texture2D texture,
        string tooltip,
        Action action,
        GameShortcutId? shortcut = null)
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
        RegisterShortcutAction(shortcut, action);
        RegisterShortcutTile(shortcut, button, tooltip);
        return button;
    }

    private Texture2D CreateStorageIcon(ItemIcon item) => CreateCompositeIcon(
        CreateStorageBaseIcon(),
        ItemIcons.CreateTexture(_itemIconAtlas, item),
        new Rect2I(18, 20, 36, 36));

    private Texture2D CreateUiItemCompositeIcon(UiIcon action, ItemIcon item) =>
        CreateBadgedIcon(
            UiIcons.CreateTexture(_iconAtlas, action),
            ItemIcons.CreateTexture(_itemIconAtlas, item));

    private Texture2D CreateGatherIcon(ItemIcon item) =>
        CreateGatherIcon(ItemIcons.CreateTexture(_itemIconAtlas, item));

    private static Texture2D CreateGatherIcon(Texture2D resource) =>
        CreateBadgedIcon(resource, CreateGatherBadgeIcon());

    private static Texture2D CreateBadgedIcon(Texture2D foundation, Texture2D badge) =>
        CreateCompositeIcon(foundation, badge, new Rect2I(36, 36, 27, 27));

    private static Texture2D CreateCompositeIcon(
        Texture2D foundation,
        Texture2D overlay,
        Rect2I overlayRegion)
    {
        var image = foundation.GetImage();
        image.Resize(64, 64, Image.Interpolation.Lanczos);
        var overlayImage = overlay.GetImage();
        overlayImage.Resize(
            overlayRegion.Size.X,
            overlayRegion.Size.Y,
            Image.Interpolation.Lanczos);
        image.BlendRect(
            overlayImage,
            new Rect2I(Vector2I.Zero, overlayRegion.Size),
            overlayRegion.Position);
        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D CreateStorageBaseIcon()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
              <path d="M6 18 L32 6 L58 18 V57 H6 Z" fill="#6f4728" stroke="#2c1b12" stroke-width="4" stroke-linejoin="round"/>
              <path d="M10 20 H54 M10 39 H54" stroke="#d09a52" stroke-width="4"/>
              <path d="M13 23 H51 V53 H13 Z" fill="#241b14" stroke="#46301f" stroke-width="2"/>
              <path d="M9 55 H55" stroke="#d09a52" stroke-width="5" stroke-linecap="round"/>
            </svg>
            """;
        return CreateSvgIcon(svg, "storage base");
    }

    private static Texture2D CreateGatherBadgeIcon()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">
              <circle cx="16" cy="16" r="14" fill="#315b35" stroke="#f2d889" stroke-width="2"/>
              <path d="M16 25 V9 M9 16 L16 9 L23 16" fill="none" stroke="#f7edc0" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
              <path d="M7 26 H25" stroke="#d09a52" stroke-width="3" stroke-linecap="round"/>
            </svg>
            """;
        return CreateSvgIcon(svg, "gathering badge");
    }

    private static Texture2D CreateMineBadgeIcon()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">
              <circle cx="16" cy="16" r="14" fill="#312d2b" stroke="#f2d889" stroke-width="2"/>
              <path d="M7 25 V18 C7 9 25 9 25 18 V25" fill="#70655b" stroke="#d8c492" stroke-width="2"/>
              <path d="M12 25 V19 C12 14 20 14 20 19 V25 Z" fill="#171514"/>
            </svg>
            """;
        return CreateSvgIcon(svg, "mining badge");
    }

    private static Texture2D CreateDirectionBadgeIcon(bool upward)
    {
        var arrow = upward
            ? "M16 7 L8 16 H13 V25 H19 V16 H24 Z"
            : "M16 25 L8 16 H13 V7 H19 V16 H24 Z";
        var svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">
              <circle cx="16" cy="16" r="14" fill="#493522" stroke="#f2d889" stroke-width="2"/>
              <path d="{arrow}" fill="#f7edc0" stroke="#241b14" stroke-width="1.5" stroke-linejoin="round"/>
            </svg>
            """;
        return CreateSvgIcon(svg, upward ? "ramp-up badge" : "ramp-down badge");
    }

    private static Texture2D CreateGoblinHutIcon()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
              <path d="M8 29 L32 8 L56 29 V57 H8 Z" fill="#79502f" stroke="#2b1b12" stroke-width="4" stroke-linejoin="round"/>
              <path d="M4 30 L32 4 L60 30" fill="none" stroke="#54713b" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/>
              <path d="M25 57 V39 Q32 31 39 39 V57 Z" fill="#211711" stroke="#d09a52" stroke-width="3"/>
              <circle cx="32" cy="43" r="3" fill="#ffd968"/>
              <path d="M15 35 H22 M42 35 H49" stroke="#d09a52" stroke-width="4" stroke-linecap="round"/>
            </svg>
            """;
        return CreateSvgIcon(svg, "goblin hut");
    }

    private static Texture2D CreateCleanBloodIcon()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
              <path d="M20 7 C20 7 9 22 9 31 C9 39 14 44 20 44 C26 44 31 39 31 31 C31 22 20 7 20 7 Z" fill="#a52d2d" stroke="#3b1515" stroke-width="3"/>
              <path d="M33 12 L56 44" stroke="#8b5a2b" stroke-width="6" stroke-linecap="round"/>
              <path d="M44 38 L58 34 L62 49 L48 54 Z" fill="#d7c28a" stroke="#4b3520" stroke-width="3"/>
              <path d="M49 42 L59 40 M50 47 L60 45" stroke="#72a9c2" stroke-width="2"/>
              <path d="M8 54 Q22 48 38 55" fill="none" stroke="#72a9c2" stroke-width="4" stroke-linecap="round"/>
            </svg>
            """;
        return CreateSvgIcon(svg, "blood-cleaning");
    }

    private static Texture2D CreateStoredResourcesOverviewIcon()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
              <rect x="7" y="8" width="50" height="48" rx="4" fill="#6f4728" stroke="#2c1b12" stroke-width="4"/>
              <path d="M10 31 H54" stroke="#d09a52" stroke-width="4"/>
              <rect x="13" y="14" width="14" height="12" rx="2" fill="#b53e36"/>
              <rect x="34" y="14" width="16" height="12" rx="2" fill="#718f3b"/>
              <rect x="13" y="37" width="16" height="13" rx="2" fill="#9a7448"/>
              <rect x="35" y="37" width="15" height="13" rx="2" fill="#777d7f"/>
            </svg>
            """;
        return CreateSvgIcon(svg, "stored-resources overview");
    }

    private static Texture2D CreateLooseResourcesOverviewIcon()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
              <ellipse cx="32" cy="55" rx="27" ry="5" fill="#2c2118"/>
              <path d="M9 45 L24 31 L34 49 Z" fill="#8b8d86" stroke="#343635" stroke-width="3"/>
              <path d="M31 50 L43 24 L54 50 Z" fill="#6f4728" stroke="#2c1b12" stroke-width="3"/>
              <circle cx="49" cy="22" r="7" fill="#b73535" stroke="#4a1717" stroke-width="2"/>
              <path d="M47 15 Q52 9 57 14" fill="none" stroke="#5d873e" stroke-width="3" stroke-linecap="round"/>
              <path d="M14 48 H56" stroke="#d09a52" stroke-width="3" stroke-dasharray="4 3"/>
            </svg>
            """;
        return CreateSvgIcon(svg, "loose-resources overview");
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

    private static Texture2D CreatePrimitiveWorkshopIcon()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
              <rect x="7" y="25" width="50" height="17" rx="3" fill="#8b6038" stroke="#342318" stroke-width="4"/>
              <path d="M14 41 L11 59 M50 41 L53 59" stroke="#5a3b26" stroke-width="6" stroke-linecap="round"/>
              <path d="M12 30 L52 30" stroke="#c18a50" stroke-width="3"/>
              <path d="M22 23 L41 10" stroke="#9ca4a1" stroke-width="5" stroke-linecap="round"/>
              <path d="M36 8 L47 15 L40 20 Z" fill="#5f6768" stroke="#252a2b" stroke-width="2"/>
              <ellipse cx="20" cy="21" rx="7" ry="5" fill="#d8cfad" stroke="#6b5d43" stroke-width="2"/>
            </svg>
            """;
        return CreateSvgIcon(svg, "primitive workshop");
    }

    private static Texture2D CreateFurnaceIcon(WorkshopKind kind)
    {
        var body = kind switch
        {
            WorkshopKind.Bloomery => "#766753",
            WorkshopKind.SmeltingFurnace => "#59606a",
            WorkshopKind.CrucibleFurnace => "#49434f",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        var chimneyHeight = kind switch
        {
            WorkshopKind.Bloomery => 18,
            WorkshopKind.SmeltingFurnace => 12,
            _ => 7,
        };
        var svg = $$"""
            <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
              <path d="M14 57 L18 25 Q32 16 46 25 L50 57 Z" fill="{{body}}" stroke="#211d1a" stroke-width="4"/>
              <path d="M25 {{chimneyHeight}} L39 {{chimneyHeight}} L41 27 L23 27 Z" fill="#62584d" stroke="#211d1a" stroke-width="3"/>
              <path d="M22 57 L24 42 Q32 34 40 42 L42 57 Z" fill="#2a211d" stroke="#17110f" stroke-width="3"/>
              <path d="M27 53 Q32 40 37 53 Z" fill="#ff8a2b"/>
            </svg>
            """;
        return CreateSvgIcon(svg, kind.ToString());
    }

    private static Texture2D CreateSlingIcon()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
              <path d="M13 7 C18 22 24 29 31 36" fill="none" stroke="#b88759" stroke-width="5" stroke-linecap="round"/>
              <path d="M51 7 C46 22 40 29 33 36" fill="none" stroke="#b88759" stroke-width="5" stroke-linecap="round"/>
              <path d="M23 34 Q32 29 41 34 L38 44 Q32 49 26 44 Z" fill="#7b4c31" stroke="#342118" stroke-width="3"/>
              <path d="M28 45 C25 51 21 56 17 60" fill="none" stroke="#b88759" stroke-width="4" stroke-linecap="round"/>
              <circle cx="32" cy="38" r="5" fill="#777d7f" stroke="#292d2e" stroke-width="2"/>
            </svg>
            """;
        return CreateSvgIcon(svg, "primitive sling");
    }

    private static Texture2D CreateSvgIcon(string svg, string name)
    {
        var image = new Image();
        if (image.LoadSvgFromString(svg) != Error.Ok)
        {
            throw new InvalidOperationException($"Cannot create the {name} icon.");
        }
        return ImageTexture.CreateFromImage(image);
    }

    private void CreateTextTileButton(
        GridContainer grid,
        PopupPanel menu,
        string text,
        string tooltip,
        Action action,
        GameShortcutId? shortcut = null)
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
        RegisterShortcutAction(shortcut, action);
        RegisterShortcutTile(shortcut, button, tooltip);
    }

    private void CreateNeedIndicators()
    {
        var definitions = SimulationDefinitions.Foundation;
        var grid = GetNode<GridContainer>("GoblinDetails/Scroll/Content/Needs");
        _healthBar = CreateNeedIndicator(
            grid, UiIcon.Health, "Zdrowie", definitions.MaximumHealth);
        _hungerBar = CreateNeedIndicator(
            grid, UiIcon.Hunger, "Nasycenie", definitions.MaximumHunger);
        _thirstBar = CreateNeedIndicator(
            grid, UiIcon.Thirst, "Nawodnienie", definitions.MaximumThirst);
        _fatigueBar = CreateNeedIndicator(
            grid, UiIcon.FieldCamp, "Wytrzymałość", definitions.MaximumFatigue);
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
        _unitOrderMode = UnitOrderMode.None;
        _worldView.SetRaidTargetPreview(null, 0);
        _resizingStorageAreaId = EntityId.None;
        _buildMode = mode;
        _isDraggingLinearBuild = false;
        _worldView.SetConstructionPreview([]);
        UpdateActiveToolCursor();
        _inspector.Text = _buildMode switch
        {
            BuildMode.FoodStorage => Ui("build-prompts", "food-storage"),
            BuildMode.WoodStorage => Ui("build-prompts", "wood-storage"),
            BuildMode.StoneStorage => Ui("build-prompts", "stone-storage"),
            BuildMode.EquipmentStorage => Ui("build-prompts", "equipment-storage"),
            BuildMode.MaterialsStorage => Ui("build-prompts", "materials-storage"),
            BuildMode.WaterBarrel => Ui("build-prompts", "water-barrel"),
            BuildMode.WoodenBox => Ui("build-prompts", "wooden-box"),
            BuildMode.WoodenChest => Ui("build-prompts", "wooden-chest"),
            BuildMode.WoodenBulkBin => Ui("build-prompts", "bulk-bin"),
            BuildMode.StorageArea => Ui("build-prompts", "storage-area"),
            BuildMode.Walkway => UiFormat(
                "build-prompts", "walkway", DescribeResourceVariant(_selectedConstructionMaterial)),
            BuildMode.BasaltWalkway => UiFormat(
                "build-prompts", "basalt-walkway",
                DescribeResourceVariant(_selectedConstructionMaterial)),
            BuildMode.FieldCamp => Ui("build-prompts", "field-camp"),
            BuildMode.GoblinHut => UiFormat(
                "build-prompts", "goblin-hut", SimulationDefinitions.GoblinHutCapacity),
            BuildMode.WoodenWall => UiFormat(
                "build-prompts", "wooden-wall", DescribeResourceVariant(_selectedConstructionMaterial)),
            BuildMode.StoneWall => UiFormat(
                "build-prompts", "stone-wall", DescribeResourceVariant(_selectedConstructionMaterial)),
            BuildMode.WoodenFloor => UiFormat(
                "build-prompts", "wooden-floor", DescribeResourceVariant(_selectedConstructionMaterial)),
            BuildMode.StoneFloor => UiFormat(
                "build-prompts", "stone-floor", DescribeResourceVariant(_selectedConstructionMaterial)),
            BuildMode.WoodenRamp => UiFormat(
                "build-prompts", "wooden-ramp", DescribeResourceVariant(_selectedConstructionMaterial)),
            BuildMode.StoneRamp => UiFormat(
                "build-prompts", "stone-ramp", DescribeResourceVariant(_selectedConstructionMaterial)),
            BuildMode.WoodenDoorFrame => UiFormat(
                "build-prompts", "wooden-door-frame",
                DescribeResourceVariant(_selectedConstructionMaterial)),
            BuildMode.StoneDoorFrame => UiFormat(
                "build-prompts", "stone-door-frame",
                DescribeResourceVariant(_selectedConstructionMaterial)),
            BuildMode.WoodenDoor => UiFormat(
                "build-prompts", "wooden-door",
                DescribeResourceVariant(_selectedConstructionMaterial)),
            BuildMode.WallTorch => Ui("build-prompts", "wall-torch"),
            BuildMode.PrimitiveWorkshop => Ui("build-prompts", "primitive-workshop"),
            BuildMode.Bloomery => Ui("build-prompts", "bloomery"),
            BuildMode.SmeltingFurnace => Ui("build-prompts", "smelting-furnace"),
            BuildMode.CrucibleFurnace => Ui("build-prompts", "crucible-furnace"),
            _ => _inspector.Text,
        };
    }

    private void SelectWorkMode(long id)
    {
        var mode = (WorkMode)id;
        CancelBuildMode(clearInspector: false);
        _unitOrderMode = UnitOrderMode.None;
        _worldView.SetRaidTargetPreview(null, 0);
        _workMode = mode;
        _isDraggingWorkArea = false;
        _worldView.SetWorkPreview(default, []);
        UpdateActiveToolCursor();
        _inspector.Text = _workMode switch
        {
            WorkMode.GatherFood => Ui("work-prompts", "gather-food"),
            WorkMode.GatherReeds => Ui("work-prompts", "gather-reeds"),
            WorkMode.GatherBrushwood => Ui("work-prompts", "gather-brushwood"),
            WorkMode.GatherStone => Ui("work-prompts", "gather-stone"),
            WorkMode.UprootBerryBushes => Ui("work-prompts", "uproot-bushes"),
            WorkMode.FellTrees => Ui("work-prompts", "fell-trees"),
            WorkMode.QuarryBoulders => Ui("work-prompts", "quarry-boulders"),
            WorkMode.MineRock => Ui("work-prompts", "mine-rock"),
            WorkMode.CarveRampDown => Ui("work-prompts", "carve-ramp-down"),
            WorkMode.CarveRampUp => Ui("work-prompts", "carve-ramp-up"),
            WorkMode.HuntAnimals => Ui("work-prompts", "hunt-animals"),
            WorkMode.Scout => Ui("work-prompts", "scout"),
            WorkMode.CleanBlood => Ui("work-prompts", "clean-blood"),
            WorkMode.Clear => Ui("work-prompts", "clear-orders"),
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

        if (IsSmallStorageBuildMode(_buildMode))
        {
            _linearBuildStart = cell;
            _isDraggingLinearBuild = true;
            UpdateBuildPreview(screenPosition);
            return;
        }

        if (_buildMode is BuildMode.WoodenRamp or BuildMode.StoneRamp)
        {
            if (!_engine.Visibility.Get(cell).IsDiscovered() ||
                !TryInferDiscoveredRamp(cell, out var upper))
            {
                _inspector.Text = Ui("construction-feedback", "ramp-direction-invalid");
                UpdateBuildPreview(screenPosition);
                return;
            }

            _engine.QueueCommand(_buildMode == BuildMode.StoneRamp
                ? SimulationCommand.BuildStoneRamp(
                    _engine.CurrentTick.Next(), _commandSequence++, cell, upper,
                    _selectedConstructionMaterial)
                : SimulationCommand.BuildWoodenRamp(
                    _engine.CurrentTick.Next(), _commandSequence++, cell, upper,
                    _selectedConstructionMaterial));
            _inspector.Text = UiFormat(
                "construction-feedback",
                _buildMode == BuildMode.StoneRamp ? "stone-ramp-ordered" : "wooden-ramp-ordered",
                cell,
                upper,
                DescribeRampDirection(cell, upper));
            UpdateBuildPreview(screenPosition);
            return;
        }

        if (_buildMode is BuildMode.WoodenDoorFrame or BuildMode.StoneDoorFrame or
            BuildMode.WoodenDoor or BuildMode.WallTorch or BuildMode.PrimitiveWorkshop or
            BuildMode.Bloomery or BuildMode.SmeltingFurnace or BuildMode.CrucibleFurnace)
        {
            if (!_engine.Visibility.Get(cell).IsDiscovered())
            {
                _inspector.Text = Ui("construction-feedback", "discovered-cell-required");
                return;
            }

            var command = _buildMode switch
            {
                BuildMode.WoodenDoorFrame => SimulationCommand.BuildWoodenDoorFrame(
                    _engine.CurrentTick.Next(), _commandSequence++, cell,
                    _selectedConstructionMaterial),
                BuildMode.StoneDoorFrame => SimulationCommand.BuildStoneDoorFrame(
                    _engine.CurrentTick.Next(), _commandSequence++, cell,
                    _selectedConstructionMaterial),
                BuildMode.WallTorch => SimulationCommand.BuildWallTorch(
                    _engine.CurrentTick.Next(), _commandSequence++, cell),
                BuildMode.PrimitiveWorkshop => SimulationCommand.BuildPrimitiveWorkshop(
                    _engine.CurrentTick.Next(), _commandSequence++, cell),
                BuildMode.Bloomery => SimulationCommand.BuildWorkshop(
                    _engine.CurrentTick.Next(), _commandSequence++, cell, WorkshopKind.Bloomery),
                BuildMode.SmeltingFurnace => SimulationCommand.BuildWorkshop(
                    _engine.CurrentTick.Next(), _commandSequence++, cell,
                    WorkshopKind.SmeltingFurnace),
                BuildMode.CrucibleFurnace => SimulationCommand.BuildWorkshop(
                    _engine.CurrentTick.Next(), _commandSequence++, cell,
                    WorkshopKind.CrucibleFurnace),
                _ => SimulationCommand.BuildWoodenDoor(
                    _engine.CurrentTick.Next(), _commandSequence++, cell,
                    _selectedConstructionMaterial),
            };
            _engine.QueueCommand(command);
            _inspector.Text = _buildMode switch
            {
                BuildMode.WoodenDoorFrame =>
                    Ui("construction-feedback", "wooden-door-frame-ordered"),
                BuildMode.StoneDoorFrame =>
                    Ui("construction-feedback", "stone-door-frame-ordered"),
                BuildMode.WallTorch =>
                    Ui("construction-feedback", "wall-torch-ordered"),
                BuildMode.PrimitiveWorkshop =>
                    Ui("construction-feedback", "primitive-workshop-ordered"),
                BuildMode.Bloomery => Ui("construction-feedback", "bloomery-ordered"),
                BuildMode.SmeltingFurnace =>
                    Ui("construction-feedback", "smelting-furnace-ordered"),
                BuildMode.CrucibleFurnace =>
                    Ui("construction-feedback", "crucible-furnace-ordered"),
                _ => Ui("construction-feedback", "wooden-door-ordered"),
            };
            UpdateBuildPreview(screenPosition);
            return;
        }

        if (_buildMode == BuildMode.FieldCamp)
        {
            var cells = GetAreaCells(cell, cell with { X = cell.X + 1, Y = cell.Y + 1 });
            if (cells.Any(item =>
                    !IsBuildableLayerCell(item) ||
                    !_engine.Visibility.Get(item).IsDiscovered()))
            {
                _inspector.Text = Ui("construction-feedback", "field-camp-invalid");
                return;
            }
            _engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
                _engine.CurrentTick.Next(), _commandSequence++, cell));
            _inspector.Text = Ui("construction-feedback", "field-camp-ordered");
            UpdateBuildPreview(screenPosition);
            return;
        }

        if (_buildMode == BuildMode.GoblinHut)
        {
            _engine.QueueCommand(SimulationCommand.BuildGoblinHut(
                _engine.CurrentTick.Next(),
                _commandSequence++,
                cell));
            _inspector.Text = Ui("construction-feedback", "goblin-hut-ordered");
            UpdateBuildPreview(screenPosition);
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
        if (!IsBuildableLayerCell(end) || end.Z != _linearBuildStart.Z)
        {
            _inspector.Text = Ui("construction-feedback", "single-level");
            return;
        }

        IReadOnlyList<GridPosition> cells = _buildMode is BuildMode.StorageArea or BuildMode.WoodenFloor or
                BuildMode.StoneFloor || IsSmallStorageBuildMode(_buildMode)
            ? GetAreaCells(_linearBuildStart, end)
            : SimulationCommand.GetLinearCells(_linearBuildStart, end);
        if (_buildMode is BuildMode.WoodenFloor or BuildMode.StoneFloor)
        {
            cells = GetPlaceableFloorCells(cells);
        }
        if (IsSmallStorageBuildMode(_buildMode))
        {
            if (cells.Count > 64 || cells.Any(cell =>
                    !IsBuildableLayerCell(cell) ||
                    !_engine.Visibility.Get(cell).IsDiscovered()))
            {
                _inspector.Text = Ui("construction-feedback", "storage-batch-invalid");
                UpdateBuildPreview(screenPosition);
                return;
            }

            var orderedCount = cells.Count(CreateSelectedStorage);
            if (orderedCount == 0)
            {
                _inspector.Text = Ui("construction-feedback", "storage-batch-invalid");
                UpdateBuildPreview(screenPosition);
                return;
            }
            _inspector.Text = UiFormat(
                "construction-feedback", "storage-batch-ordered", orderedCount);
            UpdateBuildPreview(screenPosition);
            return;
        }
        if (_buildMode is BuildMode.StorageArea or BuildMode.WoodenFloor or
                BuildMode.StoneFloor && cells.Count > 256)
        {
            _inspector.Text = Ui("construction-feedback", "area-too-large");
            UpdateBuildPreview(screenPosition);
            return;
        }
        if (!IsLinearConstructionPlacementValid(cells))
        {
            _inspector.Text = _buildMode switch
            {
                BuildMode.Walkway when ContainsLava(cells) =>
                    Ui("construction-feedback", "wooden-walkway-lava"),
                BuildMode.Walkway or BuildMode.BasaltWalkway =>
                    Ui("construction-feedback", "walkway-invalid"),
                BuildMode.StorageArea when _resizingStorageAreaId != EntityId.None =>
                    Ui("construction-feedback", "storage-resize-invalid"),
                BuildMode.StorageArea =>
                    Ui("construction-feedback", "storage-area-invalid"),
                BuildMode.WoodenFloor or BuildMode.StoneFloor =>
                    Ui("construction-feedback", "floor-invalid"),
                _ => Ui("construction-feedback", "linear-invalid"),
            };
            UpdateBuildPreview(screenPosition);
            return;
        }

        var command = _buildMode switch
        {
            BuildMode.WoodenWall => SimulationCommand.BuildWoodenWall(
                _engine.CurrentTick.Next(), _commandSequence++, _linearBuildStart, end,
                _selectedConstructionMaterial),
            BuildMode.StoneWall => SimulationCommand.BuildStoneWall(
                _engine.CurrentTick.Next(), _commandSequence++, _linearBuildStart, end,
                _selectedConstructionMaterial),
            BuildMode.WoodenFloor => SimulationCommand.BuildWoodenFloor(
                _engine.CurrentTick.Next(), _commandSequence++, _linearBuildStart, end,
                _selectedConstructionMaterial),
            BuildMode.StoneFloor => SimulationCommand.BuildStoneFloor(
                _engine.CurrentTick.Next(), _commandSequence++, _linearBuildStart, end,
                _selectedConstructionMaterial),
            BuildMode.BasaltWalkway => SimulationCommand.BuildBasaltWalkway(
                _engine.CurrentTick.Next(), _commandSequence++, _linearBuildStart, end,
                _selectedConstructionMaterial),
            BuildMode.StorageArea when _resizingStorageAreaId != EntityId.None =>
                SimulationCommand.ResizeStorageArea(
                    _engine.CurrentTick.Next(),
                    _commandSequence++,
                    _resizingStorageAreaId,
                    _linearBuildStart,
                    end),
            BuildMode.StorageArea => SimulationCommand.CreateStorageArea(
                _engine.CurrentTick.Next(), _commandSequence++, _linearBuildStart, end),
            _ => SimulationCommand.BuildWalkway(
                _engine.CurrentTick.Next(), _commandSequence++, _linearBuildStart, end,
                _selectedConstructionMaterial),
        };
        _engine.QueueCommand(command);
        _inspector.Text = _buildMode switch
        {
            BuildMode.WoodenWall =>
                UiFormat("construction-feedback", "wooden-wall-ordered", cells.Count, cells.Count * 2),
            BuildMode.StoneWall =>
                UiFormat("construction-feedback", "stone-wall-ordered", cells.Count, cells.Count * 2),
            BuildMode.WoodenFloor =>
                UiFormat("construction-feedback", "wooden-floor-ordered", cells.Count,
                    DescribeResourceVariant(_selectedConstructionMaterial)),
            BuildMode.StoneFloor =>
                UiFormat("construction-feedback", "stone-floor-ordered", cells.Count,
                    DescribeResourceVariant(_selectedConstructionMaterial)),
            BuildMode.BasaltWalkway =>
                UiFormat("construction-feedback", "basalt-walkway-ordered", cells.Count,
                    DescribeResourceVariant(_selectedConstructionMaterial)),
            BuildMode.StorageArea when _resizingStorageAreaId != EntityId.None =>
                UiFormat("construction-feedback", "storage-resize-ordered",
                    _resizingStorageAreaId, cells.Count),
            BuildMode.StorageArea =>
                UiFormat("construction-feedback", "storage-area-ordered", cells.Count),
            _ => UiFormat("construction-feedback", "walkway-ordered", cells.Count),
        };
        UpdateBuildPreview(screenPosition);
    }

    private void UpdateBuildPreview(Vector2 screenPosition)
    {
        var cell = ScreenToVisibleCell(screenPosition);
        if (!IsBuildableLayerCell(cell))
        {
            _worldView.SetConstructionPreview([]);
            return;
        }

        IReadOnlyList<GridPosition> cells = _buildMode switch
        {
            BuildMode.Walkway or BuildMode.BasaltWalkway or BuildMode.WoodenWall or
                BuildMode.StoneWall
                when _isDraggingLinearBuild =>
                SimulationCommand.GetLinearCells(_linearBuildStart, cell),
            BuildMode.StorageArea when _isDraggingLinearBuild =>
                GetAreaCells(_linearBuildStart, cell),
            _ when IsSmallStorageBuildMode(_buildMode) && _isDraggingLinearBuild =>
                GetAreaCells(_linearBuildStart, cell),
            BuildMode.WoodenFloor or BuildMode.StoneFloor when _isDraggingLinearBuild =>
                GetAreaCells(_linearBuildStart, cell),
            BuildMode.FieldCamp => GetAreaCells(cell, cell with { X = cell.X + 1, Y = cell.Y + 1 }),
            BuildMode.GoblinHut => GetAreaCells(cell, cell with { X = cell.X + 2, Y = cell.Y + 2 }),
            _ => new[] { cell },
        };
        if (_buildMode is BuildMode.WoodenFloor or BuildMode.StoneFloor)
        {
            cells = GetPlaceableFloorCells(cells);
        }
        _worldView.SetConstructionPreview(cells, IsConstructionPreviewValid(cells));
        if (_isDraggingLinearBuild)
        {
            _inspector.Text = _buildMode switch
            {
            BuildMode.WoodenWall =>
                    UiFormat("construction-feedback", "wooden-wall-preview", cells.Count, cells.Count * 2),
                BuildMode.StoneWall =>
                    UiFormat("construction-feedback", "stone-wall-preview", cells.Count, cells.Count * 2),
                BuildMode.WoodenFloor =>
                    UiFormat("construction-feedback", "wooden-floor-preview", cells.Count,
                        DescribeResourceVariant(_selectedConstructionMaterial)),
                BuildMode.StoneFloor =>
                    UiFormat("construction-feedback", "stone-floor-preview", cells.Count,
                        DescribeResourceVariant(_selectedConstructionMaterial)),
                BuildMode.BasaltWalkway =>
                    UiFormat("construction-feedback", "basalt-walkway-preview", cells.Count,
                        DescribeResourceVariant(_selectedConstructionMaterial)),
                BuildMode.Walkway when ContainsLava(cells) =>
                    Ui("construction-feedback", "walkway-lava-preview"),
                BuildMode.StorageArea when _resizingStorageAreaId != EntityId.None =>
                    UiFormat("construction-feedback", "storage-resize-preview",
                        _resizingStorageAreaId, cells.Count),
                BuildMode.StorageArea =>
                    UiFormat("construction-feedback", "storage-area-preview", cells.Count),
                _ => UiFormat("construction-feedback", "walkway-preview", cells.Count),
            };
        }
    }

    private void CancelBuildMode(bool clearInspector = true)
    {
        var wasActive = _buildMode != BuildMode.None;
        _buildMode = BuildMode.None;
        _resizingStorageAreaId = EntityId.None;
        _isDraggingLinearBuild = false;
        _worldView.SetConstructionPreview([]);
        UpdateActiveToolCursor();
        if (clearInspector && wasActive)
        {
            _inspector.Text = Ui("work-feedback", "build-mode-cancelled");
        }
    }

    private bool IsConstructionPreviewValid(IReadOnlyList<GridPosition> cells) =>
        cells.Count > 0 && (IsSmallStorageBuildMode(_buildMode)
            ? cells.Count <= 64 && cells.All(IsDiscoveredConstructionCell)
            : _buildMode == BuildMode.StorageArea
            ? IsStorageAreaPreviewValid(cells)
            : _buildMode is BuildMode.WoodenRamp or BuildMode.StoneRamp
                ? cells.Count == 1 && IsDiscoveredConstructionCell(cells[0]) &&
                    TryInferDiscoveredRamp(cells[0], out _)
            : _buildMode is BuildMode.Walkway or BuildMode.BasaltWalkway or
                BuildMode.WoodenWall or BuildMode.StoneWall or
                BuildMode.WoodenFloor or BuildMode.StoneFloor
                ? IsLinearConstructionPlacementValid(cells)
                : cells.All(IsDiscoveredConstructionCell));

    private static bool IsSmallStorageBuildMode(BuildMode mode) => mode is
        BuildMode.FoodStorage or BuildMode.WoodStorage or BuildMode.StoneStorage or
        BuildMode.EquipmentStorage or BuildMode.MaterialsStorage or BuildMode.WaterBarrel or
        BuildMode.WoodenBox or BuildMode.WoodenChest or BuildMode.WoodenBulkBin;

    private bool CreateSelectedStorage(GridPosition cell)
    {
        var resource = _buildMode switch
        {
            BuildMode.FoodStorage => ResourceKind.Food,
            BuildMode.WoodStorage => ResourceKind.Wood,
            BuildMode.StoneStorage => ResourceKind.Stone,
            BuildMode.EquipmentStorage => ResourceKind.Equipment,
            BuildMode.MaterialsStorage => ResourceKind.Materials,
            BuildMode.WaterBarrel => ResourceKind.Water,
            BuildMode.WoodenBox or BuildMode.WoodenChest or BuildMode.WoodenBulkBin =>
                ResourceKind.Any,
            _ => throw new InvalidOperationException(),
        };
        return CreateStorage(
            cell,
            resource,
            _buildMode switch
            {
                BuildMode.WaterBarrel => StorageProviderKind.WaterBarrel,
                BuildMode.WoodenBox => StorageProviderKind.WoodenBox,
                BuildMode.WoodenChest => StorageProviderKind.WoodenChest,
                BuildMode.WoodenBulkBin => StorageProviderKind.WoodenBulkBin,
                _ => StorageProviderKind.OpenPile,
            });
    }

    private string DescribeRampDirection(GridPosition lower, GridPosition upper) =>
        (upper.X - lower.X, upper.Y - lower.Y) switch
        {
            (0, -1) => Ui("directions", "north"),
            (1, 0) => Ui("directions", "east"),
            (0, 1) => Ui("directions", "south"),
            (-1, 0) => Ui("directions", "west"),
            _ => Ui("directions", "unknown"),
        };

    private bool TryInferDiscoveredRamp(GridPosition lower, out GridPosition upper)
    {
        var candidates = new[]
        {
            lower with { Y = lower.Y - 1, Z = lower.Z + 1 },
            lower with { X = lower.X + 1, Z = lower.Z + 1 },
            lower with { Y = lower.Y + 1, Z = lower.Z + 1 },
            lower with { X = lower.X - 1, Z = lower.Z + 1 },
        };
        foreach (var candidate in candidates.OrderBy(candidate =>
                     _engine.World.IsTerrainTraversable(lower with
                     {
                         X = lower.X - (candidate.X - lower.X),
                         Y = lower.Y - (candidate.Y - lower.Y),
                     })
                         ? 0
                         : 1))
        {
            if (IsBuildableLayerCell(candidate) &&
                IsDiscoveredConstructionCell(candidate) &&
                _engine.World.CanBuildRamp(lower, candidate))
            {
                upper = candidate;
                return true;
            }
        }

        upper = default;
        return false;
    }

    private bool IsStorageAreaPreviewValid(IReadOnlyList<GridPosition> cells)
    {
        if (cells.Count > 256 || !cells.All(IsDiscoveredConstructionCell) ||
            cells.Any(cell => _latestSnapshot.StorageAreas.Any(area =>
                area.Id != _resizingStorageAreaId && area.Footprint.Contains(cell))))
        {
            return false;
        }

        if (_resizingStorageAreaId == EntityId.None)
        {
            return true;
        }

        var resizedArea = _latestSnapshot.StorageAreas.FirstOrDefault(area =>
            area.Id == _resizingStorageAreaId);
        return resizedArea.Id == _resizingStorageAreaId &&
            _latestSnapshot.StorageZones
                .Where(zone => zone.StorageAreaId == resizedArea.Id)
                .All(zone => cells.Contains(zone.Position));
    }

    private bool IsLinearConstructionPlacementValid(IReadOnlyList<GridPosition> cells)
    {
        if (_buildMode is BuildMode.WoodenFloor or BuildMode.StoneFloor)
        {
            return cells.Count <= 256 &&
                cells.All(IsDiscoveredConstructionCell) &&
                _engine.World.CanBuildFloors(cells);
        }

        if (_buildMode is not (BuildMode.Walkway or BuildMode.BasaltWalkway))
        {
            return cells.All(IsDiscoveredConstructionCell);
        }

        var canBuild = _buildMode == BuildMode.BasaltWalkway
            ? _engine.World.CanBuildBasaltWalkway(cells)
            : _engine.World.CanBuildWalkway(cells);
        return canBuild &&
            IsKnownWalkwayEndpoint(cells[0]) &&
            IsKnownWalkwayEndpoint(cells[^1]);
    }

    private IReadOnlyList<GridPosition> GetPlaceableFloorCells(
        IReadOnlyList<GridPosition> cells)
    {
        var reserved = _latestSnapshot.ConstructionSites
            .SelectMany(site => site.Footprint)
            .Concat(_latestSnapshot.StorageZones.Select(zone => zone.Position))
            .ToHashSet();
        return cells
            .Where(cell =>
                IsDiscoveredConstructionCell(cell) &&
                !reserved.Contains(cell) &&
                _engine.World.CanPlanFloorConstruction([cell]))
            .ToArray();
    }

    private bool IsKnownWalkwayEndpoint(GridPosition position) =>
        IsDiscoveredConstructionCell(position) ||
        new[]
        {
            position with { Y = position.Y - 1 },
            position with { X = position.X + 1 },
            position with { Y = position.Y + 1 },
            position with { X = position.X - 1 },
        }.Any(IsDiscoveredConstructionCell);

    private bool ContainsLava(IEnumerable<GridPosition> positions) =>
        positions.Any(position =>
            _engine.World.TryGetFluid(position, out var fluid, out _) &&
            fluid == CellFluidKind.Lava);

    private bool IsDiscoveredConstructionCell(GridPosition position) =>
        _engine.Visibility.TryGet(position, out var visibility) && visibility.IsDiscovered();

    private void BeginWorkArea(Vector2 screenPosition)
    {
        var cell = ClampToCurrentMapLevel(ScreenToVisibleCell(screenPosition));
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
        var cell = ClampToCurrentMapLevel(ScreenToVisibleCell(screenPosition));
        _worldView.SetWorkAreaPreview(
            _isDraggingWorkArea ? _workAreaStart : null,
            _isDraggingWorkArea ? cell : null);
        if (!IsBuildableLayerCell(cell))
        {
            _worldView.SetWorkPreview(default, []);
            return;
        }

        var area = _isDraggingWorkArea
            ? GetAreaCells(_workAreaStart, cell)
            : new[] { cell };
        var designationKind = ToDesignationKind(_workMode);
        var behavior = _workMode == WorkMode.Clear
            ? WorkAreaSelectionBehavior.ApplyToApplicableCells
            : WorkToolCatalog.GetSelectionBehavior(designationKind);
        var candidates = behavior == WorkAreaSelectionBehavior.SingleApplicableCell
            ? new[] { _isDraggingWorkArea ? _workAreaStart : cell }
            : area;
        var cells = _workMode == WorkMode.Clear
            ? candidates
            : _engine.QueryWorkDesignationTargets(
                designationKind,
                candidates[0],
                candidates[^1]);
        _worldView.SetWorkPreview(designationKind, cells);
        if (_isDraggingWorkArea)
        {
            _inspector.Text = _workMode switch
            {
                WorkMode.MineRock => UiFormat("work-feedback", "tunnel-preview", cells.Count),
                WorkMode.CarveRampDown or WorkMode.CarveRampUp
                    when cells.Count == 0 && _engine.World.TryGetRampDestinationFluid(
                        candidates[0],
                        _workMode == WorkMode.CarveRampDown,
                        out var fluid) =>
                    UiFormat("work-feedback", "ramp-fluid", DescribeFluid(fluid)),
                WorkMode.CarveRampDown or WorkMode.CarveRampUp => cells.Count == 1
                    ? Ui("work-feedback", "ramp-valid")
                    : Ui("work-feedback", "ramp-invalid"),
                _ when behavior == WorkAreaSelectionBehavior.FilterTargets =>
                    UiFormat("work-feedback", "matching-targets", cells.Count),
                _ => UiFormat("work-feedback", "matching-cells", cells.Count),
            };
        }
    }

    private void FinishWorkArea(Vector2 screenPosition)
    {
        var end = ClampToCurrentMapLevel(ScreenToVisibleCell(screenPosition));
        _isDraggingWorkArea = false;
        _worldView.SetWorkAreaPreview(null, null);
        if (!IsBuildableLayerCell(end) || !IsValidWorkAreaSelection(_workAreaStart, end))
        {
            _worldView.SetWorkPreview(default, []);
            _inspector.Text = _workAreaStart.Z != end.Z
                ? Ui("work-feedback", "single-level")
                : Ui("work-feedback", "outside-map");
            return;
        }

        var executeAt = _engine.CurrentTick.Next();
        var designationKind = ToDesignationKind(_workMode);
        var command = TerrainModificationCatalog.TryGet(designationKind, out var terrain)
            ? TerrainModificationCommandFactory.CreateDesignation(
                terrain,
                executeAt,
                _commandSequence++,
                _workAreaStart,
                end)
            : _workMode switch
        {
            WorkMode.GatherFood => SimulationCommand.DesignateWork(
                executeAt,
                _commandSequence++,
                _workAreaStart,
                end,
                ResourceKind.Food),
            WorkMode.GatherReeds => SimulationCommand.DesignateWork(
                executeAt,
                _commandSequence++,
                _workAreaStart,
                end,
                ResourceKind.Reeds),
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
            WorkMode.HuntAnimals => SimulationCommand.DesignateAnimalHunting(
                executeAt,
                _commandSequence++,
                _workAreaStart,
                end),
            WorkMode.Scout => SimulationCommand.DesignateScouting(
                executeAt,
                _commandSequence++,
                _workAreaStart,
                end),
            WorkMode.CleanBlood => SimulationCommand.DesignateBloodCleaning(
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

        if (_replacingWorkOrderId != EntityId.None &&
            _replacementWorkPriority is { } replacementPriority)
        {
            command = command.ReplacingWorkOrder(
                _replacingWorkOrderId,
                replacementPriority,
                _replacementWorkSuspended);
        }
        try
        {
            SubmitCommand(command);
        }
        catch (ArgumentException exception)
        {
            GD.PushWarning(
                $"Odrzucono zlecenie {_workMode} z {_workAreaStart} do {end}: " +
                exception.Message);
            _worldView.SetWorkPreview(default, []);
            _inspector.Text = Ui("work-feedback", "create-failed");
            return;
        }
        _inspector.Text = _workMode switch
        {
            WorkMode.Clear => Ui("work-feedback", "clear-ordered"),
            WorkMode.MineRock =>
                Ui("work-feedback", "tunnel-ordered"),
            WorkMode.CarveRampDown => Ui("work-feedback", "ramp-down-ordered"),
            WorkMode.CarveRampUp => Ui("work-feedback", "ramp-up-ordered"),
            WorkMode.CleanBlood => Ui("work-feedback", "clean-blood-ordered"),
            _ => _speed == 0
                ? Ui("work-feedback", "targets-ordered-paused")
                : Ui("work-feedback", "targets-ordered-running"),
        };
        _replacingWorkOrderId = EntityId.None;
        _replacementWorkPriority = null;
        _replacementWorkSuspended = false;
        UpdateWorkPreview(screenPosition);
    }

    private void SubmitCommand(SimulationCommand command)
    {
        if (_speed > 0)
        {
            _engine.QueueCommand(command);
            return;
        }

        _engine.ApplyCommandImmediately(command);
        var events = _engine.DrainEvents();
        var snapshot = _engine.CreatePresentationSnapshot();
        _latestSnapshot = snapshot;
        HandleEvents(events, snapshot);
        if (_use3DView)
        {
            _worldView3D.Refresh(snapshot);
        }
        else
        {
            _worldView.Refresh(snapshot);
        }
        _minimap.Refresh(snapshot);
        UpdateStatus(snapshot);
    }

    private void CancelWorkMode(bool clearInspector = true)
    {
        var wasActive = _workMode != WorkMode.None;
        _workMode = WorkMode.None;
        _replacingWorkOrderId = EntityId.None;
        _replacementWorkPriority = null;
        _replacementWorkSuspended = false;
        _isDraggingWorkArea = false;
        _worldView.SetWorkAreaPreview(null, null);
        _worldView.SetWorkPreview(default, []);
        UpdateActiveToolCursor();
        if (clearInspector && wasActive)
        {
            _inspector.Text = Ui("work-feedback", "tool-cancelled");
        }
    }

    private void CancelActiveTool()
    {
        var hadActiveTool = _buildMode != BuildMode.None || _workMode != WorkMode.None ||
            _unitOrderMode != UnitOrderMode.None || _isRaidTargetMode;
        CancelBuildMode(clearInspector: false);
        CancelWorkMode(clearInspector: false);
        _unitOrderMode = UnitOrderMode.None;
        _patrolDraftPoints.Clear();
        _isRaidTargetMode = false;
        _worldView.SetRaidTargetPreview(null, 0);
        if (hadActiveTool)
        {
            _inspector.Text = Ui("work-feedback", "active-tool-cancelled");
        }
    }

    private void UpdateActiveToolCursor() => Input.SetDefaultCursorShape(
        _buildMode != BuildMode.None || _workMode != WorkMode.None
            ? Input.CursorShape.Cross
            : Input.CursorShape.Arrow);

    private void BeginRaidTargetSelection(SimulationSnapshot snapshot)
    {
        CancelActiveTool();
        _isRaidTargetMode = true;
        _raidTargetRadius = snapshot.RaidPlan.TargetRadius;
        _visibleLevel = snapshot.RaidPlan.Target.Z;
        _worldView.SetVisibleLevel(_visibleLevel);
        _minimap.SetVisibleLevel(_visibleLevel);
        _worldView.SetRaidTargetPreview(snapshot.RaidPlan.Target, _raidTargetRadius);
        _inspector.Text = $"Wskaż centrum najazdu • promień {_raidTargetRadius} • " +
            "kółko myszy zmienia promień (3–10).";
    }

    private void UpdateRaidTargetPreview(Vector2 screenPosition)
    {
        var cell = ScreenToVisibleCell(screenPosition);
        _worldView.SetRaidTargetPreview(cell, _raidTargetRadius);
    }

    private void ChangeRaidTargetRadius(int delta)
    {
        _raidTargetRadius = Math.Clamp(
            _raidTargetRadius + delta,
            SimulationEngine.MinimumRaidTargetRadius,
            SimulationEngine.MaximumRaidTargetRadius);
        _worldView.SetRaidTargetPreview(ScreenToVisibleCell(GetViewport().GetMousePosition()),
            _raidTargetRadius);
        _inspector.Text = $"Promień obszaru najazdu: {_raidTargetRadius}.";
    }

    private void FinishRaidTargetSelection(Vector2 screenPosition)
    {
        var target = ScreenToVisibleCell(screenPosition);
        if (!_engine.Visibility.Get(target).IsDiscovered())
        {
            _inspector.Text = "Cel najazdu musi leżeć na wcześniej rozpoznanym terenie.";
            return;
        }

        _engine.QueueCommand(SimulationCommand.ConfigureRaidTarget(
            _engine.CurrentTick.Next(),
            _commandSequence++,
            target,
            _raidTargetRadius));
        _isRaidTargetMode = false;
        _worldView.SetRaidTargetPreview(null, 0);
        _inspector.Text = $"Cel najazdu: {target}, promień {_raidTargetRadius}.";
    }

    private static WorkDesignationKind ToDesignationKind(WorkMode mode) => mode switch
    {
        WorkMode.GatherFood => WorkDesignationKind.GatherFood,
        WorkMode.GatherReeds => WorkDesignationKind.GatherReeds,
        WorkMode.GatherBrushwood => WorkDesignationKind.GatherBrushwood,
        WorkMode.GatherStone => WorkDesignationKind.GatherStone,
        WorkMode.UprootBerryBushes => WorkDesignationKind.UprootBerryBush,
        WorkMode.FellTrees => WorkDesignationKind.FellTree,
        WorkMode.QuarryBoulders => WorkDesignationKind.QuarryBoulder,
        WorkMode.MineRock => WorkDesignationKind.MineRock,
        WorkMode.CarveRampDown => WorkDesignationKind.CarveRampDown,
        WorkMode.CarveRampUp => WorkDesignationKind.CarveRampUp,
        WorkMode.HuntAnimals => WorkDesignationKind.HuntAnimal,
        WorkMode.Scout => WorkDesignationKind.Scout,
        WorkMode.CleanBlood => WorkDesignationKind.CleanBlood,
        _ => default,
    };

    private static WorkMode ToWorkMode(WorkDesignationKind kind) => kind switch
    {
        WorkDesignationKind.GatherFood => WorkMode.GatherFood,
        WorkDesignationKind.GatherReeds => WorkMode.GatherReeds,
        WorkDesignationKind.GatherBrushwood => WorkMode.GatherBrushwood,
        WorkDesignationKind.GatherStone => WorkMode.GatherStone,
        WorkDesignationKind.UprootBerryBush => WorkMode.UprootBerryBushes,
        WorkDesignationKind.FellTree => WorkMode.FellTrees,
        WorkDesignationKind.QuarryBoulder => WorkMode.QuarryBoulders,
        WorkDesignationKind.MineRock => WorkMode.MineRock,
        WorkDesignationKind.CarveRampDown => WorkMode.CarveRampDown,
        WorkDesignationKind.CarveRampUp => WorkMode.CarveRampUp,
        WorkDesignationKind.HuntAnimal => WorkMode.HuntAnimals,
        WorkDesignationKind.Scout => WorkMode.Scout,
        WorkDesignationKind.CleanBlood => WorkMode.CleanBlood,
        _ => WorkMode.None,
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

    private bool CreateStorage(
        GridPosition cell,
        ResourceKind resource,
        StorageProviderKind providerKind = StorageProviderKind.OpenPile)
    {
        var terrainAvailable = cell.Z == 0
            ? _engine.World.IsSurfaceTraversable(cell)
            : _engine.World.IsTerrainTraversable(cell);
        var discovered = _engine.Visibility.Get(cell).IsDiscovered();
        if (!discovered)
        {
            _inspector.Text = UiFormat("construction-feedback", "storage-undiscovered", cell);
            return false;
        }
        if (cell.Z < 0 && _engine.World.IsSolidCaveRock(cell))
        {
            _inspector.Text = UiFormat("construction-feedback", "storage-cave-wall", cell);
            return false;
        }
        if (!terrainAvailable)
        {
            _inspector.Text = UiFormat("construction-feedback", "storage-blocked", cell);
            return false;
        }

        var command = providerKind switch
        {
            StorageProviderKind.WaterBarrel => SimulationCommand.PlaceWaterBarrel(
                _engine.CurrentTick.Next(), _commandSequence++, cell),
            StorageProviderKind.WoodenBox => SimulationCommand.PlaceWoodenBox(
                _engine.CurrentTick.Next(), _commandSequence++, cell),
            StorageProviderKind.WoodenChest => SimulationCommand.PlaceWoodenChest(
                _engine.CurrentTick.Next(), _commandSequence++, cell),
            StorageProviderKind.WoodenBulkBin => SimulationCommand.PlaceWoodenBulkBin(
                _engine.CurrentTick.Next(), _commandSequence++, cell),
            _ => resource switch
            {
                ResourceKind.Food => SimulationCommand.BuildFoodStorage(
                    _engine.CurrentTick.Next(), _commandSequence++, cell),
                ResourceKind.Wood => SimulationCommand.BuildWoodStorage(
                    _engine.CurrentTick.Next(), _commandSequence++, cell),
                ResourceKind.Stone => SimulationCommand.BuildStoneStorage(
                    _engine.CurrentTick.Next(), _commandSequence++, cell),
                ResourceKind.Equipment => SimulationCommand.BuildEquipmentStorage(
                    _engine.CurrentTick.Next(), _commandSequence++, cell),
                ResourceKind.Materials => SimulationCommand.BuildMaterialsStorage(
                    _engine.CurrentTick.Next(), _commandSequence++, cell),
                _ => throw new ArgumentOutOfRangeException(nameof(resource)),
            },
        };
        _engine.QueueCommand(command);
        var capacity = resource switch
        {
            ResourceKind.Food => _engine.Definitions.Storage.SmallFoodCapacity,
            ResourceKind.Equipment => 32,
            ResourceKind.Materials => 64,
            ResourceKind.Water => 32,
            ResourceKind.Any when providerKind == StorageProviderKind.WoodenBox => 32,
            ResourceKind.Any when providerKind is StorageProviderKind.WoodenChest or
                StorageProviderKind.WoodenBulkBin => 64,
            _ => 64,
        };
        _inspector.Text = providerKind == StorageProviderKind.OpenPile
            ? UiFormat("construction-feedback", "storage-ordered", cell,
                Ui("storage-resources", resource.ToString()), capacity)
            : UiFormat("construction-feedback", "container-ordered", cell,
                Ui("storage-providers", providerKind.ToString()), capacity);
        return true;
    }

    private void HandleEvents(
        IReadOnlyList<SimulationEvent> events,
        SimulationSnapshot snapshot)
    {
        if (!_use3DView)
        {
            _worldView.ShowCombatEvents(events, snapshot);
        }

        var workEvent = events.LastOrDefault(item =>
            item.Kind is SimulationEventKind.WorkDesignationCreated or
                SimulationEventKind.WorkDesignationRemoved or
                SimulationEventKind.MiningHazardDiscovered or
                SimulationEventKind.StoragePullConfigured or
                SimulationEventKind.StorageHaulerConfigured or
                SimulationEventKind.StorageSourceConfigured or
                SimulationEventKind.StoragePriorityConfigured or
                SimulationEventKind.StorageMineralFilterConfigured or
                SimulationEventKind.ResourcePriorityConfigured);
        if (workEvent.Kind == SimulationEventKind.WorkDesignationCreated)
        {
            _inspector.Text = Ui(
                "work-created",
                ((WorkDesignationKind)workEvent.Amount).ToString());
        }
        else if (workEvent.Kind == SimulationEventKind.WorkDesignationRemoved)
        {
            _inspector.Text = Ui("events", "work-removed");
        }
        else if (workEvent.Kind == SimulationEventKind.MiningHazardDiscovered)
        {
            _inspector.Text = UiFormat(
                "events", "mining-hazard", DescribeFluid((CellFluidKind)workEvent.Amount));
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
            var configured = snapshot.StorageZones
                .FirstOrDefault(zone => zone.Id == configuredId);
            if (configured.Id != EntityId.None && configured.Id == _selectedStorageId)
            {
                _storageSettingsDirty = false;
                UpdateStorageDetails(configured, snapshot);
            }
        }

        if (_selectedStorageId != EntityId.None &&
            events.Any(item => item.Kind is
                SimulationEventKind.ItemPickedUp or SimulationEventKind.ItemStored or
                SimulationEventKind.ItemDropped))
        {
            var selectedStorage = snapshot.StorageZones
                .FirstOrDefault(zone => zone.Id == _selectedStorageId);
            if (selectedStorage.Id != EntityId.None)
            {
                UpdateStorageDetails(selectedStorage, snapshot);
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
            _inspector.Text = Ui("events", "construction-rejected");
        }
        else if (constructionEvent.Kind is SimulationEventKind.ConstructionOrdered or
                 SimulationEventKind.ConstructionMaterialDelivered or
                 SimulationEventKind.ConstructionPriorityConfigured)
        {
            var site = snapshot.ConstructionSites
                .FirstOrDefault(item => item.Id == constructionEvent.Target);
            if (site is not null)
            {
                _inspector.Text = DescribeConstructionSite(site);
            }
        }
        else if (constructionEvent.Kind == SimulationEventKind.ConstructionCompleted)
        {
            var zone = snapshot.StorageZones
                .FirstOrDefault(item => item.Id == constructionEvent.Target);
            var materialKey = constructionEvent.Construction switch
            {
                ConstructionKind.BasaltWalkway => "basalt",
                ConstructionKind.StoneWall or ConstructionKind.StoneFloor or
                    ConstructionKind.StoneRamp or
                    ConstructionKind.StoneDoorFrame => "stone",
                ConstructionKind.WaterBarrel => "finished-barrel",
                _ => "wood",
            };
            _inspector.Text = constructionEvent.Construction is not null
                ? UiFormat("events", "construction-completed",
                    Ui("construction-names", constructionEvent.Construction.Value.ToString()),
                    constructionEvent.Amount,
                    Ui("construction-materials", materialKey))
                : zone.Id != EntityId.None
                    ? UiFormat("events", "storage-completed",
                        Ui("storage-resources", zone.AcceptedResource.ToString()),
                        constructionEvent.Amount,
                        Ui("construction-materials", materialKey))
                    : Ui("events", "construction-completed-generic");
        }

        if (_selectedConstructionId != EntityId.None &&
            constructionEvent.Kind is SimulationEventKind.ConstructionMaterialDelivered or
                SimulationEventKind.ConstructionPriorityConfigured or
                SimulationEventKind.ConstructionCompleted)
        {
            var selectedConstruction = snapshot.ConstructionSites
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

        var craftingEvent = events.LastOrDefault(item =>
            item.Kind is SimulationEventKind.CraftingOrdered or
                SimulationEventKind.CraftingMaterialDelivered or
                SimulationEventKind.CraftingCompleted ||
            item.Kind == SimulationEventKind.CommandRejected &&
                item.Amount == (int)SimulationCommandKind.QueueCraftingOrder);
        if (craftingEvent.Kind == SimulationEventKind.CraftingOrdered)
        {
            _inspector.Text = UiFormat(
                "events", "crafting-ordered",
                DescribeCraftingRecipe((CraftingRecipeKind)craftingEvent.Amount));
        }
        else if (craftingEvent.Kind == SimulationEventKind.CraftingMaterialDelivered)
        {
            _inspector.Text = Ui("events", "crafting-material-delivered");
        }
        else if (craftingEvent.Kind == SimulationEventKind.CraftingCompleted)
        {
            _inspector.Text = UiFormat(
                "events", "crafting-completed",
                DescribeCraftingRecipe((CraftingRecipeKind)craftingEvent.Amount));
        }
        else if (craftingEvent.Kind == SimulationEventKind.CommandRejected)
        {
            _inspector.Text = Ui("events", "crafting-rejected");
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
            _inspector.Text = UiFormat(
                "events", "guard-hit", selectedHit.Target, selectedHit.Amount);
        }
        else if (selectedEvent.Kind == SimulationEventKind.CommandRejected &&
            selectedEvent.Amount == (int)SimulationCommandKind.Move)
        {
            _inspector.Text = UiFormat(
                "events", "move-rejected", selectedEvent.Subject);
        }
        else if (selectedEvent.Kind == SimulationEventKind.MoveCompleted)
        {
            _inspector.Text = UiFormat(
                "events", "move-completed", selectedEvent.Subject);
        }

        var raidEvent = events.LastOrDefault(item => item.Kind is
            SimulationEventKind.RaidVictory or SimulationEventKind.RaidDefeated);
        if (raidEvent.Kind == SimulationEventKind.RaidVictory)
        {
            _inspector.Text = UiFormat("events", "raid-victory", raidEvent.Amount);
        }
        else if (raidEvent.Kind == SimulationEventKind.RaidDefeated)
        {
            _inspector.Text = Ui("events", "raid-defeated");
        }

        var contextEvent = events.LastOrDefault(item =>
            item.Kind is SimulationEventKind.ActorOrderedToRest or
                SimulationEventKind.ActorDispatcherSuspended or
                SimulationEventKind.ActorEquippedItem or
                SimulationEventKind.ItemHaulPrioritized ||
            item.Kind == SimulationEventKind.CommandRejected &&
            (SimulationCommandKind)item.Amount is SimulationCommandKind.OrderActorFlee or
                SimulationCommandKind.OrderActorSleep or
                SimulationCommandKind.SuspendActorDispatcher or
                SimulationCommandKind.EquipItem or
                SimulationCommandKind.PrioritizeItemHauling or
                SimulationCommandKind.OrderItemPickup);
        if (contextEvent.Kind == SimulationEventKind.CommandRejected)
        {
            _inspector.Text = (SimulationCommandKind)contextEvent.Amount switch
            {
                SimulationCommandKind.OrderActorFlee =>
                    Ui("events", "flee-rejected"),
                SimulationCommandKind.OrderActorSleep =>
                    Ui("events", "sleep-rejected"),
                SimulationCommandKind.SuspendActorDispatcher =>
                    Ui("events", "dispatcher-suspend-rejected"),
                SimulationCommandKind.EquipItem =>
                    Ui("events", "equip-rejected"),
                SimulationCommandKind.PrioritizeItemHauling =>
                    Ui("events", "haul-priority-rejected"),
                _ => Ui("events", "pickup-rejected"),
            };
        }
        else if (contextEvent.Kind == SimulationEventKind.ActorOrderedToRest)
        {
            _inspector.Text = Ui("events", "actor-resting");
        }
        else if (contextEvent.Kind == SimulationEventKind.ActorDispatcherSuspended)
        {
            _inspector.Text = Ui("events", "dispatcher-suspended");
        }
        else if (contextEvent.Kind == SimulationEventKind.ActorEquippedItem)
        {
            _inspector.Text = Ui("events", "item-equipped");
        }
        else if (contextEvent.Kind == SimulationEventKind.ItemHaulPrioritized)
        {
            _inspector.Text = Ui("events", "haul-prioritized");
        }
    }

    private void InspectWorld(
        Vector2 screenPosition,
        bool extendActorSelection,
        bool showActorDetails)
    {
        var cell = ScreenToCell(screenPosition);
        if (!_engine.Map.IsWithin(cell))
        {
            return;
        }

        var snapshot = GetDisplayedSnapshot();
        if (_visibleLevel != 0)
        {
            var levelPosition = cell with { Z = _visibleLevel };
            var displayedWorldObjects = DescribeDisplayedWorldObjectsAt(snapshot, levelPosition);
            if (_visibleLevel < 0 && _engine.Map.IsCavePosition(levelPosition))
            {
                if (!snapshot.GetVisibility(levelPosition, _engine.Map.Width).IsDiscovered())
                {
                    SelectActor(EntityId.None);
                    _inspector.Text = UiFormat("inspection", "unknown-terrain", levelPosition);
                    return;
                }

                var actor = snapshot.Actors.FirstOrDefault(item => item.Position == levelPosition);
                var zone = snapshot.StorageZones.FirstOrDefault(item => item.Position == levelPosition);
                var construction = snapshot.ConstructionSites.FirstOrDefault(item =>
                    item.Footprint.Contains(levelPosition));
                var undergroundAnimals = snapshot.GetVisibility(levelPosition, _engine.Map.Width) ==
                        CellVisibility.Visible
                    ? snapshot.Animals.Where(item => item.Position == levelPosition).ToArray()
                    : [];
                var undergroundCorpses = snapshot.Corpses
                    .Where(item => item.Position == levelPosition).ToArray();
                var undergroundGroundStacks = snapshot.ItemStacks.Where(stack =>
                    stack.Location.Kind == ItemLocationKind.Ground &&
                    stack.Location.Position == levelPosition).ToArray();
                if (actor.Id != EntityId.None)
                {
                    SelectOrToggleActor(actor.Id, extendActorSelection, showActorDetails);
                    _inspector.Text = DescribeActorSelection(actor, levelPosition);
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
                if (_engine.World.TryGetWorkshopKind(levelPosition, out _))
                {
                    SelectActor(EntityId.None);
                    ShowWorkshopDetails(levelPosition);
                    return;
                }

                var caveCell = _engine.Map.GetCaveCell(levelPosition);
                var passages = _engine.World.CreateVerticalPassageSnapshot()
                    .Where(passage => passage.Upper == levelPosition || passage.Lower == levelPosition)
                    .Select(passage => Ui("passages", passage.Kind == VerticalPassageKind.CaveMouth
                        ? "cave-mouth"
                        : "ramp-between-levels"))
                    .ToArray();
                SelectActor(EntityId.None);
                var excavated = _engine.World.ExcavatedCaveCells.Contains(levelPosition);
                var excavatedTerrainRamp =
                    _engine.World.ExcavatedTerrainRamps.Contains(levelPosition);
                var caveKind = excavatedTerrainRamp
                    ? Ui("cave-kinds", "excavated-ramp")
                    : excavated
                        ? Ui("cave-kinds", "excavated-corridor")
                        : DescribeCaveKind(caveCell.Kind);
                _inspector.Text = $"{levelPosition} • {DescribeCaveRock(caveCell.Rock)} • " +
                    caveKind + DescribeCaveFluid(caveCell.Fluid) +
                    (excavated ? string.Empty : DescribeMineralDeposit(caveCell.Deposit)) +
                    (caveCell.Kind == CaveCellKind.SolidRock
                        ? DescribeMiningRequirement(caveCell.Rock)
                        : string.Empty) +
                    (passages.Length == 0 ? string.Empty : $" • {string.Join(", ", passages)}") +
                    (displayedWorldObjects.Length == 0
                        ? string.Empty
                        : $" • {string.Join(", ", displayedWorldObjects)}") +
                    (undergroundAnimals.Length == 0
                        ? string.Empty
                        : UiFormat("inspection", "animals",
                            string.Join(", ", undergroundAnimals.Select(DescribeAnimal)))) +
                    (undergroundCorpses.Length == 0
                        ? string.Empty
                        : UiFormat("inspection", "corpses",
                            string.Join(", ", undergroundCorpses.Select(DescribeCorpse)))) +
                    (undergroundGroundStacks.Length == 0
                        ? string.Empty
                        : UiFormat("inspection", "on-ground",
                            string.Join(", ", undergroundGroundStacks.Select(DescribeStack))));
                return;
            }

            var mapCell = _engine.Map.GetCell(cell);
            SelectActor(EntityId.None);
            _inspector.Text = UiFormat("inspection", "level", levelPosition, _visibleLevel) +
                Ui("inspection", mapCell.FloorLevel == _visibleLevel
                    ? "terrain-floor"
                    : "empty-space") +
                (displayedWorldObjects.Length == 0
                    ? string.Empty
                    : $" • {string.Join(", ", displayedWorldObjects)}");
            return;
        }

        var visibility = snapshot.GetVisibility(cell, _engine.Map.Width);
        if (!visibility.IsDiscovered())
        {
            SelectActor(EntityId.None);
            _inspector.Text = UiFormat("inspection", "unknown-terrain", cell);
            return;
        }

        var terrain = _engine.Map.GetCell(cell);
        var plant = _engine.World.GetPlantPatch(cell);
        var objects = _engine.World.GetWorldObjectsAt(cell);
        var actors = snapshot.Actors.Where(actor => actor.Position == cell).ToArray();
        var buds = snapshot.GoblinBuds.Where(bud => bud.Position == cell).ToArray();
        var animals = snapshot.Animals.Where(animal => animal.Position == cell).ToArray();
        var corpses = snapshot.Corpses.Where(corpse => corpse.Position == cell).ToArray();
        var villageLoot = snapshot.VillageLootContainers.Where(container =>
            container.Position == cell).ToArray();
        var humanVillagers = snapshot.HumanVillage.Villagers
            .Where(villager => villager.Health > 0 && villager.Position == cell)
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
        var craftingOrders = snapshot.CraftingOrders
            .Where(order => order.Workshop == cell)
            .ToArray();
        if (objects.Any(item => item.Kind == WorldObjectKind.WoodenDoorLeaf) &&
            _engine.World.TryGetWoodenDoorState(cell, out var isDoorOpen))
        {
            _engine.QueueCommand(SimulationCommand.ToggleWoodenDoor(
                _engine.CurrentTick.Next(), _commandSequence++, cell));
            SelectActor(EntityId.None);
            _inspector.Text = UiFormat("inspection",
                    isDoorOpen ? "door-close-command" : "door-open-command", cell) +
                (_speed == 0 ? Ui("inspection", "after-resume") : string.Empty);
            return;
        }
        var clickedActor = actors.OrderBy(actor => actor.Id).FirstOrDefault();
        if (clickedActor.Id != EntityId.None)
        {
            SelectOrToggleActor(clickedActor.Id, extendActorSelection, showActorDetails);
        }
        else
        {
            SelectActor(EntityId.None);
        }
        if (actors.Length == 0 && zones.Length > 0)
        {
            ShowStorageDetails(zones[0]);
        }
        else if (actors.Length == 0 && constructionSites.Length > 0)
        {
            ShowConstructionDetails(constructionSites[0]);
        }
        else if (actors.Length == 0 && _engine.World.TryGetWorkshopKind(cell, out _))
        {
            ShowWorkshopDetails(cell);
        }

        _inspector.Text = UiFormat("inspection", "surface", cell,
                Ui("terrain-kinds", terrain.Terrain.ToString()),
                terrain.Moisture,
                terrain.Fertility) +
            (visibility == CellVisibility.Explored ? Ui("inspection", "explored-hidden") : string.Empty) +
            DescribeWaterDepth(terrain) +
            (plant is null
                ? string.Empty
                : UiFormat("inspection", "food-source",
                    DescribeFoodSource(plant.Value.Kind),
                    plant.Value.Biomass,
                    plant.Value.Capacity)) +
            (objects.Count == 0
                ? string.Empty
                : $" • {string.Join(", ", objects.Select(DescribeWorldObject))}") +
            (objects.Any(item => item.Kind == WorldObjectKind.GoblinFieldCamp)
                ? Ui("inspection", "camp-menu")
                : string.Empty) +
            (humanVillagers.Length == 0
                ? string.Empty
                : $" • ludzie: {string.Join(", ", humanVillagers.Select(DescribeVillager))}") +
            (humanFields.Length == 0
                ? string.Empty
                : $" • pole: {string.Join(", ", humanFields.Select(field =>
                    DescribeHumanField(field, snapshot.HumanVillage)))}") +
            (!humanVillagers.Any(villager => villager.Role == HumanCohortRole.Guards)
                ? string.Empty
                : $" • alarm {snapshot.HumanVillage.Hostility}/100, siła straży " +
                  $"{snapshot.HumanVillage.GuardHitPoints}/{snapshot.HumanVillage.MaximumGuardHitPoints}") +
            (cell != snapshot.HumanVillage.Anchor &&
             !objects.Any(item => item.Owner == WorldObjectOwner.HumanVillage)
                ? string.Empty
                : $" • wieś: {snapshot.HumanVillage.Population} osób, żywność {snapshot.HumanVillage.FoodStock}, " +
                  $"ziarno siewne {snapshot.HumanVillage.GrainStock}, " +
                  $"woda {snapshot.HumanVillage.WaterStock}, drewno {snapshot.HumanVillage.WoodStock}, " +
                  $"pola {snapshot.HumanVillage.Fields.Count}/{snapshot.HumanVillage.PlannedFieldCount}, " +
                  $"spichlerze {snapshot.HumanVillage.StorehouseCount}") +
            (zones.Length == 0
                ? string.Empty
                : $" • skład: {string.Join(", ", zones.Select(zone => $"{DescribeResource(zone.AcceptedResource)} {zone.StoredQuantity}/{zone.Capacity}"))}") +
            (constructionSites.Length == 0
                ? string.Empty
                : $" • {string.Join(" • ", constructionSites.Select(DescribeConstructionSite))}") +
            (craftingOrders.Length == 0
                ? string.Empty
                : $" • warsztat: {string.Join(", ", craftingOrders.Select(DescribeCraftingOrder))}") +
            (buds.Length == 0
                ? string.Empty
                : $" • żywy pąk{(buds.Any(bud => bud.OriginCorpseId != EntityId.None) ? " ze zwłok" : string.Empty)}: opieka " +
                  $"{buds.Min(bud => bud.TotalCareTicks - bud.RemainingCareTicks)}/" +
                  $"{buds.Max(bud => bud.TotalCareTicks)}") +
            (animals.Length == 0
                ? string.Empty
                : $" • zwierzęta: {string.Join(", ", animals.Select(DescribeAnimal))}") +
            (corpses.Length == 0
                ? string.Empty
                : $" • zwłoki: {string.Join(", ", corpses.Select(DescribeCorpse))}") +
            (villageLoot.Length == 0
                ? string.Empty
                : $" • zapasy w budynku: {string.Join(", ", villageLoot.SelectMany(container =>
                    container.Contents).Select(item =>
                    $"{DescribeResourceVariant(item.Resource, item.FoodKind, item.Variant)} ×{item.Quantity}"))}") +
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

    private string[] DescribeDisplayedWorldObjectsAt(
        SimulationSnapshot snapshot,
        GridPosition position)
    {
        var descriptions = new List<string>();
        foreach (var worldObject in snapshot.WorldObjects)
        {
            var effectiveAnchor = _engine.World.GetEffectiveWorldObjectAnchor(worldObject);
            var surfaceOffset = effectiveAnchor.Z - worldObject.Anchor.Z;
            descriptions.AddRange(worldObject.GetAbsoluteParts()
                .Where(part => part.Position.X == position.X &&
                    part.Position.Y == position.Y &&
                    part.Position.Z + surfaceOffset == position.Z)
                .Select(part => DescribeWorldObjectPart(worldObject, part.Part.Kind)));

            if (worldObject.Kind is WorldObjectKind.Tree or WorldObjectKind.DeadTreeStump &&
                worldObject.Anchor.X == position.X &&
                worldObject.Anchor.Y == position.Y &&
                worldObject.Anchor.Z + surfaceOffset - 1 == position.Z)
            {
                descriptions.Add(UiFormat("world-objects", "roots",
                    DescribeTreeSpecies(GetWoodVariant(worldObject))));
            }
        }

        return descriptions.Distinct().ToArray();
    }

    private string DescribeWorldObject(WorldObjectSnapshot worldObject) => worldObject.Kind switch
    {
        WorldObjectKind.Tree => UiFormat("world-objects", "Tree",
            DescribeTreeSpecies(GetWoodVariant(worldObject))),
        WorldObjectKind.DeadTreeStump when worldObject.Parts.Any(part =>
            part.Kind == WorldObjectPartKind.FelledTreeRemains) =>
            UiFormat("world-objects", "FelledTree",
                DescribeTreeSpecies(GetWoodVariant(worldObject))),
        WorldObjectKind.DeadTreeStump =>
            UiFormat("world-objects", "DeadTreeStump",
                DescribeTreeSpecies(GetWoodVariant(worldObject))),
        WorldObjectKind.Boulder => Ui("world-objects", "Boulder"),
        WorldObjectKind.PrimitiveWorkshop =>
            Ui("world-objects", "PrimitiveWorkshop") + DescribeWorkshopMaterial(worldObject),
        WorldObjectKind.Bloomery =>
            Ui("world-objects", "Bloomery") + DescribeWorkshopMaterial(worldObject),
        WorldObjectKind.SmeltingFurnace =>
            Ui("world-objects", "SmeltingFurnace") + DescribeWorkshopMaterial(worldObject),
        WorldObjectKind.CrucibleFurnace =>
            Ui("world-objects", "CrucibleFurnace") + DescribeWorkshopMaterial(worldObject),
        WorldObjectKind.WoodenRamp => Ui("world-objects", "WoodenRamp"),
        WorldObjectKind.StoneRamp => Ui("world-objects", "StoneRamp"),
        _ => Ui("world-objects", worldObject.Kind.ToString()),
    };

    private string DescribeWorkshopMaterial(WorldObjectSnapshot workshop) =>
        workshop.MaterialVariant == ResourceVariant.None
            ? string.Empty
            : UiFormat("world-objects", "material",
                DescribeResourceVariant(workshop.MaterialVariant));

    private string DescribeWorldObjectPart(
        WorldObjectSnapshot worldObject,
        WorldObjectPartKind partKind)
    {
        if (worldObject.Kind is WorldObjectKind.Tree or WorldObjectKind.DeadTreeStump)
        {
            var species = DescribeTreeSpecies(GetWoodVariant(worldObject));
            return partKind switch
            {
                WorldObjectPartKind.TreeTrunk => UiFormat("world-object-parts", "TreeTrunk", species),
                WorldObjectPartKind.TreeCrown => UiFormat("world-object-parts", "TreeCrown", species),
                WorldObjectPartKind.TreeStump => UiFormat("world-object-parts", "TreeStump", species),
                WorldObjectPartKind.FelledTreeRemains =>
                    UiFormat("world-object-parts", "FelledTreeRemains", species),
                _ => Ui("world-object-parts", partKind.ToString()),
            };
        }

        return worldObject.Kind == WorldObjectKind.Boulder
            ? Ui("world-objects", "Boulder")
            : Ui("world-object-parts", partKind.ToString());
    }

    private ResourceVariant GetWoodVariant(WorldObjectSnapshot worldObject) =>
        WoodMaterialPolicy.VariantFor(
            _engine.WorldSeed,
            _engine.Map.Width,
            worldObject.Anchor);

    private string DescribeTreeSpecies(ResourceVariant variant) => variant switch
    {
        ResourceVariant.OakWood => Ui("tree-species", "OakWood"),
        ResourceVariant.ChestnutWood => Ui("tree-species", "ChestnutWood"),
        ResourceVariant.BirchWood => Ui("tree-species", "BirchWood"),
        ResourceVariant.WalnutWood => Ui("tree-species", "WalnutWood"),
        ResourceVariant.AppleWood => Ui("tree-species", "AppleWood"),
        ResourceVariant.PineWood => Ui("tree-species", "PineWood"),
        _ => Ui("tree-species", "unknown"),
    };

    private static string DescribeCraftingOrder(CraftingOrderSnapshot order)
    {
        var materials = string.Join(", ", order.Materials.Select(material =>
            $"{(material.Variant == ResourceVariant.None
                ? DescribeResource(material.Resource)
                : DescribeResourceVariant(
                    material.Resource,
                    FoodKind.None,
                    material.Variant))} " +
            $"{material.DeliveredQuantity}/{material.RequiredQuantity}"));
        return $"{(order.IsRepeating ? "∞ " : string.Empty)}" +
            $"{DescribeCraftingRecipe(order.Recipe)} • {materials} • praca " +
            $"{order.TotalWorkTicks - order.RemainingWorkTicks}/" +
            order.TotalWorkTicks;
    }

    private static string DescribeCorpse(CorpseSnapshot corpse)
    {
        var contents = corpse.Contents.Count == 0
            ? "puste"
            : string.Join(", ", corpse.Contents.Select(item =>
                $"{DescribeResourceVariant(item.Resource, item.FoodKind, item.Variant)} ×{item.Quantity}"));
        var water = corpse.ContainedWater > 0 ? $" • woda {corpse.ContainedWater}" : string.Empty;
        var imprintParts = new[]
        {
            DescribeSkills(corpse.InheritanceImprint.KnownSkills),
            DescribeTraits(corpse.InheritanceImprint.KnownTraits),
        }.Where(item => !string.IsNullOrWhiteSpace(item));
        var imprint = string.Join(", ", imprintParts);
        var imprintText = imprint.Length > 0 ? $" • odcisk: {imprint}" : string.Empty;
        return $"{corpse.Name} [{corpse.Kind}] • mięso {corpse.EdiblePortions} porcji • " +
            $"pojemnik {corpse.ContentsWeight} wag.{water}{imprintText} • {contents}";
    }

    private static string DescribeResourceVariant(
        ResourceKind resource,
        FoodKind foodKind,
        ResourceVariant variant) => resource switch
    {
        ResourceKind.Food => DescribeFood(foodKind),
        ResourceKind.Equipment => DescribeResourceVariant(variant),
        _ => DescribeResource(resource),
    };

    private string DescribeCaveRock(RockKind rock) => Ui("cave-rocks", rock.ToString());

    private string DescribeCaveKind(CaveCellKind kind) => Ui("cave-kinds", kind.ToString());

    private string DescribeCaveFluid(CellFluidKind fluid) => fluid switch
    {
        CellFluidKind.Lava => Ui("cave-fluids", "Lava"),
        CellFluidKind.Water => Ui("cave-fluids", "Water"),
        _ => string.Empty,
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

    private static string DescribeFood(FoodKind food) =>
        TranslationCatalog.Get(_currentLocale, "interface", "food-names", food.ToString());

    private static string DescribeResource(ResourceKind resource) =>
        TranslationCatalog.Get(_currentLocale, "interface", "resource-names", resource.ToString());

    private static string DescribeCraftingRecipe(CraftingRecipeKind recipe) =>
        TranslationCatalog.Get(_currentLocale, "recipes", "names", recipe.ToString());

    private static string DescribeStorageProvider(StorageProviderKind providerKind) =>
        TranslationCatalog.Get(
            _currentLocale, "interface", "storage-providers", providerKind.ToString());

    private static string DescribeResourceVariant(ResourceVariant variant)
    {
        if (MaterialCatalog.TryGet(variant, out var material))
        {
            return TranslationCatalog.Get(_currentLocale, "materials", "names", material.Id);
        }

        var key = EquipmentCatalog.FindDefinition(variant) is null
            ? "unknown"
            : variant.ToString();
        return TranslationCatalog.Get(_currentLocale, "interface", "equipment-names", key);
    }

    private string DescribeMineralDeposit(MineralDepositKind deposit) =>
        deposit == MineralDepositKind.None
            ? string.Empty
            : Ui("mineral-deposits", deposit.ToString());

    private string DescribeMiningRequirement(RockKind rock)
    {
        var requiredLevel = MiningCapabilityPolicy.RequiredSkillLevel(rock);
        var tool = Ui("mining", rock == RockKind.Obsidian
            ? "reinforced-pickaxe"
            : "pickaxe");
        return requiredLevel > 0
            ? UiFormat("mining", "requirement-with-level", tool, requiredLevel)
            : UiFormat("mining", "requirement", tool);
    }

    private string DescribeStoragePriority(StoragePriority priority) =>
        Ui("storage-priorities", priority.ToString());

    private static string DescribeCohort(HumanCohortSnapshot cohort) =>
        $"{cohort.Role switch
        {
            HumanCohortRole.Farmers => "rolnicy",
            HumanCohortRole.Workers => "robotnicy",
            HumanCohortRole.Guards => "strażnicy",
            _ => "nieznani",
        }} ×{cohort.Population} • {DescribeHumanTask(cohort.Task)} • um. {cohort.SkillLevel} • {cohort.Tools}";

    private static string DescribeVillager(HumanVillagerSnapshot villager) =>
        $"{villager.Name} ({villager.Role switch
        {
            HumanCohortRole.Farmers => "rolnik",
            HumanCohortRole.Workers => "robotnik",
            HumanCohortRole.Guards => "strażnik",
            _ => "nieznany",
        }}) • {DescribeHumanTask(villager.Task)} • zdrowie " +
        $"{villager.Health}/{villager.MaximumHealth} • zmęczenie " +
        $"{villager.Fatigue}/{villager.MaximumFatigue} • głód " +
        $"{villager.Hunger}/{villager.MaximumNeed} • pragnienie " +
        $"{villager.Thirst}/{villager.MaximumNeed} • praca " +
        $"{villager.WorkProgress} • {villager.Tools}";

    private static string DescribeHumanTask(HumanCohortTask task) => task switch
    {
        HumanCohortTask.WorkFields => "pracują na polach",
        HumanCohortTask.DrawWater => "czerpią wodę",
        HumanCohortTask.ClearLand => "karczują pod pola",
        HumanCohortTask.GatherBerries => "szukają jagód",
        HumanCohortTask.BuildStorehouse => "budują spichlerz",
        HumanCohortTask.CraftGoods => "pracują w stodole",
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

    private string DescribeHumanField(
        HumanFieldSnapshot field,
        HumanVillageSnapshot village)
    {
        var work = $"praca {field.WorkProgress}/" +
            _engine.Definitions.HumanVillageEconomy.FieldWorkPerStage;
        if (field.Phase == HumanFieldPhase.Cleared)
        {
            var blocker = village.GrainStock <= 0
                ? "brak ziarna siewnego"
                : village.WaterStock < 2
                    ? "brak wody 2 (potrzebny pracownik z wiadrem)"
                    : "siew zużyje 1 ziarno i 2 wody";
            return $"{DescribeField(field.Phase)} • {work} • {blocker}";
        }

        return $"{DescribeField(field.Phase)} • {work} • wzrost {field.GrowthDays}/" +
            $"{_engine.Definitions.HumanVillageEconomy.CropGrowthDays} dni";
    }

    private static string DescribeJob(ActorJobSnapshot job) =>
        ActorJobTextPresenter.Describe(_currentLocale, job);

    private string DescribeConstructionSite(ConstructionSiteSnapshot site)
    {
        var materials = string.Join(", ", site.Materials.Select(material =>
            $"{(material.DeliveredQuantity > 0
                ? DescribeResourceVariant(material.DeliveredVariant)
                : material.Variant == ResourceVariant.None
                ? Ui("storage-resources", material.Resource.ToString())
                : DescribeResourceVariant(material.Variant))} " +
            $"{material.DeliveredQuantity}/{material.RequiredQuantity}"));
        var workDone = site.TotalWorkTicks - site.RemainingWorkTicks;
        var readiness = DescribeConstructionReadiness(
            _engine.InspectConstructionReadiness(site.Id, evaluateReachability: false));
        return UiFormat(
            "construction-site",
            "summary",
            DescribeConstruction(site.Kind),
            DescribeStoragePriority(site.Priority),
            materials,
            workDone,
            site.TotalWorkTicks,
            readiness);
    }

    private string DescribeConstructionReadiness(
        ConstructionReadinessDiagnostic diagnostic) => diagnostic.State switch
    {
        ConstructionReadinessState.NoAvailableMaterials =>
            UiFormat("construction-site", "no-materials", diagnostic.MatchingSourceCount),
        ConstructionReadinessState.NoAvailableSupplier =>
            Ui("construction-site", "no-supplier"),
        ConstructionReadinessState.NoReachableMaterialSource =>
            UiFormat("construction-site", "material-unreachable",
                diagnostic.AvailableMaterialQuantity),
        ConstructionReadinessState.WaitingForSupplier =>
            UiFormat("construction-site", "waiting-for-supplier",
                diagnostic.AvailableMaterialQuantity),
        ConstructionReadinessState.MaterialsInTransit =>
            UiFormat("construction-site", "materials-in-transit", diagnostic.InTransitQuantity),
        ConstructionReadinessState.AwaitingSiteClearance =>
            Ui("construction-site", "awaiting-clearance"),
        ConstructionReadinessState.NoCapableBuilder =>
            Ui("construction-site", "no-capable-builder"),
        ConstructionReadinessState.NoReachableBuilder =>
            Ui("construction-site", "no-reachable-builder"),
        ConstructionReadinessState.WaitingForBuilder =>
            UiFormat("construction-site", "waiting-for-builder",
                diagnostic.CapableBuilderCount),
        ConstructionReadinessState.Building => Ui("construction-site", "building"),
        _ => Ui("construction-site", "unknown"),
    };

    private string DescribeConstruction(ConstructionKind kind) =>
        Ui("construction-names", kind.ToString());

    private string DescribeFoodSource(PlantKind kind) => Ui("food-sources", kind.ToString());

    private string DescribeWaterDepth(MapCell cell) => cell.Terrain switch
    {
        TerrainKind.ShallowWater => Ui("water-depth", "shallow"),
        TerrainKind.DeepWater => UiFormat("water-depth", "deep",
            cell.WaterDepthLevels, cell.FloorLevel),
        _ => string.Empty,
    };

    private void ShowStoredResources()
    {
        UpdateStoredResources(_latestSnapshot);
        _storedResourcesWindow.Popup();
    }

    private void ShowLooseResources()
    {
        UpdateLooseResources(_latestSnapshot);
        _looseResourcesWindow.Popup();
    }

    private void ShowGoblinRoster()
    {
        UpdateGoblinRoster(_latestSnapshot, force: true);
        _goblinRosterWindow.Popup();
    }

    private void ShowStatistics()
    {
        UpdateStatistics(_latestSnapshot);
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
        if (_plannerWindow.Visible)
        {
            UpdatePlanner(snapshot);
        }
    }

    private void UpdateStoredResources(SimulationSnapshot snapshot, bool force = false)
    {
        var breakdown = CreateResourceBreakdown(snapshot, ItemLocationKind.StorageZone);
        var signature = string.Join('|', snapshot.ResourceInventory.Select(item =>
            $"{(int)item.Resource}:{item.StoredQuantity}")) +
            CreateResourceBreakdownSignature(breakdown) +
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
            breakdown,
            "w magazynach",
            _storedResourcesDetailed.ButtonPressed);
    }

    private void UpdateLooseResources(SimulationSnapshot snapshot, bool force = false)
    {
        var breakdown = CreateResourceBreakdown(snapshot, ItemLocationKind.Ground);
        var signature = string.Join('|', snapshot.ResourceInventory.Select(item =>
            $"{(int)item.Resource}:{item.KnownLooseQuantity}")) +
            CreateResourceBreakdownSignature(breakdown) +
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
            breakdown,
            "na ziemi",
            _looseResourcesDetailed.ButtonPressed);
    }

    private void RebuildResourceGrid(
        GridContainer grid,
        SimulationSnapshot snapshot,
        Func<ResourceInventorySnapshot, int> quantitySelector,
        IReadOnlyList<ResourceBreakdownTotal> breakdown,
        string location,
        bool detailed)
    {
        foreach (var child in grid.GetChildren())
        {
            child.QueueFree();
        }

        void AddTile(
            ResourceKind resource,
            FoodKind foodKind,
            ResourceVariant variant,
            int quantity,
            string tooltip)
        {
            var tile = new PanelContainer
            {
                CustomMinimumSize = new Vector2(142, 92),
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
                Texture = ResourceThumbnails.Create(
                    _itemIconAtlas,
                    _treePartAtlas,
                    _foodIconAtlas,
                    _resourceThumbnailTextures,
                    resource,
                    foodKind,
                    variant),
                SelfModulate = foodKind == FoodKind.None && variant == ResourceVariant.None
                    ? ItemIcons.TintForResource(resource)
                    : Colors.White,
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
            foreach (var total in breakdown)
            {
                var name = total.Resource == ResourceKind.Food
                    ? DescribeFood(total.FoodKind)
                    : total.Variant != ResourceVariant.None
                        ? DescribeResourceVariant(total.Variant)
                        : DescribeResource(total.Resource);
                AddTile(
                    total.Resource,
                    total.FoodKind,
                    total.Variant,
                    total.Quantity,
                    $"{name}: {total.Quantity:N0} szt. {location}");
            }
            return;
        }

        foreach (var item in snapshot.ResourceInventory.OrderBy(item => item.Resource))
        {
            var quantity = quantitySelector(item);
            AddTile(
                item.Resource,
                FoodKind.None,
                ResourceVariant.None,
                quantity,
                DescribeResourceOverviewTooltip(
                    item,
                    quantity,
                    breakdown,
                    location));
        }
    }

    private ResourceBreakdownTotal[] CreateResourceBreakdown(
        SimulationSnapshot snapshot,
        ItemLocationKind locationKind)
    {
        var totals = new Dictionary<(ResourceKind Resource, FoodKind FoodKind, ResourceVariant Variant), int>();
        foreach (var stack in snapshot.ItemStacks)
        {
            if (stack.Location.Kind != locationKind ||
                (locationKind == ItemLocationKind.Ground &&
                 !snapshot.GetVisibility(stack.Location.Position, _engine.Map.Width).IsDiscovered()))
            {
                continue;
            }

            var key = (stack.Resource, stack.FoodKind, stack.Variant);
            totals[key] = totals.GetValueOrDefault(key) + stack.Quantity;
        }

        return totals
            .OrderBy(pair => pair.Key.Resource)
            .ThenBy(pair => pair.Key.FoodKind)
            .ThenBy(pair => pair.Key.Variant)
            .Select(pair => new ResourceBreakdownTotal(
                pair.Key.Resource,
                pair.Key.FoodKind,
                pair.Key.Variant,
                pair.Value))
            .ToArray();
    }

    private static string CreateResourceBreakdownSignature(
        IReadOnlyList<ResourceBreakdownTotal> breakdown) =>
        string.Concat("|types:", string.Join(',', breakdown.Select(total =>
            $"{(int)total.Resource}:{(int)total.FoodKind}:" +
            $"{(int)total.Variant}:{total.Quantity}")));

    private string DescribeResourceOverviewTooltip(
        ResourceInventorySnapshot item,
        int quantity,
        IReadOnlyList<ResourceBreakdownTotal> breakdown,
        string location)
    {
        var tooltip = $"{DescribeResource(item.Resource)}: {quantity:N0} szt. {location}";
        var details = breakdown
            .Where(total => total.Resource == item.Resource)
            .Select(total =>
            {
                var name = item.Resource == ResourceKind.Food
                    ? DescribeFood(total.FoodKind)
                    : total.Variant != ResourceVariant.None
                        ? DescribeResourceVariant(total.Variant)
                        : DescribeResource(item.Resource);
                return $"{name}: {total.Quantity:N0}";
            })
            .ToArray();
        return details.Length == 0
            ? tooltip
            : $"{tooltip}\n{string.Join(", ", details)}";
    }

    private readonly record struct ResourceBreakdownTotal(
        ResourceKind Resource,
        FoodKind FoodKind,
        ResourceVariant Variant,
        int Quantity);

    private void UpdateGoblinRoster(SimulationSnapshot snapshot, bool force = false)
    {
        var signature = string.Join('|', snapshot.Actors.Select(actor =>
            $"{actor.Id.Value}:{actor.Name}:{actor.Health}:{actor.EffectiveMaximumHealth}:" +
            $"{(int)actor.Job.Kind}:{(int)actor.Job.Phase}"));
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
                Text = $"zdrowie {actor.Health:N0}/{actor.EffectiveMaximumHealth:N0}",
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
        var actor = _latestSnapshot.Actors.FirstOrDefault(actor => actor.Id == actorId);
        if (actor.Id == EntityId.None)
        {
            return;
        }

        _goblinRosterWindow.Hide();
        if (!_use3DView && _visibleLevel != actor.Position.Z)
        {
            _visibleLevel = actor.Position.Z;
            _worldView.SetVisibleLevel(_visibleLevel);
            _minimap.SetVisibleLevel(_visibleLevel);
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
        ActorJobKind.CarveRamp => "⇅",
        ActorJobKind.TendBud => "♧",
        ActorJobKind.HuntAnimal => "⚔",
        ActorJobKind.SupplyCrafting => "⇥",
        ActorJobKind.Craft => "⚒",
        ActorJobKind.ClearConstructionSite => "↗",
        ActorJobKind.CleanBlood => "✦",
        ActorJobKind.LootRaid => "▣",
        ActorJobKind.RecoverRaidCorpse => "†",
        ActorJobKind.ConsumeRaidCorpse => "☠",
        _ => "·",
    };

    private void UpdateStatistics(SimulationSnapshot snapshot)
    {
        var needs = snapshot.TribeNeeds;
        var metrics = _engine.GetMetrics();
        var navigation = metrics.Navigation;
        var stages = metrics.LastTickBreakdown;
        var jobs = _engine.GetLastActorJobUpdateProfile();
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
            $"Plemię: {snapshot.Actors.Count} • pąki {snapshot.GoblinBuds.Count}\n" +
            $"Żywność: {needs.FoodUnits}/{needs.ExpectedDailyFoodUnits} szt. " +
            $"(zapas / przewidywana doba)\n" +
            $"Miejsca do spania: {needs.ShelterCapacity}/{snapshot.Actors.Count}\n" +
            $"Magazyny: {needs.StoredUnits}/{needs.StorageCapacity} • " +
            $"luźne towary {needs.KnownLooseUnits}\n" +
            $"Wilgotne miejsca lęgowe: {needs.SuitableMoistSites}\n" +
            $"Zdrowi robotnicy: {needs.HealthyWorkers}/{snapshot.Actors.Count} • " +
            $"otwarte prace {needs.WorkDemand}\n" +
            $"Rozmnażanie: {DescribeReproductionReadiness(needs.Reproduction)}\n" +
            $"Wrogość wsi: {needs.HumanHostility}/100\n\n" +
            $"Zwierzęta: zające {snapshot.Animals.Count(animal => animal.Kind == AnimalKind.MarshHare)} " +
            $"• dziki {snapshot.Animals.Count(animal => animal.Kind == AnimalKind.SwampBoar)} " +
            $"• pająki jaskiniowe {snapshot.Animals.Count(animal => animal.Kind == AnimalKind.CaveSpider)} " +
            $"• głębinowce {snapshot.Animals.Count(animal => animal.Kind == AnimalKind.DeepCrawler)} " +
            $"• żmije magmowe {snapshot.Animals.Count(animal => animal.Kind == AnimalKind.MagmaWyrm)}\n" +
            $"Magazyny: {snapshot.StorageZones.Count} • towary {stored:N0}\n" +
            $"Znane luźne towary: {loose:N0}\n" +
            $"Budowy: {snapshot.ConstructionSites.Count}\n" +
            $"Zlecenia terenowe: {snapshot.WorkDesignations.Count}\n" +
            $"Odkryta mapa: {explored:N0}/{snapshot.Visibility.Count:N0}\n\n" +
            $"Ticki: {metrics.TicksExecuted:N0}\n" +
            $"Ostatni tick: {metrics.LastTickDuration.TotalMilliseconds:N3} ms\n" +
            $"Średni tick: {averageTickMilliseconds:N3} ms\n" +
            $"Etapy: świat {stages.World.TotalMilliseconds:N2} • prace " +
            $"{stages.ActorJobs.TotalMilliseconds:N2} • zwierzęta " +
            $"{stages.Animals.TotalMilliseconds:N2} • ludzie " +
            $"{stages.HumanVillage.TotalMilliseconds:N2} • widoczność " +
            $"{stages.Visibility.TotalMilliseconds:N2} ms\n" +
            $"Prace: planowanie {jobs.IdlePlanning.TotalMilliseconds:N2} • aktywne " +
            $"{jobs.ActiveJobs.TotalMilliseconds:N2} • potrzeby " +
            $"{jobs.NeedInterrupts.TotalMilliseconds:N2} ms\n" +
            $"Aktywne stacki: {metrics.ItemStacks:N0}\n" +
            $"Obiekty świata: {metrics.WorldObjects:N0}\n" +
            $"Ścieżki: {navigation.Searches:N0}/{navigation.Requests:N0} wyszukań " +
            $"• cache {cacheHitRate:N1}% ({navigation.CachedRoutes:N0})";
    }

    private static string DescribeReproductionReadiness(
        GoblinReproductionReadinessSnapshot readiness) => readiness.Kind switch
    {
        GoblinReproductionReadinessKind.Ready =>
            $"gotowe ({readiness.AvailableFood}/{readiness.RequiredFood} żywności, " +
            $"rodzice {readiness.EligibleParents}, miejsca {readiness.SuitableMoistSites})",
        GoblinReproductionReadinessKind.InsufficientFood =>
            $"za mało dostępnej żywności ({readiness.AvailableFood}/{readiness.RequiredFood})",
        GoblinReproductionReadinessKind.InsufficientShelter =>
            "brak wolnego miejsca w schronieniu",
        GoblinReproductionReadinessKind.InsufficientAdultPopulation =>
            "za mało dorosłych goblinów do samoistnego pączkowania",
        GoblinReproductionReadinessKind.UnsafeConditions =>
            "trwa wyprawa — warunki nie są bezpieczne",
        GoblinReproductionReadinessKind.JuvenileCapacityReached =>
            "plemię wychowuje już maksymalną liczbę młodzików",
        GoblinReproductionReadinessKind.NoMoistSpace => "brak wolnego wilgotnego miejsca w chacie",
        GoblinReproductionReadinessKind.NoEligibleParent =>
            "brak wolnego, zdrowego, najedzonego i wypoczętego rodzica",
        GoblinReproductionReadinessKind.BudWaitingForCare =>
            $"pąk czeka na opiekuna ({readiness.UntendedBuds})",
        GoblinReproductionReadinessKind.BudBeingTended => "opiekun zajmuje się pąkiem",
        _ => "stan nieznany",
    };

    private static string DescribeAnimal(AnimalSnapshot animal) =>
        AnimalTextPresenter.Describe(_currentLocale, animal);

    private void SelectActor(EntityId actorId, bool showDetails = false)
    {
        _selectedActorIds.Clear();
        if (actorId != EntityId.None)
        {
            _selectedActorIds.Add(actorId);
        }
        ApplyActorSelection(actorId, showDetails);
    }

    private void SelectOrToggleActor(
        EntityId actorId,
        bool extendSelection,
        bool showDetails = false)
    {
        if (!extendSelection)
        {
            SelectActor(actorId, showDetails);
            return;
        }

        if (!_selectedActorIds.Add(actorId))
        {
            _selectedActorIds.Remove(actorId);
        }
        var primary = _selectedActorIds.Contains(actorId)
            ? actorId
            : _selectedActorIds.OrderBy(id => id).FirstOrDefault();
        ApplyActorSelection(primary, showDetails);
    }

    private void ApplyActorSelection(EntityId actorId, bool showDetails = false)
    {
        _selectedActorId = actorId;
        _worldView.SetSelectedActors(_selectedActorIds);
        _worldView3D.SetSelectedActors(_selectedActorIds);
        if (actorId == EntityId.None || !showDetails)
        {
            _goblinDetails.Hide();
            return;
        }

        UpdateGoblinDetails(GetDisplayedSnapshot());
        _storageDetails.Hide();
        PositionGoblinDetailsWindow();
        _goblinDetails.Show();
    }

    private void ShowSelectedGoblinDetails()
    {
        if (_selectedActorId == EntityId.None)
        {
            _inspector.Text = "Najpierw wybierz goblina; F1 otwiera dane głównej zaznaczonej postaci.";
            return;
        }
        ApplyActorSelection(_selectedActorId, showDetails: true);
    }

    private string DescribeActorSelection(ActorSnapshot actor, GridPosition position) =>
        !_selectedActorIds.Contains(actor.Id)
            ? $"{actor.Name} usunięty z bieżącej grupy • pozostało {_selectedActorIds.Count}"
            : _selectedActorIds.Count <= 1
            ? $"{actor.Name} • {position} • {DescribeJob(actor.Job)}"
            : $"Wybrano grupę {_selectedActorIds.Count} goblinów • główny: {actor.Name} • " +
              "rozkazy M/A/H/P obejmą całą grupę.";

    private void PositionGoblinDetailsWindow()
    {
        var viewportSize = (Vector2I)GetViewport().GetVisibleRect().Size;
        var margin = 18;
        _goblinDetails.Position = new Vector2I(
            Math.Max(margin, viewportSize.X - _goblinDetails.Size.X - margin),
            Math.Max(margin, viewportSize.Y - _goblinDetails.Size.Y - 88));
    }

    private void HandleViewportSizeChanged()
    {
        ConstrainCameraToMap();
        if (_goblinDetails.Visible)
        {
            PositionGoblinDetailsWindow();
        }
    }

    private void ShowStorageDetails(StorageZoneSnapshot zone)
    {
        _selectedConstructionId = EntityId.None;
        _constructionDetails.Hide();
        _selectedStorageId = zone.Id;
        _storageSettingsDirty = false;
        _storageDetails.Title = DescribeStorageWindowTitle(zone);
        UpdateStorageDetails(zone);
        _storageDetails.Popup();
    }

    private void UpdateStorageDetails(StorageZoneSnapshot zone)
    {
        var snapshot = GetDisplayedSnapshot();
        UpdateStorageDetails(zone, snapshot);
    }

    private void UpdateStorageDetails(
        StorageZoneSnapshot zone,
        SimulationSnapshot snapshot)
    {
        var delivery = _engine.InspectStorageDelivery(zone.Id);
        var contentStacks = snapshot.ItemStacks
            .Where(stack =>
                stack.Location.Kind == ItemLocationKind.StorageZone &&
                stack.Location.OwnerId == zone.Id)
            .OrderBy(stack => stack.FoodKind)
            .ThenBy(stack => stack.Id)
            .ToArray();
        RebuildStorageContentsGrid(contentStacks);
        var assignedHauler = snapshot.Actors.FirstOrDefault(actor =>
            actor.Id == zone.AssignedHaulerId);
        var haulerDescription = assignedHauler.Id == EntityId.None
            ? "publiczny dispatcher"
            : $"{assignedHauler.Name} ({assignedHauler.Id})";
        var sourceZone = snapshot.StorageZones.FirstOrDefault(candidate =>
            candidate.Id == zone.SourceStorageZoneId);
        var sourceDescription = sourceZone.Id == EntityId.None
            ? "teren i nadwyżki dowolnych składów"
            : sourceZone.AcceptedResource == ResourceKind.Water
                ? $"beczka {sourceZone.Id} przy {sourceZone.Position} " +
                  $"({sourceZone.StoredQuantity}/{sourceZone.Capacity}, " +
                  $"rezerwa {sourceZone.DesiredQuantity})"
                : $"skład {sourceZone.Id} przy {sourceZone.Position}";
        var hasGlobalResourcePriority = zone.AcceptedResource is not (
            ResourceKind.Materials or ResourceKind.Any);
        var globalPriority = hasGlobalResourcePriority
            ? snapshot.ResourcePriorities
                .Single(priority => priority.Resource == zone.AcceptedResource)
                .Priority
            : StoragePriority.Normal;
        var mineralFilterDescription = zone.AcceptedResource == ResourceKind.Stone
            ? $"Przyjmowany urobek: {DescribeMineralFilter(zone.MineralFilter)}.\n"
            : string.Empty;
        _storageSummary.Text = $"Obszar: {zone.StorageAreaId} • " +
            (zone.LogisticsNetworkId == EntityId.None
                ? "sieć Default\n"
                : $"sieć {zone.LogisticsNetworkId}\n") +
            $"Stan: {zone.StoredQuantity}/{zone.Capacity}\n" +
            (zone.SeparatesItemTypes
                ? $"Sloty rodzajowe: {zone.UsedTypeSlots}/{zone.TypeSlotCount}, " +
                  $"stos do {zone.StackCapacity} szt.\n"
                : string.Empty) +
            mineralFilterDescription +
            (zone.DesiredQuantity == 0
                ? "Automatyczne dostawy wyłączone.\n"
                : $"Żądanie dostawy do {zone.DesiredQuantity} szt.\n") +
            $"Status dostaw: {DescribeStorageDelivery(delivery, assignedHauler)}\n" +
            $"Transport: {haulerDescription}.\n" +
            $"Źródło: {sourceDescription}.\n" +
            $"Priorytet lokalny: {DescribeStoragePriority(zone.Priority)}.\n" +
            (hasGlobalResourcePriority
                ? $"Priorytet {DescribeResource(zone.AcceptedResource)} w plemieniu: " +
                  $"{DescribeStoragePriority(globalPriority)}."
                : "Priorytety materiałów są ustalane osobno dla każdego zasobu.");
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
            _resourcePriority.Disabled = !hasGlobalResourcePriority;

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
                         (candidate.ResourceFilter & zone.ResourceFilter) !=
                            StorageResourceFilter.None)
                         .OrderBy(candidate => candidate.Id))
            {
                _storageSource.AddItem(candidate.AcceptedResource == ResourceKind.Water
                    ? $"Beczka {candidate.Id} • {candidate.Position} • " +
                      $"{candidate.StoredQuantity}/{candidate.Capacity} • " +
                      $"rezerwa {candidate.DesiredQuantity}"
                    : $"Skład {candidate.Id} • {candidate.Position}");
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

    private void RebuildStorageContentsGrid(IReadOnlyList<ItemStackSnapshot> stacks)
    {
        foreach (var child in _storageContentsGrid.GetChildren())
        {
            child.QueueFree();
        }

        var groups = stacks
            .GroupBy(stack => (stack.Resource, stack.FoodKind, stack.Variant))
            .OrderBy(group => group.Key.Resource)
            .ThenBy(group => group.Key.FoodKind)
            .ThenBy(group => group.Key.Variant)
            .ToArray();
        _storageContentsLabel.Text = groups.Length == 0
            ? "Zawartość: pusty"
            : $"Zawartość • {groups.Length} zajętych slotów:";
        _storageContentsGrid.Visible = groups.Length > 0;
        foreach (var group in groups)
        {
            var quantity = group.Sum(stack => stack.Quantity);
            var name = DescribeResourceVariant(
                group.Key.Resource,
                group.Key.FoodKind,
                group.Key.Variant);
            var tile = new PanelContainer
            {
                CustomMinimumSize = new Vector2(138, 76),
                TooltipText = $"{name}: {quantity:N0} szt.",
            };
            var content = new VBoxContainer
            {
                Alignment = BoxContainer.AlignmentMode.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            content.AddChild(new TextureRect
            {
                CustomMinimumSize = new Vector2(48, 48),
                Texture = ResourceThumbnails.Create(
                    _itemIconAtlas,
                    _treePartAtlas,
                    _foodIconAtlas,
                    _resourceThumbnailTextures,
                    group.Key.Resource,
                    group.Key.FoodKind,
                    group.Key.Variant),
                SelfModulate = group.Key.FoodKind == FoodKind.None &&
                    group.Key.Variant == ResourceVariant.None
                        ? ItemIcons.TintForResource(group.Key.Resource)
                        : Colors.White,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            content.AddChild(new Label
            {
                Text = quantity.ToString("N0"),
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            tile.AddChild(content);
            _storageContentsGrid.AddChild(tile);
        }
    }

    private static string DescribeStorageWindowTitle(StorageZoneSnapshot zone) =>
        zone.ProviderKind switch
        {
            StorageProviderKind.WaterBarrel => "▣ Beczka na wodę",
            StorageProviderKind.WoodenBox => "□ Drewniana skrzynka",
            StorageProviderKind.WoodenChest => "▤ Drewniana skrzynia",
            StorageProviderKind.WoodenBulkBin => "▥ Drewniany zasobnik masowy",
            _ => $"◇ Skład: {DescribeResource(zone.AcceptedResource)}",
        };

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
        StorageDeliveryState.NoAvailableTool =>
            "brak dostępnego tragarza z drewnianym wiadrem",
        StorageDeliveryState.AssignedHaulerBusy => assignedHauler.Id == EntityId.None
            ? "przypisany tragarz jest zajęty"
            : $"{assignedHauler.Name} jest zajęty: {DescribeJob(assignedHauler.Job)}",
        StorageDeliveryState.WaitingForHauler =>
            $"oczekuje na tragarza; dostępne {delivery.AvailableSourceQuantity} szt.",
        _ => "nieznany",
    };

    private void ApplyStorageSettings()
    {
        var snapshot = _latestSnapshot;
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
        if (zone.AcceptedResource != ResourceKind.Materials)
        {
            _engine.QueueCommand(SimulationCommand.ConfigureResourcePriority(
                executeAt,
                _commandSequence++,
                zone.AcceptedResource,
                globalPriority));
        }
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
        var site = _latestSnapshot.ConstructionSites
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

        UpdateNeedBar(_healthBar, actor.Health, actor.EffectiveMaximumHealth, "Zdrowie");
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
        _healthBar.TooltipText += $" • aktualna wydolność maksymalna: " +
            $"{actor.EffectiveMaximumHealth:N0}/{_engine.Definitions.MaximumHealth:N0}";
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
            .AppendLine(actor.IsJuvenile
                ? $"Wiek: {actor.AgeDays} dni • młode, przenosi tylko lekkie ładunki i szybciej się męczy"
                : actor.IsElderly
                    ? $"Wiek: {actor.AgeDays} dni " +
                      $"({(double)actor.AgeDays / _engine.Definitions.Clock.Climate.DaysPerYear:0.0} lat) • " +
                      $"starość {actor.SenescenceProgress:P0}, wydolność " +
                      $"{actor.EffectiveMaximumHealth:N0}/{_engine.Definitions.MaximumHealth:N0}"
                    : $"Wiek: {actor.AgeDays} dni " +
                      $"({(double)actor.AgeDays / _engine.Definitions.Clock.Climate.DaysPerYear:0.0} lat) • dorosły")
            .AppendLine(actor.BleedingTicksRemaining > 0
                ? $"Stan: krwawi ({actor.BleedingTicksRemaining} ticków do samoistnego ustania)"
                : "Stan: nie krwawi")
            .AppendLine()
            .AppendLine($"Znane umiejętności: {DescribeSkills(actor.KnownSkills)}")
            .AppendLine($"Doświadczenie: {DescribeExperience(actor.Experience)}")
            .AppendLine($"Preferencje pracy: zbieractwo {DescribeWorkPreference(actor.WorkPreferences.Foraging)}, " +
                $"transport {DescribeWorkPreference(actor.WorkPreferences.Hauling)}, " +
                $"budowanie {DescribeWorkPreference(actor.WorkPreferences.Building)}")
            .AppendLine($"Znane cechy: {DescribeTraits(actor.KnownTraits)}")
            .AppendLine($"Wyposażenie: {string.Join(", ", actor.Loadout.Items.Select(item =>
                $"{item.Slot}: {DescribeResourceVariant(item.Variant)} ({item.Weight} wag.)"))}")
            .AppendLine($"Obciążenie: sprzęt {actor.Loadout.EquipmentWeight}, plecak " +
                $"{actor.Loadout.PackWeight}, ładunek {actor.Loadout.CarriedCargoWeight}; " +
                $"razem {actor.Loadout.TotalWeight}/{actor.Loadout.CarryingCapacity}")
            .AppendLine($"Służba logistyczna: " +
                (logisticsDuty.Length == 0 ? "brak przydziału" : string.Join(", ", logisticsDuty)))
            .AppendLine($"Rozkaz nadrzędny: {DescribeTacticalOrder(actor.TacticalOrder)}")
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

    private static string DescribeTacticalOrder(ActorTacticalOrderSnapshot order) =>
        order.Kind switch
        {
            ActorTacticalOrderKind.Patrol =>
                $"patrol przez {order.PatrolPoints.Count} punktów " +
                $"(następny {order.PatrolPointIndex + 1})",
            ActorTacticalOrderKind.AttackArea =>
                $"atakuj wrogów wokół {order.Center}, promień {order.Radius}",
            ActorTacticalOrderKind.HuntArea =>
                $"poluj wokół {order.Center}, promień {order.Radius}",
            _ => "brak",
        };

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
            ActorPlanIntentKind.NextPublicWork =>
                $"następnie: {DescribeJobKind(entry.JobKind)} → {entry.Target} " +
                $"(zlecenie {entry.WorkOrderId})",
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
        ActorJobKind.SupplyCrafting => "dostawy do warsztatu",
        ActorJobKind.Craft => "rzemiosła",
        ActorJobKind.ClearConstructionSite => "uprzątania placu budowy",
        ActorJobKind.ClearVegetation => "karczowania",
        ActorJobKind.SupplyConstruction => "dostawy na budowę",
        ActorJobKind.BuildConstruction => "budowy",
        ActorJobKind.Collapsed => "przymusowego snu",
        ActorJobKind.FellTree => "wyrębu",
        ActorJobKind.QuarryBoulder => "wydobycia kamienia",
        ActorJobKind.MineRock => "kopania w skale",
        ActorJobKind.CarveRamp => "wykuwania pochylni",
        ActorJobKind.TendBud => "opieki nad pąkiem",
        ActorJobKind.HuntAnimal => "polowania",
        ActorJobKind.CleanBlood => "sprzątania krwi",
        ActorJobKind.LootRaid => "plądrowania",
        ActorJobKind.RecoverRaidCorpse => "przenoszenia zwłok",
        ActorJobKind.ConsumeRaidCorpse => "pożerania zwłok",
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
        bar.MaxValue = maximum;
        bar.Value = value;
        bar.TooltipText = $"{name}: {value:N0} / {maximum:N0}";
    }

    private void UpdateInventoryIcons(ActorSnapshot actor, ItemStackSnapshot? cargo)
    {
        var signature = $"{(int)actor.Equipment}:{string.Join(',', actor.PersonalFoodKinds)}:" +
            $"{actor.PersonalWater}:{actor.PersonalStoneAmmo}:" +
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
            AddInventoryIcon(ItemIcon.WoodenAxe, "Prymitywna siekiera • narzędzie do wyrębu");
        }
        if (actor.Equipment.HasFlag(PersonalEquipment.PrimitivePickaxe))
        {
            AddInventoryIcon(_pickaxeIcon, "Prymitywny kilof • narzędzie do rozbijania głazów");
        }
        if (actor.Equipment.HasFlag(PersonalEquipment.ReinforcedPickaxe))
        {
            AddInventoryIcon(_pickaxeIcon,
                "Wzmocniony kilof • narzędzie wymagane do obsydianu");
        }
        if (actor.Equipment.HasFlag(PersonalEquipment.PrimitiveSling))
        {
            AddInventoryIcon(
                CreateSlingIcon(),
                "Prymitywna proca • broń dystansowa na małe kamienie");
        }
        if (actor.Equipment.HasFlag(PersonalEquipment.FightingStick))
        {
            AddInventoryIcon(ItemIcon.Wood, "Kij bojowy • prymitywna broń osobista");
        }
        if (actor.Equipment.HasFlag(PersonalEquipment.StoneClub))
        {
            AddInventoryIcon(ItemIcon.Stone, "Kamienna maczuga • ciężka broń osobista");
        }
        if (actor.Equipment.HasFlag(PersonalEquipment.HideClothes))
        {
            AddInventoryIcon(ItemIcon.RagClothes, "Skórzany ubiór • ubranie osobiste");
        }
        if (actor.Equipment.HasFlag(PersonalEquipment.ReedClothes))
        {
            AddInventoryIcon(ItemIcon.Reeds, "Sitowiowy ubiór • lekkie ubranie osobiste");
        }
        AddInventoryIcon(
            ItemIcon.Stone,
            "Osobiste kamienie do rzucania i procy",
            actor.PersonalStoneAmmo);
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
        if (actor.Equipment.HasFlag(PersonalEquipment.WoodenBucket))
        {
            AddInventoryIcon(ItemIcon.WoodenBucket,
                "Drewniane wiadro • narzędzie do transportu wody do beczek");
        }
        if (cargo is not null)
        {
            AddInventoryIcon(
                ResourceThumbnails.Create(
                    _itemIconAtlas,
                    _treePartAtlas,
                    _foodIconAtlas,
                    _resourceThumbnailTextures,
                    cargo.Value.Resource,
                    cargo.Value.FoodKind,
                    cargo.Value.Variant),
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
        traits.HasFlag(GoblinTrait.Fastidious) ? "porządnicki" : null,
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
            _minimap.SetVisibleLevel(0);
            _worldView3D.Refresh(_latestSnapshot);
            _viewModeButton.Text = "2D";
            _viewModeButton.TooltipText = "Wróć do stabilnego renderera 2D • F3";
            Update3DCameraControls();
            _inspector.Text = "Prototyp 3D • chunkowe meshe terenu 16×16 • prawdziwe rampy i klify • " +
                $"woda jako osobna powierzchnia • {_worldView3D.TerrainMeshCount} meshów terenu/wody + " +
                $"{_worldView3D.StructureMeshCount} wspólny mesh konstrukcji • dachy ukryte dla czytelności wnętrz.";
        }
        else
        {
            _worldView.Refresh(_latestSnapshot);
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
        var direction = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down") +
            Input.GetVector(
                CameraPanLeftAction,
                CameraPanRightAction,
                CameraPanUpAction,
                CameraPanDownAction);
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

    private void ApplyCameraShortcutBindings()
    {
        ApplyInputAction(CameraPanLeftAction, _shortcutSettings[GameShortcutId.CameraPanLeft]);
        ApplyInputAction(CameraPanRightAction, _shortcutSettings[GameShortcutId.CameraPanRight]);
        ApplyInputAction(CameraPanUpAction, _shortcutSettings[GameShortcutId.CameraPanUp]);
        ApplyInputAction(CameraPanDownAction, _shortcutSettings[GameShortcutId.CameraPanDown]);
    }

    private static void ApplyInputAction(string action, ShortcutStroke stroke)
    {
        if (!InputMap.HasAction(action))
        {
            InputMap.AddAction(action);
        }
        InputMap.ActionEraseEvents(action);
        InputMap.ActionAddEvent(action, new InputEventKey
        {
            Keycode = stroke.Key,
            CtrlPressed = stroke.Ctrl,
            AltPressed = stroke.Alt,
            ShiftPressed = stroke.Shift,
        });
    }

    private void UpdateLevelButtonLabels()
    {
        var up = GetNode<Button>("Interface/ActionBar/Controls/LevelControls/LevelUp");
        var down = GetNode<Button>("Interface/ActionBar/Controls/LevelControls/LevelDown");
        up.Text = $"+  {DescribeCompactShortcut(GameShortcutId.CameraLevelUp)}";
        down.Text = $"−  {DescribeCompactShortcut(GameShortcutId.CameraLevelDown)}";
    }

    private string DescribeCompactShortcut(GameShortcutId shortcut) =>
        _shortcutSettings[shortcut].ToString()
            .Replace("PageUp", "PgUp", StringComparison.OrdinalIgnoreCase)
            .Replace("Page Up", "PgUp", StringComparison.OrdinalIgnoreCase)
            .Replace("PageDown", "PgDn", StringComparison.OrdinalIgnoreCase)
            .Replace("Page Down", "PgDn", StringComparison.OrdinalIgnoreCase);

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

    private void CreateWorkshopWindow()
    {
        _workshopDetails = new Window
        {
            Title = "Prymitywny warsztat",
            Size = new Vector2I(480, 500),
            MinSize = new Vector2I(410, 360),
            Unresizable = false,
            Visible = false,
        };
        _workshopDetails.CloseRequested += _workshopDetails.Hide;
        AddChild(_workshopDetails);
        _workshopDetails.Hide();

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        _workshopDetails.AddChild(margin);

        var content = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddThemeConstantOverride("separation", 10);
        margin.AddChild(content);
        _workshopSummary = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        content.AddChild(_workshopSummary);
        content.AddChild(new Label { Text = "Dodaj recepturę do kolejki:" });
        var recipeScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddChild(recipeScroll);
        var recipes = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        recipes.AddThemeConstantOverride("separation", 5);
        recipeScroll.AddChild(recipes);
        AddWorkshopRecipeButton(recipes, CraftingRecipeKind.PrimitiveSling,
            CreateSlingIcon(), "Proca",
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.RagClothes), "Skóra", 1),
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Bone), "Kość", 1));
        AddWorkshopRecipeButton(recipes, CraftingRecipeKind.PrimitiveAxe,
            ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.WoodenAxe),
            "Prymitywna siekiera",
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Wood), "Drewno", 2),
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Stone), "Kamień", 1),
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Reeds), "Sitowie", 1));
        AddWorkshopRecipeButton(recipes, CraftingRecipeKind.PrimitivePickaxe,
            _pickaxeIcon,
            "Prymitywny kilof",
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Wood), "Drewno", 2),
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Stone), "Kamień", 2),
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Reeds), "Sitowie", 1));
        AddWorkshopRecipeButton(recipes, CraftingRecipeKind.BoneKnife,
            ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.BoneKnife), "Kościany nóż",
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Bone), "Kość", 1));
        AddWorkshopRecipeButton(recipes, CraftingRecipeKind.FightingStick,
            ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Wood), "Kij bojowy",
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Wood), "Drewno", 3));
        AddWorkshopRecipeButton(recipes, CraftingRecipeKind.StoneClub,
            ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Stone), "Maczuga",
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Wood), "Drewno", 1),
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Stone), "Kamień", 1));
        AddWorkshopRecipeButton(recipes, CraftingRecipeKind.HideClothes,
            ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.RagClothes), "Skórzany ubiór",
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.RagClothes), "Skóra", 2));
        AddWorkshopRecipeButton(recipes, CraftingRecipeKind.ReedClothes,
            ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Reeds), "Sitowiowy ubiór",
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Reeds), "Sitowie", 3));
        AddWorkshopRecipeButton(recipes, CraftingRecipeKind.PrimitiveWaterskin,
            ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.PrimitiveWaterskin),
            "Prymitywny bukłak",
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.RagClothes), "Skóra", 1));
        AddWorkshopRecipeButton(recipes, CraftingRecipeKind.ReinforcedPickaxe,
            ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Stone),
            "Wzmocniony kilof",
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Wood), "Drewno", 2),
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Stone), "Kamień", 3),
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Bone), "Kość", 1));
        AddWorkshopRecipeButton(recipes, CraftingRecipeKind.WoodenBucket,
            ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.WoodenBucket),
            "Drewniane wiadro",
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Wood), "Drewno", 1),
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Reeds), "Sitowie", 1));
        AddWorkshopRecipeButton(recipes, CraftingRecipeKind.WoodenBarrel,
            _woodenBarrelIcon,
            "Drewniana beczka",
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Wood), "Drewno", 3),
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Reeds), "Sitowie", 2));
        AddWorkshopRecipeButton(recipes, CraftingRecipeKind.WoodenBox,
            ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Cargo),
            "Drewniana skrzynka",
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Wood), "Drewno", 2),
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Reeds), "Sitowie", 1));
        AddWorkshopRecipeButton(recipes, CraftingRecipeKind.WoodenChest,
            ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Cargo),
            "Drewniana skrzynia",
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Wood), "Drewno", 4),
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Reeds), "Sitowie", 1));
        AddWorkshopRecipeButton(recipes, CraftingRecipeKind.WoodenBulkBin,
            ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Cargo),
            "Drewniany zasobnik masowy",
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Wood), "Drewno", 3),
            (ItemIcons.CreateTexture(_itemIconAtlas, ItemIcon.Reeds), "Sitowie", 2));
        AddRepeatableWorkshopRecipeButton(recipes, CraftingRecipeKind.SmeltIronBar,
            CreateResourceThumbnail(ResourceKind.Materials, ResourceVariant.IronBar),
            "Sztabka żelaza",
            (CreateResourceThumbnail(ResourceKind.Ore, ResourceVariant.IronOre),
                "Ruda żelaza", 2),
            (CreateResourceThumbnail(ResourceKind.Coal), "Węgiel", 1));
        AddRepeatableWorkshopRecipeButton(recipes, CraftingRecipeKind.SmeltCopperBar,
            CreateResourceThumbnail(ResourceKind.Materials, ResourceVariant.CopperBar),
            "Sztabka miedzi",
            (CreateResourceThumbnail(ResourceKind.Ore, ResourceVariant.CopperOre),
                "Ruda miedzi", 2),
            (CreateResourceThumbnail(ResourceKind.Coal), "Węgiel", 1));
        AddRepeatableWorkshopRecipeButton(recipes, CraftingRecipeKind.SmeltSilverBar,
            CreateResourceThumbnail(ResourceKind.Materials, ResourceVariant.SilverBar),
            "Sztabka srebra",
            (CreateResourceThumbnail(ResourceKind.Ore, ResourceVariant.SilverOre),
                "Ruda srebra", 2),
            (CreateResourceThumbnail(ResourceKind.Coal), "Węgiel", 1));
        AddRepeatableWorkshopRecipeButton(recipes, CraftingRecipeKind.SmeltGoldBar,
            CreateResourceThumbnail(ResourceKind.Materials, ResourceVariant.GoldBar),
            "Sztabka złota",
            (CreateResourceThumbnail(ResourceKind.Ore, ResourceVariant.GoldOre),
                "Ruda złota", 2),
            (CreateResourceThumbnail(ResourceKind.Coal), "Węgiel", 1));
        var close = new Button
        {
            Text = "Zamknij",
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
        };
        close.Pressed += _workshopDetails.Hide;
        content.AddChild(close);
    }

    private Texture2D CreateResourceThumbnail(
        ResourceKind resource,
        ResourceVariant variant = ResourceVariant.None) => ResourceThumbnails.Create(
            _itemIconAtlas,
            _treePartAtlas,
            _foodIconAtlas,
            _resourceThumbnailTextures,
            resource,
            FoodKind.None,
            variant);

    private void AddWorkshopRecipeButton(
        VBoxContainer recipes,
        CraftingRecipeKind recipe,
        Texture2D productIcon,
        string name,
        params (Texture2D Icon, string Name, int Quantity)[] ingredients) =>
        AddWorkshopRecipeRow(
            recipes,
            recipe,
            productIcon,
            name,
            supportsRepeating: false,
            ingredients);

    private void AddRepeatableWorkshopRecipeButton(
        VBoxContainer recipes,
        CraftingRecipeKind recipe,
        Texture2D productIcon,
        string name,
        params (Texture2D Icon, string Name, int Quantity)[] ingredients) =>
        AddWorkshopRecipeRow(
            recipes,
            recipe,
            productIcon,
            name,
            supportsRepeating: true,
            ingredients);

    private void AddWorkshopRecipeRow(
        VBoxContainer recipes,
        CraftingRecipeKind recipe,
        Texture2D productIcon,
        string name,
        bool supportsRepeating,
        IReadOnlyList<(Texture2D Icon, string Name, int Quantity)> ingredients)
    {
        var row = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(0, 56),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        row.AddThemeConstantOverride("separation", 8);
        var button = new Button
        {
            Icon = productIcon,
            ExpandIcon = true,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(52, 52),
            TooltipText = $"Zleć: {name.ToLowerInvariant()}",
        };
        button.Pressed += () => QueueWorkshopRecipe(recipe);
        row.AddChild(button);
        if (supportsRepeating)
        {
            var repeat = new Button
            {
                Text = "∞",
                ToggleMode = true,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(38, 38),
                TooltipText = $"Powtarzaj bez końca: {name.ToLowerInvariant()}",
            };
            repeat.Toggled += enabled => ConfigureWorkshopRecipeRepeat(recipe, enabled);
            row.AddChild(repeat);
            _workshopRepeatButtons.Add(recipe, repeat);
        }
        var productName = new Label
        {
            Text = name,
            CustomMinimumSize = new Vector2(130, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TooltipText = $"Produkt: {name}",
        };
        row.AddChild(productName);
        row.AddChild(new Label
        {
            Text = "←",
            VerticalAlignment = VerticalAlignment.Center,
            TooltipText = "Wymagane materiały",
        });
        foreach (var ingredient in ingredients)
        {
            AddWorkshopIngredient(row, ingredient.Icon, ingredient.Name, ingredient.Quantity);
        }
        recipes.AddChild(row);
        _workshopRecipeRows.Add(recipe, row);
    }

    private static void AddWorkshopIngredient(
        HBoxContainer row,
        Texture2D icon,
        string name,
        int quantity)
    {
        var tooltip = $"{name} • wymagane: {quantity}";
        var ingredient = new HBoxContainer
        {
            TooltipText = tooltip,
        };
        ingredient.AddThemeConstantOverride("separation", 2);
        ingredient.AddChild(new TextureRect
        {
            CustomMinimumSize = new Vector2(28, 28),
            Texture = icon,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TooltipText = tooltip,
        });
        ingredient.AddChild(new Label
        {
            Text = $"×{quantity}",
            VerticalAlignment = VerticalAlignment.Center,
            TooltipText = tooltip,
        });
        row.AddChild(ingredient);
    }

    private void ShowWorkshopDetails(GridPosition workshop)
    {
        if (!_engine.World.TryGetWorkshopKind(workshop, out var workshopKind))
        {
            return;
        }
        _selectedWorkshop = workshop;
        _workshopDetails.Title = TranslationCatalog.Get(
            _currentLocale,
            "workshops",
            "names",
            WorkshopCatalog.Get(workshopKind).Id);
        UpdateWorkshopDetails(GetDisplayedSnapshot());
        _workshopDetails.PopupCentered();
    }

    private void QueueWorkshopRecipe(CraftingRecipeKind recipe)
    {
        if (_selectedWorkshop is not { } workshop ||
            !_engine.World.TryGetWorkshopKind(workshop, out var workshopKind))
        {
            _selectedWorkshop = null;
            _workshopDetails.Hide();
            _inspector.Text = "Wybrany warsztat już nie istnieje.";
            return;
        }

        var definition = WorkshopCatalog.Get(workshopKind);
        if (!definition.SupportsRecipe(recipe, CraftingRecipeCatalog.GetRecipeLevel(recipe)))
        {
            _inspector.Text = "Ten warsztat nie obsługuje wybranej receptury.";
            return;
        }

        _engine.QueueCommand(SimulationCommand.QueueCraftingRecipe(
            _engine.CurrentTick.Next(), _commandSequence++, workshop, recipe));
        _inspector.Text = $"Warsztat {workshop}: dodano do kolejki " +
            DescribeCraftingRecipe(recipe) +
            (_speed == 0 ? " • zlecenie ruszy po wznowieniu czasu." : ".");
    }

    private void ConfigureWorkshopRecipeRepeat(CraftingRecipeKind recipe, bool enabled)
    {
        if (_updatingWorkshopRepeatButtons)
        {
            return;
        }
        if (_selectedWorkshop is not { } workshop ||
            !_engine.World.TryGetWorkshopKind(workshop, out var workshopKind) ||
            !WorkshopCatalog.Get(workshopKind).SupportsRecipe(
                recipe,
                CraftingRecipeCatalog.GetRecipeLevel(recipe)))
        {
            return;
        }

        _engine.QueueCommand(SimulationCommand.ConfigureRepeatingCraftingRecipe(
            _engine.CurrentTick.Next(),
            _commandSequence++,
            workshop,
            recipe,
            enabled));
        _inspector.Text = enabled
            ? $"Warsztat {workshop}: włączono ciągłe wykonywanie: " +
              $"{DescribeCraftingRecipe(recipe)}."
            : $"Warsztat {workshop}: wyłączono ciągłe wykonywanie: " +
              $"{DescribeCraftingRecipe(recipe)}.";
    }

    private void UpdateWorkshopDetails(SimulationSnapshot snapshot)
    {
        if (_selectedWorkshop is not { } workshop ||
            !_engine.World.TryGetWorkshopKind(workshop, out var workshopKind))
        {
            _selectedWorkshop = null;
            _workshopDetails.Hide();
            return;
        }

        var workshopDefinition = WorkshopCatalog.Get(workshopKind);
        foreach (var (recipe, row) in _workshopRecipeRows)
        {
            row.Visible = workshopDefinition.SupportsRecipe(
                recipe,
                CraftingRecipeCatalog.GetRecipeLevel(recipe));
        }

        var orders = snapshot.CraftingOrders
            .Where(order => order.Workshop == workshop)
            .OrderBy(order => order.Id)
            .ToArray();
        var repeatingRecipes = orders
            .Where(order => order.IsRepeating)
            .Select(order => order.Recipe)
            .ToHashSet();
        _updatingWorkshopRepeatButtons = true;
        foreach (var (recipe, button) in _workshopRepeatButtons)
        {
            button.ButtonPressed = repeatingRecipes.Contains(recipe);
        }
        _updatingWorkshopRepeatButtons = false;
        var stocks = new[]
        {
            ResourceKind.Wood, ResourceKind.Stone, ResourceKind.Reeds,
            ResourceKind.Bone, ResourceKind.Hide, ResourceKind.Coal,
            ResourceKind.Ore, ResourceKind.Materials,
        }.Select(resource => $"{DescribeResource(resource)} " +
            snapshot.ItemStacks.Where(stack => stack.Resource == resource)
                .Sum(stack => stack.Quantity));
        _workshopSummary.Text = $"Pozycja: {workshop}\n" +
            $"Znane zasoby: {string.Join(", ", stocks)}\n" +
            (orders.Length == 0
                ? "Kolejka jest pusta."
                : "Kolejka:\n" + string.Join("\n", orders.Select((order, index) =>
                    $"{index + 1}. {DescribeCraftingOrder(order)}")));
    }

    private void CreateLogisticsWindow()
    {
        _logisticsWindow = new Window
        {
            Title = "Logistyka twierdzy",
            Size = new Vector2I(920, 700),
            MinSize = new Vector2I(680, 440),
            Unresizable = false,
            Visible = false,
        };
        _logisticsWindow.CloseRequested += _logisticsWindow.Hide;
        AddChild(_logisticsWindow);
        _logisticsWindow.Hide();

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "top", "right", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", 14);
        }
        _logisticsWindow.AddChild(margin);
        var content = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddThemeConstantOverride("separation", 8);
        margin.AddChild(content);
        _logisticsSummary = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        content.AddChild(_logisticsSummary);
        var createNetwork = new Button
        {
            Text = "Utwórz sieć specjalistyczną",
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
        };
        createNetwork.Pressed += () =>
        {
            QueueLogisticsCommand(SimulationCommand.CreateLogisticsNetwork(
                _engine.CurrentTick.Next(), _commandSequence++));
            _inspector.Text = "Zlecono utworzenie nowej sieci logistycznej.";
        };
        content.AddChild(createNetwork);
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddChild(scroll);
        _logisticsRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _logisticsRows.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_logisticsRows);
        var close = new Button { Text = "Zamknij", SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
        close.Pressed += _logisticsWindow.Hide;
        content.AddChild(close);
    }

    private void ShowLogistics()
    {
        _managementMenu.Hide();
        UpdateLogisticsWindow(_latestSnapshot, force: true);
        _logisticsWindow.PopupCentered();
    }

    private void QueueLogisticsCommand(SimulationCommand command)
    {
        _engine.QueueCommand(command);
        _logisticsSignature = string.Empty;
    }

    private void UpdateLogisticsWindow(SimulationSnapshot snapshot, bool force = false)
    {
        var signature = string.Join('|', snapshot.LogisticsNetworks.Select(network =>
                $"{network.Id}:{network.Name}:{string.Join(',', network.AssignedHaulerIds)}:" +
                $"{string.Join(',', network.SourceStorageZoneIds)}:{string.Join(',', network.DestinationStorageZoneIds)}")) +
            "#" + string.Join('|', snapshot.StorageAreas.Select(area =>
                $"{area.Id}:{area.Name}:{area.LogisticsNetworkId}:{area.Capacity}:{area.StoredQuantity}:" +
                $"{string.Join(',', area.StorageZoneIds)}:" +
                string.Join(',', area.Footprint.Select(cell => $"{cell.X}/{cell.Y}/{cell.Z}")))) +
            "#" + string.Join('|', snapshot.StorageZones.Select(zone =>
                $"{zone.Id}:{zone.AcceptedResource}:{zone.ResourceFilter}:" +
                $"{zone.ProviderKind}:{zone.LogisticsNetworkId}")) +
            "#" + string.Join('|', snapshot.Actors.Select(actor =>
                $"{actor.Id}:{actor.Name}:{actor.IsJuvenile}"));
        if (!force && signature == _logisticsSignature)
        {
            return;
        }
        _logisticsSignature = signature;
        foreach (var child in _logisticsRows.GetChildren())
        {
            child.QueueFree();
        }

        _logisticsSummary.Text =
            $"Sieci: {snapshot.LogisticsNetworks.Count} • obszary: {snapshot.StorageAreas.Count} • " +
            $"pojemniki i składy: {snapshot.StorageZones.Count}.\n" +
            "Default automatycznie korzysta ze wszystkich dorosłych, którzy nie należą do sieci specjalistycznej.";
        foreach (var network in snapshot.LogisticsNetworks.OrderBy(network => network.Id))
        {
            var panel = new PanelContainer();
            var section = new VBoxContainer();
            section.AddThemeConstantOverride("separation", 4);
            panel.AddChild(section);
            _logisticsRows.AddChild(panel);
            var heading = new HBoxContainer();
            section.AddChild(heading);
            heading.AddChild(new Label
            {
                Text = network.IsDefault ? "Sieć domyślna" : $"Sieć {network.Id}",
                CustomMinimumSize = new Vector2(130, 0),
            });
            var name = new LineEdit
            {
                Text = network.Name,
                MaxLength = 40,
                Editable = !network.IsDefault,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            heading.AddChild(name);
            var rename = new Button { Text = "Zmień nazwę", Disabled = network.IsDefault };
            rename.Pressed += () =>
            {
                if (!string.IsNullOrWhiteSpace(name.Text))
                {
                    QueueLogisticsCommand(SimulationCommand.RenameLogisticsNetwork(
                        _engine.CurrentTick.Next(), _commandSequence++, network.Id, name.Text));
                }
            };
            heading.AddChild(rename);
            if (!network.IsDefault)
            {
                var delete = new Button
                {
                    Text = "Usuń",
                    TooltipText = "Obszary wrócą do sieci Default, a tragarze zostaną zwolnieni. Zawartość składów pozostanie bez zmian.",
                };
                delete.Pressed += () =>
                {
                    QueueLogisticsCommand(SimulationCommand.DeleteLogisticsNetwork(
                        _engine.CurrentTick.Next(), _commandSequence++, network.Id));
                    _inspector.Text =
                        $"Zlecono usunięcie sieci {network.Name}. Składy wrócą do Default; towary pozostaną na miejscu.";
                };
                heading.AddChild(delete);
            }

            section.AddChild(new Label { Text = "Przypisani tragarze:" });
            var haulers = new HFlowContainer();
            section.AddChild(haulers);
            foreach (var actor in snapshot.Actors.Where(actor => !actor.IsJuvenile).OrderBy(actor => actor.Id))
            {
                var check = new CheckButton
                {
                    Text = actor.Name,
                    ButtonPressed = network.IsDefault
                        ? !snapshot.LogisticsNetworks.Any(other =>
                            !other.IsDefault && other.AssignedHaulerIds.Contains(actor.Id))
                        : network.AssignedHaulerIds.Contains(actor.Id),
                    Disabled = network.IsDefault,
                };
                check.Toggled += assigned => QueueLogisticsCommand(
                    SimulationCommand.ConfigureLogisticsHauler(
                        _engine.CurrentTick.Next(), _commandSequence++, network.Id, actor.Id, assigned));
                haulers.AddChild(check);
            }

            section.AddChild(new Label { Text = "Dozwolone źródła:" });
            var sources = new HFlowContainer();
            section.AddChild(sources);
            foreach (var zone in snapshot.StorageZones.OrderBy(zone => zone.Id))
            {
                var check = new CheckButton
                {
                    Text = $"{zone.Id} {DescribeStorageProvider(zone.ProviderKind)}",
                    ButtonPressed = network.SourceStorageZoneIds.Contains(zone.Id),
                    Disabled = network.IsDefault,
                };
                check.Toggled += included => QueueLogisticsCommand(
                    SimulationCommand.ConfigureLogisticsSource(
                        _engine.CurrentTick.Next(), _commandSequence++, network.Id, zone.Id, included));
                sources.AddChild(check);
            }
        }

        _logisticsRows.AddChild(new HSeparator());
        _logisticsRows.AddChild(new Label { Text = "Obszary i pojemniki", ThemeTypeVariation = "HeaderMedium" });
        foreach (var area in snapshot.StorageAreas.OrderBy(area => area.Id))
        {
            AddStorageAreaManagementRow(snapshot, area);
        }
    }

    private void AddStorageAreaManagementRow(
        SimulationSnapshot snapshot,
        StorageAreaSnapshot area)
    {
        var panel = new PanelContainer();
        var section = new VBoxContainer();
        section.AddThemeConstantOverride("separation", 4);
        panel.AddChild(section);
        _logisticsRows.AddChild(panel);
        var heading = new HBoxContainer();
        section.AddChild(heading);
        heading.AddChild(new Label
        {
            Text = $"Obszar {area.Id}",
            CustomMinimumSize = new Vector2(110, 0),
        });
        var name = new LineEdit
        {
            Text = area.Name,
            MaxLength = 40,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        heading.AddChild(name);
        var rename = new Button { Text = "Zmień nazwę" };
        rename.Pressed += () =>
        {
            if (!string.IsNullOrWhiteSpace(name.Text))
            {
                QueueLogisticsCommand(SimulationCommand.RenameStorageArea(
                    _engine.CurrentTick.Next(), _commandSequence++, area.Id, name.Text));
            }
        };
        heading.AddChild(rename);
        var networkChoice = new OptionButton { CustomMinimumSize = new Vector2(170, 0) };
        var availableNetworks = snapshot.LogisticsNetworks.OrderBy(network => network.Id).ToArray();
        foreach (var network in availableNetworks)
        {
            networkChoice.AddItem(network.Name);
            if (network.Id == area.LogisticsNetworkId)
            {
                networkChoice.Select(networkChoice.ItemCount - 1);
            }
        }
        networkChoice.ItemSelected += index =>
        {
            var networkId = availableNetworks[(int)index].Id;
            QueueLogisticsCommand(SimulationCommand.ConfigureStorageAreaNetwork(
                _engine.CurrentTick.Next(), _commandSequence++, area.Id, networkId));
        };
        heading.AddChild(networkChoice);
        section.AddChild(new Label
        {
            Text = $"Pola: {area.Footprint.Count} • pojemność {area.StoredQuantity}/{area.Capacity}",
        });
        var actions = new HBoxContainer();
        section.AddChild(actions);
        var resize = new Button
        {
            Text = "Zmień rozmiar",
            TooltipText = "Wskaż nowy prostokąt. Nie można nakładać obszarów ani wykluczyć pola z istniejącym pojemnikiem.",
        };
        resize.Pressed += () => BeginStorageAreaResize(area.Id, area.Name);
        actions.AddChild(resize);
        var dissolve = new Button
        {
            Text = "Rozwiąż obszar",
            TooltipText = "Usuwa wspólny obszar. Pojemniki i zawartość pozostają jako bezpieczne składy jednopunktowe w tej samej sieci.",
        };
        dissolve.Pressed += () =>
        {
            QueueLogisticsCommand(SimulationCommand.DissolveStorageArea(
                _engine.CurrentTick.Next(), _commandSequence++, area.Id));
            _inspector.Text =
                $"Zlecono rozwiązanie obszaru {area.Name}. Pojemniki i towary pozostaną na miejscu.";
        };
        actions.AddChild(dissolve);

        foreach (var zone in snapshot.StorageZones
                     .Where(zone => zone.StorageAreaId == area.Id)
                     .OrderBy(zone => zone.Id))
        {
            var row = new VBoxContainer();
            section.AddChild(row);
            var provider = new HBoxContainer();
            row.AddChild(provider);
            provider.AddChild(new Label
            {
                Text = $"{zone.Id} • {DescribeStorageProvider(zone.ProviderKind)} " +
                    $"{zone.StoredQuantity}/{zone.Capacity}",
                CustomMinimumSize = new Vector2(300, 0),
            });
            if (zone.ProviderKind is StorageProviderKind.WoodenBox or
                StorageProviderKind.WoodenChest or StorageProviderKind.WoodenBulkBin)
            {
                var filters = new HFlowContainer
                {
                    TooltipText = "Filtr dotyczy nowych dostaw; istniejąca zawartość nie jest niszczona.",
                };
                row.AddChild(filters);
                foreach (var resource in SolidContainerFilterCategories)
                {
                    var resourceFilter = ToStorageResourceFilter(resource);
                    var check = new CheckButton
                    {
                        Text = DescribeResource(resource),
                        ButtonPressed = zone.ResourceFilter.HasFlag(resourceFilter),
                        TooltipText = "Włącz lub wyłącz tę kategorię dla przyszłych dostaw.",
                    };
                    check.Toggled += included => QueueLogisticsCommand(
                        SimulationCommand.ConfigureStorageFilterResource(
                            _engine.CurrentTick.Next(),
                            _commandSequence++,
                            zone.Id,
                            resource,
                            included));
                    filters.AddChild(check);
                }
            }
            else
            {
                provider.AddChild(new Label
                {
                    Text = $"filtr: {DescribeResource(zone.AcceptedResource)}",
                });
            }
        }
    }

    private void BeginStorageAreaResize(EntityId areaId, string areaName)
    {
        SelectBuildMode((long)BuildMode.StorageArea);
        if (_buildMode != BuildMode.StorageArea)
        {
            return;
        }

        _resizingStorageAreaId = areaId;
        _logisticsWindow.Hide();
        _inspector.Text =
            $"Zmiana rozmiaru {areaName}: przeciągnij nowy prostokąt do 256 pól • wszystkie pojemniki muszą pozostać wewnątrz • PPM lub Esc anuluje";
    }

    private static readonly ResourceKind[] SolidContainerFilterCategories =
    [
        ResourceKind.Food,
        ResourceKind.Wood,
        ResourceKind.Reeds,
        ResourceKind.Stone,
        ResourceKind.Bone,
        ResourceKind.Coal,
        ResourceKind.Ore,
        ResourceKind.Hide,
        ResourceKind.Equipment,
        ResourceKind.Materials,
    ];

    private static StorageResourceFilter ToStorageResourceFilter(ResourceKind resource) =>
        resource == ResourceKind.Any
            ? StorageResourceFilter.SolidGoods
            : (StorageResourceFilter)(1 << ((int)resource - 1));

    private void CreatePlannerWindow()
    {
        _plannerWindow = new Window
        {
            Title = "Planer plemienia",
            Size = new Vector2I(760, 600),
            MinSize = new Vector2I(600, 380),
            Unresizable = false,
            Visible = false,
        };
        _plannerWindow.CloseRequested += _plannerWindow.Hide;
        AddChild(_plannerWindow);
        _plannerWindow.Hide();

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "top", "right", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", 14);
        }
        _plannerWindow.AddChild(margin);

        var content = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddThemeConstantOverride("separation", 10);
        margin.AddChild(content);
        _plannerSummary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        content.AddChild(_plannerSummary);
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddChild(scroll);
        _plannerRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _plannerRows.AddThemeConstantOverride("separation", 7);
        scroll.AddChild(_plannerRows);
        var close = new Button { Text = "Zamknij", SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
        close.Pressed += _plannerWindow.Hide;
        content.AddChild(close);
    }

    private void ShowPlanner()
    {
        UpdatePlanner(_latestSnapshot, force: true);
        _plannerWindow.PopupCentered();
    }

    private void UpdatePlanner(SimulationSnapshot snapshot, bool force = false)
    {
        var signature = string.Join('|', snapshot.WorkDesignations.Select(item =>
                $"{item.Id}:{item.OrderId}:{item.Kind}:{item.Target}:{item.Priority}:{item.IsSuspended}")) + "#" +
            string.Join('|', snapshot.ConstructionSites.Select(site =>
                $"{site.Id}:{site.Priority}:{site.Materials.Sum(material => material.DeliveredQuantity)}:{site.RemainingWorkTicks}"));
        if (!force && signature == _plannerSignature)
        {
            return;
        }
        _plannerSignature = signature;
        foreach (var child in _plannerRows.GetChildren())
        {
            child.QueueFree();
        }

        var workGroups = snapshot.WorkDesignations
            .GroupBy(item => item.OrderId)
            .OrderByDescending(group => group.Max(item => item.Priority))
            .ThenBy(group => group.Key)
            .ToArray();
        _plannerSummary.Text = $"Zlecenia obszarowe: {workGroups.Length} grup / " +
            $"{snapshot.WorkDesignations.Count} celów • budowy: {snapshot.ConstructionSites.Count}.\n" +
            "Strzałki zmieniają priorytet dispatchera. „Obszar” zachowuje stare cele aż do zatwierdzenia nowego zaznaczenia.";
        if (workGroups.Length == 0 && snapshot.ConstructionSites.Count == 0)
        {
            _plannerRows.AddChild(new Label { Text = "Planer jest pusty." });
            return;
        }

        foreach (var group in workGroups)
        {
            var targets = group.ToArray();
            var kind = targets[0].Kind;
            var priority = targets.Max(item => item.Priority);
            var isSuspended = targets.All(item => item.IsSuspended);
            var minimum = new GridPosition(
                targets.Min(item => item.Target.X),
                targets.Min(item => item.Target.Y),
                targets.Min(item => item.Target.Z));
            var maximum = new GridPosition(
                targets.Max(item => item.Target.X),
                targets.Max(item => item.Target.Y),
                targets.Max(item => item.Target.Z));
            var active = snapshot.Actors.Count(actor => IsActorDoing(actor, kind, targets));
            var readiness = isSuspended
                ? "wstrzymane przez gracza"
                : DescribeWorkOrderReadiness(snapshot, kind, active, targets);
            AddPlannerRow(
                $"{DescribeWorkDesignation(kind)} • {targets.Length} celów • " +
                $"zasięg {minimum}–{maximum} • {DescribeStoragePriority(priority)}" +
                $" • {readiness}",
                priority,
                value => SetWorkPriority(group.Key, kind, value),
                isSuspended,
                () => SetWorkSuspension(group.Key, kind, !isSuspended),
                () => BeginPlannerAreaEdit(
                    group.Key, kind, priority, isSuspended, targets[0].Target),
                () => CancelWorkGroup(group.Key, kind),
                () => FocusPlannerTarget(targets[0].Target));
        }

        foreach (var site in snapshot.ConstructionSites
                     .OrderByDescending(site => site.Priority)
                     .ThenBy(site => site.Id))
        {
            AddPlannerRow(
                $"Budowa {DescribeConstruction(site.Kind)} • {site.Anchor} • " +
                $"{DescribeStoragePriority(site.Priority)} • " +
                DescribeConstructionReadiness(_engine.InspectConstructionReadiness(
                    site.Id,
                    evaluateReachability: false)),
                site.Priority,
                value => SetConstructionPriority(site.Id, value),
                isSuspended: false,
                toggleSuspension: null,
                edit: null,
                cancel: null,
                () => FocusPlannerTarget(site.Anchor));
        }
    }

    private void AddPlannerRow(
        string description,
        StoragePriority priority,
        Action<StoragePriority> setPriority,
        bool isSuspended,
        Action? toggleSuspension,
        Action? edit,
        Action? cancel,
        Action focus)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var label = new Label
        {
            Text = description,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        row.AddChild(label);
        var down = new Button { Text = "↓", TooltipText = "Obniż priorytet", Disabled = priority == StoragePriority.Low };
        down.Pressed += () => setPriority((StoragePriority)((int)priority - 1));
        row.AddChild(down);
        var up = new Button { Text = "↑", TooltipText = "Podnieś priorytet", Disabled = priority == StoragePriority.Urgent };
        up.Pressed += () => setPriority((StoragePriority)((int)priority + 1));
        row.AddChild(up);
        var show = new Button { Text = "Pokaż" };
        show.Pressed += focus;
        row.AddChild(show);
        if (toggleSuspension is not null)
        {
            var suspension = new Button
            {
                Text = isSuspended ? "Wznów" : "Wstrzymaj",
                TooltipText = isSuspended
                    ? "Ponownie udostępnij zlecenie dispatcherowi"
                    : "Zachowaj zlecenie, ale przerwij i nie przydzielaj pracy",
            };
            suspension.Pressed += toggleSuspension;
            row.AddChild(suspension);
        }
        if (edit is not null)
        {
            var editButton = new Button { Text = "Obszar", TooltipText = "Ponownie wskaż obszar zlecenia" };
            editButton.Pressed += edit;
            row.AddChild(editButton);
        }
        if (cancel is not null)
        {
            var cancelButton = new Button { Text = "Anuluj" };
            cancelButton.Pressed += cancel;
            row.AddChild(cancelButton);
        }
        _plannerRows.AddChild(row);
    }

    private void SetWorkPriority(
        EntityId orderId,
        WorkDesignationKind kind,
        StoragePriority priority)
    {
        _engine.QueueCommand(SimulationCommand.ConfigureWorkPriority(
            _engine.CurrentTick.Next(), _commandSequence++, orderId, priority));
        _plannerSignature = string.Empty;
        _inspector.Text = $"Zlecenie „{DescribeWorkDesignation(kind)}”: priorytet {DescribeStoragePriority(priority)}.";
    }

    private void SetConstructionPriority(EntityId id, StoragePriority priority)
    {
        _engine.QueueCommand(SimulationCommand.ConfigureConstructionPriority(
            _engine.CurrentTick.Next(), _commandSequence++, id, priority));
        _plannerSignature = string.Empty;
    }

    private void SetWorkSuspension(
        EntityId orderId,
        WorkDesignationKind kind,
        bool isSuspended)
    {
        _engine.QueueCommand(SimulationCommand.ConfigureWorkSuspension(
            _engine.CurrentTick.Next(), _commandSequence++, orderId, isSuspended));
        _plannerSignature = string.Empty;
        _inspector.Text = isSuspended
            ? $"Wstrzymano zlecenie „{DescribeWorkDesignation(kind)}”."
            : $"Wznowiono zlecenie „{DescribeWorkDesignation(kind)}”.";
    }

    private void BeginPlannerAreaEdit(
        EntityId orderId,
        WorkDesignationKind kind,
        StoragePriority priority,
        bool isSuspended,
        GridPosition target)
    {
        var mode = ToWorkMode(kind);
        if (mode == WorkMode.None)
        {
            return;
        }
        FocusPlannerTarget(target);
        SelectWorkMode((long)mode);
        _replacingWorkOrderId = orderId;
        _replacementWorkPriority = priority;
        _replacementWorkSuspended = isSuspended;
        _plannerWindow.Hide();
        _inspector.Text = $"Nowy obszar: {DescribeWorkDesignation(kind)}. Stare cele pozostaną aktywne do zatwierdzenia zaznaczenia.";
    }

    private void FocusPlannerTarget(GridPosition target)
    {
        if (!_use3DView && _visibleLevel != target.Z)
        {
            _visibleLevel = target.Z;
            _worldView.SetVisibleLevel(_visibleLevel);
            _minimap.SetVisibleLevel(_visibleLevel);
            UpdateLayerToolAvailability();
        }
        CenterCameraOn(target);
    }

    private void CancelWorkGroup(EntityId orderId, WorkDesignationKind kind)
    {
        _engine.QueueCommand(SimulationCommand.ClearWorkDesignationOrder(
            _engine.CurrentTick.Next(), _commandSequence++, orderId));
        _plannerSignature = string.Empty;
        _inspector.Text = $"Anulowano wszystkie cele: {DescribeWorkDesignation(kind)}.";
    }

    private static bool IsActorDoing(
        ActorSnapshot actor,
        WorkDesignationKind kind,
        IReadOnlyList<WorkDesignationSnapshot> targets) => kind switch
    {
        WorkDesignationKind.GatherFood or WorkDesignationKind.GatherReeds =>
            actor.Job.Kind == ActorJobKind.Forage &&
            targets.Any(target => target.Target == actor.Job.Target),
        WorkDesignationKind.GatherBrushwood or WorkDesignationKind.GatherStone =>
            actor.Job.Kind == ActorJobKind.Haul &&
            targets.Any(target => target.TargetEntityId == actor.Job.SourceStackId),
        WorkDesignationKind.UprootBerryBush => actor.Job.Kind == ActorJobKind.ClearVegetation &&
            targets.Any(target => target.Target == actor.Job.Target),
        WorkDesignationKind.FellTree => actor.Job.Kind == ActorJobKind.FellTree &&
            targets.Any(target => target.Id == actor.Job.SourceStackId),
        WorkDesignationKind.QuarryBoulder => actor.Job.Kind == ActorJobKind.QuarryBoulder &&
            targets.Any(target => target.Id == actor.Job.SourceStackId),
        WorkDesignationKind.MineRock => actor.Job.Kind == ActorJobKind.MineRock &&
            targets.Any(target => target.Id == actor.Job.SourceStackId),
        WorkDesignationKind.CarveRampDown or WorkDesignationKind.CarveRampUp =>
            actor.Job.Kind == ActorJobKind.CarveRamp &&
            targets.Any(target => target.Id == actor.Job.SourceStackId),
        WorkDesignationKind.Scout => actor.Job.Kind == ActorJobKind.Explore &&
            targets.Any(target => target.Target == actor.Job.Target),
        WorkDesignationKind.HuntAnimal => actor.Job.Kind == ActorJobKind.HuntAnimal &&
            targets.Any(target => target.Id == actor.Job.SourceStackId),
        WorkDesignationKind.CleanBlood => actor.Job.Kind == ActorJobKind.CleanBlood &&
            targets.Any(target => target.Id == actor.Job.SourceStackId),
        _ => false,
    };

    private string DescribeWorkOrderReadiness(
        SimulationSnapshot snapshot,
        WorkDesignationKind kind,
        int activeWorkers,
        IReadOnlyList<WorkDesignationSnapshot> targets)
    {
        if (activeWorkers > 0)
        {
            return $"w toku: {activeWorkers}";
        }

        var living = snapshot.Actors.Where(actor => actor.Health > 0).ToArray();
        if (living.Length == 0)
        {
            return "wstrzymane: brak żywych robotników";
        }
        if (kind == WorkDesignationKind.FellTree &&
            living.All(actor => !actor.Equipment.HasFlag(PersonalEquipment.WoodenAxe)))
        {
            return "wstrzymane: brak siekiery";
        }
        if (kind is WorkDesignationKind.MineRock or WorkDesignationKind.CarveRampDown or
            WorkDesignationKind.CarveRampUp)
        {
            return DescribeMiningReadiness(living, kind, targets);
        }
        if (kind == WorkDesignationKind.QuarryBoulder &&
            living.All(actor => !MiningCapabilityPolicy.HasPickaxe(actor.Equipment)))
        {
            return "wstrzymane: brak kilofa";
        }
        if (kind is WorkDesignationKind.GatherBrushwood or WorkDesignationKind.GatherStone)
        {
            var resource = kind == WorkDesignationKind.GatherBrushwood
                ? ResourceKind.Wood
                : ResourceKind.Stone;
            if (!snapshot.StorageZones.Any(zone =>
                    zone.StoredQuantity < zone.Capacity &&
                    zone.ResourceFilter.HasFlag(ToStorageResourceFilter(resource))))
            {
                return "wstrzymane: brak miejsca w pasującym składzie";
            }
        }
        return "wykonalne; oczekuje na dispatchera";
    }

    private string DescribeMiningReadiness(
        IReadOnlyList<ActorSnapshot> living,
        WorkDesignationKind kind,
        IReadOnlyList<WorkDesignationSnapshot> targets)
    {
        var builders = living
            .Where(actor => actor.KnownSkills.HasFlag(GoblinSkill.Building))
            .ToArray();
        if (builders.Length == 0)
        {
            return "wstrzymane: brak goblina ze znajomością budowania";
        }
        if (builders.All(actor => !MiningCapabilityPolicy.HasPickaxe(actor.Equipment)))
        {
            return "wstrzymane: brak kilofa";
        }

        var availableCells = targets
            .Select(target => TryGetAvailableMiningCell(kind, target.Target))
            .Where(cell => cell.HasValue)
            .Select(cell => cell!.Value)
            .ToArray();
        if (availableCells.Length == 0)
        {
            return kind == WorkDesignationKind.MineRock
                ? "oczekuje: front tunelu musi zostać odsłonięty"
                : "oczekuje: miejsce pochylni nie jest teraz dostępne";
        }
        if (builders.Any(actor => availableCells.Any(cell =>
                MiningCapabilityPolicy.CanMine(
                    cell,
                    actor.Equipment,
                    actor.Experience.Building))))
        {
            return "wykonalne; oczekuje na dispatchera";
        }

        if (availableCells.All(cell => cell.Rock == RockKind.Obsidian) &&
            builders.All(actor =>
                !actor.Equipment.HasFlag(PersonalEquipment.ReinforcedPickaxe)))
        {
            return "wstrzymane: obsydian wymaga wzmocnionego kilofa";
        }

        var requiredLevel = availableCells.Min(cell =>
            MiningCapabilityPolicy.RequiredSkillLevel(cell.Rock));
        return $"wstrzymane: wymagany poziom budowania {requiredLevel}";
    }

    private CaveCell? TryGetAvailableMiningCell(WorkDesignationKind kind, GridPosition target)
    {
        if (kind == WorkDesignationKind.MineRock)
        {
            return _engine.World.CanExcavateRock(target) && _engine.Map.IsRockPosition(target)
                ? _engine.Map.GetRockCell(target)
                : null;
        }

        var carveDown = kind == WorkDesignationKind.CarveRampDown;
        var available = carveDown
            ? _engine.World.CanCarveRampDown(target)
            : _engine.World.CanCarveRampUp(target);
        return available ? _engine.World.GetRampExcavationCell(target, carveDown) : null;
    }

    private static string DescribeWorkDesignation(WorkDesignationKind kind) => kind switch
    {
        WorkDesignationKind.GatherFood => "zbieranie żywności",
        WorkDesignationKind.GatherReeds => "zbieranie sitowia",
        WorkDesignationKind.GatherBrushwood => "zbieranie chrustu",
        WorkDesignationKind.GatherStone => "zbieranie kamienia",
        WorkDesignationKind.UprootBerryBush => "karczowanie krzaków",
        WorkDesignationKind.FellTree => "wyrąb",
        WorkDesignationKind.QuarryBoulder => "rozbijanie głazów",
        WorkDesignationKind.MineRock => "wydobycie skały",
        WorkDesignationKind.CarveRampDown => "pochylnia w dół",
        WorkDesignationKind.CarveRampUp => "pochylnia w górę",
        WorkDesignationKind.Scout => "zwiad",
        WorkDesignationKind.HuntAnimal => "polowanie",
        WorkDesignationKind.CleanBlood => "sprzątanie krwi",
        _ => "praca",
    };

    private string DescribeFluid(CellFluidKind fluid) => fluid switch
    {
        CellFluidKind.Water => Ui("fluid-objects", "water"),
        CellFluidKind.Lava => Ui("fluid-objects", "lava"),
        _ => Ui("fluid-objects", "dangerous-fluid"),
    };

    private void CreateWorldContextMenu()
    {
        _worldContextMenu = new PopupMenu
        {
            Name = "WorldContextMenu",
            MinSize = new Vector2I(245, 0),
        };
        _worldContextMenu.IdPressed += HandleWorldContextAction;
        AddChild(_worldContextMenu);

        _entitySelectorMenu = new PopupPanel
        {
            Name = "EntitySelectorMenu",
            MinSize = new Vector2I(330, 0),
            Theme = _gameUiTheme,
        };
        var selectorMargin = new MarginContainer();
        selectorMargin.AddThemeConstantOverride("margin_left", 6);
        selectorMargin.AddThemeConstantOverride("margin_top", 6);
        selectorMargin.AddThemeConstantOverride("margin_right", 6);
        selectorMargin.AddThemeConstantOverride("margin_bottom", 6);
        _entitySelectorRows = new VBoxContainer();
        _entitySelectorRows.AddThemeConstantOverride("separation", 2);
        selectorMargin.AddChild(_entitySelectorRows);
        _entitySelectorMenu.AddChild(selectorMargin);
        AddChild(_entitySelectorMenu);

        _constructionRemovalDialog = new ConfirmationDialog
        {
            Name = "ConstructionRemovalDialog",
            Title = Ui("context-menu", "confirm-removal"),
            MinSize = new Vector2I(430, 0),
        };
        _constructionRemovalDialog.Confirmed += ConfirmConstructionRemoval;
        AddChild(_constructionRemovalDialog);
    }

    private bool TryShowWorldContextMenu(Vector2 screenPosition)
    {
        if (!_hasActiveSession || _mainMenu.Visible)
        {
            return false;
        }

        var clicked = ScreenToVisibleCell(screenPosition);
        var snapshot = GetDisplayedSnapshot();
        if (!snapshot.GetVisibility(clicked, _engine.Map.Width).IsDiscovered())
        {
            return false;
        }

        _contextRemovalTarget = ConstructionRemovalTarget.None;
        _contextRemovalEntityId = EntityId.None;
        _contextMenuScreenPosition = screenPosition;
        _entitySelectorMenu.Hide();
        _worldContextMenu.Hide();

        var entityChoices = CreateContextEntityChoices(snapshot, clicked);
        if (entityChoices.Count > 1)
        {
            ShowContextEntitySelector(screenPosition, entityChoices);
            return true;
        }
        if (entityChoices.Count == 1 && entityChoices[0].Target.Kind is
                ContextEntityKind.Goblin or ContextEntityKind.Animal or
                ContextEntityKind.HumanVillager or ContextEntityKind.ItemStack)
        {
            ShowContextEntityActions(entityChoices[0].Target, snapshot, screenPosition);
            return true;
        }

        var corpse = snapshot.Corpses
            .Where(item => item.Position == clicked)
            .OrderBy(item => item.Id)
            .FirstOrDefault();
        if (corpse is not null)
        {
            ShowCorpseContextMenu(screenPosition, snapshot, corpse);
            return true;
        }

        var camp = snapshot.WorldObjects.FirstOrDefault(worldObject =>
            worldObject.Kind == WorldObjectKind.GoblinFieldCamp &&
            worldObject.Owner == WorldObjectOwner.GoblinTribe &&
            IsCampContextHit(worldObject, clicked));
        if (camp is null ||
            !snapshot.GetVisibility(camp.Anchor, _engine.Map.Width).IsDiscovered())
        {
            return TryShowConstructionContextMenu(screenPosition, snapshot, clicked);
        }

        _contextCorpseId = EntityId.None;
        _contextCampAnchor = camp.Anchor;
        var floorCells = camp.GetAbsoluteParts()
            .Where(part => part.Part.Kind == WorldObjectPartKind.Floor)
            .Select(part => part.Position)
            .ToHashSet();
        var occupants = snapshot.Actors
            .Where(actor => actor.Health > 0 && floorCells.Contains(actor.Position))
            .OrderBy(actor => actor.Id)
            .ToArray();
        var storage = snapshot.StorageZones.FirstOrDefault(zone =>
            zone.Position == camp.Anchor && zone.AcceptedResource == ResourceKind.Food);

        _worldContextMenu.Clear();
        _worldContextMenu.AddItem(UiFormat("context-menu", "camp-heading", camp.Anchor.Z));
        _worldContextMenu.SetItemDisabled(_worldContextMenu.ItemCount - 1, true);
        _worldContextMenu.AddItem(
            storage.Id == EntityId.None
                ? UiFormat("context-menu", "camp-no-storage",
                    DescribeCampOccupancy(occupants.Length))
                : UiFormat("context-menu", "camp-storage",
                    storage.StoredQuantity, storage.Capacity,
                    DescribeCampOccupancy(occupants.Length)));
        _worldContextMenu.SetItemDisabled(_worldContextMenu.ItemCount - 1, true);
        _worldContextMenu.AddItem(DescribeCampRaidStatus(snapshot, camp.Anchor));
        _worldContextMenu.SetItemDisabled(_worldContextMenu.ItemCount - 1, true);
        _worldContextMenu.AddSeparator();
        _worldContextMenu.AddItem(Ui("context-menu", "edit-raid"),
            (int)WorldContextAction.EditRaid);
        _worldContextMenu.SetItemDisabled(
            _worldContextMenu.GetItemIndex((int)WorldContextAction.EditRaid),
            snapshot.RaidPhase is GoblinRaidPhase.Marching or GoblinRaidPhase.Looting or
                GoblinRaidPhase.Returning);
        _worldContextMenu.AddItem(
            snapshot.RaidPhase is GoblinRaidPhase.Preparing or GoblinRaidPhase.Ready
                ? Ui("context-menu", "suspend-raid-preparation")
                : snapshot.RaidPhase == GoblinRaidPhase.Marching
                    ? Ui("context-menu", "recall-raid")
                : snapshot.RaidPhase is GoblinRaidPhase.Looting or GoblinRaidPhase.Returning
                    ? Ui("context-menu", "raid-in-progress")
                : Ui("context-menu", "prepare-raid"),
            (int)WorldContextAction.ToggleRaidPreparation);
        _worldContextMenu.SetItemDisabled(
            _worldContextMenu.GetItemIndex((int)WorldContextAction.ToggleRaidPreparation),
            snapshot.RaidPhase is GoblinRaidPhase.Looting or GoblinRaidPhase.Returning);
        _worldContextMenu.AddItem(
            UiFormat("context-menu", "select-raid-target", snapshot.RaidPlan.TargetRadius),
            (int)WorldContextAction.SelectRaidTarget);
        _worldContextMenu.SetItemDisabled(
            _worldContextMenu.GetItemIndex((int)WorldContextAction.SelectRaidTarget),
            snapshot.RaidPhase is GoblinRaidPhase.Marching or GoblinRaidPhase.Looting or
                GoblinRaidPhase.Returning);
        if (snapshot.RaidPhase == GoblinRaidPhase.Ready)
        {
            _worldContextMenu.AddItem(Ui("context-menu", "attack"),
                (int)WorldContextAction.LaunchRaid);
        }
        _worldContextMenu.AddSeparator();
        _worldContextMenu.AddItem(
            occupants.Length == 0
                ? Ui("context-menu", "no-camp-goblins")
                : UiFormat("context-menu", "select-camp-goblins", occupants.Length),
            (int)WorldContextAction.SelectCampOccupants);
        _worldContextMenu.SetItemDisabled(
            _worldContextMenu.GetItemIndex((int)WorldContextAction.SelectCampOccupants),
            occupants.Length == 0);
        _worldContextMenu.AddItem(
            Ui("context-menu", "open-camp-storage"),
            (int)WorldContextAction.OpenCampStorage);
        _worldContextMenu.SetItemDisabled(
            _worldContextMenu.GetItemIndex((int)WorldContextAction.OpenCampStorage),
            storage.Id == EntityId.None);
        _worldContextMenu.AddSeparator();
        _worldContextMenu.AddItem(
            Ui("context-menu", "dismantle-camp"),
            (int)WorldContextAction.DismantleConstruction);
        _worldContextMenu.SetItemDisabled(
            _worldContextMenu.GetItemIndex((int)WorldContextAction.DismantleConstruction),
            snapshot.RaidPhase != GoblinRaidPhase.None &&
                snapshot.RaidRallyPoint == camp.Anchor);
        _contextRemovalTarget = ConstructionRemovalTarget.WorldObject;
        _contextRemovalEntityId = new EntityId(camp.Id.Value);
        _contextRemovalPosition = camp.Anchor;
        _worldContextMenu.Position = new Vector2I(
            Mathf.RoundToInt(screenPosition.X),
            Mathf.RoundToInt(screenPosition.Y));
        _worldContextMenu.Popup();
        return true;
    }

    private IReadOnlyList<ContextEntityChoice> CreateContextEntityChoices(
        SimulationSnapshot snapshot,
        GridPosition clicked)
    {
        var choices = new List<ContextEntityChoice>();
        choices.AddRange(snapshot.Actors
            .Where(actor => actor.Health > 0 && actor.Position == clicked)
            .OrderBy(actor => actor.Id)
            .Select(actor => new ContextEntityChoice(
                new ContextEntityTarget(ContextEntityKind.Goblin, actor.Id.Value, clicked),
                UiFormat("context-menu", "goblin-heading", actor.Name),
                Section: 1,
                TextColorOverride: GoblinEntitySelectorColor)));
        choices.AddRange(snapshot.ConstructionSites
            .Where(site => site.Footprint.Contains(clicked))
            .OrderBy(site => site.Id)
            .Select(site => new ContextEntityChoice(
                new ContextEntityTarget(
                    ContextEntityKind.ConstructionSite,
                    site.Id.Value,
                    site.Anchor),
                UiFormat("context-menu", "construction-heading",
                    DescribeConstruction(site.Kind)),
                Section: 2)));
        choices.AddRange(snapshot.StorageZones
            .Where(zone => zone.Position == clicked)
            .OrderBy(zone => zone.Id)
            .Select(zone => new ContextEntityChoice(
                new ContextEntityTarget(ContextEntityKind.StorageZone, zone.Id.Value, clicked),
                UiFormat("context-menu", "storage-heading",
                    DescribeStorageProvider(zone.ProviderKind)),
                Section: 2)));
        choices.AddRange(snapshot.WorldObjects
            .Where(worldObject => worldObject.GetAbsoluteParts().Any(part => part.Position == clicked))
            .OrderBy(worldObject => worldObject.Id)
            .Select(worldObject => new ContextEntityChoice(
                new ContextEntityTarget(
                    ContextEntityKind.WorldObject,
                    worldObject.Id.Value,
                    worldObject.Anchor),
                DescribeWorldObject(worldObject),
                Section: 2)));
        choices.AddRange(snapshot.Corpses
            .Where(corpse => corpse.Position == clicked)
            .OrderBy(corpse => corpse.Id)
            .Select(corpse => new ContextEntityChoice(
                new ContextEntityTarget(ContextEntityKind.Corpse, corpse.Id.Value, clicked),
                UiFormat("context-menu", "corpse-heading", corpse.Name),
                Section: 2)));
        choices.AddRange(snapshot.Animals
            .Where(animal => animal.Position == clicked)
            .OrderBy(animal => animal.Id)
            .Select(animal => new ContextEntityChoice(
                new ContextEntityTarget(ContextEntityKind.Animal, animal.Id, clicked),
                UiFormat("context-menu", "animal-heading",
                    Ui("animal-kinds", animal.Kind.ToString())),
                Section: 2)));
        choices.AddRange(snapshot.HumanVillage.Villagers
            .Where(villager => villager.Health > 0 && villager.Position == clicked)
            .OrderBy(villager => villager.Id)
            .Select(villager => new ContextEntityChoice(
                new ContextEntityTarget(
                    ContextEntityKind.HumanVillager,
                    checked((ulong)villager.Id),
                    clicked),
                UiFormat("context-menu", "human-heading", villager.Name),
                Section: 2)));
        choices.AddRange(snapshot.ItemStacks
            .Where(stack => stack.Location.Kind is ItemLocationKind.Ground or
                    ItemLocationKind.StorageZone &&
                stack.Location.Position == clicked)
            .OrderBy(stack => stack.Id)
            .Select(stack => new ContextEntityChoice(
                new ContextEntityTarget(ContextEntityKind.ItemStack, stack.Id.Value, clicked),
                $"{DescribeResourceVariant(stack.Resource, stack.FoodKind, stack.Variant)} ×{stack.Quantity}",
                Section: 3)));
        return choices;
    }

    private void ShowContextEntitySelector(
        Vector2 screenPosition,
        IReadOnlyList<ContextEntityChoice> choices)
    {
        _contextEntityTarget = default;
        _worldContextMenu.Hide();
        foreach (var child in _entitySelectorRows.GetChildren())
        {
            _entitySelectorRows.RemoveChild(child);
            child.QueueFree();
        }

        var heading = new Label
        {
            Text = UiFormat("context-menu", "objects-at", choices[0].Target.Position),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        heading.AddThemeColorOverride("font_color", GameUiTheme.MutedText);
        _entitySelectorRows.AddChild(heading);
        var previousSection = 0;
        foreach (var choice in choices.OrderBy(choice => choice.Section).ThenBy(choice => choice.Label))
        {
            if (choice.Section != previousSection)
            {
                var section = new Label
                {
                    Text = choice.Section switch
                    {
                        1 => Ui("context-menu", "section-goblins"),
                        2 => Ui("context-menu", "section-world"),
                        _ => Ui("context-menu", "section-items"),
                    },
                };
                section.AddThemeColorOverride("font_color", GameUiTheme.MutedText);
                _entitySelectorRows.AddChild(section);
                previousSection = choice.Section;
            }

            var selectedChoice = choice;
            var button = new Button
            {
                Text = choice.Label,
                Alignment = HorizontalAlignment.Left,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(318, 30),
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            ApplyEntitySelectorTextColor(button, choice.TextColorOverride);
            button.Pressed += () =>
            {
                _entitySelectorMenu.Hide();
                ShowContextEntityActions(
                    selectedChoice.Target,
                    GetDisplayedSnapshot(),
                    _contextMenuScreenPosition);
            };
            _entitySelectorRows.AddChild(button);
        }
        _entitySelectorMenu.Popup();
        var menuSize = (Vector2)_entitySelectorMenu.Size;
        var viewportSize = GetViewport().GetVisibleRect().Size;
        _entitySelectorMenu.Position = new Vector2I(
            Mathf.RoundToInt(Math.Clamp(
                screenPosition.X,
                4f,
                Math.Max(4f, viewportSize.X - menuSize.X - 4f))),
            Mathf.RoundToInt(Math.Clamp(
                screenPosition.Y,
                4f,
                Math.Max(4f, viewportSize.Y - menuSize.Y - 4f))));
    }

    private static void ApplyEntitySelectorTextColor(Button button, Color? colorOverride)
    {
        if (colorOverride is not { } color)
        {
            return;
        }

        button.AddThemeColorOverride("font_color", color);
        button.AddThemeColorOverride("font_hover_color", color.Lightened(0.12f));
        button.AddThemeColorOverride("font_pressed_color", color.Lightened(0.2f));
        button.AddThemeColorOverride("font_focus_color", color.Lightened(0.12f));
    }

    private void ShowContextEntityActions(
        ContextEntityTarget target,
        SimulationSnapshot snapshot,
        Vector2 screenPosition)
    {
        _entitySelectorMenu.Hide();
        _contextEntityTarget = target;
        _contextCampAnchor = null;
        _contextCorpseId = EntityId.None;
        _contextRemovalTarget = ConstructionRemovalTarget.None;
        _contextRemovalEntityId = EntityId.None;

        if (target.Kind == ContextEntityKind.Corpse)
        {
            var corpse = snapshot.Corpses.FirstOrDefault(item => item.Id.Value == target.Id);
            if (corpse is not null)
            {
                ShowCorpseContextMenu(screenPosition, snapshot, corpse);
            }
            return;
        }

        _worldContextMenu.Clear();
        switch (target.Kind)
        {
            case ContextEntityKind.Goblin:
                var actor = snapshot.Actors.FirstOrDefault(item => item.Id.Value == target.Id);
                if (actor.Id == EntityId.None)
                {
                    return;
                }
                AddDisabledContextHeading(UiFormat("context-menu", "goblin-heading", actor.Name));
                _worldContextMenu.AddItem(Ui("context-menu", "show-details"),
                    (int)WorldContextAction.OpenEntityDetails);
                _worldContextMenu.AddSeparator(Ui("context-menu", "direct-orders"));
                _worldContextMenu.AddItem(Ui("context-menu", "flee"),
                    (int)WorldContextAction.OrderGoblinFlee);
                _worldContextMenu.AddItem(Ui("context-menu", "sleep"),
                    (int)WorldContextAction.OrderGoblinSleep);
                _worldContextMenu.AddItem(
                    Ui("context-menu", "suspend-dispatcher"),
                    (int)WorldContextAction.SuspendGoblinDispatcher);
                break;
            case ContextEntityKind.ConstructionSite:
                var site = snapshot.ConstructionSites.FirstOrDefault(item => item.Id.Value == target.Id);
                if (site is null)
                {
                    return;
                }
                AddDisabledContextHeading(UiFormat("context-menu", "construction-heading",
                    DescribeConstruction(site.Kind)));
                _worldContextMenu.AddItem(Ui("context-menu", "show-details"),
                    (int)WorldContextAction.OpenEntityDetails);
                _worldContextMenu.AddSeparator();
                _worldContextMenu.AddItem(
                    Ui("context-menu", "cancel-construction"),
                    (int)WorldContextAction.CancelConstruction);
                _contextRemovalTarget = ConstructionRemovalTarget.PendingConstruction;
                _contextRemovalEntityId = site.Id;
                _contextRemovalPosition = site.Anchor;
                break;
            case ContextEntityKind.StorageZone:
                var zone = snapshot.StorageZones.FirstOrDefault(item => item.Id.Value == target.Id);
                if (zone.Id == EntityId.None)
                {
                    return;
                }
                AddDisabledContextHeading(UiFormat("context-menu", "storage-heading",
                    DescribeStorageProvider(zone.ProviderKind)));
                _worldContextMenu.AddItem(Ui("context-menu", "edit-details"),
                    (int)WorldContextAction.OpenEntityDetails);
                _worldContextMenu.AddSeparator();
                _worldContextMenu.AddItem(Ui("context-menu", "remove-storage"),
                    (int)WorldContextAction.DismantleConstruction);
                _contextRemovalTarget = ConstructionRemovalTarget.StorageZone;
                _contextRemovalEntityId = zone.Id;
                _contextRemovalPosition = zone.Position;
                break;
            case ContextEntityKind.WorldObject:
                var worldObject = snapshot.WorldObjects.FirstOrDefault(item => item.Id.Value == target.Id);
                if (worldObject is null)
                {
                    return;
                }
                AddDisabledContextHeading(DescribeWorldObject(worldObject));
                _worldContextMenu.AddItem(Ui("context-menu", "edit-details"),
                    (int)WorldContextAction.OpenEntityDetails);
                _worldContextMenu.AddSeparator();
                _worldContextMenu.AddItem(
                    Ui("context-menu", "dismantle-construction"),
                    (int)WorldContextAction.DismantleConstruction);
                _contextRemovalTarget = ConstructionRemovalTarget.WorldObject;
                _contextRemovalEntityId = new EntityId(worldObject.Id.Value);
                _contextRemovalPosition = worldObject.Anchor;
                break;
            case ContextEntityKind.ItemStack:
                var stack = snapshot.ItemStacks.FirstOrDefault(item => item.Id.Value == target.Id);
                if (stack.Id == EntityId.None)
                {
                    return;
                }
                AddDisabledContextHeading(
                    $"{DescribeResourceVariant(stack.Resource, stack.FoodKind, stack.Variant)} ×{stack.Quantity}");
                var hasSingleGoblin = _selectedActorIds.Count == 1;
                _worldContextMenu.AddItem(
                    Ui("context-menu", "pick-up-stack"),
                    (int)WorldContextAction.PickUpItem);
                _worldContextMenu.SetItemDisabled(_worldContextMenu.ItemCount - 1, !hasSingleGoblin);
                if (stack.Resource == ResourceKind.Equipment &&
                    EquipmentCatalog.FindDefinition(stack.Variant) is not null)
                {
                    _worldContextMenu.AddItem(
                        Ui("context-menu", "equip-item"),
                        (int)WorldContextAction.EquipItem);
                    _worldContextMenu.SetItemDisabled(_worldContextMenu.ItemCount - 1, !hasSingleGoblin);
                }
                _worldContextMenu.AddItem(
                    Ui("context-menu", "prioritize-hauling"),
                    (int)WorldContextAction.PrioritizeItemHauling);
                if (!hasSingleGoblin)
                {
                    _worldContextMenu.AddSeparator();
                    AddDisabledContextHeading(Ui("context-menu", "select-one-goblin"));
                }
                break;
            case ContextEntityKind.Animal:
                var animal = snapshot.Animals.FirstOrDefault(item => item.Id == target.Id);
                AddDisabledContextHeading(UiFormat("context-menu", "animal-heading",
                    Ui("animal-kinds", animal.Kind.ToString())));
                AddDisabledContextHeading(Ui("context-menu", "no-animal-orders"));
                break;
            case ContextEntityKind.HumanVillager:
                var villager = snapshot.HumanVillage.Villagers.FirstOrDefault(item =>
                    item.Id == checked((int)target.Id));
                AddDisabledContextHeading(UiFormat("context-menu", "human-heading", villager.Name));
                AddDisabledContextHeading(Ui("context-menu", "not-tribe-member"));
                break;
        }
        PositionWorldContextMenu(screenPosition);
    }

    private void AddDisabledContextHeading(string text)
    {
        _worldContextMenu.AddItem(text);
        _worldContextMenu.SetItemDisabled(_worldContextMenu.ItemCount - 1, true);
    }

    private void PositionWorldContextMenu(Vector2 screenPosition)
    {
        _worldContextMenu.Position = new Vector2I(
            Mathf.RoundToInt(screenPosition.X),
            Mathf.RoundToInt(screenPosition.Y));
        _worldContextMenu.Popup();
    }

    private bool TryShowConstructionContextMenu(
        Vector2 screenPosition,
        SimulationSnapshot snapshot,
        GridPosition clicked)
    {
        var pending = snapshot.ConstructionSites
            .Where(site => site.Footprint.Contains(clicked))
            .OrderBy(site => site.Id)
            .FirstOrDefault();
        if (pending is not null)
        {
            _contextCampAnchor = null;
            _contextCorpseId = EntityId.None;
            _contextRemovalTarget = ConstructionRemovalTarget.PendingConstruction;
            _contextRemovalEntityId = pending.Id;
            _contextRemovalPosition = pending.Anchor;
            ShowConstructionRemovalMenu(
                screenPosition,
                UiFormat("context-menu", "construction-heading",
                    DescribeConstruction(pending.Kind)),
                Ui("context-menu", "cancel-construction"),
                WorldContextAction.CancelConstruction);
            return true;
        }

        var objects = snapshot.WorldObjects
            .Where(worldObject =>
                worldObject.Owner == WorldObjectOwner.GoblinTribe &&
                worldObject.GetAbsoluteParts().Any(part => part.Position == clicked))
            .OrderBy(worldObject => IsSurfaceConstruction(worldObject.Kind) ? 1 : 0)
            .ThenBy(worldObject => worldObject.Id)
            .ToArray();
        var primaryObject = objects.FirstOrDefault(worldObject =>
            !IsSurfaceConstruction(worldObject.Kind));
        var storage = snapshot.StorageZones
            .Where(zone => zone.Position == clicked)
            .OrderBy(zone => zone.Id)
            .FirstOrDefault();
        primaryObject ??= storage.Id == EntityId.None ? objects.FirstOrDefault() : null;

        if (primaryObject is not null)
        {
            _contextCampAnchor = null;
            _contextCorpseId = EntityId.None;
            _contextRemovalTarget = ConstructionRemovalTarget.WorldObject;
            _contextRemovalEntityId = new EntityId(primaryObject.Id.Value);
            _contextRemovalPosition = primaryObject.Anchor;
            ShowConstructionRemovalMenu(
                screenPosition,
                UiFormat("context-menu", "structure-heading",
                    DescribeWorldObject(primaryObject)),
                Ui("context-menu", "dismantle-construction"),
                WorldContextAction.DismantleConstruction);
            return true;
        }

        if (storage.Id != EntityId.None)
        {
            _contextCampAnchor = null;
            _contextCorpseId = EntityId.None;
            _contextRemovalTarget = ConstructionRemovalTarget.StorageZone;
            _contextRemovalEntityId = storage.Id;
            _contextRemovalPosition = storage.Position;
            ShowConstructionRemovalMenu(
                screenPosition,
                UiFormat("context-menu", "storage-heading",
                    DescribeStorageProvider(storage.ProviderKind)),
                Ui("context-menu", "remove-storage"),
                WorldContextAction.DismantleConstruction);
            return true;
        }

        return false;
    }

    private void ShowConstructionRemovalMenu(
        Vector2 screenPosition,
        string heading,
        string actionLabel,
        WorldContextAction action)
    {
        _worldContextMenu.Clear();
        _worldContextMenu.AddItem(heading);
        _worldContextMenu.SetItemDisabled(_worldContextMenu.ItemCount - 1, true);
        _worldContextMenu.AddSeparator();
        _worldContextMenu.AddItem(actionLabel, (int)action);
        _worldContextMenu.Position = new Vector2I(
            Mathf.RoundToInt(screenPosition.X),
            Mathf.RoundToInt(screenPosition.Y));
        _worldContextMenu.Popup();
    }

    private static bool IsSurfaceConstruction(WorldObjectKind kind) => kind is
        WorldObjectKind.WoodenWalkway or WorldObjectKind.BasaltWalkway or
        WorldObjectKind.WoodenFloor or WorldObjectKind.StoneFloor or
        WorldObjectKind.WoodenRamp or WorldObjectKind.StoneRamp;

    private string DescribeCampOccupancy(int occupantCount)
    {
        var capacity = SimulationDefinitions.FieldCampCapacity;
        return occupantCount <= capacity
            ? $"{occupantCount}/{capacity}"
            : UiFormat("context-menu", "leaving-camp",
                capacity, occupantCount - capacity);
    }

    private string DescribeCampRaidStatus(
        SimulationSnapshot snapshot,
        GridPosition campAnchor)
    {
        if (snapshot.RaidPhase == GoblinRaidPhase.None)
        {
            return snapshot.RaidPartyIds.Count == 0
                ? Ui("context-menu", "raid-inactive-no-party")
                : UiFormat("context-menu", "raid-inactive-plan",
                    snapshot.RaidPartyIds.Count, SimulationDefinitions.FieldCampCapacity);
        }
        if (snapshot.RaidRallyPoint != campAnchor)
        {
            return Ui("context-menu", "raid-other-camp");
        }

        return snapshot.RaidPhase switch
        {
            GoblinRaidPhase.Preparing => Ui("context-menu", "raid-party-preparing"),
            GoblinRaidPhase.Ready => Ui("context-menu", "raid-party-ready"),
            GoblinRaidPhase.Suspended => Ui("context-menu", "raid-party-suspended"),
            GoblinRaidPhase.Marching => Ui("context-menu", "raid-party-marching"),
            GoblinRaidPhase.Looting => Ui("context-menu", "raid-party-looting"),
            GoblinRaidPhase.Returning => Ui("context-menu", "raid-party-returning"),
            _ => Ui("context-menu", "raid-party-unknown"),
        };
    }

    private void ShowCorpseContextMenu(
        Vector2 screenPosition,
        SimulationSnapshot snapshot,
        CorpseSnapshot corpse)
    {
        _contextCampAnchor = null;
        _contextCorpseId = corpse.Id;
        var hasCamp = snapshot.WorldObjects.Any(item =>
            item.Kind == WorldObjectKind.GoblinFieldCamp &&
            item.Owner == WorldObjectOwner.GoblinTribe);
        _worldContextMenu.Clear();
        _worldContextMenu.AddItem(UiFormat("context-menu", "corpse-heading", corpse.Name));
        _worldContextMenu.SetItemDisabled(_worldContextMenu.ItemCount - 1, true);
        _worldContextMenu.AddItem(
            UiFormat("context-menu", "corpse-summary",
                corpse.EdiblePortions, corpse.Contents.Count));
        _worldContextMenu.SetItemDisabled(_worldContextMenu.ItemCount - 1, true);
        _worldContextMenu.AddSeparator(Ui("context-menu", "corpse-orders"));
        AddCorpseContextAction(
            CorpseActionLabel(Ui("context-menu", "loot-corpse"),
                corpse.Directives.HasFlag(CorpseDirective.LootContents)),
            WorldContextAction.LootCorpse,
            corpse.Contents.Count > 0);
        AddCorpseContextAction(
            CorpseActionLabel(Ui("context-menu", "consume-corpse"),
                corpse.Directives.HasFlag(CorpseDirective.Consume)),
            WorldContextAction.ConsumeCorpse,
            corpse.EdiblePortions > 0);
        AddCorpseContextAction(
            CorpseActionLabel(Ui("context-menu", "recover-corpse"),
                corpse.Directives.HasFlag(CorpseDirective.RecoverToCamp)),
            WorldContextAction.RecoverCorpse,
            hasCamp);
        AddCorpseContextAction(
            CorpseActionLabel(Ui("context-menu", "recover-and-bud"),
                corpse.Directives.HasFlag(CorpseDirective.RecoverAndBudAtCamp)),
            WorldContextAction.RecoverAndBudCorpse,
            hasCamp);
        AddCorpseContextAction(
            CorpseActionLabel(Ui("context-menu", "bud-in-place"),
                corpse.Directives.HasFlag(CorpseDirective.BudInPlace)),
            WorldContextAction.BudCorpseInPlace,
            enabled: true);
        if (corpse.Directives != CorpseDirective.None)
        {
            _worldContextMenu.AddSeparator();
            AddCorpseContextAction(
                Ui("context-menu", "clear-corpse-orders"),
                WorldContextAction.ClearCorpseDirectives,
                enabled: true);
        }
        if (!hasCamp)
        {
            _worldContextMenu.AddSeparator();
            _worldContextMenu.AddItem(Ui("context-menu", "camp-required"));
            _worldContextMenu.SetItemDisabled(_worldContextMenu.ItemCount - 1, true);
        }
        _worldContextMenu.Position = new Vector2I(
            Mathf.RoundToInt(screenPosition.X),
            Mathf.RoundToInt(screenPosition.Y));
        _worldContextMenu.Popup();
    }

    private static string CorpseActionLabel(string label, bool selected) =>
        selected ? $"✓ {label}" : label;

    private void AddCorpseContextAction(
        string label,
        WorldContextAction action,
        bool enabled)
    {
        _worldContextMenu.AddItem(label, (int)action);
        _worldContextMenu.SetItemDisabled(
            _worldContextMenu.GetItemIndex((int)action),
            !enabled);
    }

    private static bool IsCampContextHit(WorldObjectSnapshot camp, GridPosition clicked)
    {
        if (camp.Anchor.Z != clicked.Z)
        {
            return false;
        }

        var footprint = camp.GetAbsoluteParts()
            .Where(part => part.Part.Kind == WorldObjectPartKind.Floor)
            .Select(part => part.Position)
            .ToArray();
        return footprint.Length > 0 &&
            clicked.X >= footprint.Min(position => position.X) - 1 &&
            clicked.X <= footprint.Max(position => position.X) + 1 &&
            clicked.Y >= footprint.Min(position => position.Y) - 1 &&
            clicked.Y <= footprint.Max(position => position.Y) + 1;
    }

    private void HandleWorldContextAction(long actionId)
    {
        var action = (WorldContextAction)actionId;
        if (action is WorldContextAction.CancelConstruction or
                WorldContextAction.DismantleConstruction)
        {
            ShowConstructionRemovalConfirmation();
            return;
        }

        if (action is WorldContextAction.OpenEntityDetails or
            WorldContextAction.OrderGoblinFlee or
            WorldContextAction.OrderGoblinSleep or
            WorldContextAction.SuspendGoblinDispatcher or
            WorldContextAction.PickUpItem or
            WorldContextAction.EquipItem or
            WorldContextAction.PrioritizeItemHauling)
        {
            HandleContextEntityAction(action);
            return;
        }

        if (_contextCorpseId != EntityId.None)
        {
            HandleCorpseContextAction(action);
            _contextCorpseId = EntityId.None;
            return;
        }

        var campAnchor = _contextCampAnchor;
        _contextCampAnchor = null;
        if (campAnchor is null)
        {
            return;
        }

        var snapshot = GetDisplayedSnapshot();
        var camp = snapshot.WorldObjects.FirstOrDefault(worldObject =>
            worldObject.Kind == WorldObjectKind.GoblinFieldCamp &&
            worldObject.Owner == WorldObjectOwner.GoblinTribe &&
            worldObject.Anchor == campAnchor.Value);
        if (camp is null)
        {
            return;
        }

        switch (action)
        {
            case WorldContextAction.EditRaid:
                ShowRaidWindow(camp.Anchor);
                break;
            case WorldContextAction.ToggleRaidPreparation:
                var executeAt = _engine.CurrentTick.Next();
                var suspendPreparation = snapshot.RaidPhase is GoblinRaidPhase.Preparing or
                    GoblinRaidPhase.Ready or GoblinRaidPhase.Marching;
                _engine.QueueCommand(suspendPreparation
                    ? SimulationCommand.SuspendRaidPreparation(executeAt, _commandSequence++)
                    : SimulationCommand.AttackHumanVillage(
                        executeAt,
                        _commandSequence++,
                        camp.Anchor));
                _inspector.Text = snapshot.RaidPhase == GoblinRaidPhase.Marching
                    ? Ui("context-feedback", "raid-recalled")
                    : suspendPreparation
                    ? Ui("context-feedback", "raid-suspended")
                    : UiFormat("context-feedback", "raid-preparing", camp.Anchor);
                if (_speed == 0)
                {
                    _inspector.Text += Ui("context-feedback", "after-resume");
                }
                break;
            case WorldContextAction.SelectRaidTarget:
                BeginRaidTargetSelection(snapshot);
                break;
            case WorldContextAction.LaunchRaid:
                _engine.QueueCommand(SimulationCommand.LaunchRaid(
                    _engine.CurrentTick.Next(),
                    _commandSequence++));
                _inspector.Text = Ui("context-feedback", "attack-ordered") +
                    (_speed == 0 ? Ui("context-feedback", "raid-after-resume") : string.Empty);
                break;
            case WorldContextAction.SelectCampOccupants:
                SelectCampOccupants(snapshot, camp);
                break;
            case WorldContextAction.OpenCampStorage:
                var storage = snapshot.StorageZones.FirstOrDefault(zone =>
                    zone.Position == camp.Anchor && zone.AcceptedResource == ResourceKind.Food);
                if (storage.Id != EntityId.None)
                {
                    ShowStorageDetails(storage);
                }
                break;
        }
    }

    private void HandleContextEntityAction(WorldContextAction action)
    {
        var target = _contextEntityTarget;
        var snapshot = GetDisplayedSnapshot();
        switch (action)
        {
            case WorldContextAction.OpenEntityDetails:
                switch (target.Kind)
                {
                    case ContextEntityKind.Goblin:
                        SelectActor(new EntityId(target.Id), showDetails: true);
                        break;
                    case ContextEntityKind.ConstructionSite:
                        var site = snapshot.ConstructionSites.FirstOrDefault(item =>
                            item.Id.Value == target.Id);
                        if (site is not null)
                        {
                            ShowConstructionDetails(site);
                        }
                        break;
                    case ContextEntityKind.StorageZone:
                        var zone = snapshot.StorageZones.FirstOrDefault(item =>
                            item.Id.Value == target.Id);
                        if (zone.Id != EntityId.None)
                        {
                            ShowStorageDetails(zone);
                        }
                        break;
                    case ContextEntityKind.WorldObject:
                        var worldObject = snapshot.WorldObjects.FirstOrDefault(item =>
                            item.Id.Value == target.Id);
                        if (worldObject is null)
                        {
                            break;
                        }
                        if (worldObject.Kind == WorldObjectKind.GoblinFieldCamp)
                        {
                            ShowRaidWindow(worldObject.Anchor);
                        }
                        else if (_engine.World.TryGetWorkshopKind(worldObject.Anchor, out _))
                        {
                            ShowWorkshopDetails(worldObject.Anchor);
                        }
                        else
                        {
                            _inspector.Text = UiFormat("context-feedback", "no-settings",
                                worldObject.Anchor, DescribeWorldObject(worldObject));
                        }
                        break;
                }
                break;
            case WorldContextAction.OrderGoblinFlee:
                SubmitContextCommand(
                    SimulationCommand.OrderActorFlee(
                        _engine.CurrentTick.Next(),
                        _commandSequence++,
                        new EntityId(target.Id)),
                    Ui("context-feedback", "flee-ordered"));
                break;
            case WorldContextAction.OrderGoblinSleep:
                SubmitContextCommand(
                    SimulationCommand.OrderActorSleep(
                        _engine.CurrentTick.Next(),
                        _commandSequence++,
                        new EntityId(target.Id)),
                    Ui("context-feedback", "sleep-ordered"));
                break;
            case WorldContextAction.SuspendGoblinDispatcher:
                SubmitContextCommand(
                    SimulationCommand.SuspendActorDispatcher(
                        _engine.CurrentTick.Next(),
                        _commandSequence++,
                        new EntityId(target.Id)),
                    Ui("context-feedback", "dispatcher-suspended"));
                break;
            case WorldContextAction.PickUpItem:
                if (TryGetSingleSelectedActor(out var pickupActor))
                {
                    SubmitContextCommand(
                        SimulationCommand.OrderItemPickup(
                            _engine.CurrentTick.Next(),
                            _commandSequence++,
                            pickupActor,
                            new EntityId(target.Id)),
                        Ui("context-feedback", "pickup-ordered"));
                }
                break;
            case WorldContextAction.EquipItem:
                if (TryGetSingleSelectedActor(out var equipActor))
                {
                    SubmitContextCommand(
                        SimulationCommand.EquipItem(
                            _engine.CurrentTick.Next(),
                            _commandSequence++,
                            equipActor,
                            new EntityId(target.Id)),
                        Ui("context-feedback", "equip-ordered"));
                }
                break;
            case WorldContextAction.PrioritizeItemHauling:
                SubmitContextCommand(
                    SimulationCommand.PrioritizeItemHauling(
                        _engine.CurrentTick.Next(),
                        _commandSequence++,
                        new EntityId(target.Id)),
                    Ui("context-feedback", "haul-prioritized"));
                break;
        }
        _contextEntityTarget = default;
    }

    private bool TryGetSingleSelectedActor(out EntityId actorId)
    {
        if (_selectedActorIds.Count == 1)
        {
            actorId = _selectedActorIds.Single();
            return true;
        }
        actorId = EntityId.None;
        _inspector.Text = Ui("context-feedback", "one-goblin-required");
        return false;
    }

    private void SubmitContextCommand(SimulationCommand command, string acceptedMessage)
    {
        SubmitCommand(command);
        if (_speed > 0)
        {
            _inspector.Text = acceptedMessage;
        }
    }

    private void ShowConstructionRemovalConfirmation()
    {
        if (_contextRemovalTarget == ConstructionRemovalTarget.None ||
            _contextRemovalEntityId == EntityId.None)
        {
            return;
        }

        var pending = _contextRemovalTarget == ConstructionRemovalTarget.PendingConstruction;
        _constructionRemovalDialog.Title = pending
            ? Ui("context-menu", "confirm-cancel-construction")
            : Ui("context-menu", "confirm-dismantle");
        _constructionRemovalDialog.DialogText = pending
            ? Ui("context-menu", "confirm-cancel-construction-text")
            : _contextRemovalTarget == ConstructionRemovalTarget.StorageZone
                ? Ui("context-menu", "confirm-remove-storage-text")
                : Ui("context-menu", "confirm-dismantle-text");
        _constructionRemovalDialog.OkButtonText = pending
            ? Ui("context-menu", "cancel-construction-confirm")
            : Ui("context-menu", "dismantle-confirm");
        _constructionRemovalDialog.PopupCentered();
    }

    private void ConfirmConstructionRemoval()
    {
        if (_contextRemovalTarget == ConstructionRemovalTarget.None ||
            _contextRemovalEntityId == EntityId.None)
        {
            return;
        }

        var executeAt = _engine.CurrentTick.Next();
        var command = _contextRemovalTarget switch
        {
            ConstructionRemovalTarget.PendingConstruction =>
                SimulationCommand.CancelConstruction(
                    executeAt,
                    _commandSequence++,
                    _contextRemovalEntityId),
            ConstructionRemovalTarget.WorldObject =>
                SimulationCommand.DismantleWorldObject(
                    executeAt,
                    _commandSequence++,
                    new WorldObjectId(_contextRemovalEntityId.Value),
                    _contextRemovalPosition),
            ConstructionRemovalTarget.StorageZone =>
                SimulationCommand.DismantleStorageZone(
                    executeAt,
                    _commandSequence++,
                    _contextRemovalEntityId,
                    _contextRemovalPosition),
            _ => default,
        };
        _engine.QueueCommand(command);
        _inspector.Text = _contextRemovalTarget == ConstructionRemovalTarget.PendingConstruction
            ? Ui("context-feedback", "construction-cancelled")
            : Ui("context-feedback", "dismantle-ordered");
        if (_speed == 0)
        {
            _inspector.Text += Ui("context-feedback", "after-resume");
        }
        _contextRemovalTarget = ConstructionRemovalTarget.None;
        _contextRemovalEntityId = EntityId.None;
        _contextCampAnchor = null;
    }

    private void HandleCorpseContextAction(WorldContextAction action)
    {
        var snapshot = GetDisplayedSnapshot();
        var corpse = snapshot.Corpses.FirstOrDefault(item => item.Id == _contextCorpseId);
        if (corpse is null)
        {
            return;
        }
        var directives = corpse.Directives;
        switch (action)
        {
            case WorldContextAction.LootCorpse:
                directives ^= CorpseDirective.LootContents;
                break;
            case WorldContextAction.ConsumeCorpse:
                directives ^= CorpseDirective.Consume;
                break;
            case WorldContextAction.RecoverCorpse:
                directives = SetCorpseHandling(
                    directives,
                    CorpseDirective.RecoverToCamp);
                break;
            case WorldContextAction.RecoverAndBudCorpse:
                directives = SetCorpseHandling(
                    directives,
                    CorpseDirective.RecoverAndBudAtCamp);
                break;
            case WorldContextAction.BudCorpseInPlace:
                directives = SetCorpseHandling(
                    directives,
                    CorpseDirective.BudInPlace);
                break;
            case WorldContextAction.ClearCorpseDirectives:
                directives = CorpseDirective.None;
                break;
            default:
                return;
        }

        _engine.QueueCommand(SimulationCommand.ConfigureCorpseDirectives(
            _engine.CurrentTick.Next(),
            _commandSequence++,
            corpse.Id,
            directives));
        _inspector.Text = directives == CorpseDirective.None
            ? UiFormat("context-feedback", "corpse-orders-cleared", corpse.Name)
            : UiFormat("context-feedback", "corpse-orders-updated", corpse.Name);
    }

    private static CorpseDirective SetCorpseHandling(
        CorpseDirective directives,
        CorpseDirective selected)
    {
        var handling = CorpseDirective.RecoverToCamp |
            CorpseDirective.RecoverAndBudAtCamp |
            CorpseDirective.BudInPlace;
        return (directives & handling) == selected
            ? directives & ~handling
            : (directives & ~handling) | selected;
    }

    private void SelectCampOccupants(
        SimulationSnapshot snapshot,
        WorldObjectSnapshot camp)
    {
        var floorCells = camp.GetAbsoluteParts()
            .Where(part => part.Part.Kind == WorldObjectPartKind.Floor)
            .Select(part => part.Position)
            .ToHashSet();
        var occupants = snapshot.Actors
            .Where(actor => actor.Health > 0 && floorCells.Contains(actor.Position))
            .OrderBy(actor => actor.Id)
            .Select(actor => actor.Id)
            .ToArray();
        _selectedActorIds.Clear();
        _selectedActorIds.UnionWith(occupants);
        ApplyActorSelection(occupants.FirstOrDefault());
        _inspector.Text = occupants.Length == 0
            ? UiFormat("context-feedback", "camp-empty", camp.Anchor)
            : UiFormat("context-feedback", "camp-goblins-selected",
                occupants.Length, camp.Anchor);
    }

    private void CreateRaidWindow()
    {
        _raidWindow = new Window
        {
            Title = "Oddział wyprawy",
            Size = new Vector2I(620, 720),
            MinSize = new Vector2I(420, 420),
            Unresizable = false,
            Visible = false,
        };
        _raidWindow.CloseRequested += _raidWindow.Hide;
        AddChild(_raidWindow);
        _raidWindow.Hide();

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

        _raidEngagement = new OptionButton();
        _raidEngagement.AddItem("Atakuj tylko strażników", 0);
        _raidEngagement.AddItem("Atakuj wszystkich", 1);
        content.AddChild(_raidEngagement);

        var directiveGrid = new GridContainer { Columns = 2 };
        directiveGrid.AddThemeConstantOverride("h_separation", 12);
        content.AddChild(directiveGrid);
        AddRaidDirectiveCheck(directiveGrid, RaidDirective.LootEquipment, "Zabierz sprzęt");
        AddRaidDirectiveCheck(directiveGrid, RaidDirective.LootSupplies, "Zabierz zapasy");
        AddRaidDirectiveCheck(directiveGrid, RaidDirective.LootFood, "Zabierz żywność");
        AddRaidDirectiveCheck(directiveGrid, RaidDirective.ConsumeCorpses, "Pożryj zwłoki");
        AddRaidDirectiveCheck(directiveGrid, RaidDirective.BurnBuildings, "Spal budynki");
        AddRaidDirectiveCheck(directiveGrid, RaidDirective.DemolishBuildings, "Wyburz budynki");
        AddRaidDirectiveCheck(directiveGrid, RaidDirective.ContinueWhileTargetsVisible,
            "Ścigaj uciekających w obszarze najazdu");
        AddRaidDirectiveCheck(directiveGrid, RaidDirective.AutoLaunchWhenReady,
            "Atakuj automatycznie po przygotowaniu");
        content.AddChild(new Label { Text = "Postępowanie ze zwłokami:" });
        _raidCorpseHandling = new OptionButton();
        _raidCorpseHandling.AddItem("Pozostaw zwłoki", (int)RaidCorpseHandlingMode.None);
        _raidCorpseHandling.AddItem("Tylko zanieś do obozu", (int)RaidCorpseHandlingMode.RecoverToCamp);
        _raidCorpseHandling.AddItem(
            "Zanieś i zapyl w obozie",
            (int)RaidCorpseHandlingMode.RecoverAndBudAtCamp);
        _raidCorpseHandling.AddItem("Zapyl na miejscu", (int)RaidCorpseHandlingMode.BudInPlace);
        content.AddChild(_raidCorpseHandling);
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
        _raidAutoAssignButton = new Button
        {
            Text = "Dobierz automatycznie",
            TooltipText = "Wybierz do pięciu dorosłych goblinów: najlepiej uzbrojonych, " +
                "a następnie najzdrowszych i najbardziej wypoczętych.",
        };
        _raidAutoAssignButton.Pressed += AutoAssignRaidDraft;
        buttons.AddChild(_raidAutoAssignButton);
        var cancel = new Button { Text = "Zamknij" };
        cancel.Pressed += _raidWindow.Hide;
        buttons.AddChild(cancel);
        _raidStartButton = new Button { Text = "Zapisz plan" };
        _raidStartButton.Pressed += StartSelectedRaid;
        buttons.AddChild(_raidStartButton);
    }

    private void AddRaidDirectiveCheck(
        Control parent,
        RaidDirective directive,
        string text)
    {
        var check = new CheckButton { Text = text };
        _raidDirectiveChecks.Add(directive, check);
        parent.AddChild(check);
    }

    private void ShowRaidWindow() => ShowRaidWindow(null);

    private void ShowRaidWindow(GridPosition? rallyPoint)
    {
        var snapshot = _latestSnapshot;
        _raidDraftRallyPoint = rallyPoint;
        _raidEngagement.Select(snapshot.RaidPlan.Has(RaidDirective.AttackAll) ? 1 : 0);
        _raidCorpseHandling.Select(snapshot.RaidPlan.Has(RaidDirective.BudCorpsesInPlace)
            ? (int)RaidCorpseHandlingMode.BudInPlace
            : snapshot.RaidPlan.Has(RaidDirective.BudCorpses)
                ? (int)RaidCorpseHandlingMode.RecoverAndBudAtCamp
                : snapshot.RaidPlan.Has(RaidDirective.RecoverCorpses)
                    ? (int)RaidCorpseHandlingMode.RecoverToCamp
                    : (int)RaidCorpseHandlingMode.None);
        foreach (var (directive, check) in _raidDirectiveChecks)
        {
            check.ButtonPressed = snapshot.RaidPlan.Has(directive);
        }
        _raidDraftIds.Clear();
        if (snapshot.RaidRosterConfigured)
        {
            _raidDraftIds.UnionWith(snapshot.RaidPartyIds);
        }
        else if (_selectedActorIds.Count > 0)
        {
            _raidDraftIds.UnionWith(_selectedActorIds
                .Where(id => snapshot.Actors.Any(actor =>
                    actor.Id == id && actor.Health > 0 && !actor.IsJuvenile))
                .OrderBy(id => id)
                .Take(SimulationDefinitions.FieldCampCapacity));
        }
        else if (rallyPoint is not null)
        {
            var campFloor = snapshot.WorldObjects
                .Where(worldObject =>
                    worldObject.Kind == WorldObjectKind.GoblinFieldCamp &&
                    worldObject.Owner == WorldObjectOwner.GoblinTribe &&
                    worldObject.Anchor == rallyPoint.Value)
                .SelectMany(worldObject => worldObject.GetAbsoluteParts())
                .Where(part => part.Part.Kind == WorldObjectPartKind.Floor)
                .Select(part => part.Position)
                .ToHashSet();
            _raidDraftIds.UnionWith(snapshot.Actors
                .Where(actor => actor.Health > 0 && !actor.IsJuvenile &&
                    campFloor.Contains(actor.Position))
                .OrderBy(actor => actor.Id)
                .Take(SimulationDefinitions.FieldCampCapacity)
                .Select(actor => actor.Id));
        }
        else
        {
            _raidDraftIds.UnionWith(snapshot.Actors
                .Where(actor => actor.Health > 0 && !actor.IsJuvenile)
                .OrderBy(actor => actor.Id)
                .Take(SimulationDefinitions.FieldCampCapacity)
                .Select(actor => actor.Id));
        }

        foreach (var child in _raidRows.GetChildren())
        {
            child.QueueFree();
        }
        _raidMemberChecks.Clear();
        var selectionLocked = snapshot.RaidPhase is GoblinRaidPhase.Marching or
            GoblinRaidPhase.Looting or GoblinRaidPhase.Returning ||
            snapshot.HumanVillage.GoblinAttackOrdered;
        _raidEngagement.Disabled = selectionLocked;
        _raidCorpseHandling.Disabled = selectionLocked;
        _raidAutoAssignButton.Disabled = selectionLocked;
        foreach (var check in _raidDirectiveChecks.Values)
        {
            check.Disabled = selectionLocked;
        }
        foreach (var actor in snapshot.Actors.OrderBy(actor => actor.Id))
        {
            var check = new CheckButton
            {
                Text = $"{actor.Name} • zdrowie {actor.Health} • wałówka " +
                    $"{actor.PersonalFood}/{_engine.Definitions.PersonalFoodCapacity} • bukłak " +
                    $"{actor.PersonalWater}/{_engine.Definitions.PersonalWaterCapacity} • " +
                    $"głód {actor.Hunger}, pragnienie {actor.Thirst}, zmęczenie {actor.Fatigue}" +
                    (actor.IsJuvenile ? " • młodzik" : string.Empty),
                ButtonPressed = _raidDraftIds.Contains(actor.Id),
                Disabled = selectionLocked || actor.Health <= 0 || actor.IsJuvenile,
                TooltipText = DescribeJob(actor.Job),
            };
            var actorId = actor.Id;
            check.Toggled += enabled => ToggleRaidDraftMember(actorId, check, enabled);
            _raidRows.AddChild(check);
            _raidMemberChecks.Add(actorId, check);
        }

        UpdateRaidWindowSummary(snapshot);
        _raidWindow.PopupCentered();
    }

    private void AutoAssignRaidDraft()
    {
        var snapshot = _latestSnapshot;
        var selected = RaidAutoAssignmentPolicy.Select(
            snapshot.Actors,
            SimulationDefinitions.FieldCampCapacity);
        _raidDraftIds.Clear();
        _raidDraftIds.UnionWith(selected);

        _updatingRaidSelection = true;
        foreach (var (actorId, check) in _raidMemberChecks)
        {
            check.ButtonPressed = _raidDraftIds.Contains(actorId);
        }
        _updatingRaidSelection = false;

        UpdateRaidWindowSummary(snapshot);
        _inspector.Text = selected.Count == 0
            ? "Nie ma dorosłych goblinów zdolnych do udziału w wyprawie."
            : $"Automatycznie dobrano {selected.Count} najlepiej uzbrojonych i zdrowych goblinów. " +
                "Zapisz plan, aby zatwierdzić skład.";
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
        UpdateRaidWindowSummary(_latestSnapshot);
    }

    private void UpdateRaidWindowSummary(SimulationSnapshot snapshot)
    {
        var hasCamp = snapshot.WorldObjects.Any(item =>
            item.Kind == WorldObjectKind.GoblinFieldCamp &&
            item.Owner == WorldObjectOwner.GoblinTribe &&
            (_raidDraftRallyPoint is null || item.Anchor == _raidDraftRallyPoint.Value));
        var phase = snapshot.RaidPhase switch
        {
            GoblinRaidPhase.Preparing => $"Przygotowanie w punkcie {snapshot.RaidRallyPoint}.",
            GoblinRaidPhase.Ready => "Oddział gotowy — czeka na rozkaz ATAK!.",
            GoblinRaidPhase.Suspended => "Przygotowania wstrzymane; plan można edytować.",
            GoblinRaidPhase.Marching => $"Oddział maszeruje do {snapshot.RaidPlan.Target}.",
            GoblinRaidPhase.Looting => "Walka zakończona — oddział zbiera łupy i odnosi je do obozu.",
            GoblinRaidPhase.Returning => "Łupy zabezpieczone — oddział wraca do obozu.",
            _ when _raidDraftRallyPoint is not null =>
                $"Punkt zbiórki: obóz {_raidDraftRallyPoint.Value}. Wybierz od 0 do 5 goblinów.",
            _ => "Wybierz od 0 do 5 goblinów. Bez przypisanych goblinów plan pozostanie wstrzymany.",
        };
        var blockers = snapshot.RaidPhase == GoblinRaidPhase.Preparing
            ? DescribeRaidBlockers(snapshot)
            : string.Empty;
        _raidSummary.Text = $"{phase}\nWybrano: {_raidDraftIds.Count}/{SimulationDefinitions.FieldCampCapacity}." +
            "\nPrzygotowanie: automatyczne, zależne od celu i doktryny najazdu." +
            (hasCamp ? string.Empty : "\nBrak ukończonego obozowiska z drogą do wsi.") +
            blockers;
        _raidStartButton.Text = snapshot.RaidPhase is GoblinRaidPhase.Preparing or
            GoblinRaidPhase.Ready
                ? "Zapisz zmiany"
                : "Zapisz plan";
        _raidStartButton.Disabled = snapshot.RaidPhase is GoblinRaidPhase.Marching or
            GoblinRaidPhase.Looting or GoblinRaidPhase.Returning ||
            snapshot.HumanVillage.GoblinAttackOrdered || !hasCamp;
    }

    private string DescribeRaidBlockers(SimulationSnapshot snapshot)
    {
        var selected = snapshot.RaidPartyIds.ToHashSet();
        var lines = snapshot.Actors
            .Where(actor => selected.Contains(actor.Id) && actor.Health > 0)
            .OrderBy(actor => actor.Id)
            .Select(actor =>
            {
                var preparation = RaidPreparationPolicy.ResolveAutomatic(
                    snapshot.RaidPlan.Directives,
                    _engine.Definitions,
                    actor.Equipment);
                var reasons = new List<string>();
                if (actor.Position != snapshot.RaidRallyPoint)
                {
                    reasons.Add("idzie do obozu");
                }
                if (actor.CarriedStackId != EntityId.None)
                {
                    reasons.Add("odkłada ładunek");
                }
                if (actor.PersonalFood < preparation.FoodTarget)
                {
                    reasons.Add("uzupełnia wałówkę");
                }
                if (actor.PersonalWater < preparation.WaterTarget)
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
        var snapshot = _latestSnapshot;
        if (snapshot.RaidPhase is GoblinRaidPhase.Marching or
            GoblinRaidPhase.Looting or GoblinRaidPhase.Returning ||
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
        var directives = _raidEngagement.Selected == 1
            ? RaidDirective.AttackAll
            : RaidDirective.AttackGuards;
        foreach (var (directive, check) in _raidDirectiveChecks)
        {
            if (check.ButtonPressed)
            {
                directives |= directive;
            }
        }
        directives |= (RaidCorpseHandlingMode)_raidCorpseHandling.Selected switch
        {
            RaidCorpseHandlingMode.RecoverToCamp => RaidDirective.RecoverCorpses,
            RaidCorpseHandlingMode.RecoverAndBudAtCamp => RaidDirective.BudCorpses,
            RaidCorpseHandlingMode.BudInPlace => RaidDirective.BudCorpsesInPlace,
            _ => RaidDirective.None,
        };
        _engine.QueueCommand(SimulationCommand.ConfigureRaidDirectives(
            executeAt,
            _commandSequence++,
            directives));
        _raidWindow.Hide();
        _inspector.Text = _raidDraftIds.Count == 0
            ? "Zapisano plan najazdu bez przypisanych goblinów."
            : $"Zapisano plan najazdu dla {_raidDraftIds.Count} goblinów. " +
                "Przygotowania uruchomisz z menu obozu.";
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

        var snapshot = _latestSnapshot;
        var minimumSurfaceFloor = Enumerable.Range(0, _engine.Map.CellCount)
            .Select(index => _engine.Map.GetCell(new GridPosition(
                index % _engine.Map.Width,
                index / _engine.Map.Width)).FloorLevel)
            .Min(level => (int)level);
        var minimumLevel = Math.Min(minimumSurfaceFloor, _engine.Map.DeepestCaveLevel);
        var maximumLevel = _engine.World.MaximumOccupiedLevel;
        var next = Math.Clamp(_visibleLevel + delta, minimumLevel, maximumLevel);
        if (next == _visibleLevel)
        {
            return;
        }

        _visibleLevel = next;
        _worldView.SetVisibleLevel(next);
        _minimap.SetVisibleLevel(next);
        UpdateLayerToolAvailability();
        if (_buildMode != BuildMode.None)
        {
            UpdateBuildPreview(GetViewport().GetMousePosition());
        }
        else if (_workMode != WorkMode.None)
        {
            UpdateWorkPreview(GetViewport().GetMousePosition());
        }
        var selectedActors = snapshot.Actors
            .Where(actor => _selectedActorIds.Contains(actor.Id))
            .OrderBy(actor => actor.Id)
            .ToArray();
        var selection = selectedActors.Length switch
        {
            0 => string.Empty,
            1 => $" Zaznaczenie zachowane: {selectedActors[0].Name} jest na z={selectedActors[0].Position.Z}.",
            _ => $" Zaznaczenie grupy {selectedActors.Length} goblinów zachowane; poziomy: " +
                 string.Join(", ", selectedActors.Select(actor => actor.Position.Z).Distinct().Order()) + ".",
        };
        _inspector.Text = (_unitOrderMode == UnitOrderMode.Move
            ? $"Widoczna warstwa z={next}. Wskaż odkryty cel marszu lub przejście między poziomami."
            : $"Widoczna warstwa mapy: z={next}. Page Up / Page Down zmienia poziom.") +
            selection;
        UpdateStatus(snapshot);
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
        if (_visibleLevel >= _engine.Map.MinimumWorldLevel &&
            _visibleLevel <= _engine.Map.MaximumWorldLevel)
        {
            return true;
        }

        CancelBuildMode(clearInspector: false);
        _buildMenu.Hide();
        _inspector.Text = UiFormat("layer-tools", "outside-map", _visibleLevel);
        return false;
    }

    private bool IsBuildableLayerCell(GridPosition position) =>
        _engine.Map.IsColumnWithin(position) &&
        position.Z >= _engine.Map.MinimumWorldLevel &&
        position.Z <= _engine.Map.MaximumWorldLevel;

    private GridPosition ClampToCurrentMapLevel(GridPosition position) => new(
        Math.Clamp(position.X, 0, _engine.Map.Width - 1),
        Math.Clamp(position.Y, 0, _engine.Map.Height - 1),
        _visibleLevel);

    private bool IsValidWorkAreaSelection(GridPosition first, GridPosition second) =>
        first.Z == second.Z &&
        IsBuildableLayerCell(first) &&
        IsBuildableLayerCell(second);

    private void UpdateLayerToolAvailability()
    {
        var build = GetToolbarButton("Build");
        var work = GetToolbarButton("Work");
        build.Disabled = false;
        work.Disabled = false;
        build.TooltipText = _visibleLevel switch
        {
            0 => Ui("layer-tools", "build-surface"),
            < 0 => Ui("layer-tools", "build-underground"),
            _ => UiFormat("layer-tools", "build-level", _visibleLevel),
        };
        work.TooltipText = _visibleLevel switch
        {
            0 => Ui("layer-tools", "work-surface"),
            < 0 => Ui("layer-tools", "work-underground"),
            _ => UiFormat("layer-tools", "work-level", _visibleLevel),
        };
    }

    private void UpdateStatus(SimulationSnapshot? currentSnapshot = null)
    {
        var snapshot = currentSnapshot ?? _latestSnapshot;
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
        var statusParts = new List<string>
        {
            UiFormat("status", "tick", snapshot.Tick.Value),
            UiFormat("status", "level", _visibleLevel),
            UiFormat("status", "tribe", snapshot.Actors.Count, snapshot.GoblinBuds.Count,
                snapshot.TribeNeeds.ShelterCapacity),
            UiFormat("status", "food", snapshot.FoodStock, storedFood, personalFood, personalWater),
            UiFormat("status", "wood", wood),
            UiFormat("status", "explored", explored, snapshot.Visibility.Count),
            UiFormat("status", "work-targets", snapshot.WorkDesignations.Count),
            UiFormat("status", "construction", snapshot.ConstructionSites.Count,
                constructionWorkers),
            UiFormat("status", "hauling", haulers),
            UiFormat("status", "traveling", traveling),
            UiFormat("status", "working", working),
        };
        if (_use3DView)
        {
            statusParts.Add(Ui("status", "renderer-3d"));
        }
        if (_engine.DebugSettings.RevealFogFromNonPlayerUnits)
        {
            statusParts.Add(Ui("status", "debug-foreign-units"));
        }
        if (resting > 0)
        {
            statusParts.Add(UiFormat("status", "resting", resting));
        }
        if (eating > 0)
        {
            statusParts.Add(UiFormat("status", "eating", eating));
        }
        if (resupplying > 0)
        {
            statusParts.Add(UiFormat("status", "resupplying", resupplying));
        }
        if (_selectedActorId != EntityId.None)
        {
            statusParts.Add(_selectedActorIds.Count <= 1
                ? UiFormat("status", "selected", _selectedActorId)
                : UiFormat("status", "selected-group", _selectedActorIds.Count));
            UpdateGoblinDetails(snapshot);
        }
        if (_workshopDetails.Visible)
        {
            UpdateWorkshopDetails(snapshot);
        }
        if (_logisticsWindow.Visible)
        {
            UpdateLogisticsWindow(snapshot);
        }
        if (villageVisibility == CellVisibility.Visible)
        {
            statusParts.Add(UiFormat("status", "village",
                snapshot.HumanVillage.Population,
                snapshot.HumanVillage.FoodStock,
                snapshot.HumanVillage.FoodCapacity,
                snapshot.HumanVillage.GrainStock,
                snapshot.HumanVillage.WaterStock,
                snapshot.HumanVillage.WoodStock));
            statusParts.Add(UiFormat("status", "fields",
                snapshot.HumanVillage.Fields.Count,
                snapshot.HumanVillage.PlannedFieldCount));
            statusParts.Add(UiFormat("status", "alarm", snapshot.HumanVillage.Hostility));
            if (snapshot.HumanVillage.GoblinAttackOrdered)
            {
                statusParts.Add(Ui("status", "village-raid"));
            }
        }
        if (snapshot.RaidPhase == GoblinRaidPhase.Preparing)
        {
            statusParts.Add(UiFormat("status", "raid-preparing", snapshot.RaidRallyPoint));
        }
        else if (snapshot.RaidPhase == GoblinRaidPhase.Ready)
        {
            statusParts.Add(Ui("status", "raid-ready"));
        }
        else if (snapshot.RaidPhase == GoblinRaidPhase.Suspended)
        {
            statusParts.Add(Ui("status", "raid-suspended"));
        }
        else if (snapshot.RaidPhase == GoblinRaidPhase.Marching)
        {
            statusParts.Add(Ui("status", "raid-marching"));
        }
        else if (snapshot.RaidPhase == GoblinRaidPhase.Looting)
        {
            statusParts.Add(Ui("status", "raid-looting"));
        }
        else if (snapshot.RaidPhase == GoblinRaidPhase.Returning)
        {
            statusParts.Add(Ui("status", "raid-returning"));
        }
        else if (villageVisibility == CellVisibility.Explored)
        {
            statusParts.Add(Ui("status", "village-explored"));
        }
        _status.Text = string.Join("  •  ", statusParts);
    }

    private void UpdateCalendar(SimulationSnapshot snapshot)
    {
        var calendar = SimulationCalendar.At(snapshot.Tick, _engine.Definitions.Clock);
        var seasonName = Ui("seasons", calendar.Season.ToString());
        _clock.Text = UiFormat("calendar", "clock",
            calendar.Hour, calendar.Minute, calendar.Second, calendar.DayOfSeason);
        _clock.TooltipText = calendar.IsNight
            ? Ui("calendar", "night-tooltip")
            : Ui("calendar", "day-tooltip");
        _seasonName.Text = seasonName;
        var season = _engine.Definitions.Clock.Climate.GetSeason(calendar.Season);
        _seasonProgress.SetCalendar(_engine.Definitions.Clock.Climate, calendar);
        _seasonProgress.TooltipText = UiFormat("calendar", "season-tooltip",
            seasonName, calendar.DayOfSeason, season.Days,
            _engine.Definitions.Clock.Climate.Id);
    }
}
