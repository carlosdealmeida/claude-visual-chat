using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace ClaudeVisualChat.Sample;

/// <summary>
/// A made-up session for the output of a running command (0.8.0), which no recording has: a Bash <c>npm test</c> that the claude runs
/// in the foreground as a task (<c>system/task_started</c> of the type <c>local_bash</c>) for <see cref="RunsFor"/>. While it runs, the
/// view asks the host for what it has printed (<see cref="ChatConversation.TaskOutputRequested"/>); <see cref="Answer"/> is the claude's
/// answer, with two more lines each time. Then the call's result and the turn's result come (<see cref="FinishAsync"/>). The session
/// file has the whole run, ended.
/// </summary>
internal sealed class LiveOutputSession
{
    public const string Scenario = "live-output";

    /// <summary>How long the command runs, from the end of the stream.</summary>
    public static readonly TimeSpan RunsFor = TimeSpan.FromSeconds(8);

    private const string SessionId = "4c81d2f7-9e3a-4b15-a6d0-7f2b8e5c1a93";
    private const string Opus = "claude-opus-5-5";
    private const string ToolUseId = "toolu_live_npm_test";
    private const string TaskId = "b7q2k9x4m";
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 11, 14, 14, 0, 0, TimeSpan.Zero);

    private const string Prompt = "Run the tests and tell me if anything fails.";

    /// <summary>What <c>npm test</c> prints, two lines at a time.</summary>
    private static readonly string[] Printed =
    {
        "> demo-app@1.0.0 test",
        "> jest --verbose",
        "PASS src/cart.test.js",
        "  ✓ adds an item to an empty cart (4 ms)",
        "  ✓ sums the prices of every item (2 ms)",
        "  ✓ applies a discount code once (3 ms)",
        "PASS src/checkout.test.js",
        "  ✓ asks for an address before paying (12 ms)",
        "  ✓ refuses an expired card (8 ms)",
        "  ✓ sends the receipt by email (21 ms)",
        "PASS src/search.test.js",
        "  ✓ finds a product by a word of its name (6 ms)",
        "  ✓ ignores the case of the letters (2 ms)",
        "  ✓ says so when nothing matches (1 ms)",
        "Test Suites: 3 passed, 3 total",
        "Tests:       9 passed, 9 total",
    };

    private int _shown;

    public LiveOutputSession(ChatConversation conversation)
    {
        Conversation = conversation ?? throw new ArgumentNullException(nameof(conversation));
    }

    /// <summary>The conversation the command runs in.</summary>
    public ChatConversation Conversation { get; }

    // ---- the lines ----

    public static IReadOnlyList<string> Stream() => Lines(transcript: false);

    public static IReadOnlyList<string> Transcript()
    {
        // the session file's lines, each following the one before it
        var result = new List<string>();
        string? parent = null;
        var lines = Lines(transcript: true);
        for (var i = 0; i < lines.Count; i++)
        {
            var json = JsonNode.Parse(lines[i])!.AsObject();
            var uuid = json["uuid"]?.GetValue<string>() ?? "00000000-0000-4000-8000-" + (i + 1).ToString("D12");
            json["uuid"] = uuid;
            json["parentUuid"] = parent;
            json["isSidechain"] = false;
            json["sessionId"] = SessionId;
            json["cwd"] = Recordings.WorkingDirectory;
            result.Add(json.ToJsonString());
            parent = uuid;
        }
        return result;
    }

    private static IReadOnlyList<string> Lines(bool transcript)
    {
        var lines = new List<object>();
        if (transcript) lines.Add(new { type = "user", timestamp = Time(0), message = new { role = "user", content = Prompt } });
        else lines.Add(Init());
        lines.Add(Assistant(1, new { type = "text", text = "I'll run the test suite." }));
        lines.Add(Assistant(2, new
        {
            type = "tool_use", id = ToolUseId, name = "Bash",
            input = new { command = "npm test", description = "Runs the test suite" },
        }));
        if (!transcript)
        {
            // the claude runs the command as a task of its own: the view can ask what it has printed
            lines.Add(new
            {
                type = "system", subtype = "task_started", task_id = TaskId, tool_use_id = ToolUseId, description = "Runs the test suite",
                is_backgrounded = false, task_type = "local_bash", session_id = SessionId, timestamp = Time(2),
            });
            return Serialize(lines);
        }
        lines.Add(ToolResult(10));
        lines.Add(Assistant(11, new { type = "text", text = "All 9 tests pass, in 3 suites." }));
        return Serialize(lines);
    }

    /// <summary>The end of the run, live: the call's result, then the turn's.</summary>
    private static IReadOnlyList<string> End() => Serialize(new List<object>
    {
        ToolResult(10),
        new
        {
            type = "result", subtype = "success", is_error = false, duration_ms = 10_000L, num_turns = 2, result = "",
            session_id = SessionId, timestamp = Time(11),
        },
    });

    private static object Init() => new
    {
        type = "system", subtype = "init", session_id = SessionId, model = Opus, cwd = Recordings.WorkingDirectory,
        permissionMode = "default", claude_code_version = "2.1.323", tools = new[] { "Bash", "Edit", "Read", "Write" }, timestamp = Time(0),
    };

    private static object Assistant(int at, object block) => new
    {
        type = "assistant", uuid = "6e0b3a58-0000-4000-8000-" + at.ToString("D12"),
        message = new { model = Opus, id = "msg_live_" + at, type = "message", role = "assistant", content = new[] { block } },
        parent_tool_use_id = (string?)null, session_id = SessionId, timestamp = Time(at),
    };

    private static object ToolResult(int at) => new
    {
        type = "user",
        message = new { role = "user", content = new[] { new { tool_use_id = ToolUseId, type = "tool_result", content = Output(Printed.Length), is_error = false } } },
        parent_tool_use_id = (string?)null, session_id = SessionId, timestamp = Time(at),
    };

    private static string Output(int lines) => string.Join("\n", Printed.Take(lines));

    private static IReadOnlyList<string> Serialize(List<object> lines) => lines.Select(l => JsonSerializer.Serialize(l)).ToList();

    private static string Time(int seconds) => Start.AddSeconds(seconds).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    // ---- the command as it runs ----

    /// <summary>
    /// The claude's answer to the output request <paramref name="requestId"/>: what the command has printed so far, two lines more than
    /// the last time. It comes on a later UI job, as an answer from the claude's process would, never inside the request's handler.
    /// </summary>
    public void Answer(string requestId)
    {
        _shown = Math.Min(Printed.Length, _shown + 2);
        var output = Output(_shown);
        var line = JsonSerializer.Serialize(new
        {
            type = "control_response",
            response = new
            {
                subtype = "success", request_id = requestId,
                response = new { output, total_bytes = Encoding.UTF8.GetByteCount(output), truncated = false },
            },
        });
        Dispatcher.UIThread.Post(() => Conversation.AppendLine(line));
    }

    /// <summary>Waits while the command runs, then writes its result and the turn's. Cancelling stops it where it is.</summary>
    public async Task FinishAsync(CancellationToken ct)
    {
        await Task.Delay(RunsFor, ct);
        foreach (var line in End()) Conversation.AppendLine(line);
    }
}
