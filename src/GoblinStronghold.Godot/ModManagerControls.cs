using Godot;

namespace GoblinStronghold.GodotClient;

internal sealed partial class ModDragHandle : Button
{
    internal string PackKey { get; init; } = string.Empty;

    public override Variant _GetDragData(Vector2 atPosition)
    {
        var preview = new Label { Text = Text };
        SetDragPreview(preview);
        return PackKey;
    }
}

internal sealed partial class ModDropRow : PanelContainer
{
    internal string PackKey { get; init; } = string.Empty;
    internal event Action<string, string, bool>? PackDropped;

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        data.VariantType == Variant.Type.String &&
        !string.Equals(data.AsString(), PackKey, StringComparison.Ordinal);

    public override void _DropData(Vector2 atPosition, Variant data) =>
        PackDropped?.Invoke(data.AsString(), PackKey, atPosition.Y >= Size.Y / 2f);
}
