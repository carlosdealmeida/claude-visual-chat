using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using ClaudeVisualChat.Controls;
using Xunit;
using PermissionRequested = ClaudeVisualChat.Protocol.PermissionRequested;

namespace ClaudeVisualChat.Tests;

/// <summary>
/// The Focus view's list: each run of steps of a round (calls, thoughts) is one fold, the prompts, Claude's text and the latest TodoWrite
/// stay, and it follows the conversation one row at a time, without reading it whole again.
/// </summary>
public class FocusProjectionTests
{
    private static readonly ChatStrings En = ChatStrings.English;

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public FocusProjectionTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static ToolCallItem Call(string name = "Bash", ToolStatus status = ToolStatus.Succeeded, string id = "") =>
        new ToolCallItem { Name = name, Status = status, ToolUseId = id };

    private static TurnFooterItem Footer() => new TurnFooterItem(1000, null, null, null, false);

    /// <summary>A projection started over the conversation, and every change it tells.</summary>
    private sealed class Followed
    {
        public Followed(ChatConversation conversation)
        {
            Conversation = conversation;
            List = new FocusProjection(conversation);
            List.CollectionChanged += (_, e) => Changes.Add(e);
            List.Start();
            Changes.Clear(); // the Reset of the start
        }

        public ChatConversation Conversation { get; }

        public FocusProjection List { get; }

        public List<NotifyCollectionChangedEventArgs> Changes { get; } = new List<NotifyCollectionChangedEventArgs>();

        public IList<ChatItem> Items => Conversation.Items;

        public List<object> Rows => List.ToList();

        public FocusFoldItem Fold(int row) => Assert.IsType<FocusFoldItem>(List[row]);

        public int Resets => Changes.Count(e => e.Action == NotifyCollectionChangedAction.Reset);
    }

    /// <summary>The rows a projection started now makes of the conversation (it lets go of it at once).</summary>
    private static List<string> Fresh(ChatConversation c)
    {
        var fresh = new FocusProjection(c);
        fresh.Start();
        fresh.Stop();
        return Shape(fresh, c.Items);
    }

    private static Followed Follow(params ChatItem[] items)
    {
        var c = new ChatConversation();
        foreach (var item in items) c.Items.Add(item);
        return new Followed(c);
    }

    /// <summary>The rows as a fresh projection reads them: an item, or "[…]" with the steps of a fold.</summary>
    private static List<string> Shape(IEnumerable<object> rows, IList<ChatItem> items)
    {
        string Name(ChatItem item) => items.IndexOf(item).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return rows.Select(r => r is FocusFoldItem f ? "[" + string.Join(",", f.Items.Select(Name)) + "]" : Name((ChatItem)r)).ToList();
    }

    [Fact]
    public void A_run_of_tool_calls_folds_into_one_item_with_the_count_and_failures()
    {
        var prompt = new UserMessageItem("fix it");
        var answer = new AssistantTextItem { Text = "Done." };
        var footer = Footer();
        var f = Follow(prompt, Call("Read"), Call("Bash", ToolStatus.Failed), Call("Edit"), answer, footer);

        Assert.Equal(4, f.List.Count);
        Assert.Same(prompt, f.List[0]);
        var fold = f.Fold(1);
        Assert.Equal(3, fold.Items.Count);
        Assert.Same(answer, f.List[2]);
        Assert.Same(footer, f.List[3]);
        Assert.Equal("3 tool calls · 1 failed", En.FormatFocusLabel(fold.State));
        Assert.True(fold.State.HasFailed);   // its dot is red
        Assert.False(fold.State.IsLive);
        Assert.False(fold.IsExpanded);

        // one call, and the Portuguese
        var one = Follow(new UserMessageItem("a"), Call("Read"), new AssistantTextItem { Text = "b" });
        Assert.Equal("1 tool call", En.FormatFocusLabel(one.Fold(1).State));
        Assert.Equal("3 chamadas de ferramenta · 1 com falha", ChatStrings.PortugueseBrazil.FormatFocusLabel(fold.State));

        // a run of calls and thoughts is "hidden steps"; text cuts a round in two runs
        var mixed = Follow(new UserMessageItem("a"), new ThinkingItem { DurationMs = 2000 }, Call("Read"), new AssistantTextItem { Text = "x" },
            Call("Bash", ToolStatus.Failed), Call("Bash"));
        Assert.Equal(4, mixed.List.Count);
        Assert.Equal("2 hidden steps", En.FormatFocusLabel(mixed.Fold(1).State));
        Assert.Equal("2 tool calls · 1 failed", En.FormatFocusLabel(mixed.Fold(3).State));

        // while one runs, the live label, and the count after it
        var running = Follow(new UserMessageItem("a"), Call("Read"), Call("Bash", ToolStatus.Running));
        var live = running.Fold(1).State;
        Assert.True(live.IsLive);
        Assert.True(live.HasLiveLabel);
        Assert.Equal("Running Bash…", En.FormatFocusLabel(live));
        Assert.Equal("2 tool calls", En.FormatFocusCount(live));
        ((ToolCallItem)running.Items[2]).Status = ToolStatus.Succeeded;
        Assert.False(running.Fold(1).State.IsLive);
        Assert.Equal("2 tool calls", En.FormatFocusLabel(running.Fold(1).State));
    }

