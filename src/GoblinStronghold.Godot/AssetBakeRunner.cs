using Godot;

namespace GoblinStronghold.GodotClient;

public partial class AssetBakeRunner : Node
{
    private const string DefaultRecipe = "res://AssetRecipes/connected-walkways-v1.json";

    public override void _Ready()
    {
        try
        {
            var recipe = OS.GetCmdlineUserArgs()
                .FirstOrDefault(argument => argument.StartsWith("--recipe=", StringComparison.Ordinal))?
                ["--recipe=".Length..] ?? DefaultRecipe;
            var result = AssetAtlasBaker.Bake(recipe);
            GD.Print($"Baked '{result.Recipe}': {result.EntryCount} entries, " +
                $"hash {result.ContentHash}, atlas {result.AtlasPath}, manifest {result.ManifestPath}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"Asset bake failed: {exception}");
            GetTree().Quit(1);
        }
    }
}
