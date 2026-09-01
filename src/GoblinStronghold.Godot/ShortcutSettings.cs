using Godot;
using System.Text.Json;

namespace GoblinStronghold.GodotClient;

internal enum GameShortcutId
{
    OpenManagement,
    OpenConstruction,
    OpenTerrain,
    OpenWork,
    OpenStatistics,
    OpenUnitOrders,
    ShowPlanner,
    BuildFoodStorage,
    BuildWoodStorage,
    BuildStoneStorage,
    BuildEquipmentStorage,
    BuildMaterialsStorage,
    BuildWalkway,
    BuildFieldCamp,
    BuildGoblinHut,
    BuildWoodenWall,
    BuildStoneWall,
    BuildWoodenDoor,
    BuildPrimitiveWorkshop,
    GatherFood,
    GatherReeds,
    GatherBrushwood,
    GatherStone,
    UprootBushes,
    FellTrees,
    QuarryBoulders,
    MineRock,
    CarveRampDown,
    CarveRampUp,
    HuntAnimals,
    Scout,
    CleanBlood,
    ClearOrders,
    CameraLevelUp,
    CameraLevelDown,
    CameraPanUp,
    CameraPanDown,
    CameraPanLeft,
    CameraPanRight,
    MoveSelectedUnits,
    AttackArea,
    HuntArea,
    Patrol,
}

internal readonly record struct ShortcutStroke(Key Key, bool Ctrl = false, bool Alt = false, bool Shift = false)
{
    internal bool Matches(InputEventKey input) =>
        input.Keycode == Key &&
        input.CtrlPressed == Ctrl &&
        input.AltPressed == Alt &&
        input.ShiftPressed == Shift;

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        parts.Add(OS.GetKeycodeString(Key));
        return string.Join("+", parts);
    }
}

internal sealed record ShortcutDefinition(
    GameShortcutId Id,
    string Section,
    string Label,
    GameShortcutId? Parent,
    ShortcutStroke DefaultStroke);

internal sealed class ShortcutSettings
{
    private readonly string _path;
    private readonly Dictionary<GameShortcutId, ShortcutStroke> _bindings;

    internal ShortcutSettings(string path)
    {
        _path = path;
        _bindings = Definitions.ToDictionary(item => item.Id, item => item.DefaultStroke);
        Load();
    }

