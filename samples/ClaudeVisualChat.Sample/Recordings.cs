using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Platform;

namespace ClaudeVisualChat.Sample;

/// <summary>
/// The recorded sessions of the tests (<c>tests/fixtures</c>), built into the sample: what <c>claude -p</c> wrote
/// (<c>.stream.ndjson</c>) and the session file Claude Code kept (<c>.transcript.jsonl</c>).
/// </summary>
internal static class Recordings
{
    public static readonly string[] Scenarios =
    {
        "edit", "bash", "subagent", "error", "denied", "interrupted", "mode-and-model",
        "image", "commands", "effort", "plan", "plan-auto", "plan-feedback", "queued", "hook-blocked",
        DemoSession.Scenario,
    };

    /// <summary>The folder the recordings were made in, after the recorder replaced the real one.</summary>
    public const string WorkingDirectory = @"C:\demo\app";

    public static IReadOnlyList<string> Stream(string scenario) =>
        scenario == DemoSession.Scenario ? DemoSession.Stream() : Read(scenario + ".stream.ndjson");

    public static IReadOnlyList<string> Transcript(string scenario) =>
        scenario == DemoSession.Scenario ? DemoSession.Transcript() : Read(scenario + ".transcript.jsonl");

    /// <summary>
    /// What the user typed in the recording, which the claude's output does not repeat: the user's messages of the
    /// session file, as the conversation reads them.
    /// </summary>
    public static IReadOnlyList<string> Prompts(string scenario)
    {
        var history = new ChatConversation();
        history.LoadTranscript(Transcript(scenario));
        return history.Items.OfType<UserMessageItem>().Select(i => i.Text).ToList();
    }

    private static IReadOnlyList<string> Read(string fileName)
    {
        using var stream = AssetLoader.Open(new Uri("avares://ClaudeVisualChat.Sample/Fixtures/" + fileName));
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Trim().Length > 0) lines.Add(line);
        }
        return lines;
    }
}
