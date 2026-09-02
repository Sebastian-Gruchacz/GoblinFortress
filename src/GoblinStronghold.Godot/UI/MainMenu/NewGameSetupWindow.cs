using Godot;
using GoblinStronghold.GodotClient.Application.Profiles;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map.Generation;
using System.Globalization;

namespace GoblinStronghold.GodotClient.UI.MainMenu;

public readonly record struct NewGameSetup(
    string ProfileName,
    LocationGenerationRequest Map);

public sealed partial class NewGameSetupWindow : Window
{
    private static readonly int[] SupportedMapDimensions = [64, 96, 128];

    private readonly Func<string, string, string> _translate;
    private readonly Func<string> _defaultProfileNameProvider;
    private readonly OptionButton _mode = new();
    private readonly Label _description = CreateWrappedLabel();
    private readonly Label _availableHeading = new();
    private readonly Label _plannedHeading = new();
    private readonly Label _mapSizeLabel = new();
    private readonly OptionButton _mapSize = new();
    private readonly Label _profileNameLabel = new();
    private readonly LineEdit _profileName = new();
    private readonly Label _seedLabel = new();
    private readonly LineEdit _seed = new();
    private readonly Button _randomizeSeed = new();
    private readonly Label _seedError = CreateWrappedLabel();
    private readonly Label _climateLabel = new();
    private readonly OptionButton _climate = new();
    private readonly Label _riverLabel = new();
    private readonly OptionButton _river = new();
    private readonly Label _roadLabel = new();
    private readonly OptionButton _road = new();
    private readonly Label _neighborsLabel = new();
    private readonly OptionButton _neighbors = new();
    private readonly Label _villageLabel = new();
    private readonly CheckButton _village = new();
    private readonly Label _reliefLabel = new();
    private readonly OptionButton _relief = new();
    private readonly Label _difficultyLabel = new();
    private readonly OptionButton _difficulty = new();
    private readonly Button _start = new();
    private readonly Button _cancel = new();

