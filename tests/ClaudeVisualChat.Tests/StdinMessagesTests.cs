using System;
using System.Linq;
using System.Text.Json;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests;

public class StdinMessagesTests
{
    private const string Tricky = "say \"hi\"\nnão \\ <b> & 'x' +1\ttab";

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static JsonElement Parse(string line)
    {
        Assert.DoesNotContain("\n", line);
        Assert.DoesNotContain("\r", line);
        Assert.True(line.All(ch => ch < 128), "non-ASCII character in " + line);
        return J(line);
    }

    private static string[] Names(JsonElement obj) => obj.EnumerateObject().Select(p => p.Name).ToArray();

    // Same structure and order, whatever the escaping.
    private static string Canonical(JsonElement element) => JsonSerializer.Serialize(element);

    private static PermissionAnsweredEventArgs Answer(PermissionDecision decision, string? message = null,
        string input = "{\"file_path\":\"C:\\\\demo\\\\app\\\\a.txt\",\"content\":\"one\",\"replace_all\":false}",
        string? suggestions = "[{\"type\":\"setMode\",\"mode\":\"acceptEdits\",\"destination\":\"session\"}]")
    {
        var request = new PendingPermission("req-7", "Write", "Write", "a.txt", "toolu_7", "a.txt", J(input),
            suggestions == null ? null : J(suggestions));
        return new PermissionAnsweredEventArgs(request, decision, message);
    }

    [Fact]
    public void User_message_has_the_checked_shape()
    {
        var line = StdinMessages.UserMessage("hello");

        Assert.Equal(
            "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"hello\"},\"parent_tool_use_id\":null,\"session_id\":\"default\"}",
            line);
        var root = Parse(line);
        Assert.Equal(new[] { "type", "message", "parent_tool_use_id", "session_id" }, Names(root));
    }

    [Fact]
    public void User_message_with_the_event_args_writes_its_uuid()
    {
        var e = new MessageSubmittedEventArgs(Tricky);
        var line = StdinMessages.UserMessage(e);

        var root = Parse(line);
        Assert.Equal(new[] { "type", "message", "parent_tool_use_id", "session_id", "uuid" }, Names(root));
        Assert.Equal(e.Uuid, root.GetProperty("uuid").GetString());
        // the rest is what the text alone writes
        Assert.Equal(StdinMessages.UserMessage(Tricky), line.Substring(0, line.IndexOf(",\"uuid\"", StringComparison.Ordinal)) + "}");

        // with images: the same blocks as the text and images write
        var png = new ChatImage("image/png", "QUJD");
        var withImage = new MessageSubmittedEventArgs("oi", new[] { png });
        Assert.Equal(Canonical(Parse(StdinMessages.UserMessage("oi", new[] { png })).GetProperty("message")),
            Canonical(Parse(StdinMessages.UserMessage(withImage)).GetProperty("message")));

        // with files: each one that landed is named by a line after the text
        var c = new ChatConversation();
        MessageSubmittedEventArgs? sent = null;
        c.MessageSubmitted += (_, s) => sent = s;
        c.AddFile("a.txt", System.Text.Encoding.UTF8.GetBytes("a"));
        c.AddFile("b.csv", System.Text.Encoding.UTF8.GetBytes("b"));
        c.Submit("read them");
        sent!.Files[0].RemotePath = "/up/a.txt";
        sent.Files[1].RemotePath = "/up/b.csv";
        Assert.Equal("read them\n[attached file: /up/a.txt]\n[attached file: /up/b.csv]",
            Parse(StdinMessages.UserMessage(sent)).GetProperty("message").GetProperty("content").GetString());
        c.AddFile("c.txt", System.Text.Encoding.UTF8.GetBytes("c"));
        c.AddFile("d.txt", System.Text.Encoding.UTF8.GetBytes("d"));
        c.Submit("");                                                   // nothing typed: the lines alone
        sent.Files[1].RemotePath = "/up/d.txt";                         // one that has not landed is not named
        Assert.Equal("[attached file: /up/d.txt]",
            Parse(StdinMessages.UserMessage(sent)).GetProperty("message").GetProperty("content").GetString());
        Assert.Throws<ArgumentNullException>(() => StdinMessages.UserMessage((MessageSubmittedEventArgs)null!));
    }

    [Fact]
    public void Control_requests_have_the_checked_shape()
    {
        Assert.Equal("{\"type\":\"control_request\",\"request_id\":\"init-1\",\"request\":{\"subtype\":\"initialize\"}}",
            StdinMessages.Initialize("init-1"));
        Assert.Equal("{\"type\":\"control_request\",\"request_id\":\"int-2\",\"request\":{\"subtype\":\"interrupt\"}}",
            StdinMessages.Interrupt("int-2"));
        Assert.Equal(
            "{\"type\":\"control_request\",\"request_id\":\"mode-3\",\"request\":{\"subtype\":\"set_permission_mode\",\"mode\":\"acceptEdits\"}}",
            StdinMessages.SetPermissionMode("mode-3", "acceptEdits"));
        Assert.Equal(
            "{\"type\":\"control_request\",\"request_id\":\"model-4\",\"request\":{\"subtype\":\"set_model\",\"model\":\"sonnet\"}}",
            StdinMessages.SetModel("model-4", "sonnet"));
    }