    internal static IReadOnlyList<ShortcutDefinition> Definitions { get; } =
    [
        new(GameShortcutId.OpenManagement, "Menu główne", "Zarządzanie", null, new(Key.J)),
        new(GameShortcutId.OpenConstruction, "Menu główne", "Konstrukcje", null, new(Key.B)),
        new(GameShortcutId.OpenTerrain, "Menu główne", "terrain", null, new(Key.T)),
        new(GameShortcutId.OpenWork, "Menu główne", "Prace i obszary", null, new(Key.R)),
        new(GameShortcutId.OpenStatistics, "Menu główne", "Statystyki", null, new(Key.I)),
        new(GameShortcutId.OpenUnitOrders, "Menu główne", "Rozkazy jednostek", null, new(Key.O)),

        new(GameShortcutId.CameraLevelUp, "Kamera", "Poziom wyżej", null, new(Key.Pageup)),
        new(GameShortcutId.CameraLevelDown, "Kamera", "Poziom niżej", null, new(Key.Pagedown)),
        new(GameShortcutId.CameraPanUp, "Kamera", "Przesuń mapę w górę", null, new(Key.W)),
        new(GameShortcutId.CameraPanDown, "Kamera", "Przesuń mapę w dół", null, new(Key.S)),
        new(GameShortcutId.CameraPanLeft, "Kamera", "Przesuń mapę w lewo", null, new(Key.A)),
        new(GameShortcutId.CameraPanRight, "Kamera", "Przesuń mapę w prawo", null, new(Key.D)),

        new(GameShortcutId.ShowPlanner, "Zarządzanie", "Planer plemienia", GameShortcutId.OpenManagement, new(Key.P)),

        new(GameShortcutId.MoveSelectedUnits, "Rozkazy jednostek", "Przenieś zaznaczone", GameShortcutId.OpenUnitOrders, new(Key.M)),
        new(GameShortcutId.AttackArea, "Rozkazy jednostek", "Atakuj obszar", GameShortcutId.OpenUnitOrders, new(Key.A)),
        new(GameShortcutId.HuntArea, "Rozkazy jednostek", "Poluj w obszarze", GameShortcutId.OpenUnitOrders, new(Key.H)),
        new(GameShortcutId.Patrol, "Rozkazy jednostek", "Patroluj", GameShortcutId.OpenUnitOrders, new(Key.P)),

        new(GameShortcutId.BuildFoodStorage, "world-planning", "Skład żywności", GameShortcutId.OpenConstruction, new(Key.F)),
        new(GameShortcutId.BuildWoodStorage, "world-planning", "Skład drewna", GameShortcutId.OpenConstruction, new(Key.D)),
        new(GameShortcutId.BuildStoneStorage, "world-planning", "Skład kamienia", GameShortcutId.OpenConstruction, new(Key.K)),
        new(GameShortcutId.BuildEquipmentStorage, "world-planning", "Skład sprzętu", GameShortcutId.OpenConstruction, new(Key.E)),
        new(GameShortcutId.BuildMaterialsStorage, "world-planning", "Skład materiałów", GameShortcutId.OpenConstruction, new(Key.T)),
        new(GameShortcutId.BuildWalkway, "world-planning", "Pomost", GameShortcutId.OpenConstruction, new(Key.P)),
        new(GameShortcutId.BuildFieldCamp, "world-planning", "Obóz wypadowy", GameShortcutId.OpenConstruction, new(Key.O)),
        new(GameShortcutId.BuildGoblinHut, "world-planning", "Chata goblinów", GameShortcutId.OpenConstruction, new(Key.C)),
        new(GameShortcutId.BuildWoodenWall, "world-planning", "Drewniana ściana", GameShortcutId.OpenConstruction, new(Key.W)),
        new(GameShortcutId.BuildStoneWall, "world-planning", "Kamienny mur", GameShortcutId.OpenConstruction, new(Key.M)),
        new(GameShortcutId.BuildWoodenDoor, "world-planning", "Drewniane drzwi", GameShortcutId.OpenConstruction, new(Key.Z)),
        new(GameShortcutId.BuildPrimitiveWorkshop, "world-planning", "Prymitywny warsztat", GameShortcutId.OpenConstruction, new(Key.N)),
        new(GameShortcutId.MineRock, "world-planning", "Kop w skale", GameShortcutId.OpenTerrain, new(Key.X)),
        new(GameShortcutId.CarveRampDown, "world-planning", "Wykop pochylnię w dół", GameShortcutId.OpenTerrain, new(Key.Pagedown)),
        new(GameShortcutId.CarveRampUp, "world-planning", "Wykop pochylnię w górę", GameShortcutId.OpenTerrain, new(Key.Pageup)),

        new(GameShortcutId.GatherFood, "Prace i obszary", "Zbierz żywność", GameShortcutId.OpenWork, new(Key.F)),
        new(GameShortcutId.GatherReeds, "Prace i obszary", "Zbierz sitowie", GameShortcutId.OpenWork, new(Key.T)),
        new(GameShortcutId.GatherBrushwood, "Prace i obszary", "Zbierz chrust", GameShortcutId.OpenWork, new(Key.D)),
        new(GameShortcutId.GatherStone, "Prace i obszary", "Zbierz kamienie i urobek", GameShortcutId.OpenWork, new(Key.K)),
        new(GameShortcutId.UprootBushes, "Prace i obszary", "Wykarcz krzaki", GameShortcutId.OpenWork, new(Key.C)),
        new(GameShortcutId.FellTrees, "Prace i obszary", "Wyrąb drzew i pni", GameShortcutId.OpenWork, new(Key.W)),
        new(GameShortcutId.QuarryBoulders, "Prace i obszary", "Rozbij głazy", GameShortcutId.OpenWork, new(Key.G)),
        new(GameShortcutId.HuntAnimals, "Prace i obszary", "Poluj na zwierzęta", GameShortcutId.OpenWork, new(Key.H)),
        new(GameShortcutId.Scout, "Prace i obszary", "Wyznacz zwiad", GameShortcutId.OpenWork, new(Key.S)),
        new(GameShortcutId.CleanBlood, "Prace i obszary", "Zmyj krew", GameShortcutId.OpenWork, new(Key.B)),
        new(GameShortcutId.ClearOrders, "Prace i obszary", "Usuń zlecenia", GameShortcutId.OpenWork, new(Key.X)),
    ];

    internal ShortcutStroke this[GameShortcutId id] => _bindings[id];

    internal void Set(GameShortcutId id, ShortcutStroke stroke)
    {
        _bindings[id] = stroke;
        Save();
    }

    internal string Describe(GameShortcutId id)
    {
        var definition = Definitions.First(item => item.Id == id);
        return definition.Parent is { } parent
            ? $"{_bindings[parent]} → {_bindings[id]}"
            : _bindings[id].ToString();
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, StoredStroke>>(File.ReadAllText(_path));
            if (stored is null)
            {
                return;
            }

            var usesLegacyCameraBindings =
                !stored.ContainsKey(nameof(GameShortcutId.CameraPanDown));

            foreach (var (name, stroke) in stored)
            {
                if (Enum.TryParse<GameShortcutId>(name, out var id) &&
                    Enum.IsDefined(typeof(Key), stroke.Key))
                {
                    _bindings[id] = new ShortcutStroke(
                        (Key)stroke.Key,
                        stroke.Ctrl,
                        stroke.Alt,
                        stroke.Shift);
                }
            }

            if (usesLegacyCameraBindings &&
                _bindings[GameShortcutId.OpenStatistics] == new ShortcutStroke(Key.S))
            {
                _bindings[GameShortcutId.OpenStatistics] = new ShortcutStroke(Key.I);
            }
            if (_bindings[GameShortcutId.MineRock] == new ShortcutStroke(Key.M) &&
                _bindings[GameShortcutId.BuildStoneWall] == new ShortcutStroke(Key.M))
            {
                _bindings[GameShortcutId.MineRock] = new ShortcutStroke(Key.X);
            }
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Nie udało się wczytać skrótów: {exception.Message}");
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var stored = _bindings.ToDictionary(
                pair => pair.Key.ToString(),
                pair => new StoredStroke(
                    (long)pair.Value.Key,
                    pair.Value.Ctrl,
                    pair.Value.Alt,
                    pair.Value.Shift));
            File.WriteAllText(
                _path,
                JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Nie udało się zapisać skrótów: {exception.Message}");
        }
    }

    private sealed record StoredStroke(long Key, bool Ctrl, bool Alt, bool Shift);
}