    [Fact]
    public void Prompts_text_and_the_latest_todo_list_stay_visible()
    {
        var prompt = new UserMessageItem("plan it");
        var firstTodo = Call("TodoWrite");
        var text = new AssistantTextItem { Text = "On it." };
        var secondTodo = Call("TodoWrite");
        var f = Follow(prompt, firstTodo, Call("Read"), text, secondTodo, Call("Bash"));

        // the earlier list folds with its run; the latest stays, and cuts its run
        Assert.Equal(new[] { "0", "[1,2]", "3", "4", "[5]" }, Shape(f.Rows, f.Items));
        var foldAfter = f.Fold(4);
        foldAfter.IsExpanded = true;

        // a newer list: the one before folds into the run after it, whose fold stays the same (and open)
        var thirdTodo = Call("TodoWrite");
        f.Items.Add(thirdTodo);
        Assert.Equal(new[] { "0", "[1,2]", "3", "[4,5]", "6" }, Shape(f.Rows, f.Items));
        Assert.Same(foldAfter, f.List[3]);
        Assert.True(foldAfter.IsExpanded);
        Assert.Equal(0, f.Resets);

        // a background agent's call stays too
        var agent = Call("Agent", ToolStatus.Running);
        agent.Input = J("{\"description\":\"Watch\",\"run_in_background\":true}");
        f.Items.Add(agent);
        f.Items.Add(Call("Read"));
        Assert.Equal(new[] { "0", "[1,2]", "3", "[4,5]", "6", "7", "[8]" }, Shape(f.Rows, f.Items));
        Assert.Equal(0, f.Resets);
    }

    [Fact]
    public void A_thinking_only_run_reads_thought_for_n_seconds()
    {
        var thought = new ThinkingItem { Text = "hmm", IsStreaming = true };
        var f = Follow(new UserMessageItem("why?"), thought);
        var fold = f.Fold(1);
        Assert.Equal("Thinking…", En.FormatFocusLabel(fold.State));
        Assert.True(fold.State.IsLive);   // its dot blinks while the thought arrives

        thought.IsStreaming = false;
        thought.DurationMs = 3400;
        Assert.Equal("Thought for 3s", En.FormatFocusLabel(fold.State));
        Assert.False(fold.State.IsLive);

        thought.DurationMs = 200;   // at least 1 s
        Assert.Equal("Thought for 1s", En.FormatFocusLabel(fold.State));

        // two thoughts together; one whose length is unknown makes it "Thinking"
        var second = new ThinkingItem { DurationMs = 2600 };
        f.Items.Add(second);
        Assert.Same(fold, f.List[1]);
        Assert.Equal("Thought for 3s", En.FormatFocusLabel(fold.State));
        second.DurationMs = null;
        Assert.Equal("Thinking", En.FormatFocusLabel(fold.State));
        Assert.Equal("Pensando", ChatStrings.PortugueseBrazil.FormatFocusLabel(fold.State));
    }

