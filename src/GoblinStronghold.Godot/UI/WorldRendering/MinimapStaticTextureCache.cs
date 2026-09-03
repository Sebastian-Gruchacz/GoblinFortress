using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal sealed class MinimapStaticTextureCache : IDisposable
{
    private readonly Dictionary<int, CachedLayer> _layers = [];

    public Texture2D? GetTexture(int level) =>
        _layers.GetValueOrDefault(level)?.Texture;

    public bool SynchronizeLevel(
        SimulationEngine engine,
        SimulationSnapshot snapshot,
        int level)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(snapshot);
        var discoverySignature = CreateDiscoverySignature(engine, snapshot, level);
        if (_layers.TryGetValue(level, out var cached) &&
            cached.WorldVersion == snapshot.WorldVersion &&
            cached.DiscoverySignature == discoverySignature)
        {
            return false;
        }

        var replacement = BuildLayer(
            engine,
            snapshot,
            level,
            discoverySignature);
        if (_layers.Remove(level, out var previous))
        {
            previous.Dispose();
        }
        _layers.Add(level, replacement);
        return true;
    }

    public void Reset()
    {
        foreach (var layer in _layers.Values)
        {
            layer.Dispose();
        }
        _layers.Clear();
    }

    public void Dispose() => Reset();

    private static CachedLayer BuildLayer(
        SimulationEngine engine,
        SimulationSnapshot snapshot,
        int level,
        ulong discoverySignature)
    {
        var map = engine.Map;
        var image = Image.CreateEmpty(map.Width, map.Height, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var position = new GridPosition(x, y, level);
                if (!snapshot.GetVisibility(position, map.Width).IsDiscovered())
                {
                    continue;
                }

                var color = ResolveLevelColor(engine, position);
                if (color is { } terrainColor)
                {
                    image.SetPixel(x, y, terrainColor);
                }
            }
        }

        foreach (var worldObject in snapshot.WorldObjects.Where(item =>
                     item.Owner != WorldObjectOwner.Nature))
        {
            foreach (var item in worldObject.GetAbsoluteParts().Where(item =>
                         item.Position.Z == level &&
                         map.IsColumnWithin(item.Position) &&
                         snapshot.GetVisibility(item.Position, map.Width).IsDiscovered()))
            {
                image.SetPixel(
                    item.Position.X,
                    item.Position.Y,
                    ResolveStructureColor(worldObject.Owner, item.Part.Kind));
            }
        }

        var texture = ImageTexture.CreateFromImage(image);
        image.Dispose();
        return new CachedLayer(
            snapshot.WorldVersion,
            discoverySignature,
            texture);
    }

    private static Color? ResolveLevelColor(
        SimulationEngine engine,
        GridPosition position)
    {
        if (engine.Map.IsTerrainSurfacePosition(position))
        {
            var cell = engine.Map.GetColumnCell(position);
            if (cell.SurfaceRoute != SurfaceRouteKind.None)
            {
                return cell.SurfaceRoute == SurfaceRouteKind.Ford
                    ? new Color("b28a50")
                    : new Color("85633e");
            }
            return cell.Terrain switch
            {
                TerrainKind.SolidGround => new Color("668b4d"),
                TerrainKind.Mud => new Color("4f5838"),
                TerrainKind.ShallowWater => new Color("4b8890"),
                TerrainKind.DeepWater => new Color("28536d"),
                _ => Colors.Magenta,
            };
        }

        if (engine.Map.IsCavePosition(position))
        {
            var cell = engine.Map.GetCaveCell(position);
            if (cell.Fluid == CellFluidKind.Lava)
            {
                return new Color("b94a22");
            }
            if (cell.Fluid == CellFluidKind.Water)
            {
                return new Color("28536d");
            }

            var open = cell.IsOpen || engine.World.ExcavatedCaveCells.Contains(position);
            return RockColor(cell.Rock, open);
        }

        if (engine.Map.IsHillMassPosition(position))
        {
            var cell = engine.Map.GetHillMassCell(position);
            return RockColor(cell.Rock, !engine.World.IsSolidHillRock(position));
        }

        return null;
    }

    private static Color ResolveStructureColor(
        WorldObjectOwner owner,
        WorldObjectPartKind part) => (owner, part) switch
    {
        (WorldObjectOwner.HumanVillage, WorldObjectPartKind.Floor or
            WorldObjectPartKind.Walkway) => new Color("9f825e"),
        (WorldObjectOwner.HumanVillage, _) => new Color("d0aa72"),
        (WorldObjectOwner.GoblinTribe, WorldObjectPartKind.Floor or
            WorldObjectPartKind.Walkway) => new Color("766947"),
        (WorldObjectOwner.GoblinTribe, _) => new Color("b59655"),
        _ => new Color("8a7655"),
    };

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

    private static ulong CreateDiscoverySignature(
        SimulationEngine engine,
        SimulationSnapshot snapshot,
        int level)
    {
        const ulong offset = 14_695_981_039_346_656_037UL;
        const ulong prime = 1_099_511_628_211UL;
        var signature = offset;
        for (var y = 0; y < engine.Map.Height; y++)
        {
            for (var x = 0; x < engine.Map.Width; x++)
            {
                var discovered = snapshot.GetVisibility(
                    new GridPosition(x, y, level),
                    engine.Map.Width).IsDiscovered();
                signature = (signature ^ (discovered ? 1UL : 0UL)) * prime;
            }
        }
        return signature;
    }

    private sealed record CachedLayer(
        ulong WorldVersion,
        ulong DiscoverySignature,
        ImageTexture Texture) : IDisposable
    {
        public void Dispose() => Texture.Dispose();
    }
}
