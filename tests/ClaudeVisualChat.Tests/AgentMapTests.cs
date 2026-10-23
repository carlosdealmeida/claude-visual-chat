using System;
using System.Collections.Generic;
using System.Linq;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests;

/// <summary>The subagents a conversation keeps, from the <c>system/task_*</c> lines, the Agent call and its result.</summary>
public class AgentMapTests
{
    private const string Description = "Read and summarize three text files";
    private const string Haiku = "claude-haiku-4-5-20251001";
    private const string AgentCallId = "toolu_01T3QiGYgwakhhzVRtb3LFzP";

    // A foreground agent, in the shape of the recorded lines: the Agent call, then its result with the totals.
    private const string ForegroundCall =
        "{\"type\":\"assistant\",\"message\":{\"model\":\"claude-haiku-4-5-20251001\",\"id\":\"msg_fg1\",\"type\":\"message\",\"role\":\"assistant\"," +
        "\"content\":[{\"type\":\"tool_use\",\"id\":\"toolu_fg1\",\"name\":\"Agent\",\"input\":{\"description\":\"Haiku about rain\"," +
        "\"subagent_type\":\"general-purpose\",\"prompt\":\"Reply with a haiku about rain.\",\"run_in_background\":false}}]}," +
        "\"parent_tool_use_id\":null,\"session_id\":\"s1\",\"timestamp\":\"2026-10-23T10:00:00.000Z\"}";

    private const string ForegroundResult =
        "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"tool_use_id\":\"toolu_fg1\",\"type\":\"tool_result\"," +
        "\"content\":[{\"type\":\"text\",\"text\":\"Rain on the roof\"}]}]},\"parent_tool_use_id\":null,\"session_id\":\"s1\"," +
        "\"timestamp\":\"2026-10-23T10:00:06.000Z\",\"tool_use_result\":{\"status\":\"completed\",\"prompt\":\"Reply with a haiku about rain.\"," +
        "\"agentId\":\"a03380de4640e243e\",\"agentType\":\"general-purpose\"," +
        "\"content\":[{\"type\":\"text\",\"text\":\"Rain on the roof,\"},{\"type\":\"text\",\"text\":\"drops tell secrets.\"}]," +
        "\"resolvedModel\":\"claude-haiku-4-5-20251001\",\"totalDurationMs\":5573,\"totalTokens\":38119,\"totalToolUseCount\":2}}";

    private static ChatConversation Play(IEnumerable<string> lines)
    {
        var conversation = new ChatConversation();
        foreach (var line in lines) conversation.AppendLine(line);
        return conversation;
    }

    [Fact]
    public void Stream_do_subagente_monta_um_agente_em_segundo_plano()
    {
        var conversation = Play(FixtureFiles.StreamLines("subagent"));

        var agent = Assert.Single(conversation.Agents);
        Assert.Equal(AgentCallId, agent.Id);
        Assert.Equal("adb1f0b70b0f95ff6", agent.TaskId);
        Assert.Equal(Description, agent.Description);
        Assert.Equal("general-purpose", agent.AgentType);
        Assert.True(agent.IsBackground);
        Assert.Equal(1, agent.Depth);
        Assert.Null(agent.ParentId);
        Assert.Equal(Haiku, agent.Model);
        Assert.Equal(26316, agent.Tokens);
        Assert.Equal(3, agent.ToolUses);
        Assert.Equal(5850, agent.DurationMs);
        Assert.Equal(AgentStatus.Completed, agent.Status);
        Assert.False(string.IsNullOrEmpty(agent.Result));
        Assert.StartsWith("a.txt: Simple sentence", agent.Result);
        Assert.Equal(0, conversation.RunningAgents);
    }

    [Fact]
    public void Progresso_atualiza_atividade_e_tokens_enquanto_roda()
    {
        var lines = FixtureFiles.StreamLines("subagent");
        var first = Array.FindIndex(lines, l => l.Contains("\"task_progress\""));
        Assert.True(first > 0);

        var conversation = Play(lines.Take(first + 1));

        var agent = Assert.Single(conversation.Agents);
        Assert.Equal(AgentStatus.Running, agent.Status);
        Assert.Equal("Reading a.txt", agent.Activity);
        Assert.Equal(24803, agent.Tokens);
        Assert.Equal(1, agent.ToolUses);
        Assert.Equal(2832, agent.DurationMs);
        Assert.Null(agent.Result);
        Assert.Equal(1, conversation.RunningAgents);
    }

