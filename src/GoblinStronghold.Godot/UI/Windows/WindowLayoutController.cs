using Godot;
using GoblinStronghold.GodotClient.Application.Profiles;

namespace GoblinStronghold.GodotClient.UI.Windows;

internal sealed partial class WindowLayoutController : Node
{
    private const double SaveDelaySeconds = 0.5d;
    private readonly Node _windowHost;
    private readonly PlayerProfileLayoutStore _store;
    private readonly Dictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StoredWindowLayout> _layouts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _restoring = new(StringComparer.Ordinal);
    private readonly Godot.Timer _saveTimer = new()
    {
        OneShot = true,
        WaitTime = SaveDelaySeconds,
    };
    private string? _activeProfileName;

    internal WindowLayoutController(Node windowHost, PlayerProfileLayoutStore store)
    {
        _windowHost = windowHost;
        _store = store;
    }

    public override void _Ready()
    {
        AddChild(_saveTimer);
        _saveTimer.Timeout += Save;
        foreach (var child in _windowHost.GetChildren())
        {
            RegisterCandidate(child);
        }
        _windowHost.ChildEnteredTree += RegisterCandidate;
    }

    public override void _ExitTree()
    {
        _windowHost.ChildEnteredTree -= RegisterCandidate;
        Save();
    }

    public override void _Process(double delta)
    {
        foreach (var (windowId, window) in _windows)
        {
            Capture(windowId, window);
        }
    }

    internal void ActivateProfile(string profileName)
    {
        Save();
        _activeProfileName = profileName;
        _layouts.Clear();
        foreach (var (windowId, layout) in _store.Load(profileName))
        {
            _layouts[windowId] = layout;
        }

        foreach (var (windowId, window) in _windows)
        {
            if (window.Visible)
            {
                Restore(windowId, window);
            }
        }
    }

    internal void DeactivateProfile()
    {
        Save();
        _activeProfileName = null;
        _layouts.Clear();
        _saveTimer.Stop();
    }

    internal void ConstrainVisibleWindows()
    {
        foreach (var (windowId, window) in _windows.Where(item => item.Value.Visible))
        {
            _restoring.Add(windowId);
            ApplyLayout(window, new StoredWindowLayout(
                window.Position.X,
                window.Position.Y,
                window.Size.X,
                window.Size.Y));
            _restoring.Remove(windowId);
            Capture(windowId, window);
        }
    }

    private void RegisterCandidate(Node child)
    {
        if (child is not Window window ||
            window is PopupMenu or PopupPanel or AcceptDialog or ConfirmationDialog)
        {
            return;
        }

        var windowId = window.Name.ToString();
        if (string.IsNullOrWhiteSpace(windowId) || windowId.StartsWith('@'))
        {
            GD.PushWarning("A persistent window requires a stable node name.");
            return;
        }
        if (!_windows.TryAdd(windowId, window))
        {
            GD.PushWarning($"Persistent window name '{windowId}' is not unique.");
            return;
        }

        window.AboutToPopup += () => BeginRestore(windowId, window);
        window.VisibilityChanged += () =>
        {
            if (window.Visible)
            {
                BeginRestore(windowId, window);
            }
        };
        window.TreeExiting += () =>
        {
            Capture(windowId, window);
            _windows.Remove(windowId);
        };
    }

    private void BeginRestore(string windowId, Window window)
    {
        if (!_layouts.ContainsKey(windowId))
        {
            return;
        }

        _restoring.Add(windowId);
        CallDeferred(MethodName.RestoreDeferred, windowId);
    }

    private void RestoreDeferred(string windowId)
    {
        if (_windows.TryGetValue(windowId, out var window))
        {
            Restore(windowId, window);
        }
        _restoring.Remove(windowId);
    }

    private void Restore(string windowId, Window window)
    {
        if (!_layouts.TryGetValue(windowId, out var stored))
        {
            return;
        }

        ApplyLayout(window, stored);
    }

    private void ApplyLayout(Window window, StoredWindowLayout stored)
    {
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var maximumWidth = Math.Max(1, (int)viewportSize.X);
        var maximumHeight = Math.Max(1, (int)viewportSize.Y);
        var minimumWidth = Math.Min(Math.Max(1, window.MinSize.X), maximumWidth);
        var minimumHeight = Math.Min(Math.Max(1, window.MinSize.Y), maximumHeight);
        var width = Math.Clamp(stored.Width, minimumWidth, maximumWidth);
        var height = Math.Clamp(stored.Height, minimumHeight, maximumHeight);
        var x = Math.Clamp(stored.X, 0, maximumWidth - width);
        var y = Math.Clamp(stored.Y, 0, maximumHeight - height);
        window.Size = new Vector2I(width, height);
        window.Position = new Vector2I(x, y);
    }

    private void Capture(string windowId, Window window)
    {
        if (_activeProfileName is null ||
            !window.Visible ||
            _restoring.Contains(windowId) ||
            window.Size.X <= 0 ||
            window.Size.Y <= 0)
        {
            return;
        }

        var layout = new StoredWindowLayout(
            window.Position.X,
            window.Position.Y,
            window.Size.X,
            window.Size.Y);
        if (_layouts.TryGetValue(windowId, out var current) && current == layout)
        {
            return;
        }

        _layouts[windowId] = layout;
        _saveTimer.Start();
    }

    private void Save()
    {
        if (_activeProfileName is null)
        {
            return;
        }

        try
        {
            _store.Save(_activeProfileName, _layouts);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Could not save window layout: {exception.Message}");
        }
    }
}
