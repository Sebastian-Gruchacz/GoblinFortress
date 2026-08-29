using Godot;

namespace GoblinStronghold.GodotClient;

internal static class TextureResources
{
    public static Texture2D LoadRequired(string path, string description)
    {
        var texture = GD.Load<Texture2D>(path);
        if (texture is null)
        {
            throw new InvalidOperationException($"Cannot load {description}: {path}");
        }

        return texture;
    }
}