    [Fact]
    public void Historico_remonta_o_mesmo_agente()
    {
        var conversation = new ChatConversation();
        conversation.LoadTranscript(FixtureFiles.TranscriptLines("subagent"));

        var agent = Assert.Single(conversation.Agents);
        Assert.Equal(AgentCallId, agent.Id);
        Assert.Equal("adb1f0b70b0f95ff6", agent.TaskId);
        Assert.Equal(Description, agent.Description);
        Assert.True(agent.IsBackground);
        Assert.Equal(Haiku, agent.Model);
        Assert.Equal(AgentStatus.Completed, agent.Status);
        Assert.Equal(26316, agent.Tokens);
        Assert.Equal(3, agent.ToolUses);
        Assert.Equal(5850, agent.DurationMs);
        Assert.StartsWith("a.txt: Simple sentence", agent.Result);
        Assert.EndsWith("executable programs.", agent.Result);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 21, 32, 19, 561, TimeSpan.Zero), agent.StartedAt);
        Assert.Equal(0, conversation.RunningAgents);
        // the notification stays out of the visible conversation
        Assert.DoesNotContain(conversation.Items, i => i is UserMessageItem u && u.Text.Contains("task-notification"));
    }

    [Fact]
    public void Comando_do_agente_nao_vira_agente()
    {
        var conversation = new ChatConversation();
        conversation.AppendLine(
            "{\"type\":\"system\",\"subtype\":\"task_started\",\"task_id\":\"bab6anoa7\",\"owned_by_subagent\":true," +
            "\"tool_use_id\":\"toolu_bash1\",\"description\":\"Wait 20 seconds\",\"is_backgrounded\":false,\"task_type\":\"local_bash\"," +
            "\"uuid\":\"u1\",\"session_id\":\"s1\"}");
        conversation.AppendLine(
            "{\"type\":\"system\",\"subtype\":\"task_notification\",\"task_id\":\"bab6anoa7\",\"tool_use_id\":\"toolu_bash1\"," +
            "\"status\":\"completed\",\"output_file\":\"\",\"summary\":\"Wait 20 seconds\",\"uuid\":\"u2\",\"session_id\":\"s1\"}");

        Assert.Empty(conversation.Agents);
        Assert.Equal(0, conversation.RunningAgents);
    }

    [Fact]
    public void Agente_sem_fim_no_historico_vira_parado()
    {
        var conversation = new ChatConversation();
        conversation.LoadTranscript(new[]
        {
            "{\"type\":\"user\",\"uuid\":\"u1\",\"parentUuid\":null,\"isSidechain\":false,\"timestamp\":\"2026-10-23T10:00:00.000Z\"," +
            "\"sessionId\":\"s1\",\"message\":{\"role\":\"user\",\"content\":\"Count the files with a subagent.\"}}",
            "{\"type\":\"assistant\",\"uuid\":\"u2\",\"parentUuid\":\"u1\",\"isSidechain\":false,\"timestamp\":\"2026-10-23T10:00:02.000Z\"," +
            "\"sessionId\":\"s1\",\"message\":{\"model\":\"claude-haiku-4-5-20251001\",\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\"," +
            "\"content\":[{\"type\":\"tool_use\",\"id\":\"toolu_open1\",\"name\":\"Agent\",\"input\":{\"description\":\"Count files\"," +
            "\"subagent_type\":\"Explore\",\"prompt\":\"Count the files in C:\\\\demo\\\\app.\"}}]}}",
        });

        var agent = Assert.Single(conversation.Agents);
        Assert.Equal("toolu_open1", agent.Id);
        Assert.Equal("Count files", agent.Description);
        Assert.Equal("Explore", agent.AgentType);
        Assert.False(conversation.IsBusy);
        Assert.Equal(AgentStatus.Stopped, agent.Status);
        Assert.Null(agent.Result);
        Assert.Equal(0, conversation.RunningAgents);
    }

    [Fact]
    public void Agente_em_primeiro_plano_pelo_resultado()
    {
        var conversation = new ChatConversation();
        conversation.AppendLine(ForegroundCall);

        var agent = Assert.Single(conversation.Agents);
        Assert.Equal(AgentStatus.Running, agent.Status);
        Assert.False(agent.IsBackground);
        Assert.Equal(1, conversation.RunningAgents);

        conversation.AppendLine(ForegroundResult);

        Assert.Same(agent, Assert.Single(conversation.Agents));
        Assert.Equal("toolu_fg1", agent.Id);
        Assert.Equal("Haiku about rain", agent.Description);
        Assert.Equal("general-purpose", agent.AgentType);
        Assert.Equal(AgentStatus.Completed, agent.Status);
        Assert.Equal(Haiku, agent.Model);
        Assert.Equal(38119, agent.Tokens);
        Assert.Equal(5573, agent.DurationMs);
        Assert.Equal(2, agent.ToolUses);
        Assert.Equal("Rain on the roof,\n\ndrops tell secrets.", agent.Result);
        Assert.False(agent.IsBackground);
        Assert.Equal(0, conversation.RunningAgents);
    }

    [Fact]
    public void The_newest_agent_comes_first_and_a_nested_one_knows_its_parent()
    {
        var conversation = new ChatConversation();
        conversation.AppendLine(ForegroundCall);
        conversation.AppendLine(
            "{\"type\":\"assistant\",\"message\":{\"id\":\"msg_n1\",\"type\":\"message\",\"role\":\"assistant\"," +
            "\"content\":[{\"type\":\"tool_use\",\"id\":\"toolu_nested\",\"name\":\"Task\",\"input\":{\"description\":\"Nested look\"}}]}," +
            "\"parent_tool_use_id\":\"toolu_fg1\",\"session_id\":\"s1\"}");

        Assert.Equal(new[] { "toolu_nested", "toolu_fg1" }, conversation.Agents.Select(a => a.Id));
        var nested = conversation.Agents[0];
        Assert.Equal("toolu_fg1", nested.ParentId);
        Assert.Equal(2, nested.Depth);
        Assert.Equal(2, conversation.RunningAgents);
    }

    [Fact]
    public void A_task_started_first_creates_the_agent_and_the_call_completes_it()
    {
        var conversation = new ChatConversation();
        conversation.AppendLine(
            "{\"type\":\"system\",\"subtype\":\"task_started\",\"task_id\":\"t1\",\"tool_use_id\":\"toolu_fg1\"," +
            "\"description\":\"Haiku about rain\",\"subagent_type\":\"general-purpose\",\"is_backgrounded\":true,\"spawn_depth\":1," +
            "\"task_type\":\"local_agent\",\"prompt\":\"x\",\"uuid\":\"u1\",\"session_id\":\"s1\"}");
        conversation.AppendLine(ForegroundCall);

        var agent = Assert.Single(conversation.Agents);
        Assert.Equal("t1", agent.TaskId);
        Assert.True(agent.IsBackground);
        Assert.Equal("Haiku about rain", agent.Description);
        Assert.Equal(AgentStatus.Running, agent.Status);
    }

    [Fact]
    public void A_failed_notification_marks_the_agent_failed()
    {
        var conversation = new ChatConversation();
        conversation.AppendLine(ForegroundCall);
        conversation.AppendLine(
            "{\"type\":\"system\",\"subtype\":\"task_notification\",\"task_id\":\"t1\",\"tool_use_id\":\"toolu_fg1\"," +
            "\"status\":\"failed\",\"summary\":\"It broke\",\"usage\":{\"total_tokens\":10,\"tool_uses\":1,\"duration_ms\":20}}");

        var agent = Assert.Single(conversation.Agents);
        Assert.Equal(AgentStatus.Failed, agent.Status);
        Assert.Equal("It broke", agent.Result);
        Assert.Equal(0, conversation.RunningAgents);
    }

    [Fact]
    public void A_completed_update_alone_leaves_the_agent_running()
    {
        var lines = FixtureFiles.StreamLines("subagent");
        var updated = Array.FindIndex(lines, l => l.Contains("\"task_updated\""));
        Assert.True(updated > 0);

        var conversation = Play(lines.Take(updated + 1));

        var agent = Assert.Single(conversation.Agents);
        Assert.Equal(AgentStatus.Running, agent.Status);
        Assert.Null(agent.Result);
    }

    [Fact]
    public void A_failed_turn_stops_its_foreground_agent_and_leaves_a_background_one()
    {
        var conversation = new ChatConversation();
        conversation.AppendLine(ForegroundCall);
        conversation.AppendLine(
            "{\"type\":\"system\",\"subtype\":\"task_started\",\"task_id\":\"t2\",\"tool_use_id\":\"toolu_bg\"," +
            "\"description\":\"Background look\",\"subagent_type\":\"general-purpose\",\"is_backgrounded\":true,\"spawn_depth\":1," +
            "\"task_type\":\"local_agent\"}");
        conversation.AppendLine("{\"type\":\"result\",\"subtype\":\"error_during_execution\",\"is_error\":true,\"duration_ms\":10}");

        Assert.Equal(AgentStatus.Stopped, conversation.Agents.Single(a => a.Id == "toolu_fg1").Status);
        Assert.Equal(AgentStatus.Running, conversation.Agents.Single(a => a.Id == "toolu_bg").Status);
        Assert.Equal(1, conversation.RunningAgents);
    }

    [Fact]
    public void A_live_task_line_brings_back_an_agent_the_history_stopped()
    {
        var conversation = new ChatConversation();
        var history = FixtureFiles.TranscriptLines("subagent").Where(l => !l.Contains("task-notification>")).ToList();
        conversation.LoadTranscript(history);
        var agent = Assert.Single(conversation.Agents);
        Assert.Equal(AgentStatus.Stopped, agent.Status);

        var lines = FixtureFiles.StreamLines("subagent");
        conversation.AppendLine(lines.First(l => l.Contains("\"task_progress\"")));
        Assert.Equal(AgentStatus.Running, agent.Status);
        Assert.Equal(1, conversation.RunningAgents);

        conversation.AppendLine(lines.First(l => l.Contains("\"task_notification\"")));
        Assert.Equal(AgentStatus.Completed, agent.Status);
    }

    private const string HistoryWithOpenAgent =
        "{\"type\":\"assistant\",\"uuid\":\"u2\",\"parentUuid\":\"u1\",\"isSidechain\":false,\"timestamp\":\"2026-10-23T10:00:02.000Z\"," +
        "\"sessionId\":\"s1\",\"message\":{\"model\":\"claude-haiku-4-5-20251001\",\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\"," +
        "\"content\":[{\"type\":\"tool_use\",\"id\":\"toolu_open1\",\"name\":\"Agent\",\"input\":{\"description\":\"Count files\"," +
        "\"prompt\":\"Count the files.\",\"run_in_background\":true}}]}}";

    private static string SubagentText(string parent, string id, string text) =>
        "{\"type\":\"assistant\",\"message\":{\"id\":\"" + id + "\",\"type\":\"message\",\"role\":\"assistant\"," +
        "\"content\":[{\"type\":\"text\",\"text\":\"" + text + "\"}]},\"parent_tool_use_id\":\"" + parent + "\",\"session_id\":\"s1\"}";

    [Fact]
    public void A_live_line_of_the_agent_brings_back_an_agent_the_history_stopped()
    {
        var conversation = new ChatConversation();
        conversation.LoadTranscript(new[] { HistoryWithOpenAgent });
        var agent = Assert.Single(conversation.Agents);
        Assert.Equal(AgentStatus.Stopped, agent.Status);

        conversation.AppendLine(SubagentText("toolu_open1", "msg_sub1", "Still counting."));

        Assert.Equal(AgentStatus.Running, agent.Status);
        Assert.Equal(1, conversation.RunningAgents);
    }

    [Fact]
    public void A_line_of_a_nested_agent_brings_back_its_callers_the_history_stopped()
    {
        var conversation = new ChatConversation();
        conversation.LoadTranscript(new[] { HistoryWithOpenAgent });
        var outer = Assert.Single(conversation.Agents);

        // the live claude: the stopped agent calls another one, which then works
        conversation.AppendLine(
            "{\"type\":\"assistant\",\"message\":{\"id\":\"msg_n1\",\"type\":\"message\",\"role\":\"assistant\"," +
            "\"content\":[{\"type\":\"tool_use\",\"id\":\"toolu_nested\",\"name\":\"Agent\",\"input\":{\"description\":\"Nested look\"}}]}," +
            "\"parent_tool_use_id\":\"toolu_open1\",\"session_id\":\"s1\"}");
        conversation.AppendLine(SubagentText("toolu_nested", "msg_n2", "Looking."));

        var nested = conversation.Agents[0];
        Assert.Equal("toolu_open1", nested.ParentId);
        Assert.Equal(AgentStatus.Running, nested.Status);
        Assert.Equal(AgentStatus.Running, outer.Status);
        Assert.Equal(2, conversation.RunningAgents);
    }

    [Fact]
    public void A_task_line_after_the_end_resumes_the_agent()
    {
        var lines = FixtureFiles.StreamLines("subagent");
        var conversation = Play(lines);
        var agent = Assert.Single(conversation.Agents);
        Assert.Equal(AgentStatus.Completed, agent.Status);

        // the agent continued (SendMessage): the claude reports its work again
        conversation.AppendLine(
            "{\"type\":\"system\",\"subtype\":\"task_progress\",\"task_id\":\"adb1f0b70b0f95ff6\",\"tool_use_id\":\"" + AgentCallId + "\"," +
            "\"description\":\"Reading d.txt\",\"usage\":{\"total_tokens\":27000,\"tool_uses\":4,\"duration_ms\":7000}}");

        Assert.Equal(AgentStatus.Running, agent.Status);
        Assert.Equal("Reading d.txt", agent.Activity);
        Assert.Equal(1, conversation.RunningAgents);
    }

    [Fact]
    public void Clear_empties_the_agents()
    {
        var conversation = Play(new[] { ForegroundCall });
        Assert.Single(conversation.Agents);

        conversation.Clear();

        Assert.Empty(conversation.Agents);
        Assert.Equal(0, conversation.RunningAgents);
    }

    [Fact]
    public void The_parser_reads_the_task_lines()
    {
        var lines = FixtureFiles.StreamLines("subagent");
        var parser = new StreamJsonParser();

        var started = Assert.IsType<AgentTaskStarted>(Assert.Single(parser.Parse(lines.First(l => l.Contains("\"task_started\"")))));
        Assert.Equal(new AgentTaskStarted("adb1f0b70b0f95ff6", AgentCallId, Description, "general-purpose", true, 1, "local_agent"), started);

        var progress = Assert.IsType<AgentTaskProgress>(Assert.Single(parser.Parse(lines.First(l => l.Contains("\"task_progress\"")))));
        Assert.Equal(new AgentTaskProgress("adb1f0b70b0f95ff6", AgentCallId, "Reading a.txt", 24803, 1, 2832), progress);

        var updated = Assert.IsType<AgentTaskUpdated>(Assert.Single(parser.Parse(lines.First(l => l.Contains("\"task_updated\"")))));
        Assert.Equal(new AgentTaskUpdated("adb1f0b70b0f95ff6", "completed"), updated);

        var finished = Assert.IsType<AgentTaskFinished>(Assert.Single(parser.Parse(lines.First(l => l.Contains("\"task_notification\"")))));
        Assert.Equal("adb1f0b70b0f95ff6", finished.TaskId);
        Assert.Equal(AgentCallId, finished.ToolUseId);
        Assert.Equal("completed", finished.Status);
        Assert.StartsWith("a.txt:", finished.Summary);
        Assert.Equal((26316L, 3, 5850L), (finished.Tokens!.Value, finished.ToolUses!.Value, finished.DurationMs!.Value));
    }

    [Fact]
    public void The_notification_text_reads_into_a_finished_task()
    {
        const string text =
            "<task-notification>\n<task-id>t9</task-id>\n<tool-use-id>toolu_9</tool-use-id>\n<output-file>x</output-file>\n" +
            "<status>killed</status>\n<summary>Agent \"x\" was stopped</summary>\n" +
            "<usage><subagent_tokens>12</subagent_tokens><tool_uses>1</tool_uses><duration_ms>34</duration_ms></usage>\n</task-notification>";

        var finished = TaskNotificationText.Read(text);

        Assert.NotNull(finished);
        Assert.Equal(new AgentTaskFinished("t9", "toolu_9", "killed", "Agent \"x\" was stopped", 12, 1, 34), finished);
        Assert.Null(TaskNotificationText.Read("hello"));
    }
}
