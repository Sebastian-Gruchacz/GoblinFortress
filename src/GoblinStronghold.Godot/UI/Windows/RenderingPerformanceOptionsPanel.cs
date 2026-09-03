using Godot;

namespace GoblinStronghold.GodotClient.UI.Windows;

internal static class RenderingPerformanceOptionsPanel
{
    internal static Control Create(
        RenderingPerformanceOptions initialOptions,
        string help,
        Func<double, string> formatRefresh,
        Func<int, string> formatChunkSize,
        string warmCaches,
        string warmCachesHelp,
        Action<RenderingPerformanceOptions> optionsChanged)
    {
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 10);
        content.AddChild(new Label
        {
            Text = help,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        var options = initialOptions.Clamp();
        var refreshLabel = new Label { Text = formatRefresh(options.LowerLayerRefreshSeconds) };
        var refreshSlider = new HSlider
        {
            MinValue = RenderingPerformanceOptions.MinimumLowerLayerRefreshSeconds,
            MaxValue = RenderingPerformanceOptions.MaximumLowerLayerRefreshSeconds,
            Step = 0.25d,
            Value = options.LowerLayerRefreshSeconds,
            CustomMinimumSize = new Vector2(0, 28),
        };
        refreshSlider.ValueChanged += value =>
        {
            options = (options with { LowerLayerRefreshSeconds = value }).Clamp();
            refreshLabel.Text = formatRefresh(options.LowerLayerRefreshSeconds);
            optionsChanged(options);
        };
        content.AddChild(refreshLabel);
        content.AddChild(refreshSlider);

        var chunkLabel = new Label { Text = formatChunkSize(options.LowerLayerChunkSize) };
        var chunkSlider = new HSlider
        {
            MinValue = RenderingPerformanceOptions.MinimumLowerLayerChunkSize,
            MaxValue = RenderingPerformanceOptions.MaximumLowerLayerChunkSize,
            Step = 8d,
            Value = options.LowerLayerChunkSize,
            CustomMinimumSize = new Vector2(0, 28),
        };
        chunkSlider.ValueChanged += value =>
        {
            options = (options with { LowerLayerChunkSize = (int)value }).Clamp();
            chunkLabel.Text = formatChunkSize(options.LowerLayerChunkSize);
            optionsChanged(options);
        };
        content.AddChild(chunkLabel);
        content.AddChild(chunkSlider);

        var warmCachesToggle = new CheckButton
        {
            Text = warmCaches,
            ButtonPressed = options.WarmPresentationCachesBeforeShowingWorld,
            TooltipText = warmCachesHelp,
        };
        warmCachesToggle.Toggled += enabled =>
        {
            options = options with { WarmPresentationCachesBeforeShowingWorld = enabled };
            optionsChanged(options);
        };
        content.AddChild(warmCachesToggle);
        content.AddChild(new Label
        {
            Text = warmCachesHelp,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        return content;
    }
}
