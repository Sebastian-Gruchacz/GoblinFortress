using Godot;
using GoblinStronghold.Simulation.Presentation;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal readonly record struct LowerLevelCompositeTexture(
    Texture2D Texture,
    Rect2 WorldRect,
    int SourceDrawCount);

internal sealed class LowerLevelCompositeTextureCache : IDisposable
{
    private const ulong SignatureOffset = 14_695_981_039_346_656_037UL;
    private const ulong SignaturePrime = 1_099_511_628_211UL;

    private readonly SubViewport _viewport;
    private readonly Node2D _canvas;
    private readonly Dictionary<CompositeSourceKey, Sprite2D> _sources = [];
    private ulong? _signature;
    private LowerLevelCompositeTexture? _snapshot;

    public LowerLevelCompositeTexture? Current => _snapshot;

    public LowerLevelCompositeTextureCache(Node owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _viewport = new SubViewport
        {
            Name = "LowerLevelCompositeViewport",
            Disable3D = true,
            TransparentBg = true,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        _canvas = new Node2D
        {
            Name = "LowerLevelCompositeCanvas",
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        owner.AddChild(_viewport);
        _viewport.AddChild(_canvas);
    }

    public LowerLevelCompositeTexture? Synchronize(
        IReadOnlyList<LowerLevelChunkTexture> chunks,
        int activeLevel)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        if (chunks.Count == 0)
        {
            Reset();
            return null;
        }

        var signature = CreateSignature(chunks, activeLevel);
        if (_signature == signature && _snapshot is not null)
        {
            return _snapshot;
        }

        var pixelsPerChunk = checked(chunks[0].ChunkSize * chunks[0].PixelsPerCell);
        var minimumChunkX = chunks.Min(chunk => chunk.Key.X);
        var minimumChunkY = chunks.Min(chunk => chunk.Key.Y);
        var maximumChunkX = chunks.Max(chunk => chunk.Key.X) + 1;
        var maximumChunkY = chunks.Max(chunk => chunk.Key.Y) + 1;
        var pixelOrigin = new Vector2I(
            checked(minimumChunkX * pixelsPerChunk),
            checked(minimumChunkY * pixelsPerChunk));
        var pixelSize = new Vector2I(
            checked((maximumChunkX - minimumChunkX) * pixelsPerChunk),
            checked((maximumChunkY - minimumChunkY) * pixelsPerChunk));
        _viewport.Size = pixelSize;

        var retainedSources = new HashSet<CompositeSourceKey>();
        foreach (var chunk in chunks)
        {
            if (chunk.ChunkSize * chunk.PixelsPerCell != pixelsPerChunk)
            {
                throw new InvalidOperationException(
                    "Lower-level composite chunks must use one pixel scale.");
            }

            var depth = Math.Max(1, activeLevel - chunk.Key.Level);
            var brightness = LowerLevelVisualDegradationPolicy.ResolveBrightness(depth);
            var position = new Vector2(
                checked(chunk.Key.X * pixelsPerChunk - pixelOrigin.X),
                checked(chunk.Key.Y * pixelsPerChunk - pixelOrigin.Y));
            SynchronizeSource(
                new CompositeSourceKey(chunk.Key, Layer: 0),
                chunk.Lighting,
                position,
                brightness);
            SynchronizeSource(
                new CompositeSourceKey(chunk.Key, Layer: 1),
                chunk.SkyLighting,
                position,
                brightness);
            retainedSources.Add(new CompositeSourceKey(chunk.Key, Layer: 0));
            retainedSources.Add(new CompositeSourceKey(chunk.Key, Layer: 1));
        }
        foreach (var obsolete in _sources.Keys
                     .Where(key => !retainedSources.Contains(key))
                     .ToArray())
        {
            var source = _sources[obsolete];
            _sources.Remove(obsolete);
            _canvas.RemoveChild(source);
            source.QueueFree();
        }

        _viewport.RenderTargetClearMode = SubViewport.ClearMode.Always;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        _snapshot = new LowerLevelCompositeTexture(
            _viewport.GetTexture(),
            new Rect2(pixelOrigin, pixelSize),
            _sources.Count);
        _signature = signature;
        return _snapshot;
    }

    public void Reset()
    {
        ClearCanvas();
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        _signature = null;
        _snapshot = null;
    }

    public void Dispose()
    {
        Reset();
        if (GodotObject.IsInstanceValid(_viewport))
        {
            _viewport.QueueFree();
        }
    }

    private void SynchronizeSource(
        CompositeSourceKey key,
        Texture2D texture,
        Vector2 position,
        float brightness)
    {
        if (!_sources.TryGetValue(key, out var source))
        {
            source = new Sprite2D
            {
                Centered = false,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                ZIndex = checked((key.Chunk.Level * 2) + key.Layer),
            };
            _sources.Add(key, source);
            _canvas.AddChild(source);
        }
        source.Texture = texture;
        source.Position = position;
        source.Modulate = new Color(brightness, brightness, brightness, 1f);
    }

    private void ClearCanvas()
    {
        foreach (var child in _canvas.GetChildren())
        {
            _canvas.RemoveChild(child);
            child.QueueFree();
        }
        _sources.Clear();
    }

    private static ulong CreateSignature(
        IReadOnlyList<LowerLevelChunkTexture> chunks,
        int activeLevel)
    {
        var signature = Add(SignatureOffset, unchecked((ulong)activeLevel));
        signature = Add(signature, unchecked((ulong)chunks.Count));
        foreach (var chunk in chunks)
        {
            signature = Add(signature, unchecked((ulong)chunk.Key.Level));
            signature = Add(signature, unchecked((ulong)chunk.Key.X));
            signature = Add(signature, unchecked((ulong)chunk.Key.Y));
            signature = Add(signature, chunk.Lighting.GetInstanceId());
            signature = Add(signature, chunk.SkyLighting.GetInstanceId());
        }
        return signature;
    }

    private static ulong Add(ulong signature, ulong value) =>
        unchecked((signature ^ value) * SignaturePrime);

    private readonly record struct CompositeSourceKey(
        PresentationChunkKey Chunk,
        int Layer);
}
