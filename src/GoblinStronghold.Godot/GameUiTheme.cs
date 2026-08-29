using Godot;

namespace GoblinStronghold.GodotClient;

internal static class GameUiTheme
{
    internal const int WindowTitleHeight = 36;

    internal static readonly Color Background = new("241b14");
    internal static readonly Color RaisedBackground = new("35271c");
    internal static readonly Color HoverBackground = new("493522");
    internal static readonly Color PressedBackground = new("5b4327");
    internal static readonly Color Border = new("8d6b39");
    internal static readonly Color Text = new("f2d889");
    internal static readonly Color MutedText = new("bda96f");
    internal static readonly Color DisabledText = new("756747");
    internal static readonly Color Accent = new("ffd968");

    internal static Theme Create()
    {
        var theme = new Theme();

        foreach (var type in new[]
                 {
                     "Label", "RichTextLabel", "Button", "CheckButton", "CheckBox",
                     "OptionButton", "LineEdit", "SpinBox", "PopupMenu",
                 })
        {
            theme.SetColor("font_color", type, Text);
            theme.SetColor("font_hover_color", type, Accent);
            theme.SetColor("font_pressed_color", type, Accent);
            theme.SetColor("font_focus_color", type, Accent);
            theme.SetColor("font_disabled_color", type, DisabledText);
        }

        theme.SetColor("font_uneditable_color", "LineEdit", MutedText);
        theme.SetColor("font_placeholder_color", "LineEdit", MutedText);
        theme.SetColor("font_separator_color", "PopupMenu", MutedText);
        theme.SetColor("title_color", "Window", Background);
        theme.SetConstant("title_height", "Window", WindowTitleHeight);
        theme.SetIcon("close", "Window", CreateWindowCloseIcon(Background));
        theme.SetIcon("close_pressed", "Window", CreateWindowCloseIcon(RaisedBackground));

        var panel = CreateBox(Background, Border, 1, 7);
        var window = CreateBox(Background, Text, 1, 7);
        window.BorderWidthTop = WindowTitleHeight;
        window.ExpandMarginTop = WindowTitleHeight;
        var raised = CreateBox(RaisedBackground, Border, 1, 6);
        var hover = CreateBox(HoverBackground, Accent, 1, 6);
        var pressed = CreateBox(PressedBackground, Accent, 2, 6);
        var disabled = CreateBox(new Color(Background, 0.72f), new Color(Border, 0.45f), 1, 6);

        theme.SetStylebox("panel", "Panel", panel);
        theme.SetStylebox("panel", "PanelContainer", panel);
        theme.SetStylebox("panel", "PopupPanel", panel);
        theme.SetStylebox("embedded_border", "Window", window);
        theme.SetStylebox("embedded_unfocused_border", "Window", window);
        theme.SetStylebox("normal", "Button", raised);
        theme.SetStylebox("hover", "Button", hover);
        theme.SetStylebox("pressed", "Button", pressed);
        theme.SetStylebox("focus", "Button", hover);
        theme.SetStylebox("disabled", "Button", disabled);
        theme.SetStylebox("normal", "OptionButton", raised);
        theme.SetStylebox("hover", "OptionButton", hover);
        theme.SetStylebox("pressed", "OptionButton", pressed);
        theme.SetStylebox("focus", "OptionButton", hover);
        theme.SetStylebox("normal", "LineEdit", raised);
        theme.SetStylebox("focus", "LineEdit", hover);
        theme.SetStylebox("panel", "PopupMenu", panel);
        theme.SetStylebox("hover", "PopupMenu", hover);
        theme.SetStylebox("separator", "PopupMenu", CreateSeparator());
        theme.SetStylebox("separator_left", "PopupMenu", CreateSeparator());

        return theme;
    }

    private static StyleBoxFlat CreateBox(
        Color background,
        Color border,
        int borderWidth,
        int cornerRadius)
    {
        var box = new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
            CornerRadiusTopLeft = cornerRadius,
            CornerRadiusTopRight = cornerRadius,
            CornerRadiusBottomRight = cornerRadius,
            CornerRadiusBottomLeft = cornerRadius,
        };
        foreach (var side in Enum.GetValues<Side>())
        {
            box.SetBorderWidth(side, borderWidth);
        }
        return box;
    }

    private static StyleBoxLine CreateSeparator() => new()
    {
        Color = Border,
        Thickness = 1,
    };

    private static Texture2D CreateWindowCloseIcon(Color color)
    {
        var htmlColor = color.ToHtml(includeAlpha: false);
        var svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 18 18">
              <path d="M4 4 L14 14 M14 4 L4 14" fill="none" stroke="#{htmlColor}" stroke-width="2.5" stroke-linecap="round"/>
            </svg>
            """;
        var image = new Image();
        if (image.LoadSvgFromString(svg) != Error.Ok)
        {
            throw new InvalidOperationException("Cannot create the window close icon.");
        }
        return ImageTexture.CreateFromImage(image);
    }
}
