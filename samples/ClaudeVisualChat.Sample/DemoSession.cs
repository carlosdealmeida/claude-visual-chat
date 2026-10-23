using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ClaudeVisualChat.Sample;

/// <summary>
/// A made-up session for the Agent map, the context ring and the attached files, which no short recording shows: the
/// main agent runs a plan's tasks in background agents, one of them calls a helper, and most of the context is used.
/// The lines have the shape <c>claude -p</c> and Claude Code give them (see the <c>subagent</c> recording), in both
/// forms: as the stream (<see cref="Stream"/>) and as the session file (<see cref="Transcript"/>).
/// </summary>
internal static class DemoSession
{
    public const string Scenario = "agents";

    /// <summary>What the user asked; the player submits it before the claude's first line.</summary>
    public const string Prompt = "Run the plan's tasks in background agents, and review each one when it is done.";

    /// <summary>The file the reply box holds once the session has played, which the sample "uploads" when it is sent.</summary>
    public const string FileName = "release-notes.md";

    public const string FileMessage = "Check the release notes against Task 3.";

    private const string SessionId = "6f1d2c3b-0a4e-4d5f-9e8a-7b6c5d4e3f21";
    private const string MainModel = "claude-opus-5-5";
    private const string Sonnet = "claude-sonnet-5-5";
    private const string Haiku = "claude-haiku-4-5-20251001";
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 23, 14, 0, 0, TimeSpan.Zero);

    /// <summary>Seconds from the start: the session's last line.</summary>
    private const int End = 1360;

    /// <summary>The context the claude reports at the end: 142,000 of 200,000 tokens, 71 % (the ring's 75 % step).</summary>
    private const long ContextTokens = 142_000;
    private const long ContextWindow = 200_000;

    private const string ReviewResult =
        "### Spec Compliance\n" +
        "Compliant. I found nothing missing and nothing extra.\n\n" +
        "- **`AgentInfo`** takes its description and type from the Agent call, and the `task_started` line fills the task id, " +
        "the depth and whether it runs in the background.\n" +
        "- **`task_progress`** moves the activity, the tokens, the tool uses and the time.\n" +
        "- **The session file** gives the same agent back from the `<task-notification>` text, which stays out of the list.\n\n" +
        "### Strengths\n" +
        "- The tests play the recorded `subagent` session, live and from its session file.\n\n" +
        "### Issues\n" +
        "None.";

    /// <summary>An agent of the session; <see cref="EndAt"/> null while it is still at work at the end.</summary>
    private sealed record Agent(
        string Id, string TaskId, string Description, string Type, string Model, int StartAt, int? EndAt,
        long Tokens, int ToolUses, string Status, string? Result, string? Parent = null, string? Activity = null);

    private static readonly Agent[] Agents =
    {
        new Agent("toolu_demo_01", "a1d4e5f6a7b8c9d01", "Implement Task 1: the agent map model", "general-purpose", Sonnet,
            5, 394, 86_012, 31, "completed", "Done: `AgentInfo`, the task lines and the session file's notification. 18 tests, all green."),
        new Agent("toolu_demo_02", "a1d4e5f6a7b8c9d02", "Review Task 1 (spec + quality)", "general-purpose", Sonnet,
            400, 444, 68_517, 12, "completed", ReviewResult),
        new Agent("toolu_demo_03", "a1d4e5f6a7b8c9d03", "Implement Task 2: the chip and the modal", "general-purpose", Sonnet,
            450, 1165, 215_712, 58, "completed", "Done: the \"N agents\" chip and the Agent map, with the agent's card. 6 UI tests, all green."),
        new Agent("toolu_demo_04", "a1d4e5f6a7b8c9d04", "Measure the extension's thumbnails", "Explore", Haiku,
            470, 581, 24_301, 9, "killed", null),
        new Agent("toolu_demo_05", "a1d4e5f6a7b8c9d05", "Review Task 2 (spec + quality)", "general-purpose", Sonnet,
            1170, null, 61_834, 14, "running", null, Activity: "Reading AgentMapPanel.cs"),
        new Agent("toolu_demo_07", "a1d4e5f6a7b8c9d07", "Implement Task 3: the context ring", "general-purpose", Sonnet,
            1200, null, 57_903, 20, "running", null, Activity: "Editing ContextRing.cs"),
        new Agent("toolu_demo_06", "a1d4e5f6a7b8c9d06", "Compare the map with the screenshots", "Explore", Haiku,
            1295, null, 22_410, 6, "running", null, Parent: "toolu_demo_05", Activity: "Searching index.css"),
    };

    /// <summary>The bytes of <see cref="FileName"/>.</summary>
    public static byte[] FileContent => System.Text.Encoding.UTF8.GetBytes(
        "# Release notes\n\n- The Agent map: the chip, the map and each agent's card.\n- The context ring.\n- Attached files.\n");

    // ---- the stream ----

    /// <summary>The session as <c>claude -p --output-format stream-json</c> writes it.</summary>
    public static IReadOnlyList<string> Stream()
    {
        var lines = new List<(int At, int Order, object Line)>();
        var order = 0;
        void Add(int at, object line) => lines.Add((at, order++, line));

        Add(0, new
        {
            type = "system", subtype = "init", session_id = SessionId, model = MainModel, cwd = Recordings.WorkingDirectory,
            permissionMode = "default", claude_code_version = "2.1.309", tools = new[] { "Agent", "Bash", "Edit", "Read", "Write" },
            timestamp = Time(0),
        });
        Add(3, AssistantText("msg_demo_00", "I'll run each task in a background agent, and review it when it is done.", 3));

        foreach (var agent in Agents)
        {
            Add(agent.StartAt, new
            {
                type = "assistant",
                message = new
                {
                    model = agent.Parent == null ? MainModel : Sonnet, id = "msg_" + agent.Id, type = "message", role = "assistant",
                    content = new[] { AgentCall(agent) },
                },
                parent_tool_use_id = agent.Parent, session_id = SessionId, timestamp = Time(agent.StartAt),
            });
            Add(agent.StartAt, new
            {
                type = "system", subtype = "task_started", task_id = agent.TaskId, tool_use_id = agent.Id,
                description = agent.Description, subagent_type = agent.Type, is_backgrounded = true,
                spawn_depth = agent.Parent == null ? 1 : 2, task_type = "local_agent", session_id = SessionId,
                timestamp = Time(agent.StartAt),
            });
            Add(agent.StartAt + 1, new
            {
                type = "user",
                message = new { role = "user", content = new[] { new { tool_use_id = agent.Id, type = "tool_result", content = new[] { new { type = "text", text = Launched(agent) } } } } },
                parent_tool_use_id = agent.Parent, session_id = SessionId, timestamp = Time(agent.StartAt + 1),
                tool_use_result = new
                {
                    isAsync = true, status = "async_launched", agentId = agent.TaskId, description = agent.Description,
                    resolvedModel = agent.Model,
                },
            });
            var lastProgress = agent.EndAt ?? End - 20;
            Add(lastProgress, new
            {
                type = "system", subtype = "task_progress", task_id = agent.TaskId, tool_use_id = agent.Id,
                description = agent.Activity ?? "Running the tests", subagent_type = agent.Type,
                usage = new { total_tokens = agent.Tokens, tool_uses = agent.ToolUses, duration_ms = (lastProgress - agent.StartAt) * 1000L },
                session_id = SessionId, timestamp = Time(lastProgress),
            });
            if (agent.EndAt is int end)
            {
                Add(end, new
                {
                    type = "system", subtype = "task_notification", task_id = agent.TaskId, tool_use_id = agent.Id,
                    status = agent.Status, summary = agent.Result ?? Stopped(agent),
                    usage = new { total_tokens = agent.Tokens, tool_uses = agent.ToolUses, duration_ms = (end - agent.StartAt) * 1000L },
                    session_id = SessionId, timestamp = Time(end),
                });
            }
        }

        Add(End - 2, AssistantText("msg_demo_99",
            "Tasks 1 and 2 are done and Task 1 is reviewed. The Task 2 review (with a helper comparing the screenshots) " +
            "and Task 3 are still at work; I'll report when they finish.", End - 2));
        Add(End - 1, new
        {
            type = "result", subtype = "success", is_error = false, duration_ms = (End - 1) * 1000L, num_turns = 9,
            session_id = SessionId, result = "", timestamp = Time(End - 1),
        });
        // the answer to the get_context_usage request a host sends: what fills the ring and the /context panel
        Add(End, new
        {
            type = "control_response",
            response = new
            {
                subtype = "success", request_id = "demo_context",
                response = new
                {
                    totalTokens = ContextTokens, maxTokens = ContextWindow, percentage = 100.0 * ContextTokens / ContextWindow,
                    categories = new object[]
                    {
                        new { name = "System prompt", tokens = 9_000, kind = "used" },
                        new { name = "Messages", tokens = ContextTokens - 9_000, kind = "used" },
                        new { name = "Free space", tokens = ContextWindow - ContextTokens, kind = "free" },
                    },
                },
            },
            timestamp = Time(End),
        });

        return lines.OrderBy(l => l.At).ThenBy(l => l.Order).Select(l => JsonSerializer.Serialize(l.Line)).ToList();
    }

    // ---- the session file ----

    /// <summary>
    /// The session as Claude Code keeps it: the main agent's messages, the Agent calls and their results, and a
    /// <c>&lt;task-notification&gt;</c> message for each agent that ended. The helper's call is in its caller's own
    /// file, not here; the agents still at work have no end, so the history shows them stopped.
    /// </summary>
    public static IReadOnlyList<string> Transcript()
    {
        var lines = new List<(int At, int Order, object Line)>();
        var order = 0;
        void Add(int at, object line) => lines.Add((at, order++, line));

        Add(0, Entry("user", 0, new { role = "user", content = Prompt }));
        Add(3, Entry("assistant", 3, AssistantMessage("msg_demo_00", new object[] { new { type = "text", text = "I'll run each task in a background agent, and review it when it is done." } })));
        foreach (var agent in Agents.Where(a => a.Parent == null))
        {
            Add(agent.StartAt, Entry("assistant", agent.StartAt, AssistantMessage("msg_" + agent.Id, new[] { AgentCall(agent) })));
            Add(agent.StartAt + 1, Entry("user", agent.StartAt + 1,
                new { role = "user", content = new[] { new { tool_use_id = agent.Id, type = "tool_result", content = new[] { new { type = "text", text = Launched(agent) } } } } },
                new { isAsync = true, status = "async_launched", agentId = agent.TaskId, description = agent.Description, resolvedModel = agent.Model }));
            if (agent.EndAt is int end)
            {
                var text =
                    "<task-notification>\n<task-id>" + agent.TaskId + "</task-id>\n<tool-use-id>" + agent.Id + "</tool-use-id>\n" +
                    "<status>" + agent.Status + "</status>\n<summary>Agent \"" + agent.Description + "\" " +
                    (agent.Result == null ? "was stopped" : "finished") + "</summary>\n" +
                    (agent.Result != null ? "<result>" + agent.Result + "</result>\n" : "") +
                    "<usage><subagent_tokens>" + agent.Tokens + "</subagent_tokens><tool_uses>" + agent.ToolUses +
                    "</tool_uses><duration_ms>" + (end - agent.StartAt) * 1000L + "</duration_ms></usage>\n</task-notification>";
                Add(end, Entry("user", end, new { role = "user", content = text }));
            }
        }
        Add(End - 2, Entry("assistant", End - 2, AssistantMessage("msg_demo_99", new object[]
        {
            new
            {
                type = "text",
                text = "Tasks 1 and 2 are done and Task 1 is reviewed. The Task 2 review (with a helper comparing the screenshots) " +
                       "and Task 3 are still at work; I'll report when they finish.",
            },
        })));

        // each entry follows the one before it
        var sorted = lines.OrderBy(l => l.At).ThenBy(l => l.Order).Select(l => l.Line).ToList();
        var result = new List<string>();
        string? parent = null;
        for (var i = 0; i < sorted.Count; i++)
        {
            var uuid = "00000000-0000-4000-8000-" + (i + 1).ToString("D12");
            var json = JsonSerializer.SerializeToNode(sorted[i])!.AsObject();
            json["uuid"] = uuid;
            json["parentUuid"] = parent;
            result.Add(json.ToJsonString());
            parent = uuid;
        }
        return result;
    }

    // ---- the parts ----

    private static string Time(int seconds) => Start.AddSeconds(seconds).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    private static object AgentCall(Agent agent) => new
    {
        type = "tool_use", id = agent.Id, name = "Agent",
        input = new
        {
            description = agent.Description, subagent_type = agent.Type,
            prompt = agent.Description + ". Work in " + Recordings.WorkingDirectory + " and report what you did.",
            run_in_background = true,
        },
    };

    private static string Launched(Agent agent) =>
        "Async agent launched successfully.\nagentId: " + agent.TaskId + "\nThe agent is working in the background. " +
        "You will be notified automatically when it completes.";

    private static string Stopped(Agent agent) => "Agent \"" + agent.Description + "\" was stopped";

    private static object AssistantText(string id, string text, int at) => new
    {
        type = "assistant",
        message = AssistantMessage(id, new object[] { new { type = "text", text } }),
        parent_tool_use_id = (string?)null, session_id = SessionId, timestamp = Time(at),
    };

    private static object AssistantMessage(string id, object[] content) => new
    {
        model = MainModel, id, type = "message", role = "assistant", content,
    };

    private static object Entry(string type, int at, object message, object? toolUseResult = null) => toolUseResult == null
        ? new { type, isSidechain = false, timestamp = Time(at), sessionId = SessionId, cwd = Recordings.WorkingDirectory, message }
        : (object)new { type, isSidechain = false, timestamp = Time(at), sessionId = SessionId, cwd = Recordings.WorkingDirectory, message, toolUseResult };
}
