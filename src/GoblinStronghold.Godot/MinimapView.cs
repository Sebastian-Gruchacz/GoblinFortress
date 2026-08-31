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
    private int _visibleLevel;

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

    public void SetVisibleLevel(int level)
    {
        if (_visibleLevel == level)
        {
            return;
        }

        _visibleLevel = level;
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
        var constructedSurfaces = _snapshot.WorldObjects
            .SelectMany(worldObject => worldObject.GetAbsoluteParts())
            .Where(item => item.Part.Kind is WorldObjectPartKind.Walkway or
                WorldObjectPartKind.Floor or WorldObjectPartKind.ConstructedRamp)
            .Select(item => item.Position)
            .Where(position => position.Z == _visibleLevel)
            .ToHashSet();
        for (var y = 0; y < _engine.Map.Height; y++)
        {
            for (var x = 0; x < _engine.Map.Width; x++)
            {
                var position = new GridPosition(x, y, _visibleLevel);
                var visibility = _snapshot.GetVisibility(position, _engine.Map.Width);
                if (visibility == CellVisibility.Unknown)
                {
                    continue;
                }

                var color = ResolveLevelColor(position, constructedSurfaces);
                if (color is null)
                {
                    continue;
                }
                if (visibility == CellVisibility.Explored)
                {
                    color = color.Value.Darkened(0.48f);
                }

                DrawRect(
                    new Rect2(new Vector2(x, y) * tileSize, tileSize + Vector2.One),
                    color.Value);
            }
        }

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

    private Color? ResolveLevelColor(
        GridPosition position,
        IReadOnlySet<GridPosition> constructedSurfaces)
    {
        if (_engine.Map.IsTerrainSurfacePosition(position))
        {
            var cell = _engine.Map.GetColumnCell(position);
            return cell.Terrain switch
            {
                TerrainKind.SolidGround => new Color("668b4d"),
                TerrainKind.Mud => new Color("4f5838"),
                TerrainKind.ShallowWater => new Color("4b8890"),
                TerrainKind.DeepWater => new Color("28536d"),
                _ => Colors.Magenta,
            };
        }

        if (_engine.Map.IsCavePosition(position))
        {
            var cell = _engine.Map.GetCaveCell(position);
            if (cell.Fluid == CellFluidKind.Lava)
            {
                return new Color("b94a22");
            }
            if (cell.Fluid == CellFluidKind.Water)
            {
                return new Color("28536d");
            }

            var open = cell.IsOpen || _engine.World.ExcavatedCaveCells.Contains(position);
            return RockColor(cell.Rock, open);
        }

        if (_engine.Map.IsHillMassPosition(position))
        {
            var cell = _engine.Map.GetHillMassCell(position);
            return RockColor(cell.Rock, !_engine.World.IsSolidHillRock(position));
        }

        return constructedSurfaces.Contains(position)
            ? new Color("8a7655")
            : null;
    }

    private static Color RockColor(RockKind rock, bool floor) => (rock, floor) switch
    {
        (RockKind.Sandstone, true) => new Color("77634a"),
        (RockKind.Sandstone, false) => new Color("463725"),
        (RockKind.Granite, true) => new Color("656b74"),
        (RockKind.Granite, false) => new Color("343942"),
        (RockKind.Basalt, true) => new Color("44464d"),
        (RockKind.Basalt, false) => new Color("202228"),
        (RockKind.Obsidian, true) => new Color("514064"),
        (RockKind.Obsidian, false) => new Color("271d31"),
        _ => Colors.Magenta,
    };

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
