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

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => StdinMessages.PermissionResponse(null!));
        Assert.Throws<ArgumentNullException>(() => StdinMessages.UserMessage(null!));
    }
}
