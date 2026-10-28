using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The one place the component writes text to the clipboard (the Bash copy button, the copy button under an answer, a copy
/// from the plan's document, and what the later copy actions use). A refusal of the clipboard is not an exception: the write
/// says whether it worked.
/// </summary>
internal static class ChatClipboard
{
    /// <summary>Test seam: takes the text in place of the clipboard.</summary>
    internal static Func<string, Task>? Override;

    /// <summary>Whether there is somewhere to write: the seam, or the clipboard of the window <paramref name="from"/> is in.</summary>
    internal static bool IsAvailable(Visual? from) => Override != null || ClipboardOf(from) != null;

    /// <summary>
    /// Writes <paramref name="text"/> to the clipboard of the window <paramref name="from"/> is in. False when there is no
    /// clipboard or it refused the text (nothing was copied, so nothing is confirmed); it never throws.
    /// </summary>
    internal static async Task<bool> SetTextAsync(Visual? from, string text)
    {
        try
        {
            var seam = Override;
            if (seam != null)
            {
                await seam(text);
                return true;
            }
            var clipboard = ClipboardOf(from);
            if (clipboard == null) return false;
            await clipboard.SetTextAsync(text);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static Avalonia.Input.Platform.IClipboard? ClipboardOf(Visual? from) => TopLevel.GetTopLevel(from)?.Clipboard;
}
