using System.Linq;
using System.Reflection;
using Xunit;

namespace ClaudeVisualChat.Tests;

/// <summary>Names of the public API, fixed before the first release: renaming later breaks the apps that use it.</summary>
public class PublicApiTests
{
    private static readonly Assembly Library = typeof(ChatConversation).Assembly;

    [Fact]
    public void No_public_type_shares_its_name_with_an_avalonia_type()
    {
        var avalonia = typeof(Avalonia.Controls.Control).Assembly.GetExportedTypes()
            .Concat(typeof(Avalonia.AvaloniaObject).Assembly.GetExportedTypes())
            .Select(t => t.Name)
            .ToHashSet();
        var clashes = Library.GetExportedTypes().Where(t => avalonia.Contains(t.Name)).Select(t => t.FullName).ToList();
        Assert.Empty(clashes);
    }

    [Fact]
    public void Public_names_are_the_ones_of_the_first_release()
    {
        var names = Library.GetExportedTypes().Select(t => t.FullName).ToHashSet();

        Assert.Contains("ClaudeVisualChat.Protocol.TextContentBlock", names);
        Assert.DoesNotContain("ClaudeVisualChat.Protocol.TextBlock", names); // Avalonia.Controls.TextBlock
        Assert.Contains("ClaudeVisualChat.ChatObservableObject", names);
        Assert.DoesNotContain("ClaudeVisualChat.ObservableObject", names); // CommunityToolkit.Mvvm's
        Assert.DoesNotContain("ClaudeVisualChat.Controls.ToolCallView", names); // drawn by the view, like the reply box
        Assert.DoesNotContain("ClaudeVisualChat.Controls.ChatComposer", names);
        Assert.DoesNotContain("ClaudeVisualChat.Controls.PermissionPrompt", names);
        Assert.Contains("ClaudeVisualChat.PermissionModeRequestedEventArgs", names);
        Assert.Contains("ClaudeVisualChat.ModelRequestedEventArgs", names);
        Assert.DoesNotContain("ClaudeVisualChat.PermissionModeChangedEventArgs", names);
        Assert.DoesNotContain("ClaudeVisualChat.ModelChangedEventArgs", names);
    }

    [Fact]
    public void The_conversation_tells_what_the_user_asks_for()
    {
        // raised on the user's request, before the claude confirms: "Requested", not "Changed"
        var events = typeof(ChatConversation).GetEvents().Select(e => e.Name).ToList();
        Assert.Contains("PermissionModeRequested", events);
        Assert.Contains("ModelRequested", events);
        Assert.DoesNotContain("PermissionModeChanged", events);
        Assert.DoesNotContain("ModelChanged", events);
    }

    [Fact]
    public void The_conversation_can_take_a_message_back()
    {
        var undo = typeof(ChatConversation).GetMethod("UndoSubmit");
        Assert.NotNull(undo);
        Assert.Equal(typeof(bool), undo!.ReturnType);
        Assert.Equal(typeof(MessageSubmittedEventArgs), Assert.Single(undo.GetParameters()).ParameterType);
        Assert.Equal(typeof(System.EventHandler<MessageSubmittedEventArgs>),
            typeof(ChatConversation).GetEvent("SubmitUndone")?.EventHandlerType);
    }

    [Fact]
    public void The_view_has_a_read_only_mode()
    {
        var view = typeof(ClaudeVisualChat.Controls.ClaudeChatView);
        var property = view.GetProperty("IsReadOnly");
        Assert.NotNull(property);
        Assert.Equal(typeof(bool), property!.PropertyType);
        Assert.NotNull(view.GetField("IsReadOnlyProperty"));
    }

