using Godot;

namespace GoblinStronghold.GodotClient.UI.Windows;

internal sealed class WindowEscapeDismissalRouter
{
    private readonly HashSet<ulong> _registeredWindows = [];

    public void RegisterDescendants(Node root, Action<Window> dismiss)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(dismiss);
        foreach (var child in root.GetChildren())
        {
            RegisterNode(child, dismiss);
        }
    }

    private void RegisterNode(Node node, Action<Window> dismiss)
    {
        if (node is Window window && _registeredWindows.Add(window.GetInstanceId()))
        {
            window.WindowInput += inputEvent =>
            {
                if (!window.Visible || inputEvent is not InputEventKey
                    {
                        Pressed: true,
                        Echo: false,
                        Keycode: Key.Escape,
                    })
                {
                    return;
                }

                dismiss(window);
                window.GetViewport().SetInputAsHandled();
            };
        }

        foreach (var child in node.GetChildren())
        {
            RegisterNode(child, dismiss);
        }
    }
}