    [Fact]
    public void Allow_sends_the_input_back()
    {
        var e = Answer(PermissionDecision.Allow);
        var root = Parse(StdinMessages.PermissionResponse(e));

        Assert.Equal(new[] { "type", "response" }, Names(root));
        Assert.Equal("control_response", root.GetProperty("type").GetString());
        var response = root.GetProperty("response");
        Assert.Equal(new[] { "subtype", "request_id", "response" }, Names(response));
        Assert.Equal("success", response.GetProperty("subtype").GetString());
        Assert.Equal("req-7", response.GetProperty("request_id").GetString());
        var body = response.GetProperty("response");
        Assert.Equal(new[] { "behavior", "updatedInput" }, Names(body));
        Assert.Equal("allow", body.GetProperty("behavior").GetString());
        Assert.Equal(Canonical(e.Request.Input), Canonical(body.GetProperty("updatedInput")));
    }

    [Fact]
    public void Allow_always_adds_the_suggestions_as_updated_permissions()
    {
        var e = Answer(PermissionDecision.AllowAlways);
        var body = Parse(StdinMessages.PermissionResponse(e)).GetProperty("response").GetProperty("response");

        Assert.Equal(new[] { "behavior", "updatedInput", "updatedPermissions" }, Names(body));
        Assert.Equal("allow", body.GetProperty("behavior").GetString());
        Assert.Equal(Canonical(e.Request.Input), Canonical(body.GetProperty("updatedInput")));
        Assert.Equal(Canonical(e.Request.Suggestions!.Value), Canonical(body.GetProperty("updatedPermissions")));
    }

    [Fact]
    public void Allow_always_without_suggestions_is_a_plain_allow()
    {
        var body = Parse(StdinMessages.PermissionResponse(Answer(PermissionDecision.AllowAlways, suggestions: null)))
            .GetProperty("response").GetProperty("response");

        Assert.Equal(new[] { "behavior", "updatedInput" }, Names(body));
    }