    [Fact]
    public void A_waiting_agent_opens_its_fold_and_reads_waiting_for_permission()
    {
        var info = new AgentInfo("toolu_a") { Description = "Look around", Status = AgentStatus.Running, Activity = "Reading a.txt",
            Tokens = 1200, ToolUses = 3, DurationMs = 65_000 };
        var agent = Call("Agent", ToolStatus.Running, "toolu_a");
        agent.Agent = info;
        var f = Follow(new UserMessageItem("look"), Call("Read"), agent);
        var fold = f.Fold(1);
        Assert.False(fold.IsExpanded);
        Assert.Equal("Running Agent…", En.FormatFocusLabel(fold.State));

        // the agent at work is a row under the fold: its description and what it does, then its numbers
        var row = Assert.Single(fold.State.Agents);
        Assert.Equal("Look around: Reading a.txt", row.Line);
        Assert.Equal("1.2k tokens · 3 tools · 1m 5s", En.FormatFocusAgentStats(row));

        info.IsWaitingForPermission = true;
        Assert.True(fold.IsExpanded);   // it opens by itself
        Assert.Equal("Waiting for permission…", En.FormatFocusLabel(fold.State));
        Assert.Equal("2 tool calls", En.FormatFocusCount(fold.State));

        // the user closes it; it stays closed while that request waits, and opens for the next one
        fold.IsExpanded = false;
        info.Activity = "Reading b.txt";
        Assert.False(fold.IsExpanded);
        info.IsWaitingForPermission = false;
        info.IsWaitingForPermission = true;
        Assert.True(fold.IsExpanded);

        // a request of the main agent's own call does the same; a question waits for an answer and opens nothing
        var c = new ChatConversation();
        c.Items.Add(new UserMessageItem("go"));
        c.Items.Add(Call("Bash", ToolStatus.Running, "t1"));
        var main = new Followed(c);
        c.Apply(new PermissionRequested("r1", "Bash", "Bash", null, "t1", J("{\"command\":\"ls\"}"), null));
        Assert.True(main.Fold(1).IsExpanded);
        Assert.Equal("Waiting for permission…", En.FormatFocusLabel(main.Fold(1).State));
        c.Items.Add(new AssistantTextItem { Text = "x" });
        c.Items.Add(Call("AskUserQuestion", ToolStatus.Running, "t2"));
        c.Apply(new PermissionRequested("r2", "AskUserQuestion", "AskUserQuestion", null, "t2", J("{}"), null));
        Assert.Equal("Waiting for your answer…", En.FormatFocusLabel(main.Fold(3).State));
        Assert.False(main.Fold(3).IsExpanded);

        // more than four agents at work: three rows, and the others counted
        var many = new List<ChatItem> { new UserMessageItem("fan out") };
        for (var i = 0; i < 6; i++)
            many.Add(new ToolCallItem { Name = "Agent", Status = ToolStatus.Running, Summary = "Agent " + i });
        var fanned = Follow(many.ToArray());
        Assert.Equal(3, fanned.Fold(1).State.Agents.Count);
        Assert.Equal(3, fanned.Fold(1).State.MoreAgents);
        Assert.Equal("+3 more agents", En.FormatFocusMoreAgents(fanned.Fold(1).State.MoreAgents));
        Assert.Equal("Agent 0", fanned.Fold(1).State.Agents[0].Line);
    }

    [Fact]
    public void Appending_to_three_thousand_items_does_not_rebuild_the_projection()
    {
        // 3,000 items in rounds of a prompt, eight steps, an answer and a footer
        var c = new ChatConversation();
        void Round(int steps)
        {
            c.Items.Add(new UserMessageItem("q"));
            for (var i = 0; i < steps; i++) c.Items.Add(i % 3 == 0 ? new ThinkingItem { DurationMs = 1000 } : (ChatItem)Call("Read"));
            c.Items.Add(new AssistantTextItem { Text = "a" });
            c.Items.Add(Footer());
        }
        while (c.Items.Count < 3000) Round(8);
        var f = new Followed(c);
        var rowsBefore = f.List.Count;

        // 3,000 more, as they come; then one round of 2,000 steps (its fold only grows)
        var clock = Stopwatch.StartNew();
        for (var r = 0; r < 3000 / 11; r++) Round(8);
        c.Items.Add(new UserMessageItem("long"));
        for (var i = 0; i < 2000; i++) c.Items.Add(Call("Bash"));
        clock.Stop();

        Assert.Equal(0, f.Resets);
        Assert.All(f.Changes, e => Assert.Equal(NotifyCollectionChangedAction.Add, e.Action));
        Assert.All(f.Changes, e => Assert.Single(e.NewItems!));
        Assert.Equal(rowsBefore + 3000 / 11 * 4 + 2, f.List.Count);   // a prompt, a fold, an answer and a footer per round
        Assert.Equal(2000, f.Fold(f.List.Count - 1).Items.Count);
        Assert.Equal(Fresh(c), Shape(f.Rows, c.Items));
        // 5,000 items in some milliseconds: each one costs the same, however long the list (the budget is for a busy machine)
        _output.WriteLine("5,000 appends after 3,003 items: " + clock.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms");
        Assert.True(clock.ElapsedMilliseconds < 1500, clock.ElapsedMilliseconds + " ms");

        // a line put in the middle cuts one fold in two: the rows around it only, told one at a time
        f.Changes.Clear();
        var middle = c.Items.IndexOf(c.Items.OfType<ToolCallItem>().Skip(1000).First());
        clock.Restart();
        c.Items.Insert(middle, new NoticeItem(NoticeKind.ApiRetry));
        clock.Stop();
        _output.WriteLine("an insert in the middle of " + c.Items.Count + " items: " + clock.Elapsed.TotalMilliseconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " ms, " + f.Changes.Count + " changes");
        Assert.Equal(0, f.Resets);
        Assert.InRange(f.Changes.Count, 1, 3);
        Assert.Equal(Fresh(c), Shape(f.Rows, c.Items));
    }

    [Fact]
    public void Taking_out_the_line_between_two_runs_joins_their_folds_one_row_at_a_time()
    {
        var notice = new NoticeItem(NoticeKind.ApiRetry);
        var f = Follow(new UserMessageItem("go"), Call("Read"), notice, Call("Bash"), Call("Edit"), new AssistantTextItem { Text = "ok" });
        var first = f.Fold(1);
        first.IsExpanded = true;
        Assert.Equal(new[] { "0", "[1]", "2", "[3,4]", "5" }, Shape(f.Rows, f.Items));

        f.Items.Remove(notice);
        Assert.Equal(new[] { "0", "[1,2,3]", "4" }, Shape(f.Rows, f.Items));
        Assert.Same(first, f.List[1]);    // the fold that was there takes them all, still open
        Assert.True(first.IsExpanded);
        Assert.Equal(0, f.Resets);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Remove },
            f.Changes.Select(e => e.Action));

