using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ClaudeVisualChat.Tests.Ui;
using Xunit;
using Xunit.Abstractions;
using AssistantBlock = ClaudeVisualChat.Protocol.AssistantBlock;
using PermissionRequested = ClaudeVisualChat.Protocol.PermissionRequested;
using ToolUseBlock = ClaudeVisualChat.Protocol.ToolUseBlock;

namespace ClaudeVisualChat.Tests;

/// <summary>
/// The tests that measure time run alone, after the others: run beside the rest of the suite, they measure the
/// machine's load as much as the code.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "Timing";
}

/// <summary>A long conversation (3,000 items) loads and opens fast, and so do huge commands and written files.</summary>
[Collection(TimingCollection.Name)]
public class TimingTests
{
    private readonly ITestOutputHelper _output;

    public TimingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Long_transcript_loads_fast()
    {
        var oneRound = FixtureFiles.Scenarios.SelectMany(FixtureFiles.TranscriptLines).ToList();
        var warmUp = new ChatConversation();
        warmUp.LoadTranscript(oneRound);
        var perRound = warmUp.Items.Count;
        Assert.True(perRound > 0);

        var rounds = (3000 + perRound - 1) / perRound;
        var lines = Enumerable.Repeat(oneRound, rounds).SelectMany(l => l).ToList();
        var c = new ChatConversation();

        var watch = Stopwatch.StartNew();
        c.LoadTranscript(lines);
        watch.Stop();
        _output.WriteLine($"{c.Items.Count} items: {watch.ElapsedMilliseconds} ms");

        Assert.True(c.Items.Count >= 3000, $"only {c.Items.Count} items");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"took {watch.ElapsedMilliseconds} ms");
    }

    [AvaloniaFact]
    public void Long_conversation_opens_fast()
    {
        var oneRound = FixtureFiles.Scenarios.SelectMany(FixtureFiles.TranscriptLines).ToList();
        var round = new ChatConversation();
        round.LoadTranscript(oneRound);
        UiHelpers.Show(new ClaudeChatView { Conversation = round }).Close(); // warm up the templates

        var rounds = (3000 + round.Items.Count - 1) / round.Items.Count;
        var c = new ChatConversation();
        c.LoadTranscript(Enumerable.Repeat(oneRound, rounds).SelectMany(l => l));
        Assert.True(c.Items.Count >= 3000, $"only {c.Items.Count} items");

        var view = new ClaudeChatView();
        var list = UiHelpers.ItemList(view);
        var prepared = 0;
        list.ContainerPrepared += (_, _) => prepared++;
        var window = new Window { Width = UiHelpers.Width, Height = UiHelpers.Height, Content = view };

        var watch = Stopwatch.StartNew();
        view.Conversation = c;
        window.Show();
        UiHelpers.Pump();
        watch.Stop();
        _output.WriteLine($"{c.Items.Count} items: {watch.ElapsedMilliseconds} ms, {prepared} containers prepared");

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.ElapsedMilliseconds} ms");
        Assert.True(prepared < 200, $"{prepared} containers prepared");
        Assert.NotNull(list.ContainerFromIndex(c.Items.Count - 1)); // it opens at the end
        var scroller = UiHelpers.Scroller(view);
        Assert.True(UiHelpers.AtEnd(scroller));

        // the Home and End keys: with an infinite offset the 11.0 list used to loop for ever on its estimates
        scroller.ScrollToHome();
        UiHelpers.Pump();
        Assert.NotNull(list.ContainerFromIndex(0));
        scroller.ScrollToEnd();
        UiHelpers.Pump();
        Assert.True(UiHelpers.AtEnd(scroller));
        Assert.NotNull(list.ContainerFromIndex(c.Items.Count - 1));
        window.Close();
    }

    private static JsonElement J(object value)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return doc.RootElement.Clone();
    }

    /// <summary>A heredoc of 5,000 lines, a command of one line of 300,000 characters and a Write of such a line.</summary>
    private static ChatConversation HugeInputs(string heredoc, string wide)
    {
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "Bash", J(new { command = heredoc, description = "Write a big file" }))));
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t2", "Bash", J(new { command = "echo " + wide }))));
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t3", "Write", J(new { file_path = "min.js", content = wide }))));
        return c;
    }

    [AvaloniaFact]
    public void Huge_command_and_written_line_open_fast_in_the_list()
    {
        UiHelpers.Show(new ClaudeChatView { Conversation = HugeInputs("cat <<'EOF'\na\nEOF", "x") }).Close(); // warm up

        var heredoc = "cat > big.txt <<'EOF'\n" + string.Join("\n", Enumerable.Range(1, 5000).Select(i => "line " + i)) + "\nEOF";
        var wide = new string('x', 300_000);
        var c = HugeInputs(heredoc, wide);
        var view = new ClaudeChatView { Conversation = c };
        var window = new Window { Width = UiHelpers.Width, Height = UiHelpers.Height, Content = view };

        var watch = Stopwatch.StartNew();
        window.Show();
        UiHelpers.Pump();
        watch.Stop();
        _output.WriteLine($"a heredoc of 5,000 lines and two lines of 300,000 characters: {watch.ElapsedMilliseconds} ms");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"took {watch.ElapsedMilliseconds} ms");

        // the list keeps about the eight lines its boxes take, each cut like a diff line; the tooltips too
        var most = 8 * (ToolCallView.MaxLineChars + 2);
        var rows = view.GetVisualDescendants().OfType<ToolCallView>().ToList();
        Assert.Equal(3, rows.Count);
        var blocks = rows.SelectMany(r => r.GetVisualDescendants().OfType<TextBlock>()).ToList();
        var longest = blocks.Max(t => (t.Text ?? "").Length);
        Assert.True(longest <= most, $"a text of {longest} characters");
        var tips = rows.SelectMany(r => r.GetVisualDescendants().OfType<Control>()).Select(ToolTip.GetTip).OfType<string>().ToList();
        Assert.NotEmpty(tips);
        Assert.All(tips, tip => Assert.True(tip.Length <= most, $"a tooltip of {tip.Length} characters"));
        var texts = UiHelpers.Texts(view);
        Assert.Contains(texts, t => t.StartsWith("cat > big.txt <<'EOF'\nline 1\nline 2\n", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.Contains("line 5000"));

        // the request still shows the whole command, down to its end
        c.Apply(new PermissionRequested("r1", "Bash", "Bash", null, "t1", J(new { command = heredoc }), null));
        UiHelpers.Pump();
        var prompt = UiHelpers.Prompt(view);
        Assert.True(prompt.IsEffectivelyVisible);
        Assert.Contains(heredoc, UiHelpers.Texts(prompt));
        Assert.Contains(UiHelpers.Texts(prompt), t => t.EndsWith("line 5000\nEOF", StringComparison.Ordinal));
        window.Close();
    }
}
