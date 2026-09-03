using Godot;

namespace GoblinStronghold.GodotClient.UI.Windows;

internal sealed partial class LoadingProgressOverlay : Control
{
    private readonly Label _title;
    private readonly Label _stage;
    private readonly ProgressBar _progress;

    internal LoadingProgressOverlay()
    {
        Name = "LoadingProgressOverlay";
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 1_000;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var panel = new PanelContainer
        {
            AnchorLeft = 1f,
            AnchorTop = 1f,
            AnchorRight = 1f,
            AnchorBottom = 1f,
            OffsetLeft = -390f,
            OffsetTop = -126f,
            OffsetRight = -20f,
            OffsetBottom = -20f,
        };
        AddChild(panel);

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "top", "right", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", 12);
        }
        panel.AddChild(margin);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 7);
        margin.AddChild(content);
        _title = new Label { ThemeTypeVariation = "HeaderSmall" };
        content.AddChild(_title);
        _stage = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        content.AddChild(_stage);
        _progress = new ProgressBar
        {
            MinValue = 0d,
            MaxValue = 1d,
            ShowPercentage = true,
            CustomMinimumSize = new Vector2(340f, 18f),
        };
        content.AddChild(_progress);
        Hide();
    }

    internal void Begin(string title, string stage)
    {
        _title.Text = title;
        Update(stage, 0d);
        Show();
    }

    internal void Update(string stage, double progress)
    {
        _stage.Text = stage;
        _progress.Value = Math.Clamp(progress, 0d, 1d);
    }
}