        // and back between them: the run is cut again
        f.Changes.Clear();
        f.Items.Insert(2, notice);
        Assert.Equal(new[] { "0", "[1]", "2", "[3,4]", "5" }, Shape(f.Rows, f.Items));
        Assert.Same(first, f.List[1]);
        Assert.Equal(0, f.Resets);
    }

    [Fact]
    public void Every_change_leaves_the_rows_a_fresh_projection_would_make()
    {
        var random = new Random(35);
        var c = new ChatConversation();
        var f = new Followed(c);
        ChatItem Next()
        {
            switch (random.Next(9))
            {
                case 0: return new UserMessageItem("q");
                case 1: return new AssistantTextItem { Text = "a" };
                case 2: return Footer();
                case 3: return new ThinkingItem();
                case 4: return Call("TodoWrite");
                default: return Call(random.Next(2) == 0 ? "Read" : "Bash");
            }
        }
        for (var step = 0; step < 600; step++)
        {
            var roll = random.Next(10);
            if (roll < 6 || c.Items.Count < 3) c.Items.Add(Next());
            else if (roll < 8) c.Items.Insert(random.Next(c.Items.Count + 1), Next());
            else if (roll < 9) c.Items.RemoveAt(random.Next(c.Items.Count));
            else c.Items[random.Next(c.Items.Count)] = Next();
            Assert.Equal(Fresh(c), Shape(f.Rows, c.Items));
        }
        Assert.Equal(0, f.Resets);

        // the list itself says Reset: so does the projection
        c.Items.Clear();
        Assert.Equal(1, f.Resets);
        Assert.Empty(f.List);
    }

    [Fact]
    public void Stopped_it_holds_no_handler_and_started_again_it_reads_the_list_whole()
    {
        var call = Call("Bash", ToolStatus.Running);
        var thought = new ThinkingItem();
        var f = Follow(new UserMessageItem("go"), thought, call);
        Assert.True(Handlers(f.Conversation.Items) > 0);

        f.List.Stop();
        Assert.Equal(0, Handlers(f.Conversation.Items));
        Assert.Equal(0, Handlers(f.Conversation.PendingPermissions));
        Assert.Equal(0, PropertyHandlers(call));
        Assert.Equal(0, PropertyHandlers(thought));

        f.Conversation.Items.Add(Call("Read"));
        Assert.Equal(2, f.List.Count);    // the rows stay as they were
        f.List.Start();
        Assert.Equal(1, f.Resets);
        Assert.Equal(3, f.Fold(1).Items.Count);
        Assert.Equal(1, PropertyHandlers(call));
    }

    private static int Handlers(object collection)
    {
        var field = collection.GetType().GetField("CollectionChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return ((Delegate?)field!.GetValue(collection))?.GetInvocationList().Count(d => d.Target is FocusProjection) ?? 0;
    }

    private static int PropertyHandlers(ChatObservableObject item)
    {
        var field = typeof(ChatObservableObject).GetField("PropertyChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return ((Delegate?)field!.GetValue(item))?.GetInvocationList().Count(d => d.Target is FocusProjection) ?? 0;
    }
}
