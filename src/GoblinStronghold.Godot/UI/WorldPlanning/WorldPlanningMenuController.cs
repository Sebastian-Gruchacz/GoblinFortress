using Godot;
using GoblinStronghold.GodotClient;

namespace GoblinStronghold.GodotClient.UI.WorldPlanning;

internal readonly record struct WorldPlanningMenuTarget(
    PopupPanel Menu,
    GridContainer Grid);

internal delegate Button AddWorldPlanningMenuTile(
    GridContainer grid,
    PopupPanel menu,
    Texture2D texture,
    string tooltip,
    Action action,
    GameShortcutId? shortcut);

internal sealed class WorldPlanningMenuController
{
    private readonly Node _owner;
    private readonly WorldPlanningMenuTarget _root;
    private readonly AddWorldPlanningMenuTile _addTile;
    private readonly Action<PopupPanel> _showMenu;
    private readonly Dictionary<string, WorldPlanningMenuTarget> _menus =
        new(StringComparer.OrdinalIgnoreCase);

    public WorldPlanningMenuController(
        Node owner,
        PopupPanel rootMenu,
        GridContainer rootGrid,
        AddWorldPlanningMenuTile addTile,
        Action<PopupPanel> showMenu)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(rootMenu);
        ArgumentNullException.ThrowIfNull(rootGrid);
        ArgumentNullException.ThrowIfNull(addTile);
        ArgumentNullException.ThrowIfNull(showMenu);
        _owner = owner;
        _root = new WorldPlanningMenuTarget(rootMenu, rootGrid);
        _addTile = addTile;
        _showMenu = showMenu;
    }

    public IEnumerable<PopupPanel> Submenus => _menus.Values.Select(item => item.Menu);

    public void AddRootTool(
        Texture2D icon,
        string tooltip,
        Action action,
        GameShortcutId? shortcut = null) =>
        AddTool(_root, icon, tooltip, action, shortcut, disabled: false);

    public void AddDisabledRootTool(Texture2D icon, string tooltip) =>
        AddTool(_root, icon, tooltip, () => { }, null, disabled: true);

    public void AddRootSpacer() => _root.Grid.AddChild(new Control
    {
        CustomMinimumSize = new Vector2(68, 68),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    });

    public WorldPlanningMenuTarget AddSection(
        IReadOnlyList<string> path,
        string heading,
        Texture2D icon,
        int columns = 4)
    {
        ValidatePath(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(heading);
        ArgumentNullException.ThrowIfNull(icon);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);

        var key = GetKey(path);
        if (_menus.ContainsKey(key))
        {
            throw new InvalidOperationException(
                $"World-planning menu section '{key}' is already registered.");
        }

        var parent = path.Count == 1
            ? _root
            : GetMenu(path.Take(path.Count - 1).ToArray());
        var menu = new PopupPanel
        {
            Name = $"WorldPlanningMenu-{key.Replace('/', '-')}",
            MinSize = new Vector2I(84, 84),
        };
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 4);
        margin.AddThemeConstantOverride("margin_top", 4);
        margin.AddThemeConstantOverride("margin_right", 4);
        margin.AddThemeConstantOverride("margin_bottom", 4);
        var content = new VBoxContainer();
        var title = new Label
        {
            Text = heading,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 32),
        };
        var grid = new GridContainer { Columns = columns };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        content.AddChild(title);
        content.AddChild(grid);
        margin.AddChild(content);
        menu.AddChild(margin);
        _owner.AddChild(menu);

        var target = new WorldPlanningMenuTarget(menu, grid);
        _menus.Add(key, target);
        _addTile(parent.Grid, parent.Menu, icon, heading, () => _showMenu(menu), null);
        return target;
    }

    public WorldPlanningMenuTarget GetMenu(params string[] path) =>
        GetMenu((IReadOnlyList<string>)path);

    public WorldPlanningMenuTarget GetMenu(IReadOnlyList<string> path)
    {
        ValidatePath(path);
        var key = GetKey(path);
        return _menus.TryGetValue(key, out var target)
            ? target
            : throw new KeyNotFoundException(
                $"World-planning menu section '{key}' is not registered.");
    }

    public void AddTool(
        IReadOnlyList<string> menuPath,
        Texture2D icon,
        string tooltip,
        Action action,
        GameShortcutId? shortcut = null)
    {
        ArgumentNullException.ThrowIfNull(icon);
        ArgumentException.ThrowIfNullOrWhiteSpace(tooltip);
        ArgumentNullException.ThrowIfNull(action);
        AddTool(GetMenu(menuPath), icon, tooltip, action, shortcut, disabled: false);
    }

    public void AddDisabledTool(
        IReadOnlyList<string> menuPath,
        Texture2D icon,
        string tooltip) =>
        AddTool(GetMenu(menuPath), icon, tooltip, () => { }, null, disabled: true);

    private void AddTool(
        WorldPlanningMenuTarget target,
        Texture2D icon,
        string tooltip,
        Action action,
        GameShortcutId? shortcut,
        bool disabled)
    {
        ArgumentNullException.ThrowIfNull(icon);
        ArgumentException.ThrowIfNullOrWhiteSpace(tooltip);
        ArgumentNullException.ThrowIfNull(action);
        var button = _addTile(
            target.Grid,
            target.Menu,
            icon,
            tooltip,
            action,
            shortcut);
        button.Disabled = disabled;
    }

    private static string GetKey(IReadOnlyList<string> path) => string.Join('/', path);

    private static void ValidatePath(IReadOnlyList<string> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count == 0 || path.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("World-planning menu path is empty.", nameof(path));
        }
    }
}
