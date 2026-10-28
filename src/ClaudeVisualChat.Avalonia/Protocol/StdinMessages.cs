using System;
using System.Collections.Generic;
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

        /// <summary>
        /// A user turn. Without images the content is the plain text; with images it is an array of the image
        /// blocks followed by the text block (left out when the text is empty).
        /// </summary>
        public static string UserMessage(string text, IReadOnlyList<ChatImage>? images = null)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return User(text, images, null);
        }

        /// <summary>
        /// The message the user sent, as <see cref="UserMessage(string, IReadOnlyList{ChatImage})"/> writes its text and images,
        /// with its <see cref="MessageSubmittedEventArgs.Uuid"/>: the claude then tells what became of it (<c>command_lifecycle</c>),
        /// and a message held in <see cref="ChatConversation.QueuedMessages"/> goes into the list as soon as the claude takes it.
        /// Each of its files that landed (<see cref="ChatFile.RemotePath"/>) is named by a line of its own after the text
        /// (<see cref="AttachedFileLine.Format"/>), so write it once they all have.
        /// </summary>
        public static string UserMessage(MessageSubmittedEventArgs e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            var text = e.Text;
            foreach (var file in e.Files)
            {
                if (string.IsNullOrEmpty(file.RemotePath)) continue;
                var marker = AttachedFileLine.Format(file.RemotePath!);
                text = text.Length == 0 ? marker : text + "\n" + marker;
            }
            return User(text, e.Images, e.Uuid);
        }

        private static string User(string text, IReadOnlyList<ChatImage>? images, string? uuid)
        {
            return Write(w =>
            {
                w.WriteString("type", "user");
                w.WriteStartObject("message");
                w.WriteString("role", "user");
                if (images == null || images.Count == 0)
                {
                    w.WriteString("content", text);
                }
                else
                {
                    w.WriteStartArray("content");
                    foreach (var image in images)
                    {
                        w.WriteStartObject();
                        w.WriteString("type", "image");
                        w.WriteStartObject("source");
                        w.WriteString("type", "base64");
                        w.WriteString("media_type", image.MediaType);
                        w.WriteString("data", image.Data);
                        w.WriteEndObject();
                        w.WriteEndObject();
                    }
                    if (text.Length > 0)
                    {
                        w.WriteStartObject();
                        w.WriteString("type", "text");
                        w.WriteString("text", text);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }
                w.WriteEndObject();
                w.WriteNull("parent_tool_use_id");
                w.WriteString("session_id", "default");
                if (uuid != null) w.WriteString("uuid", uuid);
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

        /// <summary>Sets the effort level for the session; null clears it (back to the model's default).</summary>
        public static string ApplyEffort(string requestId, string? effort) =>
            ControlRequest(requestId, "apply_flag_settings", w =>
            {
                w.WriteStartObject("settings");
                if (effort == null) w.WriteNull("effortLevel");
                else w.WriteString("effortLevel", effort);
                w.WriteEndObject();
            });

        public static string GetSettings(string requestId) => ControlRequest(requestId, "get_settings", null);

        public static string GetContextUsage(string requestId) => ControlRequest(requestId, "get_context_usage", null);

        public static string RemoteControl(string requestId, bool enabled) =>
            ControlRequest(requestId, "remote_control", w => w.WriteBoolean("enabled", enabled));

        public static string GetChromeStatus(string requestId) => ControlRequest(requestId, "get_chrome_dialog", null);

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
