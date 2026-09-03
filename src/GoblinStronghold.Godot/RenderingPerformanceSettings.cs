using Godot;
using System.Text.Json;

namespace GoblinStronghold.GodotClient;

internal readonly record struct RenderingPerformanceOptions(
    double LowerLayerRefreshSeconds,
    int LowerLayerChunkSize,
    bool WarmPresentationCachesBeforeShowingWorld,
    bool OnionLayers,
    bool UndergroundOnionLayers)
{
    public const double DefaultLowerLayerRefreshSeconds = 1d;
    public const int DefaultLowerLayerChunkSize = 16;
    public const double MinimumLowerLayerRefreshSeconds = 0.25d;
    public const double MaximumLowerLayerRefreshSeconds = 2d;
    public const int MinimumLowerLayerChunkSize = 8;
    public const int MaximumLowerLayerChunkSize = 32;

    public static RenderingPerformanceOptions Default => new(
        DefaultLowerLayerRefreshSeconds,
        DefaultLowerLayerChunkSize,
        WarmPresentationCachesBeforeShowingWorld: false,
        OnionLayers: false,
        UndergroundOnionLayers: false);

    public RenderingPerformanceOptions Clamp() => new(
        Math.Clamp(
            LowerLayerRefreshSeconds,
            MinimumLowerLayerRefreshSeconds,
            MaximumLowerLayerRefreshSeconds),
        Math.Clamp(
            RoundChunkSize(LowerLayerChunkSize),
            MinimumLowerLayerChunkSize,
            MaximumLowerLayerChunkSize),
        WarmPresentationCachesBeforeShowingWorld,
        OnionLayers,
        UndergroundOnionLayers);

    public bool UsesOnionLayersAt(int level) =>
        level <= 0 ? UndergroundOnionLayers : OnionLayers;

    private static int RoundChunkSize(int value) =>
        (int)Math.Round(value / 8d, MidpointRounding.AwayFromZero) * 8;
}

internal sealed class RenderingPerformanceSettings
{
    private readonly string _path;

    internal RenderingPerformanceSettings(string path)
    {
        _path = path;
        Options = Load();
    }

    internal RenderingPerformanceOptions Options { get; private set; }

    internal void Set(RenderingPerformanceOptions options)
    {
        Options = options.Clamp();
        Save();
    }

    private RenderingPerformanceOptions Load()
    {
        if (!File.Exists(_path))
        {
            return RenderingPerformanceOptions.Default;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<RenderingPerformanceOptions>(
                File.ReadAllText(_path));
            return stored.Clamp();
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Could not load rendering performance settings: {exception.Message}");
            return RenderingPerformanceOptions.Default;
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(
                _path,
                JsonSerializer.Serialize(
                    Options,
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Could not save rendering performance settings: {exception.Message}");
        }
    }
}
