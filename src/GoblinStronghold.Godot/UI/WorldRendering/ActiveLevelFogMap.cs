using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Presentation;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal sealed class ActiveLevelFogMap : IDisposable
{
    private const int PixelsPerCell = 20;
    private readonly TimedPresentationOperationCounter _builds = new();
    private ImageTexture? _texture;
    private Vector2I _textureSize;
    private RenderCacheKey? _renderCacheKey;
    private long _cells;

    public (TimedPresentationOperationMetrics Timings, long Cells) GetMetrics() =>
        (_builds.Snapshot, _cells);

    public Texture2D Render(
        SimulationSnapshot snapshot,
        ulong topologyVersion,
        int level,
        int minimumX,
        int minimumY,
        int maximumX,
        int maximumY,
        Func<GridPosition, CellVisibility> getVisibility)
    {
        var bounds = new PresentationCellBounds(
            minimumX,
            minimumY,
            maximumX,
            maximumY);
        var key = new RenderCacheKey(
            ActiveLevelVisibilitySignaturePolicy.Create(
                level,
                bounds,
                getVisibility),
            topologyVersion,
            level,
            minimumX,
            minimumY,
            maximumX,
            maximumY);
        if (_texture is not null && _renderCacheKey == key)
        {
            return _texture;
        }

        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var cellWidth = Math.Max(1, maximumX - minimumX);
        var cellHeight = Math.Max(1, maximumY - minimumY);
        var width = cellWidth * PixelsPerCell;
        var height = cellHeight * PixelsPerCell;
        using var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        for (var localY = 0; localY < cellHeight; localY++)
        {
            for (var localX = 0; localX < cellWidth; localX++)
            {
                var visibility = getVisibility(new GridPosition(
                    minimumX + localX,
                    minimumY + localY,
                    level));
                image.FillRect(
                    new Rect2I(
                        localX * PixelsPerCell,
                        localY * PixelsPerCell,
                        PixelsPerCell,
                        PixelsPerCell),
                    visibility switch
                    {
                        CellVisibility.Unknown => new Color(0.035f, 0.045f, 0.04f, 0.97f),
                        CellVisibility.Explored => new Color(0.04f, 0.06f, 0.05f, 0.58f),
                        _ => Colors.Transparent,
                    });
            }
        }

        var size = new Vector2I(width, height);
        if (_texture is null || _textureSize != size)
        {
            _texture?.Dispose();
            _texture = ImageTexture.CreateFromImage(image);
            _textureSize = size;
        }
        else
        {
            _texture.Update(image);
        }

        _renderCacheKey = key;
        _cells = checked(_cells + (long)cellWidth * cellHeight);
        _builds.Record(startedAt);
        return _texture;
    }

    public void Reset()
    {
        _renderCacheKey = null;
        _builds.Reset();
        _cells = 0;
    }

    public void Dispose()
    {
        _texture?.Dispose();
        _texture = null;
        _textureSize = default;
        Reset();
    }

    private readonly record struct RenderCacheKey(
        ulong VisibilitySignature,
        ulong TopologyVersion,
        int Level,
        int MinimumX,
        int MinimumY,
        int MaximumX,
        int MaximumY);
}
