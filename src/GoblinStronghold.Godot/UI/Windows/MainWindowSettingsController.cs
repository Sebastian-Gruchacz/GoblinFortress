using Godot;
using GoblinStronghold.GodotClient.Application.Profiles;

namespace GoblinStronghold.GodotClient.UI.Windows;

internal sealed partial class MainWindowSettingsController : Node
{
    private const double SaveDelaySeconds = 0.5d;
    private readonly MainWindowSettingsStore _store;
    private readonly bool _enabled;
    private Godot.Timer? _saveTimer;
    private StoredMainWindowSettings? _settings;
    private bool _restoring;

    internal MainWindowSettingsController(
        MainWindowSettingsStore store,
        bool enabled)
    {
        _store = store;
        _enabled = enabled;
    }

    public override void _Ready()
    {
        if (!_enabled)
        {
            SetProcess(false);
            return;
        }

        _saveTimer = new Godot.Timer
        {
            OneShot = true,
            WaitTime = SaveDelaySeconds,
        };
        AddChild(_saveTimer);
        _saveTimer.Timeout += Save;
        _settings = _store.Load();
        if (_settings is { } settings)
        {
            _restoring = true;
            Apply(settings);
            GetTree().CreateTimer(0.25d).Timeout += FinishRestore;
        }
        else
        {
            Capture();
        }
    }

    public override void _Process(double delta) => Capture();

    public override void _ExitTree()
    {
        if (_enabled)
        {
            Capture();
            Save();
        }
    }

    private void Apply(StoredMainWindowSettings settings)
    {
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetSize(new Vector2I(
            settings.WindowedWidth,
            settings.WindowedHeight));
        DisplayServer.WindowSetMode(settings.Mode switch
        {
            StoredMainWindowMode.Maximized => DisplayServer.WindowMode.Maximized,
            StoredMainWindowMode.Fullscreen => DisplayServer.WindowMode.Fullscreen,
            StoredMainWindowMode.ExclusiveFullscreen =>
                DisplayServer.WindowMode.ExclusiveFullscreen,
            _ => DisplayServer.WindowMode.Windowed,
        });
    }

    private void Capture()
    {
        if (!_enabled || _restoring)
        {
            return;
        }

        var mode = DisplayServer.WindowGetMode();
        if (mode == DisplayServer.WindowMode.Minimized)
        {
            return;
        }

        var currentSize = DisplayServer.WindowGetSize();
        var previous = _settings;
        var windowedWidth = mode == DisplayServer.WindowMode.Windowed
            ? currentSize.X
            : previous?.WindowedWidth ?? currentSize.X;
        var windowedHeight = mode == DisplayServer.WindowMode.Windowed
            ? currentSize.Y
            : previous?.WindowedHeight ?? currentSize.Y;
        var settings = new StoredMainWindowSettings(
            mode switch
            {
                DisplayServer.WindowMode.Maximized => StoredMainWindowMode.Maximized,
                DisplayServer.WindowMode.Fullscreen => StoredMainWindowMode.Fullscreen,
                DisplayServer.WindowMode.ExclusiveFullscreen =>
                    StoredMainWindowMode.ExclusiveFullscreen,
                _ => StoredMainWindowMode.Windowed,
            },
            windowedWidth,
            windowedHeight);
        if (settings == previous)
        {
            return;
        }

        _settings = settings;
        _saveTimer!.Start();
    }

    private void FinishRestore()
    {
        _restoring = false;
        Capture();
    }

    private void Save()
    {
        if (_settings is not { } settings)
        {
            return;
        }

        try
        {
            _store.Save(settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Could not save main window settings: {exception.Message}");
        }
    }
}
