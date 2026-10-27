using System;
using System.Threading;
using System.Threading.Tasks;

namespace ClaudeVisualChat;

/// <summary>Speech to text the host offers to the reply box (the touch layout shows a microphone while it is available).</summary>
/// <remarks>
/// What is heard goes into the box, at the caret, and is never sent by itself. The box asks for one listening for each tap on
/// the microphone, on the UI thread, and a second tap cancels its <c>stop</c>. It cancels it too, and ignores what comes back
/// after, when the listening is cut short: another conversation is shown, the layout changes, the view turns read-only or leaves
/// the window, or the dictation goes away or stops being available. A new listening can be asked for while one that was just
/// cancelled is still returning. The <c>partial</c> reports and <see cref="AvailabilityChanged"/> may come from any thread.
/// </remarks>
public interface IChatDictation
{
    /// <summary>True while dictation can start (for example, the language's model is on the device).</summary>
    bool IsAvailable { get; }

    /// <summary>Raised when <see cref="IsAvailable"/> changes; may be raised from any thread.</summary>
    event EventHandler? AvailabilityChanged;

    /// <summary>
    /// Listens until the speaker stops or <paramref name="stop"/> is cancelled, reporting what it heard so far through
    /// <paramref name="partial"/>; returns what it heard (null for nothing). A cancelled <paramref name="stop"/> ends
    /// listening early and still returns what was heard; it does not throw.
    /// </summary>
    Task<string?> ListenAsync(IProgress<string>? partial, CancellationToken stop);
}