    [Fact]
    public void Deny_sends_the_message()
    {
        var root = Parse(StdinMessages.PermissionResponse(Answer(PermissionDecision.Deny, "Do not create files.")));
        var response = root.GetProperty("response");
        var body = response.GetProperty("response");

        Assert.Equal("req-7", response.GetProperty("request_id").GetString());
        Assert.Equal(new[] { "behavior", "message" }, Names(body));
        Assert.Equal("deny", body.GetProperty("behavior").GetString());
        Assert.Equal("Do not create files.", body.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Deny_without_text_uses_the_default_message(string? message)
    {
        var body = Parse(StdinMessages.PermissionResponse(Answer(PermissionDecision.Deny, message)))
            .GetProperty("response").GetProperty("response");

        Assert.Equal("The user denied this tool use.", body.GetProperty("message").GetString());
    }

    [Fact]
    public void Quotes_line_breaks_and_accents_are_escaped_and_come_back_equal()
    {
        var user = Parse(StdinMessages.UserMessage(Tricky));
        Assert.Equal(Tricky, user.GetProperty("message").GetProperty("content").GetString());

        var deny = Parse(StdinMessages.PermissionResponse(Answer(PermissionDecision.Deny, Tricky)));
        Assert.Equal(Tricky, deny.GetProperty("response").GetProperty("response").GetProperty("message").GetString());

        var input = "{\"command\":" + JsonSerializer.Serialize(Tricky) + "}";
        var allow = Parse(StdinMessages.PermissionResponse(Answer(PermissionDecision.Allow, input: input)));
        Assert.Equal(Tricky,
            allow.GetProperty("response").GetProperty("response").GetProperty("updatedInput").GetProperty("command").GetString());

        var mode = Parse(StdinMessages.SetPermissionMode("id \"1\"", "não"));
        Assert.Equal("id \"1\"", mode.GetProperty("request_id").GetString());
        Assert.Equal("não", mode.GetProperty("request").GetProperty("mode").GetString());
    }

    [Fact]
    public void Response_built_from_a_recorded_request_matches_what_the_recorder_sent()
    {
        var conversation = new ChatConversation();
        PermissionAnsweredEventArgs? answered = null;
        conversation.PermissionAnswered += (_, e) => answered = e;
        foreach (var line in FixtureFiles.StreamLines("mode-and-model"))
        {
            conversation.AppendLine(line);
            if (conversation.CurrentPermission != null) break;
        }

        var request = conversation.CurrentPermission!;
        conversation.AnswerPermission(request, PermissionDecision.AllowAlways);
        var body = Parse(StdinMessages.PermissionResponse(answered!)).GetProperty("response");

        Assert.Equal(request.RequestId, body.GetProperty("request_id").GetString());
        var inner = body.GetProperty("response");
        Assert.EndsWith("a.txt", inner.GetProperty("updatedInput").GetProperty("file_path").GetString());
        Assert.Equal("setMode", inner.GetProperty("updatedPermissions")[0].GetProperty("type").GetString());
    }

    private static JsonElement Request(string line) => Parse(line).GetProperty("request");

    [Fact]
    public void User_message_without_images_keeps_string_content()
    {
        var content = Parse(StdinMessages.UserMessage("oi")).GetProperty("message").GetProperty("content");
        Assert.Equal(JsonValueKind.String, content.ValueKind);
        Assert.Equal(StdinMessages.UserMessage("oi"), StdinMessages.UserMessage("oi", Array.Empty<ChatImage>()));
    }

    [Fact]
    public void User_message_with_images_puts_images_before_the_text()
    {
        var png = new ChatImage("image/png", "QUJD");

        var content = Parse(StdinMessages.UserMessage("oi", new[] { png })).GetProperty("message").GetProperty("content");

        Assert.Equal(2, content.GetArrayLength());
        Assert.Equal("image", content[0].GetProperty("type").GetString());
        var source = content[0].GetProperty("source");
        Assert.Equal("base64", source.GetProperty("type").GetString());
        Assert.Equal("image/png", source.GetProperty("media_type").GetString());
        Assert.Equal("QUJD", source.GetProperty("data").GetString());
        Assert.Equal("text", content[1].GetProperty("type").GetString());
        Assert.Equal("oi", content[1].GetProperty("text").GetString());
    }

    [Fact]
    public void User_message_with_only_images_has_no_text_block()
    {
        var content = Parse(StdinMessages.UserMessage("", new[] { new ChatImage("image/png", "QUJD") }))
            .GetProperty("message").GetProperty("content");

        Assert.Equal(1, content.GetArrayLength());
        Assert.Equal("image", content[0].GetProperty("type").GetString());
    }

    [Fact]
    public void Apply_effort_sends_the_level_or_null()
    {
        var high = Request(StdinMessages.ApplyEffort("e1", "high"));
        Assert.Equal("apply_flag_settings", high.GetProperty("subtype").GetString());
        Assert.Equal("high", high.GetProperty("settings").GetProperty("effortLevel").GetString());
        Assert.Equal("e1", Parse(StdinMessages.ApplyEffort("e1", "high")).GetProperty("request_id").GetString());

        var cleared = Request(StdinMessages.ApplyEffort("e1", null));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("settings").GetProperty("effortLevel").ValueKind);
    }

    [Fact]
    public void Query_and_remote_control_requests_have_the_right_subtype()
    {
        Assert.Equal("get_settings", Request(StdinMessages.GetSettings("a")).GetProperty("subtype").GetString());
        Assert.Equal("get_context_usage", Request(StdinMessages.GetContextUsage("b")).GetProperty("subtype").GetString());
        Assert.Equal("get_chrome_dialog", Request(StdinMessages.GetChromeStatus("c")).GetProperty("subtype").GetString());
        var remote = Request(StdinMessages.RemoteControl("r1", true));
        Assert.Equal("remote_control", remote.GetProperty("subtype").GetString());
        Assert.True(remote.GetProperty("enabled").GetBoolean());
        Assert.False(Request(StdinMessages.RemoteControl("r2", false)).GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => StdinMessages.PermissionResponse(null!));
        Assert.Throws<ArgumentNullException>(() => StdinMessages.UserMessage((string)null!)); // a bare null names no overload since 0.6.0
    }

    [Fact]
    public void User_message_writes_the_inline_pastes_after_the_uuid_only_when_there_are_some()
    {
        var e = new MessageSubmittedEventArgs("see AAA and BBB") { InlinePastes = new[] { "AAA", "BBB" } };

        var root = Parse(StdinMessages.UserMessage(e));

        Assert.Equal(new[] { "type", "message", "parent_tool_use_id", "session_id", "uuid", "inline_pastes" }, Names(root));
        Assert.Equal(new[] { "AAA", "BBB" }, root.GetProperty("inline_pastes").EnumerateArray().Select(p => p.GetString()).ToArray());
        Assert.Equal("see AAA and BBB", root.GetProperty("message").GetProperty("content").GetString());

        // none: the line is the one it always was; a null list is none, too
        Assert.Empty(new MessageSubmittedEventArgs("x").InlinePastes);
        Assert.Empty(new MessageSubmittedEventArgs("x") { InlinePastes = null! }.InlinePastes);
        Assert.DoesNotContain("inline_pastes", StdinMessages.UserMessage(new MessageSubmittedEventArgs("x") { InlinePastes = new string[0] }));
        // the text and the images alone write none either
        Assert.DoesNotContain("inline_pastes", StdinMessages.UserMessage("x"));

        // a paste with characters that need escaping, and with images
        var png = new ChatImage("image/png", "QUJD");
        var tricky = new MessageSubmittedEventArgs(Tricky, new[] { png }) { InlinePastes = new[] { Tricky } };
        var line = Parse(StdinMessages.UserMessage(tricky));
        Assert.Equal(Tricky, line.GetProperty("inline_pastes")[0].GetString());
    }
}
