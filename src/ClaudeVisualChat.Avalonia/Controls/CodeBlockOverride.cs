using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Markdown.Avalonia;
using Markdown.Avalonia.Parsers;
using Markdown.Avalonia.Plugins;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The code blocks of a markdown text (fenced and indented) as the engine draws them, a <c>Border.CodeBlock</c> around a
/// sideways-scrolling <c>TextBlock.CodeBlock</c>, plus a "Copy code" button over the top right corner that does not scroll with
/// the code. There is no highlighting (the extension has none): the code is in the text color. It replaces the engine's own
/// converters, so it does their work again, the closing fence and the block still open while the answer arrives; it keeps no
/// state, because the engine is shared by every block.
/// </summary>
internal sealed class CodeBlockOverride : IBlockOverride
{
    /// <summary>The engine's parsers this replaces: a fenced block (any language) and an indented one.</summary>
    internal const string FencedParser = "CodeBlocksWithLangEvaluator";

    internal const string IndentedParser = "CodeBlocksWithoutLangEvaluator";

    private static readonly Regex NewlinesLeadingTrailing = new Regex("^\\n+|\\n+\\z", RegexOptions.Compiled);

    private readonly bool _fenced;

    private CodeBlockOverride(bool fenced)
    {
        _fenced = fenced;
    }

    /// <summary>Puts the override of each of the two parsers in the engine.</summary>
    internal sealed class Plugin : IMdAvPlugin
    {
        public void Setup(SetupInfo info)
        {
            info.Register(new CodeBlockOverride(true));
            info.Register(new CodeBlockOverride(false));
        }
    }

    public string ParserName => _fenced ? FencedParser : IndentedParser;

    public IEnumerable<Control>? Convert(string text, Match firstMatch, ParseStatus status, IMarkdownEngine engine,
        out int parseTextBegin, out int parseTextEnd)
    {
        if (!_fenced)
        {
            // the indented block: its lines without the four spaces, and no empty lines around
            parseTextBegin = firstMatch.Index;
            parseTextEnd = firstMatch.Index + firstMatch.Length;
            var lines = firstMatch.Groups[1].Value.Split('\n').Select(Dedent);
            return new Control[] { CodeBlock(NewlinesLeadingTrailing.Replace(string.Join("\n", lines), "")) };
        }

        var start = firstMatch.Index + firstMatch.Length;
        var closing = new Regex("\n[ ]*" + Regex.Escape(firstMatch.Groups[1].Value) + "[ ]*\n").Match(text, start);
        int end;
        if (closing.Success)
        {
            end = closing.Index;
            parseTextEnd = closing.Index + closing.Length;
        }
        else
        {
            // no closing fence yet: while the answer arrives the block is code to the end, if the engine is set up so
            if (!engine.Plugins.Info.EnablePreRenderingCodeBlock)
            {
                parseTextBegin = parseTextEnd = -1;
                return null;
            }
            end = text.Length;
            parseTextEnd = text.Length;
        }
        parseTextBegin = firstMatch.Index;
        return new Control[] { CodeBlock(text.Substring(start, end - start)) };
    }

    /// <summary>Up to four columns of indent off the line (a tab counts to the next multiple of four).</summary>
    private static string Dedent(string line)
    {
        int at = 0, column = 0;
        while (column < 4 && at < line.Length)
        {
            if (line[at] == ' ')
            {
                at++;
                column++;
            }
            else if (line[at] == '\t')
            {
                at++;
                column = ((column >> 2) + 1) << 2;
            }
            else break;
        }
        return line.Substring(at);
    }

    /// <summary>The block the engine draws for <paramref name="code"/>, with the copy button over it.</summary>
    private static Border CodeBlock(string code)
    {
        var text = new TextBlock { Text = code, TextWrapping = Avalonia.Media.TextWrapping.NoWrap };
        text.Classes.Add("CodeBlock");
        var scroller = new ScrollViewer { Content = text, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        scroller.Classes.Add("CodeBlock");
        var block = new Border { Child = new Grid { Children = { scroller, new CodeCopyButton(code) } } };
        block.Classes.Add("CodeBlock");
        return block;
    }
}

/// <summary>Switches on the code block that is still open (no closing fence yet) while an answer arrives, which the engine leaves off.</summary>
internal sealed class PreRenderCodeBlocks : IMdAvPlugin
{
    public void Setup(SetupInfo info) => info.EnablePreRenderingCodeBlock = true;
}

/// <summary>
/// The "Copy code" button of a code block: copies the block's text through <see cref="ChatClipboard"/>, and for 2 s after a copy
/// its icon is a check. A copy the clipboard refused shows nothing. The check lives in the button, which the block's drawing makes
/// anew, so a block drawn again for another text never shows a stale one. It follows the 0.3.1 rule: while the view is out of the
/// window the timer is stopped and nothing is drawn; back, the button is a copy icon again.
/// </summary>
internal sealed class CodeCopyButton : Button
{
    /// <summary>How long the check shows after a copy.</summary>
    private static readonly TimeSpan CopiedFor = TimeSpan.FromSeconds(2);

    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<CodeCopyButton>();

    private readonly string _code;
    private readonly Path _icon = new Path();
    private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = CopiedFor };
    private bool _attached;
    private bool _copied;

    public CodeCopyButton(string code)
    {
        _code = code;
        _icon.Classes.Add("ClaudeChatCopyIcon");
        var box = new Viewbox { Child = _icon };
        box.Classes.Add("ClaudeChatCopyCodeIcon");
        Content = box;
        Classes.Add("ClaudeChatButton");
        Classes.Add("ClaudeChatCopyCode");
        _timer.Tick += (_, _) => EndCopied();
        Update();
    }

    /// <summary>It is a <see cref="Button"/> to the theme's styles.</summary>
    protected override Type StyleKeyOverride => typeof(Button);

    /// <summary>True from a copy until the check goes away.</summary>
    internal bool IsCopied => _copied;

    /// <summary>True while the timer waits to take the check away.</summary>
    internal bool IsTicking => _timer.IsEnabled;

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Update();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _timer.Stop();
        _copied = false; // drawn again, as a copy icon, when it comes back
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty && _attached) Update();
    }

    protected override void OnClick()
    {
        base.OnClick();
        _ = CopyAsync();
    }

    private async Task CopyAsync()
    {
        if (!await ChatClipboard.SetTextAsync(this, _code)) return; // refused: nothing was copied, so nothing is confirmed
        if (!_attached) return;                                      // the view left meanwhile
        _copied = true;
        Update();
        _timer.Stop();
        _timer.Start(); // a second copy gives the check another 2 s
    }

    /// <summary>The check goes away (its timer's tick, 2 s after a copy): the copy icon and its name come back.</summary>
    internal void EndCopied()
    {
        _timer.Stop();
        if (!_copied) return;
        _copied = false;
        Update();
    }

    private void Update()
    {
        var strings = Strings;
        _icon.Classes.Set("copied", _copied);
        AutomationProperties.SetName(this, _copied ? strings.Copied : strings.CopyCodeToClipboard);
        ToolTip.SetTip(this, strings.CopyCode);
    }
}
