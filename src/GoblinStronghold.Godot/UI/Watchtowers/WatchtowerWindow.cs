using System.Globalization;
using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Watchtowers;

namespace GoblinStronghold.GodotClient.UI.Watchtowers;

internal sealed partial class WatchtowerWindow : Window
{
    private readonly Func<string, string, string> _translate;
    private readonly Func<ActorJobSnapshot, string> _describeJob;
    private readonly Texture2D _watchtowerIcon;
    private readonly Label _summary;
    private readonly VBoxContainer _rows;
    private readonly HashSet<EntityId> _draftGuardIds = [];
    private WorldObjectId _watchtowerId;
    private bool _selectionDirty;
    private bool _updatingSelection;
    private EntityId _foodStorageId;
    private string _rowsSignature = string.Empty;

    internal WatchtowerWindow(
        Func<string, string, string> translate,
        Func<ActorJobSnapshot, string> describeJob,
        Texture2D watchtowerIcon)
    {
        _translate = translate;
        _describeJob = describeJob;
        _watchtowerIcon = watchtowerIcon;
        Name = "WatchtowerWindow";
        Size = new Vector2I(540, 560);
        MinSize = new Vector2I(380, 360);
        Visible = false;
        CloseRequested += Hide;

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "top", "right", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", 14);
        }
        AddChild(margin);

        var content = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddThemeConstantOverride("separation", 10);
        margin.AddChild(content);
        _summary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        content.AddChild(_summary);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddChild(scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 5);
        scroll.AddChild(_rows);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        content.AddChild(buttons);
        var storageButton = new Button { Text = T("open-food-storage") };
        storageButton.Pressed += () => FoodStorageRequested?.Invoke(_foodStorageId);
        buttons.AddChild(storageButton);
        var closeButton = new Button { Text = T("close") };
        closeButton.Pressed += Hide;
        buttons.AddChild(closeButton);
        Title = T("title");
    }

    internal event Action<WorldObjectId, EntityId, bool>? GuardSelectionChanged;

    internal event Action<EntityId>? FoodStorageRequested;

    internal event Action<string>? FeedbackRequested;

    internal void ShowWatchtower(SimulationSnapshot snapshot, WorldObjectId watchtowerId)
    {
        _watchtowerId = watchtowerId;
        _selectionDirty = false;
        _rowsSignature = string.Empty;
        Refresh(snapshot);
        PopupCentered();
    }

    internal void Refresh(SimulationSnapshot snapshot)
    {
        var post = snapshot.WatchtowerPosts.FirstOrDefault(item =>
            item.WatchtowerId == _watchtowerId);
        var watchtowerExists = snapshot.WorldObjects.Any(item =>
            item.Id == _watchtowerId && item.Kind == WorldObjectKind.WoodenWatchtower);
        if (!watchtowerExists || post.WatchtowerId == WorldObjectId.None)
        {
            Hide();
            return;
        }

        _foodStorageId = post.FoodStorageId;
        var storage = snapshot.StorageZones.First(item => item.Id == _foodStorageId);
        var guardAssignments = snapshot.WatchtowerPosts
            .SelectMany(item => item.GuardIds.Select(guardId =>
                (GuardId: guardId, item.WatchtowerId)))
            .ToDictionary(item => item.GuardId, item => item.WatchtowerId);
        _draftGuardIds.RemoveWhere(actorId =>
            guardAssignments.TryGetValue(actorId, out var assignedTower) &&
            assignedTower != _watchtowerId);
        var savedGuardIds = post.GuardIds.ToHashSet();
        if (!_selectionDirty || _draftGuardIds.SetEquals(savedGuardIds))
        {
            _selectionDirty = false;
            _draftGuardIds.Clear();
            _draftGuardIds.UnionWith(savedGuardIds);
        }
        _summary.Text = Format(
            "summary",
            _draftGuardIds.Count,
            storage.StoredQuantity,
            storage.Capacity,
            post.PlatformPosition);

        var rowsSignature = string.Join('|', snapshot.Actors
            .OrderBy(actor => actor.Id)
            .Select(actor => $"{actor.Id.Value}:{actor.Name}:{actor.Health}:" +
                $"{actor.IsJuvenile}:{_draftGuardIds.Contains(actor.Id)}:" +
                $"{guardAssignments.GetValueOrDefault(actor.Id).Value}"));
        if (rowsSignature == _rowsSignature)
        {
            return;
        }
        _rowsSignature = rowsSignature;

        foreach (var child in _rows.GetChildren())
        {
            child.QueueFree();
        }
        _updatingSelection = true;
        foreach (var actor in snapshot.Actors.OrderBy(actor => actor.Id))
        {
            var row = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            row.AddThemeConstantOverride("separation", 6);
            var assignedElsewhere = guardAssignments.TryGetValue(actor.Id, out var assignedTower) &&
                assignedTower != _watchtowerId;
            if (assignedElsewhere)
            {
                row.AddChild(new TextureRect
                {
                    CustomMinimumSize = new Vector2(28, 28),
                    Texture = _watchtowerIcon,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    TooltipText = T("assigned-other-tower"),
                });
            }
            else
            {
                var check = new CheckBox
                {
                    CustomMinimumSize = new Vector2(28, 28),
                    ButtonPressed = _draftGuardIds.Contains(actor.Id),
                    Disabled = actor.Health <= 0 || actor.IsJuvenile,
                    TooltipText = _describeJob(actor.Job),
                };
                var actorId = actor.Id;
                check.Toggled += enabled => ToggleGuard(actorId, check, enabled);
                row.AddChild(check);
            }

            row.AddChild(new Label
            {
                Text = Format("guard-row", actor.Name, actor.Health),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center,
                TooltipText = assignedElsewhere
                    ? T("assigned-other-tower")
                    : _describeJob(actor.Job),
            });
            _rows.AddChild(row);
        }
        _updatingSelection = false;
    }

    private void ToggleGuard(EntityId actorId, BaseButton check, bool enabled)
    {
        if (_updatingSelection)
        {
            return;
        }
        if (enabled && _draftGuardIds.Count >= WatchtowerDutyPolicy.Capacity)
        {
            _updatingSelection = true;
            check.ButtonPressed = false;
            _updatingSelection = false;
            FeedbackRequested?.Invoke(T("capacity-reached"));
            return;
        }

        if (enabled)
        {
            _draftGuardIds.Add(actorId);
        }
        else
        {
            _draftGuardIds.Remove(actorId);
        }
        _selectionDirty = true;
        FeedbackRequested?.Invoke(T(enabled ? "assigned" : "unassigned"));
        GuardSelectionChanged?.Invoke(_watchtowerId, actorId, enabled);
    }

    private string T(string key) => _translate("watchtower", key);

    private string Format(string key, params object?[] arguments) => string.Format(
        CultureInfo.CurrentCulture,
        T(key),
        arguments);
}
