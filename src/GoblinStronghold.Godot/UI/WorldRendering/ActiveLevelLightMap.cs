using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Lighting;
using GoblinStronghold.Simulation.Map;
using System.Diagnostics;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal sealed class ActiveLevelLightMap : IDisposable
{
    private const int PixelsPerCell = 4;
    private static readonly IReadOnlySet<GridPosition> EmptyBlockingCells =
        new HashSet<GridPosition>();

    private readonly LightBlockingCellIndex _blockingCells = new();
    private readonly TimedPresentationOperationCounter _builds = new();
    private ImageTexture? _texture;
    private Vector2I _textureSize;
    private long _cells;
    private long _emitterEvaluations;

    public (TimedPresentationOperationMetrics Timings, long Cells, long EmitterEvaluations)
        GetMetrics() => (_builds.Snapshot, _cells, _emitterEvaluations);

    public Texture2D Render(
        SimulationEngine engine,
        SimulationSnapshot snapshot,
        int level,
        int minimumX,
        int minimumY,
        int maximumX,
        int maximumY,
        IReadOnlyList<LightEmitterSnapshot> emitters,
        float ambientDarkness,
        double animationElapsed)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var cellWidth = Math.Max(1, maximumX - minimumX);
        var cellHeight = Math.Max(1, maximumY - minimumY);
        var width = cellWidth * PixelsPerCell;
        var height = cellHeight * PixelsPerCell;
        using var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        var blockers = emitters.Count == 0
            ? EmptyBlockingCells
            : _blockingCells.Get(
                engine,
                snapshot.WorldObjects,
                level,
                emitters.Select(emitter => emitter.Position.X).Append(minimumX).Min(),
                emitters.Select(emitter => emitter.Position.Y).Append(minimumY).Min(),
                emitters.Select(emitter => emitter.Position.X + 1).Append(maximumX).Max(),
                emitters.Select(emitter => emitter.Position.Y + 1).Append(maximumY).Max());
        for (var localY = 0; localY < cellHeight; localY++)
        {
            for (var localX = 0; localX < cellWidth; localX++)
            {
                var position = new GridPosition(minimumX + localX, minimumY + localY, level);
                var remainingDarkness = 1f;
                foreach (var emitter in emitters)
                {
                    var definition = LightEmitterCatalog.Get(emitter.Handle.DefinitionId);
                    var phase = (animationElapsed / 6d) * Math.Tau * 7d +
                        (emitter.Handle.InstanceId % 997UL) * 0.173d;
                    var flicker = 1f - definition.FlickerAmount +
                        definition.FlickerAmount * (0.5f + 0.5f * MathF.Sin((float)phase));
                    var contribution = LightOcclusionPolicy.CalculateSoftContribution(
                        emitter with { Intensity = emitter.Intensity * flicker },
                        position,
                        blockers);
                    remainingDarkness *= 1f - Math.Clamp(contribution, 0f, 1f);
                }

                var alpha = ambientDarkness * remainingDarkness;
                image.FillRect(
                    new Rect2I(
                        localX * PixelsPerCell,
                        localY * PixelsPerCell,
                        PixelsPerCell,
                        PixelsPerCell),
                    level < 0
                    ? new Color(0.008f, 0.012f, 0.016f, alpha)
                    : new Color(0.025f, 0.055f, 0.12f, alpha));
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

        _cells = checked(_cells + (long)cellWidth * cellHeight);
        _emitterEvaluations = checked(
            _emitterEvaluations + ((long)cellWidth * cellHeight * emitters.Count));
        _builds.Record(startedAt);
        return _texture;
    }

    public void Reset()
    {
        _blockingCells.Reset();
        _builds.Reset();
        _cells = 0;
        _emitterEvaluations = 0;
    }

    public void Dispose()
    {
        _texture?.Dispose();
        _texture = null;
        _textureSize = default;
        Reset();
    }
}