    public NewGameSetupWindow(
        Func<string, string, string> translate,
        Func<string> defaultProfileNameProvider)
    {
        _translate = translate;
        _defaultProfileNameProvider = defaultProfileNameProvider;
        Size = new Vector2I(760, 690);
        MinSize = new Vector2I(620, 560);
        Visible = false;
        Exclusive = true;
        Transient = true;
        CloseRequested += Hide;

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "top", "right", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", 16);
        }
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(margin);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 10);
        margin.AddChild(content);

        _mode.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _mode.AddItem(string.Empty);
        _mode.SetItemDisabled(0, true);
        _mode.AddItem(string.Empty);
        _mode.Select(1);
        content.AddChild(_mode);
        content.AddChild(_description);

        _availableHeading.ThemeTypeVariation = "HeaderSmall";
        content.AddChild(_availableHeading);
        var activeGrid = CreateGrid();
        content.AddChild(activeGrid);
        _profileName.MaxLength = GameProfileName.MaximumLength;
        AddRow(activeGrid, _profileNameLabel, _profileName);
        AddRow(activeGrid, _mapSizeLabel, _mapSize);
        AddRow(activeGrid, _seedLabel, CreateSeedControls());
        AddRow(activeGrid, _climateLabel, _climate);
        AddRow(activeGrid, _riverLabel, _river);
        AddRow(activeGrid, _roadLabel, _road);
        content.AddChild(_seedError);

        content.AddChild(new HSeparator());
        _plannedHeading.ThemeTypeVariation = "HeaderSmall";
        content.AddChild(_plannedHeading);
        var plannedGrid = CreateGrid();
        content.AddChild(plannedGrid);
        AddRow(plannedGrid, _neighborsLabel, _neighbors);
        AddRow(plannedGrid, _villageLabel, _village);
        AddRow(plannedGrid, _reliefLabel, _relief);
        AddRow(plannedGrid, _difficultyLabel, _difficulty);

        var spacer = new Control
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddChild(spacer);
        var actions = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
        };
        actions.AddThemeConstantOverride("separation", 8);
        content.AddChild(actions);
        actions.AddChild(_cancel);
        actions.AddChild(_start);

        _randomizeSeed.Pressed += RandomizeSeed;
        _start.Pressed += Start;
        _cancel.Pressed += Hide;

        PopulateOptions();
        RefreshLocalization();
        RandomizeSeed();
    }

    public event Action<NewGameSetup>? StartRequested;

    public void ShowSetup()
    {
        _profileName.Text = _defaultProfileNameProvider();
        _seedError.Text = string.Empty;
        PopupCentered();
        _start.GrabFocus();
    }

    public void RefreshLocalization()
    {
        Title = T("title");
        _mode.SetItemText(0, T("tutorial"));
        _mode.SetItemText(1, T("custom-map"));
        _mode.TooltipText = T("tutorial-tooltip");
        _description.Text = T("custom-map-description");
        _availableHeading.Text = T("available-heading");
        _plannedHeading.Text = T("planned-heading");
        _profileNameLabel.Text = T("profile-name");
        _profileName.TooltipText = T("profile-name-tooltip");
        _mapSizeLabel.Text = T("map-size");
        _mapSize.SetItemText(0, T("map-size-small"));
        _mapSize.SetItemText(1, T("map-size-standard"));
        _mapSize.SetItemText(2, T("map-size-large"));
        _seedLabel.Text = T("seed");
        _seed.TooltipText = T("seed-tooltip");
        _randomizeSeed.Text = T("randomize-seed");
        _climateLabel.Text = T("climate-zone");
        _climate.SetItemText(0, T("climate-temperate-marsh"));
        _riverLabel.Text = T("river");
        _river.SetItemText(0, T("river-none"));
        _river.SetItemText(1, T("river-single"));
        _river.SetItemText(2, T("river-branching"));
        _river.TooltipText = T("river-tooltip");
        _roadLabel.Text = T("road");
        _road.SetItemText(0, T("road-none"));
        _road.SetItemText(1, T("road-through"));
        _road.SetItemText(2, T("road-junction"));
        _road.TooltipText = T("road-tooltip");
        _neighborsLabel.Text = T("neighbor-civilizations");
        _neighbors.SetItemText(0, T("neighbor-current"));
        _villageLabel.Text = T("human-village");
        _village.Text = T("village-present");
        _reliefLabel.Text = T("terrain-relief");
        _relief.SetItemText(0, T("relief-current"));
        _difficultyLabel.Text = T("enemy-difficulty");
        _difficulty.SetItemText(0, T("difficulty-standard"));
        _start.Text = T("start");
        _cancel.Text = T("cancel");

        var plannedTooltip = T("planned-tooltip");
        foreach (var control in new Control[]
                 {
                     _neighbors, _village, _relief, _difficulty,
                 })
        {
            control.TooltipText = plannedTooltip;
        }
    }

    private void PopulateOptions()
    {
        foreach (var dimension in SupportedMapDimensions)
        {
            _mapSize.AddItem(string.Empty);
            _mapSize.SetItemMetadata(_mapSize.ItemCount - 1, dimension);
        }
        _mapSize.Select(1);

        _climate.AddItem(string.Empty);
        foreach (var mode in Enum.GetValues<RiverGenerationMode>())
        {
            _river.AddItem(string.Empty);
            _river.SetItemMetadata(_river.ItemCount - 1, (int)mode);
        }
        _river.Select((int)RiverGenerationMode.SingleChannel);
        foreach (var mode in Enum.GetValues<RoadGenerationMode>())
        {
            _road.AddItem(string.Empty);
            _road.SetItemMetadata(_road.ItemCount - 1, (int)mode);
        }
        _road.Select((int)RoadGenerationMode.ThroughRoad);
        _neighbors.AddItem(string.Empty);
        _neighbors.Disabled = true;
        _village.ButtonPressed = true;
        _village.Disabled = true;
        _relief.AddItem(string.Empty);
        _relief.Disabled = true;
        _difficulty.AddItem(string.Empty);
        _difficulty.Disabled = true;
    }

    private Control CreateSeedControls()
    {
        var controls = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        controls.AddThemeConstantOverride("separation", 8);
        _seed.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _seed.MaxLength = 16;
        controls.AddChild(_seed);
        controls.AddChild(_randomizeSeed);
        return controls;
    }

    private void RandomizeSeed()
    {
        _seed.Text = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0)
            .ToString("X16", CultureInfo.InvariantCulture);
        _seedError.Text = string.Empty;
    }

    private void Start()
    {
        if (!GameProfileName.TryNormalize(_profileName.Text, out var profileName))
        {
            _seedError.Text = T("invalid-profile-name");
            _profileName.GrabFocus();
            return;
        }

        var seedText = _seed.Text.Trim();
        if (seedText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            seedText = seedText[2..];
        }
        if (!ulong.TryParse(
                seedText,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var seedValue))
        {
            _seedError.Text = T("invalid-seed");
            _seed.GrabFocus();
            return;
        }

        var dimension = _mapSize.GetItemMetadata(_mapSize.Selected).AsInt32();
        var request = LocationGenerationRequest.CreateDefault(
            new WorldSeed(seedValue),
            dimension,
            dimension) with
        {
            RiverMode = (RiverGenerationMode)_river
                .GetItemMetadata(_river.Selected)
                .AsInt32(),
            RoadMode = (RoadGenerationMode)_road
                .GetItemMetadata(_road.Selected)
                .AsInt32(),
        };
        StartRequested?.Invoke(new NewGameSetup(profileName, request));
    }

    private string T(string key) => _translate("new-game", key);

    private static GridContainer CreateGrid()
    {
        var grid = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 7);
        return grid;
    }

    private static void AddRow(GridContainer grid, Label label, Control control)
    {
        label.VerticalAlignment = VerticalAlignment.Center;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(220, 0);
        grid.AddChild(label);
        control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        grid.AddChild(control);
    }

    private static Label CreateWrappedLabel() => new()
    {
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
    };
}
