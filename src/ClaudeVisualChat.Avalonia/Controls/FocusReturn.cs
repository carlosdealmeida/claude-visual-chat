using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// What a modal does with the keyboard, as the extension's dialogs do: when it opens it remembers what has the keyboard
/// (<see cref="Remember"/>), and when it closes it gives the keyboard back to that, with its focus ring when the keyboard had
/// brought it there (<see cref="Restore"/>). A place that is gone by then (hidden, out of the tree, no longer focusable) gets
/// nothing, and the caller falls back to the view's own rule. The Agent map and the two information modals share it.
/// </summary>
internal sealed class FocusReturn
{
    private Control? _back;
    private bool _byKey;

    /// <summary>What has the keyboard now, unless it is inside <paramref name="modal"/> already (the modal was opened again).</summary>
    internal void Remember(Visual modal)
    {
        var focused = TopLevel.GetTopLevel(modal)?.FocusManager?.GetFocusedElement() as Control;
        if (focused != null && (ReferenceEquals(focused, modal) || modal.IsVisualAncestorOf(focused))) return;
        _back = focused;
        _byKey = focused != null && focused.Classes.Contains(":focus-visible");
    }

    /// <summary>Gives the keyboard back to what had it, when that is still on screen; false, and nothing moves, when it is not.</summary>
    internal bool Restore()
    {
        var back = _back;
        Forget();
        return back is { IsEffectivelyVisible: true, Focusable: true } && back.IsAttachedToVisualTree()
            && back.Focus(_byKey ? NavigationMethod.Tab : NavigationMethod.Pointer);
    }

    /// <summary>The modal closed without the keyboard in it, or the view left the window: nothing to give back.</summary>
    internal void Forget()
    {
        _back = null;
        _byKey = false;
    }
}
