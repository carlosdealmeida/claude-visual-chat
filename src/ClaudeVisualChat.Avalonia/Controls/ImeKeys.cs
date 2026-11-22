using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// A key an input method takes while it composes (Japanese, Chinese, Korean…): the Enter that picks the word, the arrows that walk
/// its candidates, the Esc that drops them. The component's key handlers ask first and leave such a key alone, unhandled: it is the
/// input method's.
/// </summary>
internal static class ImeKeys
{
    /// <summary>
    /// True when the key is the input method's: the platform says so (<see cref="Key.ImeProcessed"/>), or it comes from a text box
    /// (or from inside one) whose text presenter shows text being composed. A key from anything else has only the first sign.
    /// </summary>
    internal static bool IsComposing(KeyEventArgs e)
    {
        if (e.Key == Key.ImeProcessed) return true;
        var box = (e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true);
        var presenter = box?.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault(); // its PART_TextPresenter
        return !string.IsNullOrEmpty(presenter?.PreeditText);
    }
}
