using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeVisualChat.Sample;

/// <summary>
/// Made-up sessions for the pieces of 0.6.0 that no recording has, with the lines <c>claude -p</c> and Claude Code write: the
/// plan the user can comment on, a compaction, a message from another session, the "Switched to" band
/// (<c>system/model_refusal_fallback</c>), and the fast mode bolt (<c>fast_mode_state</c>). <see cref="Tour"/> plays them in one
/// conversation (the stream and the session file); <see cref="AutoMode"/> is an empty conversation in auto mode, which shows the
/// "Auto mode is enabled" notice. Bookmarks are the user's: <see cref="TheReplies"/> names what the sample bookmarks.
/// </summary>
internal static class TourSession
{
    public const string Tour = "tour";
    public const string AutoMode = "auto-mode";

    private const string SessionId = "7a2e9c41-5b3d-4f60-8c1a-2d9e0b7f6a15";
    private const string Opus = "claude-opus-5-5";
    private const string Sonnet = "claude-sonnet-5-5";
    private const string PlanFile = @"C:\Users\demo\.claude\plans\release-notes.md";
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 29, 9, 30, 0, TimeSpan.Zero);

    private const string Prompt = "Plan the release notes for 0.6.0, then carry on once the plan is approved.";

    private const string Plan =
        "# Plan: release notes for 0.6.0\n\n" +
        "## Context\n" +
        "The version adds the plan with comments, compaction cards, bookmarks, the fast mode bolt and the auto mode notice. " +
        "The notes must say what each one does for the user.\n\n" +
        "## Steps\n" +
        "1. Write `release-notes.md` with a section per piece, in the order a user meets them.\n" +
        "2. Check each claim against the CHANGELOG, and drop any that the CHANGELOG does not make.\n" +
        "3. Keep the deliberate differences from the extension in a list of their own.\n\n" +
        "## Verification\n" +
        "Read the file once more and run the full test suite.\n";

    private const string Summary =
        "This session is being continued from a previous conversation that ran out of context. The summary below covers the earlier " +
        "portion of the conversation.\n\nThe user asked for the release notes of 0.6.0. The plan was approved: one section per piece, " +
        "each claim checked against the CHANGELOG, the deliberate differences in a list of their own.";

    private const string CrossSession =
        "<cross-session-message from=\"docs\" from-session=\"3f9a0c\">\nThe docs session is done with the screenshots; " +
        "the README can point at them now.\n</cross-session-message>";

    /// <summary>The uuid of the reply the sample bookmarks (what the user does with the button under it).</summary>
    public const string BookmarkedReply = "9d6f1a22-0000-4000-8000-000000000004";

    public static bool Is(string scenario) => scenario == Tour || scenario == AutoMode;

    public static IReadOnlyList<string> Stream(string scenario) =>
        scenario == AutoMode ? new[] { JsonSerializer.Serialize(Init("auto")) } : Lines(transcript: false);

    public static IReadOnlyList<string> Transcript(string scenario) =>
        scenario == AutoMode ? Array.Empty<string>() : Chain(Lines(transcript: true));

    // ---- the lines ----

    private static IReadOnlyList<string> Lines(bool transcript)
    {
        var lines = new List<object>();
        if (transcript) lines.Add(Entry("user", 0, new { role = "user", content = Prompt }));
        else lines.Add(Init("default"));

        lines.Add(Assistant(1, "9d6f1a22-0000-4000-8000-000000000001", Opus,
            new { type = "text", text = "I'll plan it first, and ask you to approve the plan." }));
        lines.Add(Assistant(2, "9d6f1a22-0000-4000-8000-000000000002", Opus,
            new { type = "tool_use", id = "toolu_tour_plan", name = "ExitPlanMode", input = new { plan = Plan, planFilePath = PlanFile } }));
        if (!transcript)
        {
            lines.Add(new
            {
                type = "control_request", request_id = "tour-plan-1",
                request = new
                {
                    subtype = "can_use_tool", tool_name = "ExitPlanMode", display_name = "ExitPlanMode",
                    input = new { plan = Plan, planFilePath = PlanFile }, tool_use_id = "toolu_tour_plan", requires_user_interaction = true,
                },
            });
        }
        lines.Add(User(4, new[] { new { tool_use_id = "toolu_tour_plan", type = "tool_result", content = "User has approved your plan. You can now start coding." } }));
        lines.Add(Assistant(5, "9d6f1a22-0000-4000-8000-000000000003", Opus,
            new { type = "text", text = "The plan is approved. I'll write the notes now; the conversation is long, so I'll compact it first." }));

        // the compaction: the boundary, then the summary the claude writes for itself
        if (!transcript) lines.Add(new { type = "system", subtype = "status", status = "compacting", session_id = SessionId, timestamp = Time(6) });
        lines.Add(new
        {
            type = "system", subtype = "compact_boundary", session_id = SessionId, timestamp = Time(7),
            compact_metadata = new { trigger = "auto", pre_tokens = 168_400, post_tokens = 9_100 },
        });
        lines.Add(transcript
            ? Entry("user", 7, new { role = "user", content = Summary }, isCompactSummary: true)
            : new
            {
                type = "user", message = new { role = "user", content = Summary }, parent_tool_use_id = (string?)null, isSynthetic = true,
                session_id = SessionId, timestamp = Time(7),
            });

        // a message from another session of the same person
        lines.Add(transcript
            ? Entry("user", 9, new { role = "user", content = CrossSession }, isMeta: true, origin: new { kind = "peer", name = "Docs session" })
            : new
            {
                type = "user", message = new { role = "user", content = CrossSession }, parent_tool_use_id = (string?)null, isMeta = true,
                origin = new { kind = "peer", name = "Docs session" }, session_id = SessionId, timestamp = Time(9),
            });

        // the claude's safeguards switch the model: live only, a session file never has it
        if (!transcript)
        {
            lines.Add(new
            {
                type = "system", subtype = "model_refusal_fallback", original_model = Opus, fallback_model = Sonnet,
                api_refusal_category = "cyber", session_id = SessionId, timestamp = Time(10),
            });
        }

        lines.Add(Assistant(12, BookmarkedReply, Sonnet, new
        {
            type = "text",
            text = "The release notes are written. Each piece of 0.6.0 has its section, and the deliberate differences from the " +
                   "extension are in a list of their own.\n\nRate this reply with the thumbs under it, or bookmark it to find it again.",
        }));
        if (!transcript)
        {
            lines.Add(new
            {
                type = "result", subtype = "success", is_error = false, duration_ms = 11_000L, num_turns = 4, result = "",
                fast_mode_state = "on", session_id = SessionId, timestamp = Time(13),
            });
        }
        return lines.Select(l => JsonSerializer.Serialize(l)).ToList();
    }

    private static object Init(string mode) => new
    {
        type = "system", subtype = "init", session_id = SessionId, model = Opus, cwd = Recordings.WorkingDirectory,
        permissionMode = mode, claude_code_version = "2.1.310", fast_mode_state = "on",
        tools = new[] { "Bash", "Edit", "ExitPlanMode", "Read", "Write" }, timestamp = Time(0),
    };

    private static object Assistant(int at, string uuid, string model, object block) => new
    {
        type = "assistant", uuid,
        message = new { model, id = "msg_tour_" + at, type = "message", role = "assistant", content = new[] { block } },
        parent_tool_use_id = (string?)null, session_id = SessionId, timestamp = Time(at),
    };

    private static object User(int at, object[] content) => new
    {
        type = "user", message = new { role = "user", content }, parent_tool_use_id = (string?)null, session_id = SessionId, timestamp = Time(at),
    };

    private static JsonObject Entry(string type, int at, object message, bool isMeta = false, bool isCompactSummary = false, object? origin = null)
    {
        var node = JsonSerializer.SerializeToNode(new
        {
            type, isSidechain = false, timestamp = Time(at), sessionId = SessionId, cwd = Recordings.WorkingDirectory, message,
        })!.AsObject();
        if (isMeta) node["isMeta"] = true;
        if (isCompactSummary) node["isCompactSummary"] = true;
        if (origin != null) node["origin"] = JsonSerializer.SerializeToNode(origin);
        return node;
    }

    /// <summary>
    /// The session file's form of the lines: the ones the stream wrote get the file's own fields, and each line follows the one
    /// before it (the claude's replies keep their uuid).
    /// </summary>
    private static IReadOnlyList<string> Chain(IReadOnlyList<string> lines)
    {
        var result = new List<string>();
        string? parent = null;
        for (var i = 0; i < lines.Count; i++)
        {
            var json = JsonNode.Parse(lines[i])!.AsObject();
            if (json["isSidechain"] == null)
            {
                json["isSidechain"] = false;
                json["sessionId"] = SessionId;
                json["cwd"] = Recordings.WorkingDirectory;
            }
            var uuid = json["uuid"]?.GetValue<string>() ?? "00000000-0000-4000-8000-" + (i + 1).ToString("D12");
            json["uuid"] = uuid;
            json["parentUuid"] = parent;
            result.Add(json.ToJsonString());
            parent = uuid;
        }
        return result;
    }

    private static string Time(int seconds) => Start.AddSeconds(seconds).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
}
