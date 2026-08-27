using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.GodotClient;

public partial class MinimapView : Control
{
    private SimulationEngine _engine = null!;
    private SimulationSnapshot _snapshot = null!;
    private Vector2 _cameraCenterNormalized = new(0.5f, 0.5f);
    private Vector2 _cameraSizeNormalized = Vector2.One;

    public event Action<GridPosition>? NavigationRequested;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
    }

    public void SetWorld(SimulationEngine engine)
    {
        _engine = engine;
        Refresh(engine.CreatePresentationSnapshot());
    }

    public void Refresh(SimulationSnapshot snapshot)
    {
        _snapshot = snapshot;
        QueueRedraw();
    }

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
        var tileSize = new Vector2(
            Size.X / _engine.Map.Width,
            Size.Y / _engine.Map.Height);
        for (var y = 0; y < _engine.Map.Height; y++)
        {
            for (var x = 0; x < _engine.Map.Width; x++)
            {
                var position = new GridPosition(x, y);
                var visibility = _snapshot.GetVisibility(position, _engine.Map.Width);
                if (visibility == CellVisibility.Unknown)
                {
                    continue;
                }

                var cell = _engine.Map.GetCell(position);
                var color = cell.Terrain switch
                {
                    TerrainKind.SolidGround => new Color("668b4d"),
                    TerrainKind.Mud => new Color("4f5838"),
                    TerrainKind.ShallowWater => new Color("4b8890"),
                    TerrainKind.DeepWater => new Color("28536d"),
                    _ => Colors.Magenta,
                };
                color = cell.SurfaceLevel switch
                {
                    > 0 => color.Lightened(Math.Min(0.28f, cell.SurfaceLevel * 0.13f)),
                    < 0 => color.Darkened(0.22f),
                    _ => color,
                };
                if (visibility == CellVisibility.Explored)
                {
                    color = color.Darkened(0.48f);
                }

                DrawRect(
                    new Rect2(new Vector2(x, y) * tileSize, tileSize + Vector2.One),
                    color);
            }
        }

        foreach (var actor in _snapshot.Actors)
        {
            DrawCircle(ToMinimap(actor.Position, tileSize), 2f, new Color("8dff65"));
        }

        foreach (var cohort in _snapshot.HumanVillage.Cohorts.Where(cohort =>
                     cohort.Population > 0 &&
                     _snapshot.GetVisibility(cohort.Position, _engine.Map.Width) != CellVisibility.Unknown))
        {
            DrawCircle(ToMinimap(cohort.Position, tileSize), 2f, new Color("f0c96d"));
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
            Mathf.FloorToInt(normalized.Y * _engine.Map.Height)));
    }

    private static Vector2 ToMinimap(GridPosition position, Vector2 tileSize) =>
        new((position.X + 0.5f) * tileSize.X, (position.Y + 0.5f) * tileSize.Y);
}
