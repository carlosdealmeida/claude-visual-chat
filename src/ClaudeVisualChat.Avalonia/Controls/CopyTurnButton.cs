using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The button under a finished turn in the touch layout (it replaces the line of duration, tokens and cost): a 44 px
/// icon that copies what Claude wrote in the turn (<see cref="ChatTurns.AnswerText"/>) to the clipboard. For 1.5 s
/// after it copied, the icon is a check and the button's name is "Copied". A turn that wrote nothing has no button.
/// </summary>
internal sealed class CopyTurnButton : Button
{
    /// <summary>How long the check shows after a copy.</summary>
    private static readonly TimeSpan CopiedFor = TimeSpan.FromSeconds(1.5);

    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<CopyTurnButton>();

    /// <summary>Test seam: copies the text in place of the clipboard.</summary>
    internal static Func<string, Task>? CopyOverride;

    private readonly Path _icon = new Path();
    private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = CopiedFor };

    private bool _copied;
    private bool _attached;

    public CopyTurnButton()
    {
        Classes.Add("ClaudeChatButton");
        Classes.Add("ClaudeChatCopyTurn");
        _icon.Classes.Add("ClaudeChatCopyIcon");
        var box = new Viewbox { Child = _icon };
        box.Classes.Add("ClaudeChatCopyIcon");
        Content = box;
        _timer.Tick += (_, _) => Tick();
        IsVisible = false;
        UpdateName();
    }

    /// <summary>True from a copy until the check goes away.</summary>
    internal bool IsCopied => _copied;

    /// <summary>True while the timer waits to take the check away.</summary>
    internal bool IsTicking => _timer.IsEnabled;

    /// <summary>The timer's tick, 1.5 s after a copy: the check goes away.</summary>
    internal void Tick() => EndCopied();

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    /// <summary>Styled as a Button: the theme's <c>Button.ClaudeChatButton</c> template and the copy button's class.</summary>
    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateVisibility();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        EndCopied(); // the check does not wait in a control that is out of the window
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        EndCopied(); // the list reuses the control for another turn
        if (_attached) UpdateVisibility();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty) UpdateName();
    }

    protected override void OnClick()
    {
        base.OnClick();
        _ = CopyAsync();
    }

    /// <summary>The check goes away: the copy icon and its name come back.</summary>
    private void EndCopied()
    {
        _timer.Stop();
        if (!_copied) return;
        _copied = false;
        _icon.Classes.Set("copied", false);
        UpdateName();
    }

    private async Task CopyAsync()
    {
        var text = Answer();
        if (text.Length == 0) return;
        var turn = DataContext;
        try
        {
            if (CopyOverride != null) await CopyOverride(text);
            else if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
            else return;
        }
        catch (Exception)
        {
            return; // the clipboard refused it: nothing was copied, so nothing is confirmed
        }
        if (!_attached || !ReferenceEquals(DataContext, turn)) return; // the list took the control to another turn meanwhile
        _copied = true;
        _icon.Classes.Set("copied", true);
        UpdateName();
        _timer.Stop();
        _timer.Start(); // a second copy gives the check another 1.5 s
    }

    /// <summary>What Claude wrote in the turn this button ends; empty when the turn wrote nothing.</summary>
    private string Answer()
    {
        if (!(DataContext is TurnFooterItem footer)) return "";
        var items = this.FindAncestorOfType<ItemsControl>()?.ItemsSource as IList<ChatItem>;
        var index = items == null ? -1 : items.IndexOf(footer);
        return items == null || index < 0 ? "" : ChatTurns.AnswerText(items, index);
    }

    private void UpdateVisibility() => IsVisible = Answer().Length > 0;

    private void UpdateName() =>
        AutomationProperties.SetName(this, _copied ? Strings.Copied : Strings.CopyAnswer);
}
