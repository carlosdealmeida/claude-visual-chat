using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia.Data.Converters;

namespace ClaudeVisualChat.Controls;

/// <summary>Converters of the theme's item templates.</summary>
internal static class ChatConverters
{
    /// <summary>Longest message or thought shown; more than this is cut, so a huge paste stays quick to draw.</summary>
    public const int MaxTextChars = 20000;

    /// <summary>(<see cref="NoticeKind"/>, <see cref="ChatStrings"/>) → the notice text.</summary>
    public static readonly IMultiValueConverter Notice = new StringsConverter<NoticeKind>((kind, strings) => strings.Notice(kind));

    /// <summary>(<see cref="TurnFooterItem"/>, <see cref="ChatStrings"/>) → the footer, empty when nothing is known.</summary>
    public static readonly IMultiValueConverter Footer = new StringsConverter<TurnFooterItem>((footer, strings) => strings.FormatFooter(footer));

    /// <summary>
    /// (<see cref="DateTimeOffset"/>?, <see cref="ChatStrings"/>, the view's <see cref="ClaudeChatView.ShowTimestamps"/>) → the time over
    /// a message; null when unknown, or while the view hides the times.
    /// </summary>
    public static readonly IMultiValueConverter Time = new TimeConverter();

    /// <summary><see cref="DateTimeOffset"/>? → the whole date and time, as the tooltip of a message's time shows it; null when unknown.</summary>
    public static readonly IValueConverter FullTime = new FuncValueConverter<object?, string?>(value =>
        value is DateTimeOffset time ? ChatStrings.FormatFullTime(time) : null);

    public static readonly IValueConverter IsTurnFailed = new FuncValueConverter<NoticeKind, bool>(kind => kind == NoticeKind.TurnFailed);

    /// <summary>
    /// <see cref="ChatLayout"/> → whether it is <see cref="ChatLayout.Touch"/>. The templates bind it to the view's
    /// layout (<c>$parent[controls:ClaudeChatView].Layout</c>) to mark an element with the class <c>touch</c>, which
    /// the touch styles select.
    /// </summary>
    public static readonly IValueConverter IsTouch = new FuncValueConverter<ChatLayout, bool>(layout => layout == ChatLayout.Touch);

    /// <summary>How much of the list's width the user's bubble takes at most in the touch layout.</summary>
    public const double BubbleShare = 0.85;

    /// <summary>
    /// The list's width → the widest the user's bubble gets in the touch layout, <see cref="BubbleShare"/> of it, down
    /// to the pixel. No ceiling while the list has no width yet (before its first layout).
    /// </summary>
    public static readonly IValueConverter BubbleMaxWidth = new FuncValueConverter<double, double>(width =>
        width > 0 && !double.IsInfinity(width) ? Math.Floor(width * BubbleShare) : double.PositiveInfinity);

    /// <summary>How many lines the touch layout's reply box grows to; past them its text scrolls.</summary>
    public const int InputMaxLines = 6;

    /// <summary>
    /// The text box's line height → the tallest it grows in the touch layout, <see cref="InputMaxLines"/> lines of it; no
    /// ceiling without a line height.
    /// </summary>
    public static readonly IValueConverter InputMaxHeight = new FuncValueConverter<double, double>(lineHeight =>
        lineHeight > 0 && !double.IsInfinity(lineHeight) ? lineHeight * InputMaxLines : double.PositiveInfinity);

    public static readonly IValueConverter Clip = new FuncValueConverter<string?, string?>(text => ChatText.Clip(text, MaxTextChars));

    /// <summary>A tool's summary as its tooltip: none when empty, else cut like the texts of the list.</summary>
    public static readonly IValueConverter ToolTipText = new FuncValueConverter<string?, string?>(text =>
        string.IsNullOrEmpty(text) ? null : ChatText.ClipLines(text, ToolCallView.BoxLines, ToolCallView.MaxLineChars));

    private sealed class TimeConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count < 1 || !(values[0] is DateTimeOffset time)) return null;
            if (values.Count > 2 && values[2] is false) return null;
            var strings = values.Count > 1 ? values[1] as ChatStrings ?? ChatStrings.English : ChatStrings.English;
            return strings.FormatTime(time);
        }
    }

    /// <summary>A value and the view's <see cref="ChatStrings"/> turned into text; anything odd gives an empty text.</summary>
    private sealed class StringsConverter<T> : IMultiValueConverter
    {
        private readonly Func<T, ChatStrings, string> _convert;

        public StringsConverter(Func<T, ChatStrings, string> convert)
        {
            _convert = convert;
        }

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count < 2 || !(values[0] is T value)) return "";
            var strings = values[1] as ChatStrings ?? ChatStrings.English;
            return _convert(value, strings);
        }
    }
}

/// <summary>Text helpers of the view.</summary>
internal static class ChatText
{
    /// <summary>The text cut to <paramref name="maxChars"/> characters followed by "…", never inside a surrogate pair.</summary>
    public static string Clip(string? text, int maxChars)
    {
        if (text == null) return "";
        if (text.Length <= maxChars) return text;
        var cut = maxChars;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1])) cut--;
        return text.Substring(0, cut) + "…";
    }

    /// <summary>
    /// The first <paramref name="maxLines"/> lines of the text, each cut like <see cref="Clip"/> to
    /// <paramref name="maxLineChars"/> characters; a "\r" before a line feed is left out. Only the start of the text is
    /// read, so a heredoc of thousands of lines costs what its first lines cost.
    /// </summary>
    public static string ClipLines(string? text, int maxLines, int maxLineChars)
    {
        if (string.IsNullOrEmpty(text) || maxLines <= 0) return "";
        var result = new StringBuilder();
        var start = 0;
        for (var line = 0; line < maxLines; line++)
        {
            var end = text!.IndexOf('\n', start);
            var last = end < 0;
            var length = (last ? text.Length : end) - start;
            if (!last && length > 0 && text[end - 1] == '\r') length--;
            if (line > 0) result.Append('\n');
            if (length > maxLineChars)
            {
                var cut = maxLineChars;
                if (cut > 0 && char.IsHighSurrogate(text[start + cut - 1])) cut--;
                result.Append(text, start, cut).Append('…');
            }
            else
            {
                result.Append(text, start, length);
            }
            if (last) break;
            start = end + 1;
        }
        return result.ToString();
    }
}