    [Fact]
    public void The_bar_api_exists()
    {
        var names = Library.GetExportedTypes().Select(t => t.FullName).ToHashSet();
        foreach (var type in new[]
        {
            "ClaudeVisualChat.ChatImage", "ClaudeVisualChat.AddImageResult", "ClaudeVisualChat.ChatCommand",
            "ClaudeVisualChat.ChatModel", "ClaudeVisualChat.ContextUsage", "ClaudeVisualChat.ContextCategory",
            "ClaudeVisualChat.UsageLimits", "ClaudeVisualChat.UsageWindow", "ClaudeVisualChat.ChatPanel",
            "ClaudeVisualChat.ChatBrowserStatus", "ClaudeVisualChat.ChatRemoteControlStatus",
            "ClaudeVisualChat.EffortRequestedEventArgs", "ClaudeVisualChat.CommandInvokedEventArgs",
            "ClaudeVisualChat.RemoteControlLinkRequestedEventArgs", "ClaudeVisualChat.CommandOutputItem",
            "ClaudeVisualChat.Protocol.SettingsApplied",
        })
        {
            Assert.Contains(type, names);
        }

        var conversation = typeof(ChatConversation);
        foreach (var member in new[]
        {
            "Attachments", "MaxImages", "ImageBudgetBytes", "MaxImageSide", "AvailableModels", "CurrentModel", "ModelSuffix", "Effort",
            "RequestedEffort", "Commands", "TerminalCommands", "PaletteCommands", "ShownPanel", "ContextUsage",
            "UsageLimits", "Clock", "PromptCacheExpiresAt", "PromptCacheTtl", "IsPromptCacheCompacted", "BrowserStatus",
            "BrowserError", "RemoteControlStatus", "RemoteControlUrl", "RemoteControlError",
            "AddImageAsync", "RemoveAttachment", "SetEffort", "InvokeCommand", "DisconnectBrowser", "OpenRemoteControl",
            "ClosePanel", "EffortRequested", "CommandInvoked", "BrowserDisconnectRequested", "RemoteControlLinkRequested",
        })
        {
            Assert.True(conversation.GetMember(member).Length > 0, "ChatConversation." + member);
        }

        // the agent map (0.4.0)
        foreach (var type in new[]
        {
            "ClaudeVisualChat.AgentInfo", "ClaudeVisualChat.AgentStatus",
            "ClaudeVisualChat.Protocol.AgentTaskStarted", "ClaudeVisualChat.Protocol.AgentTaskProgress",
            "ClaudeVisualChat.Protocol.AgentTaskUpdated", "ClaudeVisualChat.Protocol.AgentTaskFinished",
        })
        {
            Assert.Contains(type, names);
        }
        Assert.DoesNotContain("ClaudeVisualChat.Protocol.TaskNotificationText", names);
        foreach (var member in new[] { "Agents", "RunningAgents", "ShowAgents" })
            Assert.True(conversation.GetMember(member).Length > 0, "ChatConversation." + member);
        Assert.True(System.Enum.IsDefined(typeof(ChatPanel), "Agents"));
        foreach (var member in new[]
        {
            "Id", "TaskId", "Description", "AgentType", "Model", "IsBackground", "Depth", "ParentId", "Status",
            "StartedAt", "DurationMs", "Tokens", "ToolUses", "Activity", "Result",
        })
        {
            Assert.NotNull(typeof(AgentInfo).GetProperty(member));
        }
        Assert.True(typeof(ChatObservableObject).IsAssignableFrom(typeof(AgentInfo)));
        Assert.Empty(typeof(AgentInfo).GetConstructors()); // only the conversation makes them
        Assert.Equal(new[] { "Running", "Completed", "Failed", "Stopped" }, System.Enum.GetNames(typeof(AgentStatus)));
        Assert.Equal(typeof(System.Collections.ObjectModel.ObservableCollection<AgentInfo>), conversation.GetProperty("Agents")!.PropertyType);
        Assert.Equal(typeof(int), conversation.GetProperty("RunningAgents")!.PropertyType);

        // the texts of 0.4.0
        foreach (var text in new[]
        {
            "FileTooBig", "FileNotSupported", "TooManyFiles", "FileUploading", "AttachableFiles", "AgentsChip",
            "AgentsChipOne", "ContextRingTip", "ContextRingHint", "AgentMapTitle", "AgentMapSubtitle", "AgentMapSubtitleOne",
            "AgentMain", "AgentSpawnedByMain", "AgentSpawnedBy", "AgentRunning", "AgentFinished", "AgentFailed",
            "AgentStopped", "AgentBackground", "AgentNoResult", "AgentTokens", "AgentContextTokens", "Back",
        })
        {
            Assert.Equal(typeof(string), typeof(ChatStrings).GetProperty(text)?.PropertyType);
        }

        // attached files (0.4.0)
        foreach (var type in new[]
        {
            "ClaudeVisualChat.ChatFile", "ClaudeVisualChat.ChatFileKind", "ClaudeVisualChat.AddFileResult",
            "ClaudeVisualChat.Protocol.AttachedFileLine",
        })
        {
            Assert.Contains(type, names);
        }
        foreach (var member in new[] { "Files", "MaxFiles", "MaxFileBytes", "AddFileAsync", "AddFile", "RemoveFile" })
            Assert.True(conversation.GetMember(member).Length > 0, "ChatConversation." + member);
        foreach (var member in new[] { "Name", "Size", "MediaType", "Kind", "LocalPath", "Content", "Progress", "RemotePath" })
            Assert.NotNull(typeof(ChatFile).GetProperty(member));
        Assert.True(typeof(ChatObservableObject).IsAssignableFrom(typeof(ChatFile)));
        Assert.Equal(typeof(System.Collections.Generic.IReadOnlyList<ChatFile>), typeof(MessageSubmittedEventArgs).GetProperty("Files")?.PropertyType);
        Assert.Equal(typeof(System.Collections.Generic.IReadOnlyList<ChatFile>), typeof(UserMessageItem).GetProperty("Files")?.PropertyType);
        Assert.NotNull(typeof(UserMessageItem).GetConstructor(new[]
        {
            typeof(string), typeof(System.Collections.Generic.IReadOnlyList<ChatImage>), typeof(System.Collections.Generic.IReadOnlyList<ChatFile>),
        }));
        Assert.NotNull(typeof(MessageSubmittedEventArgs).GetConstructor(new[] { typeof(string), typeof(System.Collections.Generic.IReadOnlyList<ChatImage>) }));
        Assert.Empty(typeof(ChatFile).GetConstructors()); // only the conversation makes them
        Assert.Equal(new[] { "Pdf", "Text", "Csv" }, System.Enum.GetNames(typeof(ChatFileKind)));
        Assert.Equal(new[] { "Added", "TooMany", "TooBig", "NotSupported" }, System.Enum.GetNames(typeof(AddFileResult)));
        Assert.Equal(typeof(System.Threading.Tasks.Task<AddFileResult>), conversation.GetMethod("AddFileAsync")!.ReturnType);
        var line = typeof(ClaudeVisualChat.Protocol.AttachedFileLine);
        Assert.NotNull(line.GetMethod("Format"));
        Assert.NotNull(line.GetMethod("Split"));

        var send = typeof(ClaudeVisualChat.Protocol.StdinMessages);
        foreach (var method in new[] { "ApplyEffort", "GetSettings", "GetContextUsage", "RemoteControl", "GetChromeStatus", "Initialize" })
            Assert.NotNull(send.GetMethod(method));
        Assert.Equal(2, send.GetMethod("UserMessage")!.GetParameters().Length);

        // the models come from the conversation now
        var view = typeof(ClaudeVisualChat.Controls.ClaudeChatView);
        Assert.Null(view.GetProperty("AvailableModels"));
        Assert.Null(view.GetField("AvailableModelsProperty"));
        Assert.NotNull(view.GetProperty("AvailableModes"));
    }
}
