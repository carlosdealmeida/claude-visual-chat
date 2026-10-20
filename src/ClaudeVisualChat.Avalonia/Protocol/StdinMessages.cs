using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ClaudeVisualChat.Protocol
{
    /// <summary>
    /// Builds the lines to write to the stdin of <c>claude -p --input-format stream-json</c>. Each one is a single
    /// JSON line without the trailing line feed. Text outside ASCII is escaped (<c>\u00E3</c>), so the line reads
    /// the same whatever encoding the host gives the process stdin.
    /// </summary>
    public static class StdinMessages
    {
        public const string DefaultDenyMessage = "The user denied this tool use.";

        public static string UserMessage(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return Write(w =>
            {
                w.WriteString("type", "user");
                w.WriteStartObject("message");
                w.WriteString("role", "user");
                w.WriteString("content", text);
                w.WriteEndObject();
                w.WriteNull("parent_tool_use_id");
                w.WriteString("session_id", "default");
            });
        }

        /// <summary>
        /// The answer to a <c>can_use_tool</c> request: <c>allow</c> sends the input back unchanged, "allow always"
        /// also sends the request's suggestions as <c>updatedPermissions</c>, and <c>deny</c> sends the message.
        /// </summary>
        public static string PermissionResponse(PermissionAnsweredEventArgs e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            var request = e.Request;
            return Write(w =>
            {
                w.WriteString("type", "control_response");
                w.WriteStartObject("response");
                w.WriteString("subtype", "success");
                w.WriteString("request_id", request.RequestId);
                w.WriteStartObject("response");
                if (e.Decision == PermissionDecision.Deny)
                {
                    w.WriteString("behavior", "deny");
                    w.WriteString("message", string.IsNullOrWhiteSpace(e.Message) ? DefaultDenyMessage : e.Message);
                }
                else
                {
                    w.WriteString("behavior", "allow");
                    w.WritePropertyName("updatedInput");
                    if (request.Input.ValueKind == JsonValueKind.Undefined)
                    {
                        w.WriteStartObject();
                        w.WriteEndObject();
                    }
                    else
                    {
                        request.Input.WriteTo(w);
                    }
                    if (e.Decision == PermissionDecision.AllowAlways && request.Suggestions is { } suggestions
                        && suggestions.ValueKind == JsonValueKind.Array)
                    {
                        w.WritePropertyName("updatedPermissions");
                        suggestions.WriteTo(w);
                    }
                }
                w.WriteEndObject();
                w.WriteEndObject();
            });
        }

        public static string SetPermissionMode(string requestId, string mode) =>
            ControlRequest(requestId, "set_permission_mode", w => w.WriteString("mode", mode));

        public static string SetModel(string requestId, string model) =>
            ControlRequest(requestId, "set_model", w => w.WriteString("model", model));

        public static string Interrupt(string requestId) => ControlRequest(requestId, "interrupt", null);

        public static string Initialize(string requestId) => ControlRequest(requestId, "initialize", null);

        private static string ControlRequest(string requestId, string subtype, Action<Utf8JsonWriter>? fields)
        {
            if (requestId == null) throw new ArgumentNullException(nameof(requestId));
            return Write(w =>
            {
                w.WriteString("type", "control_request");
                w.WriteString("request_id", requestId);
                w.WriteStartObject("request");
                w.WriteString("subtype", subtype);
                fields?.Invoke(w);
                w.WriteEndObject();
            });
        }

        private static string Write(Action<Utf8JsonWriter> body)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    body(writer);
                    writer.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
