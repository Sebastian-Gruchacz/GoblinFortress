using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using System.Text;

namespace GoblinStronghold.GodotClient;

public partial class Main : Node
{
    private const int MaximumSimulationTicksPerFrame = 8;
    private const double MaximumSimulationMillisecondsPerFrame = 8d;
    private const double PresentationRefreshIntervalSeconds = 1d / 5d;
    private const double MinimumAutosaveIntervalSeconds = 10d * 60d;
    private SimulationEngine _engine = null!;
    private SimulationSnapshot _latestSnapshot = null!;
    private WorldView _worldView = null!;
    private WorldView3D _worldView3D = null!;
    private MinimapView _minimap = null!;
    private Camera2D _camera = null!;
    private Label _status = null!;
    private Label _clock = null!;
    private Label _seasonName = null!;
    private SeasonCycleView _seasonProgress = null!;
    private Label _inspector = null!;
    private PopupPanel _managementMenu = null!;
    private PopupPanel _buildMenu = null!;
    private PopupPanel _workMenu = null!;
    private PopupPanel _statisticsMenu = null!;
    private GridContainer _managementMenuGrid = null!;
    private GridContainer _buildMenuGrid = null!;
    private GridContainer _workMenuGrid = null!;
    private GridContainer _statisticsMenuGrid = null!;
    private Texture2D _iconAtlas = null!;
    private Texture2D _itemIconAtlas = null!;
    private Texture2D _pickaxeIcon = null!;
    private Texture2D _commandingHandIcon = null!;
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
    private Label _populationTargetText = null!;
    private int _populationTargetDraft;
    private Window _raidWindow = null!;
    private Label _raidSummary = null!;
    private VBoxContainer _raidRows = null!;
    private Button _raidStartButton = null!;
    private OptionButton _raidEngagement = null!;
    private OptionButton _raidCorpseHandling = null!;
    private readonly Dictionary<RaidDirective, CheckButton> _raidDirectiveChecks = [];
    private readonly HashSet<EntityId> _raidDraftIds = [];
    private bool _updatingRaidSelection;
    private GridPosition? _raidDraftRallyPoint;
    private bool _isRaidTargetMode;
    private int _raidTargetRadius = SimulationEngine.DefaultRaidTargetRadius;
    private PopupMenu _worldContextMenu = null!;
    private GridPosition? _contextCampAnchor;
    private EntityId _contextCorpseId = EntityId.None;
    private Window _plannerWindow = null!;
    private VBoxContainer _plannerRows = null!;
    private Label _plannerSummary = null!;
    private string _plannerSignature = string.Empty;
    private EntityId _replacingWorkOrderId = EntityId.None;
    private StoragePriority? _replacementWorkPriority;
    private bool _replacementWorkSuspended;
    private int _speed = 1;
    private int _visibleLevel;
    private double _accumulator;
    private double _presentationRefreshElapsed;
    private ulong _commandSequence = 1;
    private EntityId _selectedActorId = EntityId.None;
    private readonly HashSet<EntityId> _selectedActorIds = [];
    private BuildMode _buildMode;
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
    private GameSaveStore _saveStore = null!;
    private SimulationTick _nextAutosaveTick;
    private double _autosaveElapsedRealSeconds;
    private Control _mainMenu = null!;
    private Button _resumeGameButton = null!;
    private Button _newGameButton = null!;
    private Button _loadMenuButton = null!;
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
        WoodStorage,
        StoneStorage,
        EquipmentStorage,
        MaterialsStorage,
        FieldCamp,
        WoodenWall,
        StoneWall,
        WoodenDoorFrame,
        StoneDoorFrame,
        WoodenDoor,
        WallTorch,
        PrimitiveWorkshop,
        GoblinHut,
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
        LootRaidCorpses = 100,
        ConsumeRaidCorpses = 101,
        RecoverRaidCorpses = 102,
        RecoverAndBudRaidCorpses = 103,
        BudRaidCorpsesInPlace = 104,
    }

    public override void _Ready()
    {
        _saveStore = new GameSaveStore(ProjectSettings.GlobalizePath("user://saves"));
        _shortcutSettings = new ShortcutSettings(
            ProjectSettings.GlobalizePath("user://settings/shortcuts.json"));
        _gameUiTheme = GameUiTheme.Create();

        _worldView = GetNode<WorldView>("WorldView");
        _worldView3D = GetNode<WorldView3D>("WorldView3D");
        _minimap = GetNode<MinimapView>("Interface/RightHud/MinimapFrame/Minimap");
        _camera = GetNode<Camera2D>("Camera2D");
        _status = GetNode<Label>("Interface/StatusBar/Status");
        _clock = GetNode<Label>("Interface/Calendar/Controls/Clock");
        _seasonName = GetNode<Label>("Interface/Calendar/Controls/SeasonName");
        _seasonProgress = GetNode<SeasonCycleView>("Interface/Calendar/Controls/Season");
        _inspector = GetNode<Label>("Interface/Inspector/Text");
        _managementMenu = GetNode<PopupPanel>("ManagementMenu");
        _buildMenu = GetNode<PopupPanel>("BuildMenu");
        _workMenu = GetNode<PopupPanel>("WorkMenu");
        _statisticsMenu = GetNode<PopupPanel>("StatisticsMenu");
        _managementMenuGrid = GetNode<GridContainer>("ManagementMenu/Margin/Grid");
        _buildMenuGrid = GetNode<GridContainer>("BuildMenu/Margin/Grid");
        _workMenuGrid = GetNode<GridContainer>("WorkMenu/Margin/Grid");
        _statisticsMenuGrid = GetNode<GridContainer>("StatisticsMenu/Margin/Grid");
        _mainMenu = GetNode<Control>("Interface/MainMenu");
        _resumeGameButton = GetNode<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/Resume");
        _newGameButton = GetNode<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/NewGame");
        _loadMenuButton = GetNode<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/LoadGame");
        var titleSplash = GetNode<Label>("Interface/MainMenu/Center/Panel/Margin/Controls/Subtitle");
        titleSplash.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        titleSplash.Text = TitleSplashCatalog.Pick("Plemię pamięta to, co zdoła pożreć.");
        _titleMusic = GetNode<AudioStreamPlayer>("TitleMusic");
        _titleMusic.Finished += ReplayTitleMusic;
        _viewModeButton = GetNode<Button>("Interface/RightHud/SessionPanel/Controls/ViewMode");
        _cameraModePanel = GetNode<Control>("Interface/RightHud/CameraPanel");
        _cameraAngleButton = GetNode<Button>("Interface/RightHud/CameraPanel/Controls/Angle");
        _iconAtlas = UiIcons.LoadAtlas();
        _itemIconAtlas = ItemIcons.LoadAtlas();
        _pickaxeIcon = GD.Load<Texture2D>("res://Assets/UI/primitive-pickaxe-v1.svg");
        _commandingHandIcon = GD.Load<Texture2D>("res://Assets/UI/commanding-hand-v1.svg");
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
        _statisticsText = GetNode<Label>("StatisticsWindow/Margin/Content/Text");
        _populationTargetText = GetNode<Label>(
            "StatisticsWindow/Margin/Content/Population/Target");
        GetNode<Button>("StatisticsWindow/Margin/Content/Population/Decrease").Pressed +=
            () => ChangePopulationTarget(-1);
        GetNode<Button>("StatisticsWindow/Margin/Content/Population/Increase").Pressed +=
            () => ChangePopulationTarget(1);
        GetViewport().GuiEmbedSubwindows = true;
        CreateTextureTileButton(
            _managementMenuGrid,
            _managementMenu,
            _commandingHandIcon,
            "Planer plemienia\nPriorytety, obszary i stan zleceń",
            ShowPlanner,
            GameShortcutId.ShowPlanner);
        CreateTileButton(_buildMenuGrid, _buildMenu, UiIcon.FoodStorage,
            "Skład żywności\nKoszt: 2 drewna", () => SelectBuildMode((long)BuildMode.FoodStorage),
            GameShortcutId.BuildFoodStorage);
        CreateTileButton(_buildMenuGrid, _buildMenu, UiIcon.WoodStorage,
            "Skład drewna\nKoszt: 2 drewna", () => SelectBuildMode((long)BuildMode.WoodStorage),
            GameShortcutId.BuildWoodStorage);
        CreateItemTileButton(_buildMenuGrid, _buildMenu, ItemIcon.Stone,
            "Skład kamienia i urobku\nKoszt: 2 drewna",
            () => SelectBuildMode((long)BuildMode.StoneStorage),
            GameShortcutId.BuildStoneStorage);
        CreateItemTileButton(_buildMenuGrid, _buildMenu, ItemIcon.Cargo,
            "Skład sprzętu\nKoszt: 2 drewna • wspólna pojemność 32",
            () => SelectBuildMode((long)BuildMode.EquipmentStorage),
            GameShortcutId.BuildEquipmentStorage);
        CreateItemTileButton(_buildMenuGrid, _buildMenu, ItemIcon.Reeds,
            "Skład materiałów\nKoszt: 2 drewna • skóry, kości i sitowie",
            () => SelectBuildMode((long)BuildMode.MaterialsStorage),
            GameShortcutId.BuildMaterialsStorage);
        CreateTileButton(_buildMenuGrid, _buildMenu, UiIcon.Walkway,
            "Pomost\nKoszt: 1 drewno za segment", () => SelectBuildMode((long)BuildMode.Walkway),
            GameShortcutId.BuildWalkway);
        CreateTileButton(_buildMenuGrid, _buildMenu, UiIcon.FieldCamp,
            "Obozowisko wypadowe\nKoszt: 6 drewna", () => SelectBuildMode((long)BuildMode.FieldCamp),
            GameShortcutId.BuildFieldCamp);
        CreateTileButton(_buildMenuGrid, _buildMenu, UiIcon.FieldCamp,
            "Chata goblinów\nKoszt: 8 drewna • zwiększa pojemność plemienia",
            () => SelectBuildMode((long)BuildMode.GoblinHut),
            GameShortcutId.BuildGoblinHut);
        CreateTileButton(_buildMenuGrid, _buildMenu, UiIcon.WoodenWall,
            "Drewniana ściana\nKoszt: 2 drewna", () => SelectBuildMode((long)BuildMode.WoodenWall),
            GameShortcutId.BuildWoodenWall);
        CreateItemTileButton(_buildMenuGrid, _buildMenu, ItemIcon.Stone,
            "Kamienny mur\nKoszt: 2 jednostki kamienia • wymaga kilofa",
            () => SelectBuildMode((long)BuildMode.StoneWall),
            GameShortcutId.BuildStoneWall);
        CreateTileButton(_buildMenuGrid, _buildMenu, UiIcon.WoodenDoorFrame,
            "Drewniana ościeżnica\nKoszt: 1 drewno", () => SelectBuildMode((long)BuildMode.WoodenDoorFrame));
        CreateItemTileButton(_buildMenuGrid, _buildMenu, ItemIcon.Stone,
            "Kamienna ościeżnica\nKoszt: 1 kamień • wymaga kilofa",
            () => SelectBuildMode((long)BuildMode.StoneDoorFrame));
        CreateTileButton(_buildMenuGrid, _buildMenu, UiIcon.WoodenDoor,
            "Drewniane drzwi\nKoszt: 1 drewno", () => SelectBuildMode((long)BuildMode.WoodenDoor),
            GameShortcutId.BuildWoodenDoor);
        CreateTextureTileButton(_buildMenuGrid, _buildMenu, CreateWallTorchIcon(),
            "Pochodnia ścienna\nKoszt: 1 drewno • wskaż ścianę",
            () => SelectBuildMode((long)BuildMode.WallTorch));
        CreateTextureTileButton(_buildMenuGrid, _buildMenu, CreatePrimitiveWorkshopIcon(),
            "Prymitywny warsztat\nKoszt: 4 drewna",
            () => SelectBuildMode((long)BuildMode.PrimitiveWorkshop),
            GameShortcutId.BuildPrimitiveWorkshop);
        CreateTileButton(_workMenuGrid, _workMenu, UiIcon.GatherFood,
            "Zbierz żywność\nJagody, grzyby, korzonki i ryby",
            () => SelectWorkMode((long)WorkMode.GatherFood),
            GameShortcutId.GatherFood);
        CreateItemTileButton(_workMenuGrid, _workMenu, ItemIcon.Reeds,
            "Zbierz sitowie\nWskaż trzcinowiska na płytkiej wodzie",
            () => SelectWorkMode((long)WorkMode.GatherReeds),
            GameShortcutId.GatherReeds);
        CreateTileButton(_workMenuGrid, _workMenu, UiIcon.GatherBrushwood,
            "Zbierz chrust\nPrzeciągnij obszar", () => SelectWorkMode((long)WorkMode.GatherBrushwood),
            GameShortcutId.GatherBrushwood);
        CreateItemTileButton(_workMenuGrid, _workMenu, ItemIcon.Stone,
            "Zbierz kamienie i urobek\nPrzeciągnij obszar",
            () => SelectWorkMode((long)WorkMode.GatherStone),
            GameShortcutId.GatherStone);
        CreateTileButton(_workMenuGrid, _workMenu, UiIcon.UprootBush,
            "Wykarcz krzaki\nTrwale usuwa źródła jagód", () => SelectWorkMode((long)WorkMode.UprootBerryBushes),
            GameShortcutId.UprootBushes);
        CreateTileButton(_workMenuGrid, _workMenu, UiIcon.FellTree,
            "Wyrąb drzew i pni\nWymaga goblina z siekierą", () => SelectWorkMode((long)WorkMode.FellTrees),
            GameShortcutId.FellTrees);
        CreateTextureTileButton(_workMenuGrid, _workMenu, _pickaxeIcon,
            "Rozbij głazy\nWymaga goblina z kilofem", () => SelectWorkMode((long)WorkMode.QuarryBoulders),
            GameShortcutId.QuarryBoulders);
        CreateTextureTileButton(_workMenuGrid, _workMenu, _pickaxeIcon,
            "Kop w skale\nWymaga goblina z kilofem", () => SelectWorkMode((long)WorkMode.MineRock),
            GameShortcutId.MineRock);
        CreateTextureTileButton(_workMenuGrid, _workMenu, _pickaxeIcon,
            "Wykop pochylnię w dół\nWskaż odkrytą podłogę; wymaga kilofa",
            () => SelectWorkMode((long)WorkMode.CarveRampDown),
            GameShortcutId.CarveRampDown);
        CreateTextureTileButton(_workMenuGrid, _workMenu, _pickaxeIcon,
            "Wykop pochylnię w górę\nWskaż odkrytą podłogę jaskini; wymaga kilofa",
            () => SelectWorkMode((long)WorkMode.CarveRampUp),
            GameShortcutId.CarveRampUp);
        CreateTileButton(_workMenuGrid, _workMenu, UiIcon.Expedition,
            "Poluj na zwierzęta\nPrzeciągnij obszar; pozostaną konkretne cele",
            () => SelectWorkMode((long)WorkMode.HuntAnimals),
            GameShortcutId.HuntAnimals);
        CreateTileButton(_workMenuGrid, _workMenu, UiIcon.Expedition,
            "Wyznacz obszar zwiadu\nSkauci nie wejdą w nieznany teren poza zaznaczeniem",
            () => SelectWorkMode((long)WorkMode.Scout),
            GameShortcutId.Scout);
        CreateTileButton(_workMenuGrid, _workMenu, UiIcon.ClearOrders,
            "Zmyj zaschniętą krew\nWskaż plamy na podłogach i pomostach",
            () => SelectWorkMode((long)WorkMode.CleanBlood),
            GameShortcutId.CleanBlood);
        CreateTileButton(_workMenuGrid, _workMenu, UiIcon.ClearOrders,
            "Usuń zlecenia\nPrzeciągnij obszar", () => SelectWorkMode((long)WorkMode.Clear),
            GameShortcutId.ClearOrders);
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
        _managementMenu.GetNode<Control>("Margin").GuiInput += inputEvent =>
            CloseWindowOnSecondaryInput(inputEvent, _managementMenu);
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
        ConfigureActionButton("Pause", UiIcon.Pause, "Pauza • ~ lub Spacja");
        ConfigureActionButton("Speed1", UiIcon.Play, "Normalna prędkość • klawisz 1 • 1×");
        ConfigureActionButton("Speed2", UiIcon.Faster, "Przyspieszenie • klawisz 2 • 2×");
        ConfigureActionButton("Speed4", UiIcon.Fastest, "Przyspieszenie • klawisz 3 • 4×");
        ConfigureActionButton("Speed8", UiIcon.Fastest, "Maksymalne przyspieszenie • klawisz 4 • 8×");
        GetToolbarButton("Speed8").Icon = UiIcons.LoadSpeed8Texture();
        ConfigureActionButton("Management", UiIcon.Work, "Zarządzanie plemieniem");
        GetToolbarButton("Management").Icon = _commandingHandIcon;
        ConfigureActionButton("Build", UiIcon.Build, "Budowanie");
        ConfigureActionButton("Work", UiIcon.Work, "Zlecenia pracy");
        ConfigureActionButton("Move", UiIcon.Expedition, "Rozkazy wybranych goblinów • M/A/H/P");
        var statisticsButton = GetToolbarButton("Statistics");
        statisticsButton.FocusMode = Control.FocusModeEnum.None;
        statisticsButton.TooltipText = "Zestawienia i statystyki";
        GetToolbarButton("Management").Pressed += ShowManagementMenu;
        GetToolbarButton("Build").Pressed += ShowBuildMenu;
        GetToolbarButton("Work").Pressed += ShowWorkMenu;
        GetToolbarButton("Move").Pressed += ShowUnitOrderMenu;
        GetToolbarButton("Raid").Hide();
        statisticsButton.Pressed += ShowStatisticsMenu;
        RegisterShortcutAction(GameShortcutId.OpenManagement, ShowManagementMenu);
        RegisterShortcutAction(GameShortcutId.OpenConstruction, ShowBuildMenu);
        RegisterShortcutAction(GameShortcutId.OpenWork, ShowWorkMenu);
        RegisterShortcutAction(GameShortcutId.OpenStatistics, ShowStatisticsMenu);
        RegisterShortcutAction(GameShortcutId.OpenUnitOrders, ShowUnitOrderMenu);
        RegisterShortcutTile(
            GameShortcutId.OpenManagement,
            GetToolbarButton("Management"),
            "Zarządzanie plemieniem");
        RegisterShortcutTile(
            GameShortcutId.OpenConstruction,
            GetToolbarButton("Build"),
            "Budowanie");
        RegisterShortcutTile(
            GameShortcutId.OpenWork,
            GetToolbarButton("Work"),
            "Zlecenia pracy");
        RegisterShortcutTile(
            GameShortcutId.OpenStatistics,
            statisticsButton,
            "Zestawienia i statystyki");
        RegisterShortcutTile(
            GameShortcutId.OpenUnitOrders,
            GetToolbarButton("Move"),
            "Rozkazy wybranych goblinów • M/A/H/P");
        CreatePlannerWindow();
        CreateRaidWindow();
        CreateWorkshopWindow();
        CreateWorldContextMenu();
        CreateUnitOrderMenu();
        CreateOptionsWindow();
        ApplyGameThemeToWindows();
        UpdateSpeedButtons();
        ShowMainMenu();
    }

    public override void _Process(double delta)
    {
        if (!_hasActiveSession || _mainMenu.Visible)
        {
            return;
        }

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

        if (_optionsWindow.Visible &&
            inputEvent is InputEventKey { Pressed: true, Echo: false } optionsKey)
        {
            if (optionsKey.Keycode == Key.Escape)
            {
                CloseOptions();
            }
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
            case InputEventKey key when key.Pressed && !key.Echo && key.Keycode == Key.F1:
                ShowSelectedGoblinDetails();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey key when key.Pressed && !key.Echo && !key.CtrlPressed &&
                key.Keycode == Key.M:
                SelectUnitOrderMode(UnitOrderMode.Move);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey key when key.Pressed && !key.Echo && !key.CtrlPressed &&
                key.Keycode == Key.A:
                SelectUnitOrderMode(UnitOrderMode.AttackArea);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey key when key.Pressed && !key.Echo && !key.CtrlPressed &&
                key.Keycode == Key.H:
                SelectUnitOrderMode(UnitOrderMode.HuntArea);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey key when key.Pressed && !key.Echo && !key.CtrlPressed &&
                key.Keycode == Key.P:
                SelectUnitOrderMode(UnitOrderMode.Patrol);
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

    private SimulationEngine CreateNewEngine(WorldSeed seed)
    {
        var map = SwampMapGenerator.Generate(
            seed,
            SwampMapGenerator.DefaultDimension,
            SwampMapGenerator.DefaultDimension);
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
        string? newestFailure = null;
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
                newestFailure ??= $"{Path.GetFileName(candidate.Path)}: {exception.Message}";
                // Try an older rotating slot if the newest save is incompatible or damaged.
            }
        }

        _inspector.Text = newestFailure is null
            ? "Nie znaleziono zapisu do wczytania."
            : $"Nie znaleziono zgodnego zapisu • {newestFailure}";
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
        _loadMenuButton.Disabled = !_saveStore.HasAnySave;
        _mainMenu.Show();
        (_hasActiveSession ? _resumeGameButton : _newGameButton).GrabFocus();
    }

    private void CreateOptionsWindow()
    {
        _optionsWindow = new Window
        {
            Title = "Opcje",
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
            Text = "Interfejs",
            ThemeTypeVariation = "HeaderSmall",
        });
        content.AddChild(new Label
        {
            Text = "Motyw: ciemna sepia • tekst: jasne złoto\n" +
                "Paleta jest wspólna dla menu kontekstowych, paneli i okien.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        content.AddChild(new HSeparator());
        content.AddChild(new Label
        {
            Text = "Skróty klawiaturowe",
            ThemeTypeVariation = "HeaderSmall",
        });
        content.AddChild(new Label
        {
            Text = "Kliknij skrót i naciśnij nowy klawisz. Pozycje wcięte działają " +
                "po wcześniejszym otwarciu nadrzędnego menu.",
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

        var close = new Button { Text = "Zamknij" };
        close.Pressed += CloseOptions;
        content.AddChild(close);

        var controls = GetNode<VBoxContainer>(
            "Interface/MainMenu/Center/Panel/Margin/Controls");
        var optionsButton = new Button
        {
            Text = "Opcje",
            CustomMinimumSize = new Vector2(0, 42),
        };
        optionsButton.Pressed += ShowOptions;
        controls.AddChild(optionsButton);
        var quitButton = GetNode<Button>(
            "Interface/MainMenu/Center/Panel/Margin/Controls/Quit");
        controls.MoveChild(optionsButton, quitButton.GetIndex());
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
                var section = new Label { Text = currentSection };
                section.AddThemeColorOverride("font_color", GameUiTheme.Accent);
                section.AddThemeFontSizeOverride("font_size", 17);
                _shortcutRows.AddChild(section);
            }

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.AddChild(new Label
            {
                Text = definition.Parent is null
                    ? definition.Label
                    : $"    {definition.Label}",
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
        _shortcutBindingButtons[shortcut].Text = "Naciśnij klawisz…";
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
                    $"Zajęty: {ShortcutSettings.Definitions.First(item => item.Id == conflict).Label}";
                return true;
            }

            _shortcutSettings.Set(captured, stroke);
            _capturedShortcut = null;
            RefreshShortcutTooltips();
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
        button.TooltipText = $"{tooltip}\nSkrót: {_shortcutSettings.Describe(shortcut)}";
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

    private void RefreshShortcutTooltips()
    {
        foreach (var (shortcut, tile) in _shortcutTiles)
        {
            tile.Button.TooltipText =
                $"{tile.Tooltip}\nSkrót: {_shortcutSettings.Describe(shortcut)}";
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
        _saveStore.SaveAuto(_engine.Save());
        _autosaveElapsedRealSeconds = 0;
    }

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
        _worldView.Visible = !_use3DView;
        _camera.Enabled = !_use3DView;
        _worldView3D.SetActive(_use3DView);
        _camera.Position = _worldView.CellToWorld(engine.Map.GoblinSpawn);
        UpdateLayerToolAvailability();
        ScheduleNextAutosave();
        ConstrainCameraToMap();
        UpdateStatus(_latestSnapshot);
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
        _selectedWorkshop = null;
        _storageDetails.Hide();
        _constructionDetails.Hide();
        _workshopDetails.Hide();
        _inspector.Text = "Zaznaczenie wyczyszczone. PPM przeciągnięty przesuwa mapę.";
    }

    private void ShowBuildMenu()
    {
        ShowToolbarMenu(_buildMenu, "Build");
    }

    private void ShowManagementMenu() => ShowToolbarMenu(_managementMenu, "Management");

    private void ShowWorkMenu()
    {
        ShowToolbarMenu(_workMenu, "Work");
    }

    private void CreateUnitOrderMenu()
    {
        _unitOrderMenu = new PopupMenu { MinSize = new Vector2I(210, 0) };
        _unitOrderMenu.AddItem("Marsz", (int)UnitOrderAction.Move, (Key)Key.M);
        _unitOrderMenu.AddItem("Atakuj obszar", (int)UnitOrderAction.AttackArea, (Key)Key.A);
        _unitOrderMenu.AddItem("Poluj w obszarze", (int)UnitOrderAction.HuntArea, (Key)Key.H);
        _unitOrderMenu.AddItem("Patrol", (int)UnitOrderAction.Patrol, (Key)Key.P);
        _unitOrderMenu.IdPressed += action => SelectUnitOrderMode(
            (UnitOrderMode)(int)action);
        AddChild(_unitOrderMenu);
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
                     _managementMenu, _buildMenu, _workMenu, _statisticsMenu,
                 })
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

    private void CreateItemTileButton(
        GridContainer grid,
        PopupPanel menu,
        ItemIcon icon,
        string tooltip,
        Action action,
        GameShortcutId? shortcut = null)
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
        RegisterShortcutAction(shortcut, action);
        RegisterShortcutTile(shortcut, button, tooltip);
    }

    private void CreateTextureTileButton(
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
        _buildMode = mode;
        _isDraggingLinearBuild = false;
        _worldView.SetConstructionPreview([]);
        _inspector.Text = _buildMode switch
        {
            BuildMode.FoodStorage => "Budowa składu żywności: wskaż pole LPM • koszt 2 drewna • Esc anuluje",
            BuildMode.WoodStorage => "Budowa składu drewna: wskaż pole LPM • koszt 2 drewna • Esc anuluje",
            BuildMode.StoneStorage => "Budowa składu kamienia: wskaż pole LPM • koszt 2 drewna • Esc anuluje",
            BuildMode.EquipmentStorage => "Budowa składu sprzętu: wskaż pole LPM • koszt 2 drewna • Esc anuluje",
            BuildMode.MaterialsStorage => "Budowa składu materiałów: wskaż pole LPM • koszt 2 drewna • Esc anuluje",
            BuildMode.Walkway => "Budowa pomostu: przeciągnij LPM od początku do końca • 1 drewno/segment • Esc anuluje",
            BuildMode.FieldCamp => "Obozowisko 2×2: wskaż lewy górny narożnik • koszt 6 drewna • zawiera skład prowiantu",
            BuildMode.GoblinHut => $"Chata 3×3: wskaż lewy górny narożnik • koszt 8 drewna • " +
                $"daje {SimulationDefinitions.GoblinHutCapacity} miejsc i podnosi cel populacji",
            BuildMode.WoodenWall => "Budowa drewnianej ściany: przeciągnij LPM od początku do końca • 2 drewna/segment • blokuje przejście",
            BuildMode.StoneWall => "Budowa kamiennego muru: przeciągnij LPM od początku do końca • 2 jednostki kamienia/segment • wymaga kilofa",
            BuildMode.WoodenDoorFrame => "Budowa drewnianej ościeżnicy: wskaż pole LPM • koszt 1 drewna • może zastąpić gotową ścianę",
            BuildMode.StoneDoorFrame => "Budowa kamiennej ościeżnicy: wskaż pole LPM • koszt 1 kamienia • wymaga kilofa • może zastąpić gotowy kamienny mur",
            BuildMode.WoodenDoor => "Budowa drewnianych drzwi: wskaż gotową ościeżnicę LPM • koszt 1 drewna • po budowie kliknij skrzydło, aby je otworzyć",
            BuildMode.WallTorch => "Budowa pochodni: wskaż odkrytą ścianę LPM • koszt 1 drewna • strona montażu wynika z wnętrza i sąsiedztwa",
            BuildMode.PrimitiveWorkshop => "Budowa prymitywnego warsztatu: wskaż pole LPM • koszt 4 drewna • Esc anuluje",
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
        _inspector.Text = _workMode switch
        {
            WorkMode.GatherFood => "Praca: przeciągnij obszar zbierania żywności • Esc anuluje",
            WorkMode.GatherReeds => "Praca: przeciągnij obszar zbierania sitowia • Esc anuluje",
            WorkMode.GatherBrushwood => "Praca: przeciągnij obszar zbierania chrustu • Esc anuluje",
            WorkMode.GatherStone => "Praca: przeciągnij obszar zbierania małych kamieni • Esc anuluje",
            WorkMode.UprootBerryBushes => "Praca: przeciągnij obszar karczowania krzaków • usuwa je trwale • Esc anuluje",
            WorkMode.FellTrees => "Praca: przeciągnij obszar wyrębu • pozostaną konkretne drzewa i martwe pnie • Esc anuluje",
            WorkMode.QuarryBoulders => "Praca: przeciągnij obszar wydobycia • pozostaną konkretne głazy • wymaga kilofa • Esc anuluje",
            WorkMode.MineRock => "Praca: przeciągnij obszar tunelu • nieznane pola pozostaną w planie aż front kopania je odsłoni • wymaga kilofa • Esc anuluje",
            WorkMode.CarveRampDown => "Praca: wskaż odkrytą podłogę • goblin z kilofem wykopie pochylnię na poziom niżej • Esc anuluje",
            WorkMode.CarveRampUp => "Praca: wskaż odkrytą podłogę jaskini • goblin z kilofem wykopie pochylnię na poziom wyżej • Esc anuluje",
            WorkMode.HuntAnimals => "Polowanie: przeciągnij obszar • pozostaną konkretne zwierzęta • Esc anuluje",
            WorkMode.Scout => "Zwiad: przeciągnij dozwolony obszar • skauci mogą przechodzić przez znany teren, ale nie wejdą w nieznany teren poza zaznaczeniem",
            WorkMode.CleanBlood => "Praca: przeciągnij obszar sprzątania zaschniętej krwi z wykonanych podłóg • Esc anuluje",
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

        if (_buildMode is BuildMode.FoodStorage or BuildMode.WoodStorage or
            BuildMode.StoneStorage or BuildMode.EquipmentStorage or BuildMode.MaterialsStorage)
        {
            var resource = _buildMode switch
            {
                BuildMode.FoodStorage => ResourceKind.Food,
                BuildMode.WoodStorage => ResourceKind.Wood,
                BuildMode.StoneStorage => ResourceKind.Stone,
                BuildMode.EquipmentStorage => ResourceKind.Equipment,
                BuildMode.MaterialsStorage => ResourceKind.Materials,
                _ => throw new InvalidOperationException(),
            };
            CreateStorage(
                cell,
                resource);
            CancelBuildMode(clearInspector: false);
            return;
        }

        if (_buildMode is BuildMode.WoodenDoorFrame or BuildMode.StoneDoorFrame or
            BuildMode.WoodenDoor or BuildMode.WallTorch or BuildMode.PrimitiveWorkshop)
        {
            if (!_engine.Visibility.Get(cell).IsDiscovered())
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
                BuildMode.PrimitiveWorkshop => SimulationCommand.BuildPrimitiveWorkshop(
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
                BuildMode.PrimitiveWorkshop =>
                    "Zlecono prymitywny warsztat • koszt 4 drewna",
                _ => "Zlecono zamknięte drewniane drzwi w ościeżnicy • koszt 1 drewna",
            };
            CancelBuildMode(clearInspector: false);
            return;
        }

        if (_buildMode == BuildMode.FieldCamp)
        {
            var cells = GetAreaCells(cell, cell with { X = cell.X + 1, Y = cell.Y + 1 });
            if (cells.Any(item =>
                    !IsBuildableLayerCell(item) ||
                    !_engine.Visibility.Get(item).IsDiscovered()))
            {
                _inspector.Text = "Całe obozowisko musi mieścić się na odkrytej, dostępnej warstwie.";
                CancelBuildMode(clearInspector: false);
                return;
            }
            _engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
                _engine.CurrentTick.Next(), _commandSequence++, cell));
            _inspector.Text = "Zlecono obozowisko 2×2 • koszt 6 drewna • skład prowiantu do 48, cel zależny od liczebności plemienia";
            CancelBuildMode(clearInspector: false);
            return;
        }

        if (_buildMode == BuildMode.GoblinHut)
        {
            _engine.QueueCommand(SimulationCommand.BuildGoblinHut(
                _engine.CurrentTick.Next(),
                _commandSequence++,
                cell));
            _inspector.Text = "Zlecono budowę chaty goblinów. Materiały mogą zostać dostarczone później.";
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
        if (!IsBuildableLayerCell(end) || end.Z != _linearBuildStart.Z)
        {
            _inspector.Text = "Cała konstrukcja musi leżeć na jednym dostępnym poziomie.";
            CancelBuildMode(clearInspector: false);
            return;
        }

        var cells = SimulationCommand.GetLinearCells(_linearBuildStart, end);
        if (cells.Any(cell =>
                !_engine.Visibility.Get(cell).IsDiscovered()))
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
            BuildMode.GoblinHut => GetAreaCells(cell, cell with { X = cell.X + 2, Y = cell.Y + 2 }),
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
                WorkMode.MineRock => $"Planowany obszar tunelu: {cells.Count} pól; nieznane komórki będą rozstrzygane wraz z odsłanianiem.",
                WorkMode.CarveRampDown or WorkMode.CarveRampUp => cells.Count == 1
                    ? "Pochylnia połączy tę komórkę z sąsiednim poziomem."
                    : "Tu nie można wykopać pochylni: potrzebna jest wolna podłoga i pełna skała po drugiej stronie.",
                _ when behavior == WorkAreaSelectionBehavior.FilterTargets =>
                    $"Zaznaczanie pracy: filtr znalazł {cells.Count} pasujących celów.",
                _ => $"Zaznaczanie pracy: {cells.Count} pasujących pól.",
            };
        }
    }

    private void FinishWorkArea(Vector2 screenPosition)
    {
        var end = ClampToCurrentMapLevel(ScreenToVisibleCell(screenPosition));
        _isDraggingWorkArea = false;
        if (!IsBuildableLayerCell(end) || !IsValidWorkAreaSelection(_workAreaStart, end))
        {
            _worldView.SetWorkPreview(default, []);
            _inspector.Text = _workAreaStart.Z != end.Z
                ? "Zlecenie pracy musi mieścić się na jednym poziomie."
                : "Zaznaczenie wychodzi poza obszar mapy na oglądanym poziomie.";
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
            WorkMode.MineRock => SimulationCommand.DesignateRockMining(
                executeAt,
                _commandSequence++,
                _workAreaStart,
                end),
            WorkMode.CarveRampDown => SimulationCommand.DesignateRampDown(
                executeAt,
                _commandSequence++,
                _workAreaStart),
            WorkMode.CarveRampUp => SimulationCommand.DesignateRampUp(
                executeAt,
                _commandSequence++,
                _workAreaStart),
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
            _inspector.Text = "Nie można utworzyć tego zlecenia na wskazanym obszarze.";
            return;
        }
        _inspector.Text = _workMode switch
        {
            WorkMode.Clear => "Zlecono usunięcie celów pracy z zaznaczenia.",
            WorkMode.MineRock =>
                "Zlecono obszar tunelu; dispatcher będzie udostępniał kolejne ściany wraz z postępem kopania.",
            WorkMode.CarveRampDown => "Zlecono wykopanie pochylni na poziom niżej.",
            WorkMode.CarveRampUp => "Zlecono wykopanie pochylni na poziom wyżej.",
            WorkMode.CleanBlood => "Zlecono sprzątanie zaschniętych plam z wykonanych podłóg.",
            _ => _speed == 0
                ? "Zlecono wskazanie pasujących obiektów; cele dodano bez wznawiania czasu."
                : "Zlecono wskazanie pasujących obiektów; cele pojawią się po następnym ticku.",
        };
        CancelWorkMode(clearInspector: false);
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
        _worldView.SetWorkPreview(default, []);
        if (clearInspector && wasActive)
        {
            _inspector.Text = "Narzędzie obszaru pracy anulowane.";
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
            _inspector.Text = "Aktywne narzędzie anulowane.";
        }
    }

    private void BeginRaidTargetSelection(SimulationSnapshot snapshot)
    {
        CancelActiveTool();
        _isRaidTargetMode = true;
        _raidTargetRadius = snapshot.RaidPlan.TargetRadius;
        _visibleLevel = snapshot.RaidPlan.Target.Z;
        _worldView.SetVisibleLevel(_visibleLevel);
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

    private void CreateStorage(GridPosition cell, ResourceKind resource)
    {
        var terrainAvailable = cell.Z == 0
            ? _engine.World.IsSurfaceTraversable(cell)
            : _engine.World.IsTerrainTraversable(cell);
        var discovered = _engine.Visibility.Get(cell).IsDiscovered();
        if (!discovered)
        {
            _inspector.Text = $"{cell} • skład można zaplanować dopiero na odkrytym polu.";
            return;
        }
        if (cell.Z < 0 && _engine.World.IsSolidCaveRock(cell))
        {
            _inspector.Text = $"{cell} • to ściana jaskini; najpierw oznacz ją do wykopania.";
            return;
        }
        if (!terrainAvailable)
        {
            _inspector.Text = $"{cell} • pole jest zablokowane i nie może przyjąć składu.";
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
            ResourceKind.Equipment => SimulationCommand.BuildEquipmentStorage(
                _engine.CurrentTick.Next(), _commandSequence++, cell),
            ResourceKind.Materials => SimulationCommand.BuildMaterialsStorage(
                _engine.CurrentTick.Next(), _commandSequence++, cell),
            _ => throw new ArgumentOutOfRangeException(nameof(resource)),
        };
        _engine.QueueCommand(command);
        var capacity = resource switch
        {
            ResourceKind.Food => _engine.Definitions.Storage.SmallFoodCapacity,
            ResourceKind.Equipment => 32,
            ResourceKind.Materials => 64,
            _ => 64,
        };
        _inspector.Text = $"{cell} • wyznaczono plac pod skład {DescribeResource(resource)} 0/{capacity} • blueprint żąda 2 drewna";
    }

    private void HandleEvents(
        IReadOnlyList<SimulationEvent> events,
        SimulationSnapshot snapshot)
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
                WorkDesignationKind.GatherReeds => "Dispatcher dodał wskazane trzcinowisko do zebrania.",
                WorkDesignationKind.GatherBrushwood => "Dispatcher dodał wskazany stos chrustu do transportu.",
                WorkDesignationKind.GatherStone => "Dispatcher dodał wskazany stos kamieni do transportu.",
                WorkDesignationKind.UprootBerryBush => "Dispatcher dodał krzak do trwałego wykarczowania.",
                WorkDesignationKind.FellTree => "Dispatcher dodał drzewo lub martwy pień do wyrębu.",
                WorkDesignationKind.QuarryBoulder => "Dispatcher dodał głaz do rozbicia kilofem.",
                WorkDesignationKind.MineRock => "Dispatcher dodał ścianę jaskini do wykopania.",
                WorkDesignationKind.CarveRampDown => "Dispatcher dodał pochylnię w dół do wykopania.",
                WorkDesignationKind.CarveRampUp => "Dispatcher dodał pochylnię w górę do wykopania.",
                WorkDesignationKind.Scout => "Dispatcher ograniczył zwiad do wskazanego obszaru.",
                WorkDesignationKind.HuntAnimal => "Dispatcher dodał wskazane zwierzę do upolowania.",
                WorkDesignationKind.CleanBlood => "Dispatcher dodał zaschniętą plamę do sprzątnięcia.",
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
            _inspector.Text = "Nie można wyznaczyć placu budowy: teren jest niedostępny albo zajęty.";
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
            _inspector.Text = $"Warsztat przyjął zlecenie: " +
                DescribeCraftingRecipe((CraftingRecipeKind)craftingEvent.Amount) + ".";
        }
        else if (craftingEvent.Kind == SimulationEventKind.CraftingMaterialDelivered)
        {
            _inspector.Text = "Dostarczono składnik do prymitywnego warsztatu.";
        }
        else if (craftingEvent.Kind == SimulationEventKind.CraftingCompleted)
        {
            _inspector.Text = $"Goblin ukończył: " +
                DescribeCraftingRecipe((CraftingRecipeKind)craftingEvent.Amount) +
                " • przedmiot trafił do osobistego ekwipunku.";
        }
        else if (craftingEvent.Kind == SimulationEventKind.CommandRejected)
        {
            _inspector.Text = "Nie można dodać receptury: we wskazanym miejscu nie ma warsztatu.";
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

        var raidEvent = events.LastOrDefault(item => item.Kind is
            SimulationEventKind.RaidVictory or SimulationEventKind.RaidDefeated);
        if (raidEvent.Kind == SimulationEventKind.RaidVictory)
        {
            _inspector.Text = $"Najazd zakończony zwycięstwem • wróciło {raidEvent.Amount} goblinów.";
        }
        else if (raidEvent.Kind == SimulationEventKind.RaidDefeated)
        {
            _inspector.Text = "Najazd zakończony klęską • oddział został rozbity.";
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
                if (_engine.World.HasPrimitiveWorkshop(levelPosition))
                {
                    SelectActor(EntityId.None);
                    ShowWorkshopDetails(levelPosition);
                    return;
                }

                var caveCell = _engine.Map.GetCaveCell(levelPosition);
                var passages = _engine.World.CreateVerticalPassageSnapshot()
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
            _inspector.Text = isDoorOpen
                ? $"{cell} • polecenie zamknięcia drzwi" +
                  (_speed == 0 ? " • zostanie wykonane po wznowieniu czasu" : string.Empty)
                : $"{cell} • polecenie otwarcia drzwi" +
                  (_speed == 0 ? " • zostanie wykonane po wznowieniu czasu" : string.Empty);
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
        else if (actors.Length == 0 && _engine.World.HasPrimitiveWorkshop(cell))
        {
            ShowWorkshopDetails(cell);
        }

        _inspector.Text = $"{cell}" +
            (visibility == CellVisibility.Explored ? " • odkryte, obecnie niewidoczne" : string.Empty) +
            $" • {terrain.Terrain}{DescribeWaterDepth(terrain)} • wilgoć {terrain.Moisture} • żyzność {terrain.Fertility}" +
            (plant is null
                ? string.Empty
                : $" • {DescribeFoodSource(plant.Value.Kind)} {plant.Value.Biomass}/{plant.Value.Capacity}") +
            (objects.Count == 0 ? string.Empty : $" • {string.Join(", ", objects.Select(item => item.Kind))}") +
            (objects.Any(item => item.Kind == WorldObjectKind.GoblinFieldCamp)
                ? " • PPM: menu obozu"
                : string.Empty) +
            (humanVillagers.Length == 0
                ? string.Empty
                : $" • ludzie: {string.Join(", ", humanVillagers.Select(DescribeVillager))}") +
            (humanFields.Length == 0
                ? string.Empty
                : $" • pole: {string.Join(", ", humanFields.Select(field => $"{DescribeField(field.Phase)} {field.GrowthDays}/120 dni"))}") +
            (!humanVillagers.Any(villager => villager.Role == HumanCohortRole.Guards)
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

    private static string DescribeCraftingOrder(CraftingOrderSnapshot order)
    {
        var materials = string.Join(", ", order.Materials.Select(material =>
            $"{DescribeResource(material.Resource)} " +
            $"{material.DeliveredQuantity}/{material.RequiredQuantity}"));
        return $"{DescribeCraftingRecipe(order.Recipe)} • {materials} • praca " +
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
        FoodKind.RawMeat => "surowe mięso",
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
        ResourceKind.Hide => "skór",
        ResourceKind.Vegetation => "roślinności",
        ResourceKind.Equipment => "sprzętu",
        ResourceKind.Materials => "materiałów",
        _ => "towarów",
    };

    private static string DescribeCraftingRecipe(CraftingRecipeKind recipe) => recipe switch
    {
        CraftingRecipeKind.PrimitiveSling => "prymitywna proca",
        CraftingRecipeKind.BoneKnife => "kościany nóż",
        CraftingRecipeKind.FightingStick => "kij bojowy",
        CraftingRecipeKind.StoneClub => "kamienna maczuga",
        CraftingRecipeKind.HideClothes => "skórzany ubiór",
        CraftingRecipeKind.ReedClothes => "sitowiowy ubiór",
        CraftingRecipeKind.PrimitiveWaterskin => "prymitywny bukłak",
        _ => recipe.ToString(),
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
        ResourceVariant.EquipmentPrimitiveSling => "prymitywna proca",
        ResourceVariant.EquipmentBoneKnife => "kościany nóż",
        ResourceVariant.EquipmentFightingStick => "kij bojowy",
        ResourceVariant.EquipmentStoneClub => "kamienna maczuga",
        ResourceVariant.EquipmentHideClothes => "skórzany ubiór",
        ResourceVariant.EquipmentReedClothes => "sitowiowy ubiór",
        ResourceVariant.EquipmentPrimitiveWaterskin => "prymitywny bukłak",
        ResourceVariant.EquipmentRagClothes => "łachmany",
        ResourceVariant.EquipmentWoodenAxe => "drewniana siekiera",
        ResourceVariant.EquipmentPrimitivePickaxe => "prymitywny kilof",
        ResourceVariant.EquipmentWoodenHoe => "drewniana motyka",
        ResourceVariant.EquipmentHumanWoodenAxe => "ludzka drewniana siekiera",
        ResourceVariant.EquipmentWoodenBucket => "drewniane wiadro",
        ResourceVariant.EquipmentWoodenSpear => "drewniana włócznia",
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
        ActorJobKind.SupplyCrafting when job.Stage == ActorJobStage.Collecting &&
            job.Phase == ActorJobPhase.Traveling =>
            $"idzie po składnik do warsztatu ×{job.ReservedQuantity}",
        ActorJobKind.SupplyCrafting when job.Stage == ActorJobStage.Collecting =>
            $"pobiera składnik ×{job.ReservedQuantity}",
        ActorJobKind.SupplyCrafting when job.Phase == ActorJobPhase.Traveling =>
            $"niesie składnik do warsztatu ×{job.ReservedQuantity}",
        ActorJobKind.SupplyCrafting => $"odkłada składnik ({job.RemainingWorkTicks})",
        ActorJobKind.Craft when job.Phase == ActorJobPhase.Traveling =>
            $"idzie do warsztatu → {job.Target}",
        ActorJobKind.Craft => $"wytwarza przedmiot ({job.RemainingWorkTicks})",
        ActorJobKind.ClearConstructionSite when job.Stage == ActorJobStage.Collecting &&
            job.Phase == ActorJobPhase.Traveling => "idzie uprzątnąć plac budowy",
        ActorJobKind.ClearConstructionSite when job.Stage == ActorJobStage.Collecting =>
            $"podnosi przeszkodę ({job.RemainingWorkTicks})",
        ActorJobKind.ClearConstructionSite when job.Phase == ActorJobPhase.Traveling =>
            $"wynosi przedmiot z placu ×{job.ReservedQuantity}",
        ActorJobKind.ClearConstructionSite =>
            $"odkłada przedmiot z placu ({job.RemainingWorkTicks})",
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
        ActorJobKind.Resupply when job.Stage == ActorJobStage.ProvisioningAmmo &&
            job.Phase == ActorJobPhase.Traveling => $"idzie po kamienie do rzucania → {job.Target}",
        ActorJobKind.Resupply when job.Stage == ActorJobStage.ProvisioningAmmo =>
            $"pakuje kamienie ×{job.ReservedQuantity} ({job.RemainingWorkTicks})",
        ActorJobKind.Resupply when job.Stage == ActorJobStage.ProvisioningEquipment &&
            job.Phase == ActorJobPhase.Traveling => $"idzie po sprzęt → {job.Target}",
        ActorJobKind.Resupply when job.Stage == ActorJobStage.ProvisioningEquipment =>
            $"pobiera sprzęt ({job.RemainingWorkTicks})",
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
        ActorJobKind.CarveRamp when job.Phase == ActorJobPhase.Traveling =>
            $"idzie wykopać pochylnię → {job.Target}",
        ActorJobKind.CarveRamp => $"wykuwa pochylnię ({job.RemainingWorkTicks})",
        ActorJobKind.TendBud when job.Phase == ActorJobPhase.Traveling =>
            $"idzie opiekować się pąkiem → {job.Target}",
        ActorJobKind.TendBud => $"dogląda pąka ({job.RemainingWorkTicks})",
        ActorJobKind.HuntAnimal when job.Phase == ActorJobPhase.Traveling =>
            $"ściga zwierzę → {job.Target}",
        ActorJobKind.HuntAnimal => $"poluje ({job.RemainingWorkTicks})",
        ActorJobKind.CleanBlood when job.Phase == ActorJobPhase.Traveling =>
            $"idzie zmyć krew → {job.Target}",
        ActorJobKind.CleanBlood => $"szoruje podłogę ({job.RemainingWorkTicks})",
        ActorJobKind.LootRaid when job.Stage == ActorJobStage.Collecting =>
            $"idzie po łupy → {job.Target}",
        ActorJobKind.LootRaid => $"odnosi łupy do obozu → {job.Target}",
        ActorJobKind.RecoverRaidCorpse when job.Stage == ActorJobStage.Collecting =>
            $"idzie po zwłoki → {job.Target}",
        ActorJobKind.RecoverRaidCorpse => $"niesie zwłoki do obozu → {job.Target}",
        ActorJobKind.ConsumeRaidCorpse when job.Phase == ActorJobPhase.Traveling =>
            $"idzie pożreć zwłoki → {job.Target}",
        ActorJobKind.ConsumeRaidCorpse => $"pożera zwłoki ({job.RemainingWorkTicks})",
        _ => "bez zadania",
    };

    private string DescribeConstructionSite(ConstructionSiteSnapshot site)
    {
        var materials = string.Join(", ", site.Materials.Select(material =>
            $"{DescribeResource(material.Resource)} {material.DeliveredQuantity}/{material.RequiredQuantity}"));
        var workDone = site.TotalWorkTicks - site.RemainingWorkTicks;
        var readiness = DescribeConstructionReadiness(
            _engine.InspectConstructionReadiness(site.Id, evaluateReachability: false));
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
        ConstructionReadinessState.AwaitingSiteClearance =>
            "oczekuje na uprzątnięcie luźnych przedmiotów z placu",
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
        ConstructionKind.EquipmentStorage => "składu sprzętu",
        ConstructionKind.MaterialsStorage => "składu materiałów",
        ConstructionKind.WoodenWalkway => "pomostu",
        ConstructionKind.GoblinFieldCamp => "obozu wypadowego",
        ConstructionKind.GoblinHut => "chaty goblinów",
        ConstructionKind.WoodenWall => "drewnianej ściany",
        ConstructionKind.StoneWall => "kamiennego muru",
        ConstructionKind.WoodenDoorFrame => "drewnianej ościeżnicy",
        ConstructionKind.StoneDoorFrame => "kamiennej ościeżnicy",
        ConstructionKind.WoodenDoor => "drewnianych drzwi",
        ConstructionKind.WallTorch => "pochodni ściennej",
        ConstructionKind.PrimitiveWorkshop => "prymitywnego warsztatu",
        _ => "konstrukcji",
    };

    private static string DescribeFoodSource(PlantKind kind) => kind switch
    {
        PlantKind.BerryBush => "jagody",
        PlantKind.MushroomCluster => "grzyby",
        PlantKind.EdibleRoots => "korzonki",
        PlantKind.FishShoal => "ryby",
        PlantKind.ReedBed => "sitowie",
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
        var snapshot = _engine.CreateSnapshot();
        _populationTargetDraft = snapshot.PopulationTarget;
        UpdateStatistics(snapshot);
        _statisticsWindow.Popup();
    }

    private void ChangePopulationTarget(int delta)
    {
        _populationTargetDraft = Math.Clamp(_populationTargetDraft + delta, 0, 1_000);
        _populationTargetText.Text = $"Docelowa liczebność: {_populationTargetDraft}";
        _engine.QueueCommand(SimulationCommand.ConfigurePopulationTarget(
            _engine.CurrentTick.Next(),
            _commandSequence++,
            _populationTargetDraft));
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
        _populationTargetText.Text =
            $"Docelowa liczebność: {_populationTargetDraft} • pąki: {snapshot.GoblinBuds.Count}";
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
            $"Plemię: {snapshot.Actors.Count} • pąki {snapshot.GoblinBuds.Count} " +
            $"• cel {snapshot.PopulationTarget}\n" +
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
            $"• pająki jaskiniowe {snapshot.Animals.Count(animal => animal.Kind == AnimalKind.CaveSpider)}\n" +
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
        GoblinReproductionReadinessKind.AtTarget => "osiągnięto docelową liczebność",
        GoblinReproductionReadinessKind.Ready =>
            $"gotowe ({readiness.AvailableFood}/{readiness.RequiredFood} żywności, " +
            $"rodzice {readiness.EligibleParents}, miejsca {readiness.SuitableMoistSites})",
        GoblinReproductionReadinessKind.InsufficientFood =>
            $"za mało dostępnej żywności ({readiness.AvailableFood}/{readiness.RequiredFood})",
        GoblinReproductionReadinessKind.NoMoistSpace => "brak wolnego wilgotnego miejsca w chacie",
        GoblinReproductionReadinessKind.NoEligibleParent =>
            "brak wolnego, zdrowego, najedzonego i wypoczętego rodzica",
        GoblinReproductionReadinessKind.BudWaitingForCare =>
            $"pąk czeka na opiekuna ({readiness.UntendedBuds})",
        GoblinReproductionReadinessKind.BudBeingTended => "opiekun zajmuje się pąkiem",
        _ => "stan nieznany",
    };

    private static string DescribeAnimal(AnimalSnapshot animal)
    {
        var name = animal.Kind switch
        {
            AnimalKind.MarshHare => "zając bagienny",
            AnimalKind.SwampBoar => "dzik bagienny",
            AnimalKind.CaveSpider => "pająk jaskiniowy",
            _ => "nieznane stworzenie",
        };
        var activity = animal.Activity switch
        {
            AnimalActivity.Foraging => "żeruje",
            AnimalActivity.Resting => "odpoczywa",
            AnimalActivity.Fleeing => "ucieka",
            AnimalActivity.Threatening => "atakuje",
            _ => "wędruje",
        };
        var sex = animal.Sex == AnimalSex.Female ? "samica" : "samiec";
        var age = animal.IsAdult ? "dorosły" : "młode";
        return $"{name} • {sex} • {age} • {activity} • zdrowie {animal.Health}";
    }

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

        UpdateGoblinDetails(_engine.CreateSnapshot());
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
        UpdateStorageDetails(zone);
        _storageDetails.Popup();
    }

    private void UpdateStorageDetails(StorageZoneSnapshot zone)
    {
        var snapshot = _engine.CreateSnapshot();
        UpdateStorageDetails(zone, snapshot);
    }

    private void UpdateStorageDetails(
        StorageZoneSnapshot zone,
        SimulationSnapshot snapshot)
    {
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
        var hasGlobalResourcePriority = zone.AcceptedResource != ResourceKind.Materials;
        var globalPriority = hasGlobalResourcePriority
            ? snapshot.ResourcePriorities
                .Single(priority => priority.Resource == zone.AcceptedResource)
                .Priority
            : StoragePriority.Normal;
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
                ? $"Wiek: {actor.AgeDays} dni • młode, nie pracuje przez pierwszy sezon"
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
            AddInventoryIcon(ItemIcon.WoodenAxe, "Drewniana siekiera • narzędzie do wyrębu");
        }
        if (actor.Equipment.HasFlag(PersonalEquipment.PrimitivePickaxe))
        {
            AddInventoryIcon(_pickaxeIcon, "Prymitywny kilof • narzędzie do rozbijania głazów");
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
        margin.GuiInput += inputEvent => CloseWindowOnSecondaryInput(inputEvent, _workshopDetails);

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
        var close = new Button
        {
            Text = "Zamknij",
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
        };
        close.Pressed += _workshopDetails.Hide;
        content.AddChild(close);
    }

    private void AddWorkshopRecipeButton(
        VBoxContainer recipes,
        CraftingRecipeKind recipe,
        Texture2D productIcon,
        string name,
        params (Texture2D Icon, string Name, int Quantity)[] ingredients)
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
        _selectedWorkshop = workshop;
        UpdateWorkshopDetails(_engine.CreateSnapshot());
        _workshopDetails.PopupCentered();
    }

    private void QueueWorkshopRecipe(CraftingRecipeKind recipe)
    {
        if (_selectedWorkshop is not { } workshop ||
            !_engine.World.HasPrimitiveWorkshop(workshop))
        {
            _selectedWorkshop = null;
            _workshopDetails.Hide();
            _inspector.Text = "Wybrany prymitywny warsztat już nie istnieje.";
            return;
        }

        _engine.QueueCommand(SimulationCommand.QueueCraftingRecipe(
            _engine.CurrentTick.Next(), _commandSequence++, workshop, recipe));
        _inspector.Text = $"Warsztat {workshop}: dodano do kolejki " +
            DescribeCraftingRecipe(recipe) +
            (_speed == 0 ? " • zlecenie ruszy po wznowieniu czasu." : ".");
    }

    private void UpdateWorkshopDetails(SimulationSnapshot snapshot)
    {
        if (_selectedWorkshop is not { } workshop ||
            !_engine.World.HasPrimitiveWorkshop(workshop))
        {
            _selectedWorkshop = null;
            _workshopDetails.Hide();
            return;
        }

        var orders = snapshot.CraftingOrders
            .Where(order => order.Workshop == workshop)
            .OrderBy(order => order.Id)
            .ToArray();
        var stocks = new[]
        {
            ResourceKind.Wood, ResourceKind.Stone, ResourceKind.Reeds,
            ResourceKind.Bone, ResourceKind.Hide,
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
        UpdatePlanner(_engine.CreateSnapshot(), force: true);
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
                : DescribeWorkOrderReadiness(snapshot, kind, active);
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

    private static string DescribeWorkOrderReadiness(
        SimulationSnapshot snapshot,
        WorkDesignationKind kind,
        int activeWorkers)
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
        if (kind is WorkDesignationKind.QuarryBoulder or WorkDesignationKind.MineRock or
                WorkDesignationKind.CarveRampDown or WorkDesignationKind.CarveRampUp &&
            living.All(actor => !actor.Equipment.HasFlag(PersonalEquipment.PrimitivePickaxe)))
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
                    (zone.AcceptedResource is ResourceKind.Any ||
                     zone.AcceptedResource == resource)))
            {
                return "wstrzymane: brak miejsca w pasującym składzie";
            }
        }
        return "wykonalne; oczekuje na dispatchera";
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

    private void CreateWorldContextMenu()
    {
        _worldContextMenu = new PopupMenu
        {
            Name = "WorldContextMenu",
            MinSize = new Vector2I(245, 0),
        };
        _worldContextMenu.IdPressed += HandleWorldContextAction;
        AddChild(_worldContextMenu);
    }

    private bool TryShowWorldContextMenu(Vector2 screenPosition)
    {
        if (!_hasActiveSession || _mainMenu.Visible)
        {
            return false;
        }

        var clicked = ScreenToVisibleCell(screenPosition);
        var snapshot = _engine.CreateSnapshot();
        if (!snapshot.GetVisibility(clicked, _engine.Map.Width).IsDiscovered())
        {
            return false;
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
            return false;
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
        _worldContextMenu.AddItem($"Obóz wypadowy • z={camp.Anchor.Z}");
        _worldContextMenu.SetItemDisabled(_worldContextMenu.ItemCount - 1, true);
        _worldContextMenu.AddItem(
            storage.Id == EntityId.None
                ? $"Prowiant: brak składu • obsada {occupants.Length}/{SimulationDefinitions.FieldCampCapacity}"
                : $"Prowiant {storage.StoredQuantity}/{storage.Capacity} • obsada " +
                  $"{occupants.Length}/{SimulationDefinitions.FieldCampCapacity}");
        _worldContextMenu.SetItemDisabled(_worldContextMenu.ItemCount - 1, true);
        _worldContextMenu.AddSeparator();
        _worldContextMenu.AddItem("Edytuj najazd…", (int)WorldContextAction.EditRaid);
        _worldContextMenu.SetItemDisabled(
            _worldContextMenu.GetItemIndex((int)WorldContextAction.EditRaid),
            snapshot.RaidPhase is GoblinRaidPhase.Marching or GoblinRaidPhase.Looting or
                GoblinRaidPhase.Returning);
        _worldContextMenu.AddItem(
            snapshot.RaidPhase is GoblinRaidPhase.Preparing or GoblinRaidPhase.Ready
                ? "Wstrzymaj przygotowania"
                : snapshot.RaidPhase == GoblinRaidPhase.Marching
                    ? "Odwołaj najazd"
                : snapshot.RaidPhase is GoblinRaidPhase.Looting or GoblinRaidPhase.Returning
                    ? "Najazd w toku"
                : "Przygotuj najazd",
            (int)WorldContextAction.ToggleRaidPreparation);
        _worldContextMenu.SetItemDisabled(
            _worldContextMenu.GetItemIndex((int)WorldContextAction.ToggleRaidPreparation),
            snapshot.RaidPhase is GoblinRaidPhase.Looting or GoblinRaidPhase.Returning);
        _worldContextMenu.AddItem(
            $"Wybierz cel… (promień {snapshot.RaidPlan.TargetRadius})",
            (int)WorldContextAction.SelectRaidTarget);
        _worldContextMenu.SetItemDisabled(
            _worldContextMenu.GetItemIndex((int)WorldContextAction.SelectRaidTarget),
            snapshot.RaidPhase is GoblinRaidPhase.Marching or GoblinRaidPhase.Looting or
                GoblinRaidPhase.Returning);
        if (snapshot.RaidPhase == GoblinRaidPhase.Ready)
        {
            _worldContextMenu.AddItem("ATAK!", (int)WorldContextAction.LaunchRaid);
        }
        _worldContextMenu.AddSeparator();
        _worldContextMenu.AddItem(
            occupants.Length == 0
                ? "Brak goblinów w obozie"
                : $"Zaznacz gobliny w obozie ({occupants.Length})",
            (int)WorldContextAction.SelectCampOccupants);
        _worldContextMenu.SetItemDisabled(
            _worldContextMenu.GetItemIndex((int)WorldContextAction.SelectCampOccupants),
            occupants.Length == 0);
        _worldContextMenu.AddItem(
            "Otwórz skład prowiantu",
            (int)WorldContextAction.OpenCampStorage);
        _worldContextMenu.SetItemDisabled(
            _worldContextMenu.GetItemIndex((int)WorldContextAction.OpenCampStorage),
            storage.Id == EntityId.None);
        _worldContextMenu.Position = new Vector2I(
            Mathf.RoundToInt(screenPosition.X),
            Mathf.RoundToInt(screenPosition.Y));
        _worldContextMenu.Popup();
        return true;
    }

    private void ShowCorpseContextMenu(
        Vector2 screenPosition,
        SimulationSnapshot snapshot,
        CorpseSnapshot corpse)
    {
        _contextCampAnchor = null;
        _contextCorpseId = corpse.Id;
        var isActiveRaid = snapshot.RaidPhase is GoblinRaidPhase.Marching or
            GoblinRaidPhase.Looting;
        var isInRaidArea = corpse.Position.Z == snapshot.RaidPlan.Target.Z &&
            Math.Abs(corpse.Position.X - snapshot.RaidPlan.Target.X) +
            Math.Abs(corpse.Position.Y - snapshot.RaidPlan.Target.Y) <=
            snapshot.RaidPlan.TargetRadius;
        var enabled = corpse.Kind == CorpseKind.Human && isActiveRaid && isInRaidArea;

        _worldContextMenu.Clear();
        _worldContextMenu.AddItem($"{corpse.Name} • zwłoki");
        _worldContextMenu.SetItemDisabled(_worldContextMenu.ItemCount - 1, true);
        _worldContextMenu.AddItem(
            $"Mięso: {corpse.EdiblePortions} • przedmioty: {corpse.Contents.Count}");
        _worldContextMenu.SetItemDisabled(_worldContextMenu.ItemCount - 1, true);
        _worldContextMenu.AddSeparator("Los zwłok w obszarze najazdu");
        AddCorpseContextAction(
            "Przeszukaj wyposażenie i zapasy",
            WorldContextAction.LootRaidCorpses,
            enabled);
        AddCorpseContextAction(
            "Pożryj zwłoki",
            WorldContextAction.ConsumeRaidCorpses,
            enabled);
        AddCorpseContextAction(
            "Zanieś do obozu",
            WorldContextAction.RecoverRaidCorpses,
            enabled);
        AddCorpseContextAction(
            "Zanieś i zapyl w obozie",
            WorldContextAction.RecoverAndBudRaidCorpses,
            enabled);
        AddCorpseContextAction(
            "Zapyl na miejscu",
            WorldContextAction.BudRaidCorpsesInPlace,
            enabled);
        if (!enabled)
        {
            _worldContextMenu.AddSeparator();
            _worldContextMenu.AddItem(isActiveRaid
                ? "Zwłoki leżą poza obszarem tego najazdu"
                : "Brak aktywnego najazdu — rozkaz pozostaje niedostępny");
            _worldContextMenu.SetItemDisabled(_worldContextMenu.ItemCount - 1, true);
        }
        _worldContextMenu.Position = new Vector2I(
            Mathf.RoundToInt(screenPosition.X),
            Mathf.RoundToInt(screenPosition.Y));
        _worldContextMenu.Popup();
    }

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
        if (_contextCorpseId != EntityId.None)
        {
            HandleCorpseContextAction((WorldContextAction)actionId);
            _contextCorpseId = EntityId.None;
            return;
        }

        var campAnchor = _contextCampAnchor;
        _contextCampAnchor = null;
        if (campAnchor is null)
        {
            return;
        }

        var snapshot = _engine.CreateSnapshot();
        var camp = snapshot.WorldObjects.FirstOrDefault(worldObject =>
            worldObject.Kind == WorldObjectKind.GoblinFieldCamp &&
            worldObject.Owner == WorldObjectOwner.GoblinTribe &&
            worldObject.Anchor == campAnchor.Value);
        if (camp is null)
        {
            return;
        }

        switch ((WorldContextAction)actionId)
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
                    ? "Zlecono odwołanie trwającego najazdu."
                    : suspendPreparation
                    ? "Zlecono wstrzymanie przygotowań do najazdu."
                    : $"Zlecono przygotowanie najazdu w obozie {camp.Anchor}.";
                if (_speed == 0)
                {
                    _inspector.Text += " Rozkaz zostanie wykonany po wznowieniu czasu.";
                }
                break;
            case WorldContextAction.SelectRaidTarget:
                BeginRaidTargetSelection(snapshot);
                break;
            case WorldContextAction.LaunchRaid:
                _engine.QueueCommand(SimulationCommand.LaunchRaid(
                    _engine.CurrentTick.Next(),
                    _commandSequence++));
                _inspector.Text = "Wydano rozkaz ATAK!" +
                    (_speed == 0 ? " Najazd ruszy po wznowieniu czasu." : string.Empty);
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

    private void HandleCorpseContextAction(WorldContextAction action)
    {
        var snapshot = _engine.CreateSnapshot();
        var directives = snapshot.RaidPlan.Directives;
        switch (action)
        {
            case WorldContextAction.LootRaidCorpses:
                directives |= RaidDirective.LootEquipment |
                    RaidDirective.LootSupplies |
                    RaidDirective.LootFood;
                break;
            case WorldContextAction.ConsumeRaidCorpses:
                directives |= RaidDirective.ConsumeCorpses;
                break;
            case WorldContextAction.RecoverRaidCorpses:
                directives = SetCorpseHandling(
                    directives,
                    RaidDirective.RecoverCorpses);
                break;
            case WorldContextAction.RecoverAndBudRaidCorpses:
                directives = SetCorpseHandling(
                    directives,
                    RaidDirective.BudCorpses);
                break;
            case WorldContextAction.BudRaidCorpsesInPlace:
                directives = SetCorpseHandling(
                    directives,
                    RaidDirective.BudCorpsesInPlace);
                break;
            default:
                return;
        }

        _engine.QueueCommand(SimulationCommand.ConfigureRaidDirectives(
            _engine.CurrentTick.Next(),
            _commandSequence++,
            directives));
        _inspector.Text = "Zmieniono sposób traktowania zwłok po walce. " +
            "Rozkaz obejmuje zwłoki w aktualnym obszarze najazdu.";
    }

    private static RaidDirective SetCorpseHandling(
        RaidDirective directives,
        RaidDirective selected) =>
        (directives & ~(RaidDirective.RecoverCorpses |
            RaidDirective.BudCorpses |
            RaidDirective.BudCorpsesInPlace)) | selected;

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
            ? $"Obóz {camp.Anchor} jest pusty."
            : $"Zaznaczono {occupants.Length} goblinów przebywających w obozie {camp.Anchor}.";
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
        _raidEngagement.AddItem("Atakuj wszystkich, którzy nie uciekają", 1);
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
            "Kontynuuj, gdy widać cele");
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
        var snapshot = _engine.CreateSnapshot();
        _raidDraftRallyPoint = rallyPoint;
        _raidEngagement.Select(snapshot.RaidPlan.Has(RaidDirective.AttackNonFleeing) ? 1 : 0);
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
        if (snapshot.RaidPartyIds.Count > 0)
        {
            _raidDraftIds.UnionWith(snapshot.RaidPartyIds);
        }
        else if (_selectedActorIds.Count > 0)
        {
            _raidDraftIds.UnionWith(_selectedActorIds
                .Where(id => snapshot.Actors.Any(actor => actor.Id == id && actor.Health > 0))
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
                .Where(actor => actor.Health > 0 && campFloor.Contains(actor.Position))
                .OrderBy(actor => actor.Id)
                .Take(SimulationDefinitions.FieldCampCapacity)
                .Select(actor => actor.Id));
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
        var selectionLocked = snapshot.RaidPhase is GoblinRaidPhase.Marching or
            GoblinRaidPhase.Looting or GoblinRaidPhase.Returning ||
            snapshot.HumanVillage.GoblinAttackOrdered;
        _raidEngagement.Disabled = selectionLocked;
        _raidCorpseHandling.Disabled = selectionLocked;
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
                $"Punkt zbiórki: obóz {_raidDraftRallyPoint.Value}. Wybierz od 1 do 5 goblinów.",
            _ => "Wybierz od 1 do 5 goblinów. Wyruszą po zebraniu się w obozie i uzupełnieniu zapasów.",
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
        var snapshot = _engine.CreateSnapshot();
        if (_raidDraftIds.Count == 0 || snapshot.RaidPhase is GoblinRaidPhase.Marching or
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
            ? RaidDirective.AttackNonFleeing
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
        _inspector.Text = $"Zapisano plan najazdu dla {_raidDraftIds.Count} goblinów. " +
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

        var snapshot = _engine.CreatePresentationSnapshot();
        var minimumSurfaceFloor = Enumerable.Range(0, _engine.Map.CellCount)
            .Select(index => _engine.Map.GetCell(new GridPosition(
                index % _engine.Map.Width,
                index / _engine.Map.Width)).FloorLevel)
            .Min(level => (int)level);
        var minimumLevel = Math.Min(minimumSurfaceFloor, _engine.Map.DeepestCaveLevel);
        var maximumLevel = Math.Max(
            _engine.Map.MaximumTerrainLevel,
            snapshot.WorldObjects
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
        _inspector.Text = $"Warstwa z={_visibleLevel} leży poza obszarem mapy.";
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
            0 => "Budowanie",
            < 0 => "Budowanie pod ziemią • składy, pomosty, ściany i drzwi",
            _ => $"Budowanie na warstwie z={_visibleLevel}",
        };
        work.TooltipText = _visibleLevel switch
        {
            0 => "Zlecenia pracy",
            < 0 => "Zlecenia podziemne • zbieranie urobku, kopanie i czyszczenie",
            _ => $"Zlecenia pracy na warstwie z={_visibleLevel}",
        };
    }

    private void UpdateStatus(SimulationSnapshot? currentSnapshot = null)
    {
        var snapshot = currentSnapshot ?? _engine.CreatePresentationSnapshot();
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
            $" + {snapshot.GoblinBuds.Count} pąk. / cel {snapshot.PopulationTarget}" +
            $" / miejsca {snapshot.TribeNeeds.ShelterCapacity}" +
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
            _status.Text += _selectedActorIds.Count <= 1
                ? $"  •  wybrany {_selectedActorId}"
                : $"  •  wybrana grupa {_selectedActorIds.Count}";
            UpdateGoblinDetails(snapshot);
        }
        if (_workshopDetails.Visible)
        {
            UpdateWorkshopDetails(snapshot);
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
        else if (snapshot.RaidPhase == GoblinRaidPhase.Ready)
        {
            _status.Text += "  •  najazd: GOTOWY";
        }
        else if (snapshot.RaidPhase == GoblinRaidPhase.Suspended)
        {
            _status.Text += "  •  najazd: przygotowania wstrzymane";
        }
        else if (snapshot.RaidPhase == GoblinRaidPhase.Marching)
        {
            _status.Text += "  •  najazd: wymarsz";
        }
        else if (snapshot.RaidPhase == GoblinRaidPhase.Looting)
        {
            _status.Text += "  •  najazd: plądrowanie";
        }
        else if (snapshot.RaidPhase == GoblinRaidPhase.Returning)
        {
            _status.Text += "  •  najazd: powrót do obozu";
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
