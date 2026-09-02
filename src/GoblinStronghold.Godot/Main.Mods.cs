using Godot;
using GoblinStronghold.Simulation.Animals;
using GoblinStronghold.Simulation.Civilizations;
using GoblinStronghold.Simulation.Civilizations.Naming;
using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Localization;
using System.Text;

namespace GoblinStronghold.GodotClient;

public partial class Main
{
    private readonly List<ModManagerEntry> _modEntries = [];
    private ContentPackUserPreferences _modPreferences =
        ContentPackUserPreferences.Empty();
    private string _modPreferencesPath = string.Empty;
    private string? _modPreferencesError;
    private Window _modManagerWindow = null!;
    private VBoxContainer _modRows = null!;
    private Label _modStatus = null!;
    private Window _modInformationWindow = null!;
    private RichTextLabel _modInformationText = null!;
    private Button _modReportButton = null!;
    private ModManagerEntry? _reportedMod;

    private void LoadLocalContentPacks()
    {
        var modsPath = ProjectSettings.GlobalizePath("user://mods");
        _modPreferencesPath = ProjectSettings.GlobalizePath("user://settings/mods.json");
        try
        {
            Directory.CreateDirectory(modsPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            _modPreferencesError = exception.Message;
            GD.PushWarning($"Could not open the local content pack directory: {exception.Message}");
            TranslationCatalog.ResetToCorePack();
            AnimalSpeciesCatalog.ResetToCore();
            CivilizationCatalog.ResetToCore();
            ContentPackRuntime.ResetToCorePack();
            return;
        }

        try
        {
            _modPreferences = ContentPackUserPreferences.Load(_modPreferencesPath);
        }
        catch (InvalidDataException exception)
        {
            _modPreferences = ContentPackUserPreferences.Empty();
            _modPreferencesError = exception.Message;
            GD.PushWarning($"Could not load content pack preferences: {exception.Message}");
        }

        var discovery = LocalContentPackDiscovery.Discover(modsPath);
        var orderedPacks = _modPreferences.Order(discovery.Packs);
        var duplicateIds = orderedPacks
            .GroupBy(pack => pack.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in orderedPacks.Where(pack =>
                     duplicateIds.Contains(pack.Manifest.Id)))
        {
            errors[pack.SourceName] =
                $"Package ID '{pack.Manifest.Id}' is duplicated; no copy was loaded.";
        }

        var enabledPacks = new List<ContentPack>();
        TranslationCatalog.ResetToCorePack();
        AnimalSpeciesCatalog.ResetToCore();
        NameGeneratorCatalog.ResetToCore();
        CivilizationCatalog.ResetToCore();
        foreach (var type in new[] { "content", "language" })
        {
            foreach (var pack in orderedPacks.Where(pack =>
                         pack.Manifest.Type == type &&
                         _modPreferences.IsEnabled(pack.Manifest.Id) &&
                         !duplicateIds.Contains(pack.Manifest.Id)))
            {
                try
                {
                    var candidatePacks = enabledPacks.Append(pack).ToArray();
                    var animalCatalog = AnimalSpeciesCatalog.Compose(candidatePacks);
                    var nameGeneratorCatalog = NameGeneratorCatalog.Compose(candidatePacks);
                    var civilizationCatalog = CivilizationCatalog.Compose(
                        candidatePacks,
                        nameGeneratorCatalog);
                    AnimalVisualAssetRegistry.Validate(animalCatalog);
                    TranslationCatalog.ConfigurePacks(candidatePacks);
                    AnimalSpeciesCatalog.Activate(animalCatalog);
                    NameGeneratorCatalog.Activate(nameGeneratorCatalog);
                    CivilizationCatalog.Activate(civilizationCatalog);
                    enabledPacks.Add(pack);
                }
                catch (InvalidDataException exception)
                {
                    errors[pack.SourceName] = exception.Message;
                    GD.PushWarning(
                        $"Could not apply content pack '{pack.SourceName}': " +
                        exception.Message);
                }
            }
        }
        ContentPackRuntime.Configure(enabledPacks);

        _modEntries.Clear();
        foreach (var pack in orderedPacks)
        {
            _modEntries.Add(new ModManagerEntry(
                pack,
                pack.SourceName,
                _modPreferences.IsEnabled(pack.Manifest.Id),
                errors.GetValueOrDefault(pack.SourceName),
                !duplicateIds.Contains(pack.Manifest.Id)));
        }
        foreach (var failure in discovery.Failures)
        {
            _modEntries.Add(new ModManagerEntry(
                null,
                failure.FilePath,
                enabled: false,
                failure.Error,
                canConfigure: false));
            GD.PushWarning(
                $"Could not load content pack '{failure.FilePath}': {failure.Error}");
        }
    }

    private void CreateModManagerWindow()
    {
        _modManagerWindow = new Window
        {
            Name = "ModManagerWindow",
            Title = Ui("mods", "title"),
            Size = new Vector2I(860, 650),
            MinSize = new Vector2I(660, 430),
            Visible = false,
            Transient = true,
            Exclusive = true,
        };
        _modManagerWindow.CloseRequested += _modManagerWindow.Hide;
        AddChild(_modManagerWindow);

        var margin = CreateModWindowMargin();
        _modManagerWindow.AddChild(margin);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 9);
        margin.AddChild(content);
        content.AddChild(new Label
        {
            Text = Ui("mods", "help"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        var restartNotice = new Label
        {
            Text = Ui("mods", "restart-note"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        restartNotice.AddThemeColorOverride("font_color", GameUiTheme.MutedText);
        content.AddChild(restartNotice);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        content.AddChild(scroll);
        _modRows = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _modRows.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_modRows);

        _modStatus = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _modStatus.AddThemeColorOverride("font_color", GameUiTheme.MutedText);
        content.AddChild(_modStatus);
        var close = new Button { Text = Ui("common", "close") };
        close.Pressed += _modManagerWindow.Hide;
        content.AddChild(close);

        CreateModInformationWindow();
        RebuildModRows();
        GetNode<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/Mods")
            .Pressed += ShowModManager;
    }

    private void CreateModInformationWindow()
    {
        _modInformationWindow = new Window
        {
            Name = "ModInformationWindow",
            Size = new Vector2I(720, 560),
            MinSize = new Vector2I(520, 360),
            Visible = false,
            Transient = true,
            Exclusive = true,
        };
        _modInformationWindow.CloseRequested += _modInformationWindow.Hide;
        AddChild(_modInformationWindow);
        var margin = CreateModWindowMargin();
        _modInformationWindow.AddChild(margin);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 8);
        margin.AddChild(content);
        _modInformationText = new RichTextLabel
        {
            BbcodeEnabled = false,
            FitContent = false,
            SelectionEnabled = true,
            ScrollActive = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        content.AddChild(_modInformationText);
        _modReportButton = new Button
        {
            Text = Ui("mods", "prepare-report"),
            TooltipText = Ui("mods", "prepare-report-tooltip"),
            Visible = false,
        };
        _modReportButton.Pressed += PrepareModReport;
        content.AddChild(_modReportButton);
        var close = new Button { Text = Ui("common", "close") };
        close.Pressed += _modInformationWindow.Hide;
        content.AddChild(close);
    }

    private static MarginContainer CreateModWindowMargin()
    {
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "top", "right", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", 14);
        }
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return margin;
    }

    private void ShowModManager()
    {
        RebuildModRows();
        _modManagerWindow.PopupCentered();
    }

    private void UpdateModMenuButtonAvailability()
    {
        var button = GetNode<Button>(
            "Interface/MainMenu/Center/Panel/Margin/Controls/Mods");
        button.Disabled = _modEntries.Count == 0 && _modPreferencesError is null;
        button.TooltipText = button.Disabled
            ? Ui("main-menu", "mods-unavailable-tooltip")
            : string.Empty;
    }

    private void RebuildModRows()
    {
        foreach (var child in _modRows.GetChildren())
        {
            child.QueueFree();
        }

        if (_modEntries.Count == 0)
        {
            _modRows.AddChild(new Label { Text = Ui("mods", "empty") });
        }
        foreach (var entry in _modEntries)
        {
            _modRows.AddChild(CreateModRow(entry));
        }
        _modStatus.Text = _modPreferencesError is null
            ? Ui("mods", "changes-restart")
            : UiFormat("mods", "preferences-error", _modPreferencesError);
    }

    private Control CreateModRow(ModManagerEntry entry)
    {
        var row = new ModDropRow
        {
            PackKey = entry.Key,
            CustomMinimumSize = new Vector2(0, 54),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        row.PackDropped += MoveModEntry;
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        row.AddChild(line);

        var handle = new ModDragHandle
        {
            PackKey = entry.Key,
            Text = "≡",
            TooltipText = Ui("mods", "drag-tooltip"),
            Disabled = !entry.CanConfigure,
            CustomMinimumSize = new Vector2(34, 0),
            MouseDefaultCursorShape = Control.CursorShape.Move,
        };
        line.AddChild(handle);
        var enabled = new CheckButton
        {
            ButtonPressed = entry.Enabled,
            Disabled = !entry.CanConfigure,
            TooltipText = Ui("mods", "enabled-tooltip"),
        };
        enabled.Toggled += value =>
        {
            entry.Enabled = value;
            PersistModPreferences();
        };
        line.AddChild(enabled);
        var names = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        names.AddChild(new Label { Text = entry.DisplayName });
        var id = new Label { Text = entry.Identifier };
        id.AddThemeColorOverride("font_color", GameUiTheme.MutedText);
        id.AddThemeFontSizeOverride("font_size", 11);
        names.AddChild(id);
        line.AddChild(names);
        line.AddChild(new Label
        {
            Text = entry.Pack is null
                ? Ui("mods", "unavailable")
                : Ui("mods", entry.Pack.Manifest.Type),
            CustomMinimumSize = new Vector2(100, 0),
        });
        line.AddChild(new Label
        {
            Text = entry.Version,
            CustomMinimumSize = new Vector2(90, 0),
        });
        if (entry.Error is not null)
        {
            var warning = new Button
            {
                Text = "⚠",
                TooltipText = Ui("mods", "warning-tooltip"),
                CustomMinimumSize = new Vector2(38, 0),
            };
            warning.AddThemeColorOverride("font_color", new Color("ffd24a"));
            warning.Pressed += () => ShowModError(entry);
            line.AddChild(warning);
        }
        var details = new Button
        {
            Text = "…",
            TooltipText = Ui("mods", "details-tooltip"),
            CustomMinimumSize = new Vector2(38, 0),
            Disabled = entry.Pack is null,
        };
        details.Pressed += () => ShowModDetails(entry);
        line.AddChild(details);
        return row;
    }

    private void MoveModEntry(string sourceKey, string targetKey, bool insertAfter)
    {
        var sourceIndex = _modEntries.FindIndex(entry =>
            entry.Key == sourceKey && entry.CanConfigure);
        var targetIndex = _modEntries.FindIndex(entry =>
            entry.Key == targetKey && entry.CanConfigure);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        var entry = _modEntries[sourceIndex];
        _modEntries.RemoveAt(sourceIndex);
        targetIndex = _modEntries.FindIndex(candidate => candidate.Key == targetKey);
        var insertionIndex = insertAfter ? targetIndex + 1 : targetIndex;
        _modEntries.Insert(insertionIndex, entry);
        PersistModPreferences();
        RebuildModRows();
    }

    private void PersistModPreferences()
    {
        try
        {
            _modPreferences.ReplaceVisible(_modEntries
                .Where(entry => entry.Pack is not null && entry.CanConfigure)
                .Select(entry => new ContentPackPreference(
                    entry.Pack!.Manifest.Id,
                    entry.Enabled)));
            _modPreferences.Save(_modPreferencesPath);
            _modPreferencesError = null;
            _modStatus.Text = Ui("mods", "changes-saved");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _modPreferencesError = exception.Message;
            _modStatus.Text = UiFormat("mods", "preferences-error", exception.Message);
        }
    }

    private void ShowModDetails(ModManagerEntry entry)
    {
        var pack = entry.Pack!;
        var authors = pack.Manifest.Authors
            .Concat(string.IsNullOrWhiteSpace(pack.Manifest.Author)
                ? []
                : [pack.Manifest.Author])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string? readme;
        try
        {
            readme = ReadModReadme(pack);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or DecoderFallbackException)
        {
            readme = UiFormat("mods", "readme-error", exception.Message);
        }
        var lines = new List<string>
        {
            UiFormat("mods", "id-line", pack.Manifest.Id),
            UiFormat("mods", "version-line", pack.Manifest.Version),
            UiFormat("mods", "type-line", Ui("mods", pack.Manifest.Type)),
            UiFormat("mods", "authors-line", authors.Length == 0
                ? Ui("mods", "author-unknown")
                : string.Join(", ", authors)),
            UiFormat("mods", "source-line", pack.SourceName),
        };
        if (!string.IsNullOrWhiteSpace(pack.Manifest.ContactEmail))
        {
            lines.Add(UiFormat("mods", "contact-line", pack.Manifest.ContactEmail));
        }
        lines.Add(string.Empty);
        lines.Add(Ui("mods", "readme"));
        lines.Add(string.Empty);
        lines.Add(readme ?? Ui("mods", "no-readme"));
        _modInformationWindow.Title = UiFormat("mods", "details-title", entry.DisplayName);
        _modInformationText.Text = string.Join(System.Environment.NewLine, lines);
        _modReportButton.Visible = false;
        _reportedMod = null;
        _modInformationWindow.PopupCentered();
    }

    private void ShowModError(ModManagerEntry entry)
    {
        _modInformationWindow.Title = UiFormat("mods", "error-title", entry.DisplayName);
        _modInformationText.Text = entry.Error ?? Ui("mods", "unknown-error");
        _reportedMod = entry;
        _modReportButton.Visible = !string.IsNullOrWhiteSpace(
            entry.Pack?.Manifest.ContactEmail);
        _modInformationWindow.PopupCentered();
    }

    private static string? ReadModReadme(ContentPack pack)
    {
        var path = pack.Manifest.ReadmePath ?? pack.FilePaths.FirstOrDefault(candidate =>
            candidate.Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
            candidate.Equals("README.txt", StringComparison.OrdinalIgnoreCase));
        if (path is null)
        {
            return null;
        }

        var contents = pack.ReadAllText(path);
        const int maximumDisplayedCharacters = 100_000;
        return contents.Length <= maximumDisplayedCharacters
            ? contents
            : contents[..maximumDisplayedCharacters] + "…";
    }

    private void PrepareModReport()
    {
        if (_reportedMod?.Pack is not { } pack ||
            string.IsNullOrWhiteSpace(pack.Manifest.ContactEmail))
        {
            return;
        }

        var subject = UiFormat("mods", "report-subject", pack.Manifest.Id);
        var body = UiFormat(
            "mods",
            "report-body",
            pack.Manifest.Id,
            pack.Manifest.Version,
            _reportedMod.Error ?? Ui("mods", "unknown-error"));
        var target = $"mailto:{pack.Manifest.ContactEmail}" +
            $"?subject={Uri.EscapeDataString(subject)}" +
            $"&body={Uri.EscapeDataString(body)}";
        if (OS.ShellOpen(target) != Error.Ok)
        {
            _modInformationText.Text += System.Environment.NewLine +
                System.Environment.NewLine + Ui("mods", "email-open-failed");
        }
    }

    private sealed class ModManagerEntry(
        ContentPack? pack,
        string key,
        bool enabled,
        string? error,
        bool canConfigure)
    {
        internal ContentPack? Pack { get; } = pack;
        internal string Key { get; } = key;
        internal bool Enabled { get; set; } = enabled;
        internal string? Error { get; } = error;
        internal bool CanConfigure { get; } = canConfigure;
        internal string DisplayName => string.IsNullOrWhiteSpace(Pack?.Manifest.Title)
            ? Path.GetFileNameWithoutExtension(Key)
            : Pack.Manifest.Title;
        internal string Identifier => Pack?.Manifest.Id ?? Path.GetFileName(Key);
        internal string Version => Pack?.Manifest.Version ?? "—";
    }
}
