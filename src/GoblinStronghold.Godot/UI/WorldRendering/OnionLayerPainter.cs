using Godot;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal sealed class OnionLayerPainter : IDisposable
{
    private const int PatternSize = 20;
    private readonly ImageTexture _hatchTexture;

    public OnionLayerPainter()
    {
        var image = Image.CreateEmpty(
            PatternSize,
            PatternSize,
            false,
            Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        var hatch = new Color(0.62f, 0.68f, 0.65f, 0.16f);
        for (var y = 0; y < PatternSize; y++)
        {
            var x = PatternSize - y - 1;
            image.SetPixel(x, y, hatch);
            if (x + 1 < PatternSize)
            {
                image.SetPixel(x + 1, y, hatch);
            }
        }
        _hatchTexture = ImageTexture.CreateFromImage(image);
        image.Dispose();
    }

    public void DrawPlane(CanvasItem canvas, Rect2 worldRect)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        canvas.DrawRect(worldRect, new Color("232725"));
        canvas.DrawTextureRect(
            _hatchTexture,
            worldRect,
            tile: true,
            modulate: new Color(1f, 1f, 1f, 0.72f));
    }

    public static void DrawOpeningVignette(CanvasItem canvas, Rect2 cellRect)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        canvas.DrawRect(
            cellRect.Grow(-1f),
            new Color(0.025f, 0.03f, 0.028f, 0.78f),
            filled: false,
            width: 2f);
        canvas.DrawRect(
            cellRect.Grow(-3f),
            new Color(0.48f, 0.53f, 0.5f, 0.18f),
            filled: false,
            width: 1f);
    }

    public void Dispose() => _hatchTexture.Dispose();
}
