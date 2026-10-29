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
/// The Focus view's list: each run of steps of a round (calls, thoughts) is one fold, followed by the latest TodoWrite when the run holds
/// it; the prompts and Claude's text stay; and it follows the conversation one row at a time, without reading it whole again.
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

    /// <summary>A TodoWrite that ended well, with a list of <paramref name="todos"/> entries (the one that counts as the latest).</summary>
    private static ToolCallItem Todo(int todos = 1, ToolStatus status = ToolStatus.Succeeded)
    {
        var entries = string.Join(",", Enumerable.Range(0, todos).Select(i => "{\"content\":\"step " + i + "\",\"status\":\"pending\"}"));
        return new ToolCallItem { Name = "TodoWrite", Status = status, Input = J("{\"todos\":[" + entries + "]}") };
    }

    private static TurnFooterItem Footer() => new TurnFooterItem(1000, null, null, null, false);

    /// <summary>The session at work or idle, as its turns make it (its setter is the conversation's own).</summary>
    private static void SetBusy(ChatConversation c, bool busy) =>
        typeof(ChatConversation).GetProperty(nameof(ChatConversation.IsBusy))!.SetValue(c, busy);

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

        public System.Collections.ObjectModel.ObservableCollection<ChatItem> Items => Conversation.Items;

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

    /// <summary>A conversation of these items, at work (as while a turn arrives), and its projection.</summary>
    private static Followed Follow(params ChatItem[] items) => Follow(true, items);

    private static Followed Follow(bool busy, params ChatItem[] items)
    {
        var c = new ChatConversation();
        SetBusy(c, busy);
        foreach (var item in items) c.Items.Add(item);
        return new Followed(c);
    }

    /// <summary>
    /// The rows: an item by its place, or "[…]" with the steps of a fold and what it shows (its label, live, failed), so that a fold kept
    /// up step by step is compared with one read whole.
    /// </summary>
    private static List<string> Shape(IEnumerable<object> rows, IList<ChatItem> items)
    {
        string Name(ChatItem item) => items.IndexOf(item).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return rows.Select(r => r is FocusFoldItem f
            ? "[" + string.Join(",", f.Items.Select(Name)) + "] " + En.FormatFocusLabel(f.State) + (f.State.HasLiveLabel ? " + " + En.FormatFocusCount(f.State) : "")
              + (f.State.IsLive ? " live" : "") + (f.State.HasFailed ? " failed" : "") + " agents " + f.State.Agents.Count + "+" + f.State.MoreAgents
            : Name((ChatItem)r)).ToList();
    }

    /// <summary>The places of the rows only: "[1,2]" for a fold.</summary>
    private static List<string> Places(Followed f) =>
        Shape(f.Rows, f.Items).Select(s => s.StartsWith("[", StringComparison.Ordinal) ? s.Substring(0, s.IndexOf(']') + 1) : s).ToList();

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

        // with a call in it, a run of calls and thoughts counts the calls; text cuts a round in two runs
        var mixed = Follow(new UserMessageItem("a"), new ThinkingItem { DurationMs = 2000 }, Call("Read"), new AssistantTextItem { Text = "x" },
            Call("Bash", ToolStatus.Failed), new ThinkingItem { DurationMs = 1000 }, Call("Bash"));
        Assert.Equal(4, mixed.List.Count);
        Assert.Equal("1 tool call", En.FormatFocusLabel(mixed.Fold(1).State));
        Assert.Equal("2 tool calls · 1 failed", En.FormatFocusLabel(mixed.Fold(3).State));

        // a refused call, and one interrupted (rejected when the turn stopped), failed too: red, and counted
        var refused = Follow(new UserMessageItem("a"), Call("Edit", ToolStatus.Rejected), Call("Read"));
        Assert.Equal("2 tool calls · 1 failed", En.FormatFocusLabel(refused.Fold(1).State));
        Assert.True(refused.Fold(1).State.HasFailed);

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

        // a thought arriving in a run of calls: "Thinking…", the count after it
        var thinking = new ThinkingItem { IsStreaming = true };
        running.Items.Add(thinking);
        Assert.Equal("Thinking…", En.FormatFocusLabel(running.Fold(1).State));
        Assert.Equal("2 tool calls", En.FormatFocusCount(running.Fold(1).State));
        Assert.True(running.Fold(1).State.HasLiveLabel);
        thinking.IsStreaming = false;
        Assert.Equal("2 tool calls", En.FormatFocusLabel(running.Fold(1).State));
    }

    [Fact]
    public void Idle_a_call_without_a_result_failed_and_nothing_is_live()
    {
        // a history read back without the results: the session is idle
        var stopped = new AgentInfo("toolu_s") { Description = "Gone", Status = AgentStatus.Stopped };
        var stoppedCall = Call("Agent", ToolStatus.Running, "toolu_s");
        stoppedCall.Agent = stopped;
        var thought = new ThinkingItem { IsStreaming = true, DurationMs = null };
        var f = Follow(false, new UserMessageItem("go"), thought, Call("Bash", ToolStatus.Running, "t1"), stoppedCall);
        var state = f.Fold(1).State;
        Assert.Equal("2 tool calls · 2 failed", En.FormatFocusLabel(state));
        Assert.False(state.IsLive);          // no blinking
        Assert.False(state.HasLiveLabel);    // no "Running…"
        Assert.True(state.HasFailed);
        Assert.Empty(state.Agents);

        // the session at work again: the call that runs is live
        SetBusy(f.Conversation, true);
        Assert.Equal("Running Agent…", En.FormatFocusLabel(f.Fold(1).State));
        Assert.True(f.Fold(1).State.IsLive);
        SetBusy(f.Conversation, false);
        Assert.Equal("2 tool calls · 2 failed", En.FormatFocusLabel(f.Fold(1).State));

        // an agent call whose agent still runs keeps its progress: the dot, no label, its row; not a failure
        var running = new AgentInfo("toolu_r") { Description = "Watch", Status = AgentStatus.Running, IsBackground = true };
        var call = Call("Agent", ToolStatus.Succeeded, "toolu_r");
        call.Agent = running;
        var idle = Follow(false, new UserMessageItem("go"), call, Call("Read"));
        Assert.True(idle.Fold(1).State.IsLive);
        Assert.False(idle.Fold(1).State.HasLiveLabel);
        Assert.False(idle.Fold(1).State.HasFailed);
        Assert.Equal("2 tool calls", En.FormatFocusLabel(idle.Fold(1).State));
        Assert.Single(idle.Fold(1).State.Agents);
        Assert.Equal(new[] { "0", "[1,2]" }, Places(idle));   // a background agent's call folds like any other
    }

    [Fact]
    public void Prompts_text_and_the_latest_todo_list_stay_visible()
    {
        var prompt = new UserMessageItem("plan it");
        var firstTodo = Todo();
        var text = new AssistantTextItem { Text = "On it." };
        var secondTodo = Todo();
        var f = Follow(prompt, firstTodo, Call("Read"), text, secondTodo, Call("Bash"));

        // the earlier list folds with its run; the latest folds out of its run's fold and comes right after it
        Assert.Equal(new[] { "0", "[1,2]", "3", "[5]", "4" }, Places(f));
        Assert.Equal("1 tool call", En.FormatFocusLabel(f.Fold(3).State));
        var foldOfRun = f.Fold(3);
        foldOfRun.IsExpanded = true;

        // a step that comes joins the fold, before the list
        f.Items.Add(Call("Read"));
        Assert.Equal(new[] { "0", "[1,2]", "3", "[5,6]", "4" }, Places(f));
        Assert.Empty(f.Changes);

        // a newer list: the one before folds into its run, whose fold stays the same (and open)
        var thirdTodo = Todo();
        f.Items.Add(thirdTodo);
        Assert.Equal(new[] { "0", "[1,2]", "3", "[4,5,6]", "7" }, Places(f));
        Assert.Same(foldOfRun, f.List[3]);
        Assert.True(foldOfRun.IsExpanded);
        Assert.Equal("3 tool calls", En.FormatFocusLabel(foldOfRun.State));
        Assert.Equal(0, f.Resets);

        // in the middle of a run: one fold, then the list
        var middle = Follow(new UserMessageItem("a"), Call("Read"), Todo(), Call("Bash"), new AssistantTextItem { Text = "b" });
        Assert.Equal(new[] { "0", "[1,3]", "2", "4" }, Places(middle));
        Assert.Equal("2 tool calls", En.FormatFocusLabel(middle.Fold(1).State));

        // only a list that ended well counts: one still running, or that failed, folds; a newer empty list leaves none
        var running = Todo(status: ToolStatus.Running);
        var later = Follow(new UserMessageItem("a"), Todo(), Call("Read"), running);
        Assert.Equal(new[] { "0", "[2,3]", "1" }, Places(later));
        running.Status = ToolStatus.Succeeded;
        Assert.Equal(new[] { "0", "[1,2]", "3" }, Places(later));
        later.Items.Add(Todo(0));
        Assert.Equal(new[] { "0", "[1,2,3,4]" }, Places(later));
        Assert.Equal(0, later.Resets);
    }

    [Fact]
    public void A_thinking_only_run_reads_thought_for_n_seconds()
    {
        var thought = new ThinkingItem { Text = "hmm", IsStreaming = true };
        var f = Follow(new UserMessageItem("why?"), thought);
        var fold = f.Fold(1);
        Assert.Equal("Thinking…", En.FormatFocusLabel(fold.State));
        Assert.True(fold.State.IsLive);   // its dot blinks while the thought arrives
        Assert.False(fold.State.HasLiveLabel);

        thought.IsStreaming = false;
        thought.DurationMs = 3400;
        Assert.Equal("Thought for 3s", En.FormatFocusLabel(fold.State));
        Assert.False(fold.State.IsLive);

        thought.DurationMs = 200;   // at least 1 s
        Assert.Equal("Thought for 1s", En.FormatFocusLabel(fold.State));

        // two thoughts together; the known lengths are added, and "Thinking" only when none is known
        var second = new ThinkingItem { DurationMs = 2600 };
        f.Items.Add(second);
        Assert.Same(fold, f.List[1]);
        Assert.Equal("Thought for 3s", En.FormatFocusLabel(fold.State));
        second.DurationMs = null;
        Assert.Equal("Thought for 1s", En.FormatFocusLabel(fold.State));
        thought.DurationMs = null;
        Assert.Equal("Thinking", En.FormatFocusLabel(fold.State));
        Assert.Equal("Pensando", ChatStrings.PortugueseBrazil.FormatFocusLabel(fold.State));
    }

    [Fact]
    public void A_waiting_agent_opens_its_fold_and_reads_waiting_for_permission()
    {
        var started = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
        var info = new AgentInfo("toolu_a") { Description = "Look around", Status = AgentStatus.Running, Activity = "Reading a.txt",
            Tokens = 1200, ToolUses = 3, StartedAt = started };
        var agent = Call("Agent", ToolStatus.Running, "toolu_a");
        agent.Agent = info;
        var f = Follow(new UserMessageItem("look"), Call("Read"), agent);
        var fold = f.Fold(1);
        Assert.False(fold.IsExpanded);
        Assert.Equal("Running Agent…", En.FormatFocusLabel(fold.State));

        // the agent at work is a row of the fold: its description and what it does, then its numbers, its time from its start
        var row = Assert.Single(fold.State.Agents);
        Assert.Equal("Look around: Reading a.txt", row.Line);
        Assert.Equal("1.2k tokens · 3 tools · 1m 5s", En.FormatFocusAgentStats(row, started.AddSeconds(65)));
        Assert.Equal("1.2k tokens · 3 tools · 1m 6s", En.FormatFocusAgentStats(row, started.AddSeconds(66)));

        // waiting, it opens, and stays open until the request is answered; then it is as the user left it
        info.IsWaitingForPermission = true;
        Assert.True(fold.IsExpanded);
        Assert.Equal("Waiting for permission…", En.FormatFocusLabel(fold.State));
        Assert.Equal("2 tool calls", En.FormatFocusCount(fold.State));
        fold.IsExpanded = false;
        Assert.True(fold.IsExpanded);
        info.IsWaitingForPermission = false;
        Assert.False(fold.IsExpanded);
        fold.IsExpanded = true;
        info.IsWaitingForPermission = true;
        info.IsWaitingForPermission = false;
        Assert.True(fold.IsExpanded);

        // a request of the main agent's own call does the same; a question waits for an answer and opens nothing
        var c = new ChatConversation();
        SetBusy(c, true);
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

        // more than four agents at work: three rows, and a row for the others with their numbers added
        var many = new List<ChatItem> { new UserMessageItem("fan out") };
        for (var i = 0; i < 6; i++)
        {
            var call = new ToolCallItem { Name = "Agent", Status = ToolStatus.Running, Summary = "Agent " + i };
            call.Agent = new AgentInfo("a" + i) { Description = "Agent " + i, Status = AgentStatus.Running, Tokens = 1000, ToolUses = 2, StartedAt = started };
            many.Add(call);
        }
        var fanned = Follow(many.ToArray());
        var state = fanned.Fold(1).State;
        Assert.Equal(3, state.Agents.Count);
        Assert.Equal(3, state.MoreAgents);
        Assert.Equal("+3 more agents", En.FormatFocusMoreAgents(state.MoreAgents));
        Assert.Equal("Agent 0", state.Agents[0].Line);
        Assert.Equal("3.0k tokens · 6 tools · 3m 0s combined", En.FormatFocusAgentStats(state.MoreAgentsRow!, started.AddMinutes(1)));
        // without tokens yet, the time alone
        var fresh = new ToolCallItem { Name = "Agent", Status = ToolStatus.Running };
        fresh.Agent = new AgentInfo("b") { Description = "New", Status = AgentStatus.Running, StartedAt = started };
        var newcomer = Follow(new UserMessageItem("go"), fresh);
        Assert.Equal("4s", En.FormatFocusAgentStats(newcomer.Fold(1).State.Agents[0], started.AddSeconds(4)));
    }

    [Fact]
    public void Appending_to_three_thousand_items_does_not_rebuild_the_projection()
    {
        // 3,000 items in rounds of a prompt, eight steps, an answer and a footer
        var c = new ChatConversation();
        SetBusy(c, true);
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
        var readBefore = f.List.ItemsRead;
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
        // the cost is counted, not only timed: an item at the end reads nothing but itself, however long the list and its run
        Assert.Equal(0, f.List.ItemsRead - readBefore);
        _output.WriteLine("5,000 appends after 3,003 items: " + clock.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms");
        Assert.True(clock.ElapsedMilliseconds < 1500, clock.ElapsedMilliseconds + " ms");

        // a line put in the middle cuts one fold in two: the rows around it only, told one at a time
        f.Changes.Clear();
        var middle = c.Items.IndexOf(c.Items.OfType<ToolCallItem>().Skip(1000).First());
        readBefore = f.List.ItemsRead;
        clock.Restart();
        c.Items.Insert(middle, new NoticeItem(NoticeKind.ApiRetry));
        clock.Stop();
        _output.WriteLine("an insert in the middle of " + c.Items.Count + " items: " + clock.Elapsed.TotalMilliseconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " ms, " + f.Changes.Count + " changes, " + (f.List.ItemsRead - readBefore) + " items read");
        Assert.Equal(0, f.Resets);
        Assert.InRange(f.Changes.Count, 1, 3);
        Assert.InRange(f.List.ItemsRead - readBefore, 1, 30);   // its run (eight steps or fewer, read twice at most), not the list
        Assert.Equal(Fresh(c), Shape(f.Rows, c.Items));
    }

    [Fact]
    public void Taking_out_the_line_between_two_runs_joins_their_folds_one_row_at_a_time()
    {
        var notice = new NoticeItem(NoticeKind.ApiRetry);
        var f = Follow(new UserMessageItem("go"), Call("Read"), notice, Call("Bash"), Call("Edit"), new AssistantTextItem { Text = "ok" });
        var first = f.Fold(1);
        first.IsExpanded = true;
        Assert.Equal(new[] { "0", "[1]", "2", "[3,4]", "5" }, Places(f));

        f.Items.Remove(notice);
        Assert.Equal(new[] { "0", "[1,2,3]", "4" }, Places(f));
        Assert.Same(first, f.List[1]);    // the fold that was there takes them all, still open
        Assert.True(first.IsExpanded);
        Assert.Equal(0, f.Resets);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Remove },
            f.Changes.Select(e => e.Action));

        // and back between them: the run is cut again
        f.Changes.Clear();
        f.Items.Insert(2, notice);
        Assert.Equal(new[] { "0", "[1]", "2", "[3,4]", "5" }, Places(f));
        Assert.Same(first, f.List[1]);
        Assert.Equal(0, f.Resets);
    }

    [Fact]
    public void A_move_either_way_leaves_each_row_once()
    {
        // A B C D E: D to the second place, then back; a step across text, both ways
        var a = new UserMessageItem("a");
        var b = new AssistantTextItem { Text = "b" };
        var cc = Call("Read");
        var d = new NoticeItem(NoticeKind.ApiRetry);
        var e = new AssistantTextItem { Text = "e" };
        var f = Follow(a, b, cc, d, e);
        f.Items.Move(3, 1);
        Assert.Equal(5, f.List.Count);
        Assert.Equal(Fresh(f.Conversation), Shape(f.Rows, f.Items));
        f.Items.Move(1, 3);
        Assert.Equal(Fresh(f.Conversation), Shape(f.Rows, f.Items));
        f.Items.Move(2, 0);
        Assert.Equal(Fresh(f.Conversation), Shape(f.Rows, f.Items));
        f.Items.Move(0, 4);
        Assert.Equal(Fresh(f.Conversation), Shape(f.Rows, f.Items));
        Assert.Equal(0, f.Resets);
    }

    [Fact]
    public void Every_change_leaves_the_rows_a_fresh_projection_would_make()
    {
        // several rounds, then hundreds of mixed changes with a fixed seed: adds at the end and in the middle, removes, replaces, moves both
        // ways, and the changes of a step that make it the latest list or not, a call that ends, fails or is refused, an agent that starts or
        // stops working, the session going idle and back. After each one, the rows are those a projection read now would make.
        var random = new Random(35);
        var c = new ChatConversation();
        SetBusy(c, true);
        for (var round = 0; round < 8; round++)
        {
            c.Items.Add(new UserMessageItem("q"));
            c.Items.Add(new ThinkingItem { DurationMs = 1000 });
            c.Items.Add(Call("Read"));
            c.Items.Add(Todo());
            c.Items.Add(Call("Bash", ToolStatus.Running));
            c.Items.Add(new AssistantTextItem { Text = "a" });
            c.Items.Add(Footer());
        }
        var f = new Followed(c);
        ChatItem Next()
        {
            switch (random.Next(10))
            {
                case 0: return new UserMessageItem("q");
                case 1: return new AssistantTextItem { Text = "a" };
                case 2: return Footer();
                case 3: return new ThinkingItem { IsStreaming = random.Next(2) == 0, DurationMs = random.Next(2) == 0 ? 1500 : (long?)null };
                case 4: return Todo(random.Next(3), random.Next(3) == 0 ? ToolStatus.Running : ToolStatus.Succeeded);
                case 5:
                    var agent = new ToolCallItem { Name = "Agent", Status = ToolStatus.Running, Summary = "sub" };
                    if (random.Next(2) == 0) agent.Agent = new AgentInfo("x" + random.Next()) { Status = AgentStatus.Running, IsBackground = random.Next(2) == 0 };
                    return agent;
                default: return Call(random.Next(2) == 0 ? "Read" : "Bash", (ToolStatus)random.Next(4));
            }
        }
        void ChangeAStep()
        {
            var steps = c.Items.Where(i => i is ToolCallItem || i is ThinkingItem).ToList();
            if (steps.Count == 0) return;
            switch (steps[random.Next(steps.Count)])
            {
                case ToolCallItem { Agent: { } agent }:
                    agent.Status = agent.Status == AgentStatus.Running ? AgentStatus.Completed : AgentStatus.Running;
                    break;
                case ToolCallItem { Name: "TodoWrite" } todo when random.Next(2) == 0:
                    todo.Input = J(random.Next(2) == 0 ? "{\"todos\":[]}" : "{\"todos\":[{\"content\":\"x\"}]}");
                    break;
                case ToolCallItem call:
                    call.Status = (ToolStatus)random.Next(4);
                    break;
                case ThinkingItem thought:
                    thought.IsStreaming = !thought.IsStreaming;
                    break;
            }
        }
        for (var step = 0; step < 800; step++)
        {
            var roll = random.Next(20);
            if (roll < 6 || c.Items.Count < 3) c.Items.Add(Next());
            else if (roll < 9) c.Items.Insert(random.Next(c.Items.Count + 1), Next());
            else if (roll < 11) c.Items.RemoveAt(random.Next(c.Items.Count));
            else if (roll < 12) c.Items[random.Next(c.Items.Count)] = Next();
            else if (roll < 15) c.Items.Move(random.Next(c.Items.Count), random.Next(c.Items.Count));
            else if (roll < 19) ChangeAStep();
            else SetBusy(c, !c.IsBusy);
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
        Assert.True(PropertyHandlers(f.Conversation) > 0);

        f.List.Stop();
        Assert.Equal(0, Handlers(f.Conversation.Items));
        Assert.Equal(0, Handlers(f.Conversation.PendingPermissions));
        Assert.Equal(0, PropertyHandlers(f.Conversation));
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
