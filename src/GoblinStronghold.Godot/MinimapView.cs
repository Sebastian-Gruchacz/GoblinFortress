using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.GodotClient.UI.WorldRendering;

namespace GoblinStronghold.GodotClient;

public partial class MinimapView : Control
{
    private SimulationEngine _engine = null!;
    private SimulationSnapshot _snapshot = null!;
    private Vector2 _cameraCenterNormalized = new(0.5f, 0.5f);
    private Vector2 _cameraSizeNormalized = Vector2.One;
    private int _visibleLevel;
    private readonly MinimapStaticTextureCache _staticLayers = new();

    public event Action<GridPosition>? NavigationRequested;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public void SetWorld(SimulationEngine engine, int visibleLevel = 0)
    {
        _staticLayers.Reset();
        _engine = engine;
        _visibleLevel = visibleLevel;
        Refresh(engine.CreatePresentationSnapshot());
    }

    public void Refresh(SimulationSnapshot snapshot)
    {
        _snapshot = snapshot;
        _staticLayers.SynchronizeLevel(_engine, snapshot, _visibleLevel);
        QueueRedraw();
    }

    public void SetVisibleLevel(int level)
    {
        if (_visibleLevel == level)
        {
            return;
        }

        _visibleLevel = level;
        if (_engine is not null && _snapshot is not null)
        {
            _staticLayers.SynchronizeLevel(_engine, _snapshot, _visibleLevel);
        }
        QueueRedraw();
    }

    public override void _ExitTree() => _staticLayers.Dispose();

    public void SetCameraView(Vector2 centerNormalized, Vector2 sizeNormalized)
    {
        if (_cameraCenterNormalized.IsEqualApprox(centerNormalized) &&
            _cameraSizeNormalized.IsEqualApprox(sizeNormalized))
        {
            return;
        }

        _cameraCenterNormalized = centerNormalized;
        _cameraSizeNormalized = sizeNormalized;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case InputEventMouseButton
                {
                    Pressed: true,
                    ButtonIndex: MouseButton.Left,
                } mouse:
                RequestNavigation(mouse.Position);
                AcceptEvent();
                break;
            case InputEventMouseMotion mouse when Input.IsMouseButtonPressed(MouseButton.Left):
                RequestNavigation(mouse.Position);
                AcceptEvent();
                break;
        }
    }

    public override void _Draw()
    {
        if (_engine is null)
        {
            return;
        }

        DrawRect(new Rect2(Vector2.Zero, Size), new Color("101719"));
        if (_staticLayers.GetTexture(_visibleLevel) is { } staticLayer)
        {
            DrawTextureRect(
                staticLayer,
                new Rect2(Vector2.Zero, Size),
                tile: false);
        }
        var tileSize = new Vector2(
            Size.X / _engine.Map.Width,
            Size.Y / _engine.Map.Height);

        foreach (var actor in _snapshot.Actors.Where(actor =>
                     actor.Position.Z == _visibleLevel))
        {
            DrawCircle(ToMinimap(actor.Position, tileSize), 2f, new Color("8dff65"));
        }

        foreach (var villager in _snapshot.HumanVillage.Villagers.Where(villager =>
                     villager.Health > 0 &&
                     villager.Position.Z == _visibleLevel &&
                     _snapshot.GetVisibility(villager.Position, _engine.Map.Width) !=
                         CellVisibility.Unknown))
        {
            DrawCircle(ToMinimap(villager.Position, tileSize), 1.4f, new Color("f0c96d"));
        }

        var cameraRect = new Rect2(
            (_cameraCenterNormalized - (_cameraSizeNormalized / 2f)) * Size,
            _cameraSizeNormalized * Size);
        DrawRect(cameraRect, new Color(1f, 1f, 1f, 0.82f), filled: false, width: 1.5f);
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("91a29b"), filled: false, width: 1f);
    }

    private void RequestNavigation(Vector2 localPosition)
    {
        if (_engine is null || Size.X <= 0f || Size.Y <= 0f)
        {
            return;
        }

        var normalized = new Vector2(
            Mathf.Clamp(localPosition.X / Size.X, 0f, 0.999999f),
            Mathf.Clamp(localPosition.Y / Size.Y, 0f, 0.999999f));
        NavigationRequested?.Invoke(new GridPosition(
            Mathf.FloorToInt(normalized.X * _engine.Map.Width),
            Mathf.FloorToInt(normalized.Y * _engine.Map.Height),
            _visibleLevel));
    }

    private static Vector2 ToMinimap(GridPosition position, Vector2 tileSize) =>
        new((position.X + 0.5f) * tileSize.X, (position.Y + 0.5f) * tileSize.Y);
}
