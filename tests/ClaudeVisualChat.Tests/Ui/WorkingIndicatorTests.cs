using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>The working line at the end of the list: the star, the verbs and the states.</summary>
public class WorkingIndicatorTests
{
    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static WorkingIndicator Row(ClaudeChatView view) =>
        view.GetVisualDescendants().OfType<WorkingIndicator>().Single();

    private static TextBlock Glyph(ClaudeChatView view) =>
        Row(view).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("ClaudeChatWorkingGlyph"));

    private static (ClaudeChatView View, Window Window) Open(ChatConversation c, ChatLayout layout, ChatStrings? strings = null)
    {
        var view = new ClaudeChatView { Conversation = c, Layout = layout };
        if (strings != null) view.Strings = strings;
        return (view, layout == ChatLayout.Touch ? UiHelpers.ShowPhone(view) : UiHelpers.Show(view));
    }

    private static object? Resource(Control view, string key) =>
        view.TryFindResource(key, out var value) ? value : null;

    private static void WithClock(long start, Action<Action<long>> body)
    {
        var now = start;
        TokenCountUp.ClockOverride = () => now;
        try { body(by => now += by); }
        finally { TokenCountUp.ClockOverride = null; }
    }

    [AvaloniaTheory]
    [InlineData(ChatLayout.Desktop)]
    [InlineData(ChatLayout.Touch)]
    public void The_working_row_shows_a_verb_with_three_dots(ChatLayout layout)
    {
        WithClock(10_000, advance =>
        {
            var c = new ChatConversation();
            var (view, window) = Open(c, layout);
            Assert.False(Row(view).IsEffectivelyVisible);

            c.Submit("go");
            UiHelpers.Pump();
            var row = Row(view);
            Assert.True(row.IsEffectivelyVisible);
            Assert.Contains(row.Verb, ChatStrings.English.WorkingVerbs);
            Assert.Equal(row.Verb + "...", row.FullText);
            Assert.StartsWith("|", row.Shown);                    // nothing written yet: the cursor, and "." or "_" over the next three
            Assert.Equal(4, row.Shown.Length);
            Assert.All(row.Shown.Skip(1), ch => Assert.Contains(ch, "._"));

            advance(RevealMs(row.FullText) / 2);                   // half way: the first half written, then the cursor
            row.Tick();
            var half = row.FullText.Length / 2;
            Assert.StartsWith(row.FullText.Substring(0, half) + "|", row.Shown);

            advance(RevealMs(row.FullText));
            row.Tick();
            Assert.Equal(row.Verb + "...", row.Shown);
            Assert.Contains(row.Shown, UiHelpers.Texts(row));
            Assert.Equal("Claude is working", AutomationProperties.GetName(row));

            // the words come after the star, on the same line
            var words = row.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("ClaudeChatWorkingText"));
            Assert.True(words.TranslatePoint(default, view)!.Value.X >= Glyph(view).TranslatePoint(default, view)!.Value.X + Glyph(view).Bounds.Width - 1);
            window.Close();
        });
    }

    private static long RevealMs(string text) => text.Length * WorkingIndicator.RevealStepMs;

    [AvaloniaFact]
    public void The_star_goes_forward_and_back_over_twelve_frames_and_stays_in_one_place_for_the_words()
    {
        var c = new ChatConversation();
        c.Submit("go");
        var (view, window) = Open(c, ChatLayout.Desktop);
        var row = Row(view);
        var seen = new List<string> { row.Glyph };
        for (var i = 0; i < 12; i++)
        {
            row.StepGlyph();
            seen.Add(row.Glyph);
        }
        Assert.Equal(new[] { "·", "✢", "✳", "✶", "✻", "✽", "✽", "✻", "✶", "✳", "✢", "·", "·" }, seen);
        Assert.Equal(6, seen.Distinct().Count());
        window.Close();
    }

    [AvaloniaFact]
    public void Verbs_change_after_two_three_and_then_five_seconds()
    {
        WithClock(10_000, advance =>
        {
            var c = new ChatConversation();
            c.Submit("go");
            var (view, window) = Open(c, ChatLayout.Desktop);
            var row = Row(view);

            string Step(long ms)
            {
                advance(ms);
                row.Tick();
                return row.Verb;
            }

            var first = row.Verb;
            Assert.Equal(first, Step(1999));                    // not yet
            var second = Step(1);                               // 2 s
            Assert.NotEqual(first, second);
            Assert.Equal(second, Step(2999));
            var third = Step(1);                                // 3 s after the last
            Assert.NotEqual(second, third);
            Assert.Equal(third, Step(4999));
            var fourth = Step(1);                               // then 5 s
            Assert.NotEqual(third, fourth);
            Assert.Equal(fourth, Step(4999));
            Assert.NotEqual(fourth, Step(1));                   // and every 5 s
            Assert.Contains(row.Verb, ChatStrings.English.WorkingVerbs);
            Assert.Equal(new long[] { 2000, 3000, 5000, 5000 }, Enumerable.Range(0, 4).Select(WorkingIndicator.DelayMs).ToArray());
            window.Close();
        });
    }

    [AvaloniaFact]
    public void Compacting_status_shows_compacting_dots()
    {
        WithClock(10_000, advance =>
        {
            var c = new ChatConversation();
            c.Submit("/compact");
            var (view, window) = Open(c, ChatLayout.Desktop);
            c.Apply(new StatusChanged("compacting", null));
            UiHelpers.Pump();
            var row = Row(view);
            Assert.True(row.IsEffectivelyVisible);
            Assert.Equal("Compacting...", row.FullText);
            Assert.Equal("Compacting conversation", AutomationProperties.GetName(row));
            advance(RevealMs(row.FullText));
            row.Tick();
            Assert.Equal("Compacting...", row.Shown);

            view.Strings = ChatStrings.PortugueseBrazil;
            UiHelpers.Pump();
            Assert.Equal("Compactando...", row.FullText);
            Assert.Equal("Compactando a conversa", AutomationProperties.GetName(row));

            c.Apply(new StatusChanged(null, null));              // the claude is done: back to the verbs
            UiHelpers.Pump();
            Assert.Equal(row.Verb + "...", row.FullText);
            Assert.Equal("O Claude está trabalhando", AutomationProperties.GetName(row));
            window.Close();
        });
    }

    [AvaloniaFact]
    public void An_unfinished_session_start_hook_shows_running_start_up_hooks()
    {
        WithClock(10_000, advance =>
        {
            var c = new ChatConversation();
            var (view, window) = Open(c, ChatLayout.Desktop);
            var row = Row(view);
            Assert.False(row.IsEffectivelyVisible);

            c.Apply(new HookStarted("h0", "PreToolUse"));         // a hook of the turn is no start-up hook
            UiHelpers.Pump();
            Assert.False(row.IsEffectivelyVisible);

            c.Apply(new HookStarted("h1", "SessionStart"));       // it shows even before the first message
            c.Apply(new HookStarted("h2", "Setup"));
            UiHelpers.Pump();
            Assert.True(row.IsEffectivelyVisible);
            Assert.Equal("Running start-up hooks...", row.FullText);
            advance(RevealMs(row.FullText));
            row.Tick();
            Assert.Equal("Running start-up hooks...", row.Shown);

            c.Apply(new HookFinished("h1"));                       // one still runs
            UiHelpers.Pump();
            Assert.Equal("Running start-up hooks...", row.FullText);
            Assert.True(row.IsEffectivelyVisible);

            c.Apply(new HookFinished("h2"));
            UiHelpers.Pump();
            Assert.False(row.IsEffectivelyVisible);

            c.Submit("go");                                        // a message sent meanwhile: the verbs
            c.Apply(new HookStarted("h3", "SessionStart"));
            UiHelpers.Pump();
            Assert.Equal("Running start-up hooks...", row.FullText);
            c.Apply(new HookFinished("h3"));
            UiHelpers.Pump();
            Assert.Equal(row.Verb + "...", row.FullText);
            window.Close();
        });
    }

    [AvaloniaFact]
    public void Replace_with_custom_verbs_uses_only_them_and_an_empty_list_falls_back()
    {
        WithClock(10_000, advance =>
        {
            var c = new ChatConversation();
            c.Submit("go");
            var (view, window) = Open(c, ChatLayout.Desktop);
            var row = Row(view);
            var own = ChatStrings.English.WorkingVerbs;

            // appended (the default): ours and then the host's
            view.SpinnerVerbs = new[] { "Zigzagging", " ", "Zigzagging", "Noodling" };
            Assert.Equal(own.Concat(new[] { "Zigzagging", "Noodling" }), WorkingIndicator.PoolOf(view.SpinnerVerbs, view.SpinnerVerbsReplace, ChatStrings.English));

            // replaced: only the host's, and every change picks from them
            view.SpinnerVerbsReplace = true;
            UiHelpers.Pump();
            Assert.Contains(row.Verb, new[] { "Zigzagging", "Noodling" });
            var seen = new HashSet<string> { row.Verb };
            for (var i = 0; i < 10; i++)
            {
                advance(5000);
                row.Tick();
                seen.Add(row.Verb);
            }
            Assert.Equal(new[] { "Noodling", "Zigzagging" }, seen.OrderBy(x => x, StringComparer.Ordinal));

            // one verb: always it
            view.SpinnerVerbs = new[] { "Zigzagging" };
            UiHelpers.Pump();
            Assert.Equal("Zigzagging", row.Verb);
            advance(5000);
            row.Tick();
            Assert.Equal("Zigzagging", row.Verb);
            Assert.Equal("Zigzagging...", row.FullText);

            // replace with nothing (null, empty or blank): ours
            foreach (var none in new IReadOnlyList<string>?[] { null, new string[0], new[] { "", "  " } })
            {
                view.SpinnerVerbs = none;
                UiHelpers.Pump();
                Assert.Contains(row.Verb, own);
                Assert.Equal(own, WorkingIndicator.PoolOf(none, true, ChatStrings.English));
            }
            window.Close();
        });
    }

    [AvaloniaFact]
    public void Reduce_motion_keeps_the_glyph_still()
    {
        WithClock(10_000, advance =>
        {
            var c = new ChatConversation();
            c.Submit("go");
            var (view, window) = Open(c, ChatLayout.Desktop);
            var row = Row(view);
            Assert.True(row.IsGlyphMoving);
            row.StepGlyph();
            Assert.NotEqual("✻", row.Glyph);                       // it moves on its own

            view.ReduceMotion = true;                              // inherited by the line
            UiHelpers.Pump();
            Assert.False(row.IsGlyphMoving);
            Assert.Equal("✻", row.Glyph);
            Assert.Equal("✻", Glyph(view).Text);
            row.StepGlyph();                                       // a late tick keeps it still
            Assert.Equal("✻", row.Glyph);
            Assert.Equal(row.FullText, row.Shown);                 // words whole, with no cursor

            advance(2000);                                         // and swapped whole
            row.Tick();
            Assert.Equal(row.FullText, row.Shown);
            Assert.DoesNotContain("|", row.Shown);
            Assert.True(row.IsTicking);                            // the verbs still change

            view.ReduceMotion = false;
            UiHelpers.Pump();
            Assert.True(row.IsGlyphMoving);
            window.Close();
        });
    }

    [AvaloniaFact]
    public void The_working_row_hides_while_a_permission_card_shows()
    {
        var c = new ChatConversation();
        c.Submit("go");
        var (view, window) = Open(c, ChatLayout.Desktop);
        var row = Row(view);
        Assert.True(row.IsEffectivelyVisible);
        Assert.Contains("on", row.Classes);

        c.Apply(new PermissionRequested("r1", "Bash", "Bash", null, "t1", J("{\"command\":\"ls\"}"), null));
        UiHelpers.Pump();
        Assert.False(row.IsEffectivelyVisible);
        Assert.False(row.IsGlyphMoving);                           // hidden: the timers rest
        Assert.False(row.IsTicking);

        c.Apply(new PermissionCancelled("r1"));
        UiHelpers.Pump();
        Assert.True(row.IsEffectivelyVisible);
        Assert.True(row.IsGlyphMoving);
        window.Close();
    }

    [AvaloniaFact]
    public void The_star_takes_the_color_of_the_mode()
    {
        var c = new ChatConversation();
        c.Submit("go");
        var (view, window) = Open(c, ChatLayout.Desktop);
        var glyph = Glyph(view);
        Assert.Same(Resource(view, "ClaudeChat.Spinner"), glyph.Foreground);
        foreach (var (mode, key) in new[] { ("acceptEdits", "ClaudeChat.Foreground"), ("plan", "ClaudeChat.AccentFill"), ("auto", "ClaudeChat.Error"), ("default", "ClaudeChat.Spinner") })
        {
            c.Apply(new StatusChanged(null, mode));
            UiHelpers.Pump();
            Assert.Same(Resource(view, key), glyph.Foreground);
        }
        window.Close();
    }

    [AvaloniaFact]
    public void The_working_line_is_1_85_em_high_with_a_gap_of_4_px_over_it()
    {
        var c = new ChatConversation();
        c.Submit("go");
        var (view, window) = Open(c, ChatLayout.Desktop);
        var row = Row(view);
        Assert.Equal(new Thickness(0, 4, 0, 0), row.Margin);
        var size = (double)Resource(view, "ClaudeChat.FontSize")!;
        Assert.InRange(row.Bounds.Height, size * 1.85 - 1, size * 1.85 + 1);
        window.Close();
    }

    [AvaloniaFact]
    public void The_view_adds_the_hosts_verbs_and_moves_by_default()
    {
        var view = new ClaudeChatView();
        Assert.Null(view.SpinnerVerbs);
        Assert.False(view.SpinnerVerbsReplace);
        Assert.False(view.ReduceMotion);
    }

    [AvaloniaFact]
    public void The_word_lists_are_short_and_differ_between_the_languages()
    {
        var en = ChatStrings.English.WorkingVerbs;
        var pt = ChatStrings.PortugueseBrazil.WorkingVerbs;
        Assert.InRange(en.Count, 12, 20);
        Assert.InRange(pt.Count, 12, 20);
        Assert.Equal(en.Count, en.Distinct().Count());
        Assert.Equal(pt.Count, pt.Distinct().Count());
        Assert.Empty(en.Intersect(pt));
        Assert.All(en.Concat(pt), verb => Assert.False(string.IsNullOrWhiteSpace(verb)));
    }
}
