using Godot;
using GoblinStronghold.Simulation.Localization;
using System.Globalization;

namespace GoblinStronghold.GodotClient;

public partial class Main
{
    private LocaleSettings _localeSettings = null!;
    private static string _currentLocale = TranslationCatalog.FallbackLocale;

    private string Ui(string subsection, string key) =>
        TranslationCatalog.Get(_currentLocale, "interface", subsection, key);

    private string UiFormat(string subsection, string key, params object?[] arguments) =>
        string.Format(
            GetFormattingCulture(),
            Ui(subsection, key),
            arguments);

    private static CultureInfo GetFormattingCulture()
    {
        try
        {
            return CultureInfo.GetCultureInfo(_currentLocale);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo("en-US");
        }
    }

    private void ApplyStaticTranslations()
    {
        SetText<Button>("Interface/RightHud/SessionPanel/Controls/Menu", "hud", "menu");
        SetTooltip("Interface/RightHud/SessionPanel/Controls/Menu", "hud", "menu-tooltip");
        SetText<Button>("Interface/RightHud/SessionPanel/Controls/SaveGame", "hud", "save");
        SetTooltip("Interface/RightHud/SessionPanel/Controls/SaveGame", "hud", "save-tooltip");
        SetTooltip("Interface/RightHud/SessionPanel/Controls/ViewMode", "hud", "view-tooltip");
        SetTooltip("Interface/RightHud/CameraPanel/Controls/RotateLeft", "hud", "rotate-left-tooltip");
        SetTooltip("Interface/RightHud/CameraPanel/Controls/Angle", "hud", "angle-tooltip");
        SetTooltip("Interface/RightHud/CameraPanel/Controls/RotateRight", "hud", "rotate-right-tooltip");
        SetText<Label>("Interface/Inspector/Text", "hud", "help");

        SetTooltip("Interface/MainMenu/Center/Panel/Margin/Controls/Title/Goblin", "main-menu", "goblin-tooltip");
        SetText<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/Resume", "main-menu", "resume");
        SetText<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/NewGame", "main-menu", "new-game");
        SetText<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/LoadGame", "main-menu", "load-last");
        SetText<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/ChooseSave", "main-menu", "choose-save");
        SetText<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/Options", "main-menu", "options");
        SetText<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/Mods", "main-menu", "mods");
        UpdateModMenuButtonAvailability();
        SetText<Button>("Interface/MainMenu/Center/Panel/Margin/Controls/Quit", "main-menu", "quit");
        _newGameSetupWindow.RefreshLocalization();

        SetWindowTitle("GoblinDetails", "windows", "goblin");
        SetWindowTitle("StoredResourcesWindow", "windows", "stored-resources");
        SetText<Label>("StoredResourcesWindow/Margin/Content/Summary", "windows", "stored-summary");
        SetText<CheckButton>("StoredResourcesWindow/Margin/Content/Detailed", "windows", "show-details");
        SetTooltip("StoredResourcesWindow/Margin/Content/Detailed", "windows", "details-tooltip");
        SetWindowTitle("LooseResourcesWindow", "windows", "loose-resources");
        SetText<Label>("LooseResourcesWindow/Margin/Content/Summary", "windows", "loose-summary");
        SetText<CheckButton>("LooseResourcesWindow/Margin/Content/Detailed", "windows", "show-details");
        SetTooltip("LooseResourcesWindow/Margin/Content/Detailed", "windows", "details-tooltip");
        SetWindowTitle("GoblinRosterWindow", "windows", "tribe");
        SetWindowTitle("StatisticsWindow", "windows", "statistics");
        SetWindowTitle("StorageDetails", "windows", "storage-settings");
        SetWindowTitle("ConstructionDetails", "windows", "construction-site");
        SetText<Button>("StorageDetails/Margin/Controls/Apply", "common", "apply");
        SetText<Button>("ConstructionDetails/Margin/Controls/Apply", "common", "apply");
    }

    private void SetText<TControl>(string path, string subsection, string key)
        where TControl : Control
    {
        var value = Ui(subsection, key);
        switch (GetNode<TControl>(path))
        {
            case Label label:
                label.Text = value;
                break;
            case Button button:
                button.Text = value;
                break;
        }
    }

    private void SetTooltip(string path, string subsection, string key) =>
        GetNode<Control>(path).TooltipText = Ui(subsection, key);

    private void SetWindowTitle(string path, string subsection, string key) =>
        GetNode<Window>(path).Title = Ui(subsection, key);
}
