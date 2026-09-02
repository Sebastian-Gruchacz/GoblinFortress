using Godot;
using GoblinStronghold.Simulation.Resources;
using GoblinStronghold.Simulation.WorkPriorities;

namespace GoblinStronghold.GodotClient.UI.Windows;

internal sealed partial class WorkTypePriorityWindow : Window
{
    private readonly Func<string, string, string> _translate;
    private readonly Func<IReadOnlyList<WorkTypePrioritySnapshot>> _readPriorities;
    private readonly Dictionary<string, OptionButton> _selectors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Label> _labels = new(StringComparer.Ordinal);
    private readonly Label _description = new()
    {
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
    };

    internal WorkTypePriorityWindow(
        Func<string, string, string> translate,
        Func<IReadOnlyList<WorkTypePrioritySnapshot>> readPriorities)
    {
        _translate = translate;
        _readPriorities = readPriorities;
        Name = "WorkTypePriorityWindow";
        Size = new Vector2I(560, 660);
        MinSize = new Vector2I(440, 420);
        Visible = false;
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
        content.AddChild(_description);
        content.AddChild(new HSeparator());

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddChild(scroll);
        var rows = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        rows.AddThemeConstantOverride("h_separation", 14);
        rows.AddThemeConstantOverride("v_separation", 8);
        scroll.AddChild(rows);

        foreach (var definition in WorkTypePriorityCatalog.All)
        {
            var label = new Label
            {
                Name = $"Label-{definition.Id}",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            rows.AddChild(label);
            _labels.Add(definition.Id, label);
            var selector = new OptionButton
            {
                Name = $"Priority-{definition.Id}",
                CustomMinimumSize = new Vector2(150, 0),
            };
            foreach (var priority in Enum.GetValues<StoragePriority>())
            {
                selector.AddItem(string.Empty, (int)priority);
            }
            selector.ItemSelected += index => PriorityChanged?.Invoke(
                definition.Id,
                (StoragePriority)selector.GetItemId((int)index));
            rows.AddChild(selector);
            _selectors.Add(definition.Id, selector);
        }

        RefreshLocalization();
    }

    internal event Action<string, StoragePriority>? PriorityChanged;

    internal void ShowPriorities()
    {
        RefreshValues();
        PopupCentered();
    }

    internal void RefreshLocalization()
    {
        Title = T("title");
        _description.Text = T("description");
        foreach (var definition in WorkTypePriorityCatalog.All)
        {
            _labels[definition.Id].Text = T(definition.Id);
            var selector = _selectors[definition.Id];
            selector.SetItemText((int)StoragePriority.Low, T("priority-low"));
            selector.SetItemText((int)StoragePriority.Normal, T("priority-normal"));
            selector.SetItemText((int)StoragePriority.High, T("priority-high"));
            selector.SetItemText((int)StoragePriority.Urgent, T("priority-urgent"));
        }
    }

    internal void RefreshValues()
    {
        foreach (var priority in _readPriorities())
        {
            if (_selectors.TryGetValue(priority.Id, out var selector))
            {
                selector.Select((int)priority.Priority);
            }
        }
    }

    private string T(string key) => _translate("work-type-priorities", key);
}
