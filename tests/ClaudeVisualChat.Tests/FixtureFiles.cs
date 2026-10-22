using System;
using System.IO;

namespace ClaudeVisualChat.Tests
{
    /// <summary>Reads the recorded, sanitized sessions copied next to the test assembly.</summary>
    public static class FixtureFiles
    {
        public static readonly string[] Scenarios =
        {
            "edit", "bash", "subagent", "error", "denied", "interrupted", "mode-and-model",
            "image", "commands", "effort",
        };

        public static string Directory =>
            Path.Combine(AppContext.BaseDirectory, "fixtures");

        public static string[] StreamLines(string scenario) =>
            ReadLines(scenario + ".stream.ndjson");

        public static string[] TranscriptLines(string scenario) =>
            ReadLines(scenario + ".transcript.jsonl");

        private static string[] ReadLines(string fileName)
        {
            var lines = File.ReadAllLines(Path.Combine(Directory, fileName));
            return Array.FindAll(lines, l => l.Trim().Length > 0);
        }
    }
}
