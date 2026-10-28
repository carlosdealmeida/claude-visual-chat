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
    public void The_touch_api_exists()
    {
        // the touch layout (0.5.0)
        var layout = Library.GetExportedTypes().SingleOrDefault(t => t.FullName == "ClaudeVisualChat.ChatLayout");
        Assert.NotNull(layout);
        Assert.True(layout!.IsEnum);
        Assert.Equal(new[] { "Desktop", "Touch" }, System.Enum.GetNames(layout));
        var view = typeof(ClaudeVisualChat.Controls.ClaudeChatView);
        Assert.Equal(layout, view.GetProperty("Layout")?.PropertyType);
        Assert.NotNull(view.GetField("LayoutProperty"));

        // what the host puts in the usage sheet, under the context window; the sheet's texts
        Assert.Equal(typeof(object), view.GetProperty("UsageSheetContent")?.PropertyType);
        Assert.NotNull(view.GetField("UsageSheetContentProperty"));
        foreach (var text in new[] { "ContextSection", "ContextWindow", "PercentUsed", "TokensOfWindow", "PlanUsage", "Refresh" })
            Assert.Equal(typeof(string), typeof(ChatStrings).GetProperty(text)?.PropertyType);

        // the host's back button, which does not arrive as Esc on Android: it closes what is on top and says whether it did
        var close = view.GetMethod("CloseTopLayer");
        Assert.NotNull(close);
        Assert.Equal(typeof(bool), close!.ReturnType);
        Assert.Empty(close.GetParameters());

        // a host that dims its own parts with the sheets: whether one is open (read only, for a binding), and the tap on its veil
        Assert.Equal(typeof(bool), view.GetProperty("IsSheetOpen")?.PropertyType);
        Assert.Null(view.GetProperty("IsSheetOpen")!.GetSetMethod());
        Assert.Equal(typeof(Avalonia.DirectProperty<ClaudeVisualChat.Controls.ClaudeChatView, bool>), view.GetField("IsSheetOpenProperty")?.FieldType);
        var dismiss = view.GetMethod("DismissSheet");
        Assert.NotNull(dismiss);
        Assert.Equal(typeof(bool), dismiss!.ReturnType);
        Assert.Empty(dismiss.GetParameters());
        foreach (var text in new[] { "AgentMapSubtitleTouch", "AgentMapSubtitleTouchOne" })
            Assert.Equal(typeof(string), typeof(ChatStrings).GetProperty(text)?.PropertyType);

        // the speech to text a host offers to the reply box: the touch layout shows a microphone while it is available
        var dictation = Library.GetExportedTypes().SingleOrDefault(t => t.FullName == "ClaudeVisualChat.IChatDictation");
        Assert.NotNull(dictation);
        Assert.True(dictation!.IsInterface);
        Assert.Equal(typeof(bool), dictation.GetProperty("IsAvailable")?.PropertyType);
        Assert.Equal(typeof(System.EventHandler), dictation.GetEvent("AvailabilityChanged")?.EventHandlerType);
        var listen = dictation.GetMethod("ListenAsync");
        Assert.NotNull(listen);
        Assert.Equal(typeof(System.Threading.Tasks.Task<string>), listen!.ReturnType);
        Assert.Equal(new[] { typeof(System.IProgress<string>), typeof(System.Threading.CancellationToken) }, listen.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(dictation, view.GetProperty("Dictation")?.PropertyType);
        Assert.NotNull(view.GetField("DictationProperty"));
        foreach (var text in new[] { "Dictate", "StopDictation" })
            Assert.Equal(typeof(string), typeof(ChatStrings).GetProperty(text)?.PropertyType);
    }

    [Fact]
    public void The_permission_card_api_exists()
    {
        // the card of the extension 2.1.310 (0.6.0): the request's agent, "No" first, and no rule to remember
        foreach (var type in new[] { typeof(PendingPermission), typeof(ClaudeVisualChat.Protocol.PermissionRequested) })
        {
            Assert.Equal(typeof(string), type.GetProperty("AgentId")?.PropertyType);
            Assert.Equal(typeof(bool), type.GetProperty("DefaultToNo")?.PropertyType);
            Assert.Equal(typeof(bool), type.GetProperty("SuppressAlwaysAllowRule")?.PropertyType);
        }
        // the positional constructor does not change
        Assert.Equal(8, Assert.Single(typeof(PendingPermission).GetConstructors()).GetParameters().Length);
        foreach (var text in new[]
        {
            "PermissionBash", "PermissionPowerShell", "PermissionEdit", "PermissionWrite", "PermissionRead", "PermissionGlob",
            "PermissionGlobAnywhere", "PermissionGrep", "PermissionGrepAnywhere", "PermissionWebFetch", "PermissionWebSearch",
            "PermissionAllowedDomains", "PermissionBlockedDomains", "PermissionSkill", "PermissionSkillNote", "PermissionSkillArguments",
            "PermissionProceed", "PermissionDetails", "PermissionRefuse", "PermissionEscToCancel", "WaitingForPermission",
            "PermissionCollapse", "PermissionExpand",
            // option 2 names the rule and where it is saved
            "AllowAlwaysOneRule", "AllowAlwaysTwoRules", "AllowAlwaysManyRules", "AllowAlwaysMoreRules", "AllowAlwaysOneDirectory",
            "AllowAlwaysManyDirectories", "AllowAlwaysAllEdits", "AllowAlwaysNormalMode", "AllowAlwaysThisProjectJustYou",
            "AllowAlwaysThisProjectJustYouTip", "AllowAlwaysAllProjects", "AllowAlwaysAllProjectsTip", "AllowAlwaysThisProjectShared",
            "AllowAlwaysThisProjectSharedTip", "AllowAlwaysThisSession", "AllowAlwaysThisSessionTip",
        })
        {
            Assert.Equal(typeof(string), typeof(ChatStrings).GetProperty(text)?.PropertyType);
        }
    }

    [Fact]
    public void The_plan_card_api_exists()
    {
        // the plan card (0.6.0): its texts; the plan under review and how it ended stay internal
        foreach (var text in new[]
        {
            "PlanAccept", "PlanCommentHint", "PlanYesAutoMode", "PlanYesAutoAccept", "PlanYesManual", "PlanKeepPlanning",
        })
        {
            Assert.Equal(typeof(string), typeof(ChatStrings).GetProperty(text)?.PropertyType);
        }
        var names = Library.GetExportedTypes().Select(t => t.FullName).ToHashSet();
        Assert.DoesNotContain("ClaudeVisualChat.PlanReview", names);
        Assert.DoesNotContain("ClaudeVisualChat.PlanOutcome", names);
        Assert.Null(typeof(ChatConversation).GetMethod("ApprovePlan"));
    }

    [Fact]
    public void The_plan_document_api_exists()
    {
        // the plan's document over the list and its row in the timeline (0.6.0): their texts; the panel itself stays internal
        foreach (var text in new[]
        {
            "PlanTitle", "PlanModeTool", "PlanApproved", "PlanStayedInPlanMode", "PlanReadyForReview", "PlanSelectToComment",
        })
        {
            Assert.Equal(typeof(string), typeof(ChatStrings).GetProperty(text)?.PropertyType);
        }
        var names = Library.GetExportedTypes().Select(t => t.FullName).ToHashSet();
        Assert.DoesNotContain("ClaudeVisualChat.Controls.PlanDocumentPanel", names);
        Assert.Null(typeof(ClaudeVisualChat.Controls.ClaudeChatView).GetMethod("ShowPlan"));
    }

    [Fact]
    public void The_tool_model_api_exists()
    {
        // the whole result, the typed reason of a refusal and the two interruption lines (0.6.0)
        Assert.Equal(typeof(string), typeof(ToolCallItem).GetProperty("FullResultText")?.PropertyType);
        Assert.Equal(typeof(string), typeof(ToolCallItem).GetProperty("RejectionReason")?.PropertyType);
        Assert.NotNull(typeof(ToolCallItem).GetProperty("ResultText")); // the short form stays
        Assert.NotNull(typeof(ToolCallItem).GetProperty("HiddenResultLines"));
        // the open or cut state of a tool's output and command (0.6.0): in the item, because the list recycles its rows
        foreach (var name in new[] { "IsOutputExpanded", "IsInputExpanded" })
        {
            var open = typeof(ToolCallItem).GetProperty(name);
            Assert.Equal(typeof(bool), open?.PropertyType);
            Assert.True(open!.CanWrite && open.SetMethod!.IsPublic);
        }
        // Edit and Write of 2.1.310 and the Diff View (0.6.0)
        foreach (var text in new[] { "ToolReason", "ToolShowFullText", "ToolCutText", "WriteSucceeded", "ClickToExpand", "DiffViewTitle", "CopyCode", "CopyCodeToClipboard" })
            Assert.Equal(typeof(string), typeof(ChatStrings).GetProperty(text)?.PropertyType);
        // Glob, Grep and the web tools of 2.1.310 (0.6.0)
        foreach (var text in new[] { "GlobNone", "GlobOne", "GlobMany", "GrepNone", "GrepOne", "GrepMany", "FetchedFrom", "WebAllowed", "WebBlocked" })
            Assert.Equal(typeof(string), typeof(ChatStrings).GetProperty(text)?.PropertyType);
        // the names of the tools the rows write (0.6.0): the same in every language
        foreach (var text in new[] { "AgentToolTitle", "WebFetchToolTitle", "WebSearchToolTitle", "ChromeToolTitle" })
            Assert.Equal(typeof(string), typeof(ChatStrings).GetProperty(text)?.PropertyType);
        // MCP and Claude in Chrome rows of 2.1.310 (0.6.0): the images a result carried, and the lines the Chrome actions leave
        var images = typeof(ToolCallItem).GetProperty("ResultImages");
        Assert.Equal(typeof(System.Collections.Generic.IReadOnlyList<ChatImage>), images?.PropertyType);
        Assert.False(images!.SetMethod!.IsPublic); // the conversation sets it
        var blockImages = typeof(ClaudeVisualChat.Protocol.ToolResultBlock).GetProperty("Images");
        Assert.Equal(typeof(System.Collections.Generic.IReadOnlyList<ClaudeVisualChat.Protocol.ImageBlock>), blockImages?.PropertyType);
        Assert.Contains(blockImages!.SetMethod!.ReturnParameter.GetRequiredCustomModifiers(), m => m.Name == "IsExternalInit"); // init only
        foreach (var text in new[]
        {
            "ChromeActionCompleted", "ChromeScreenshot", "ChromeNavigated", "ChromeTabCreated", "ChromeTabsRead", "ChromeInputCompleted",
            "ChromeWindowResized", "ChromeSearchCompleted", "ChromeGifCompleted", "ChromeConsoleRetrieved", "ChromeNetworkRetrieved",
            "ChromeScriptExecuted", "ChromePageRead", "ChromeTextRetrieved", "ChromeImageUploaded", "ChromePlanUpdated",
        })
            Assert.Equal(typeof(string), typeof(ChatStrings).GetProperty(text)?.PropertyType);
        var max = typeof(ToolPresentation).GetField("MaxKeptResultChars");
        Assert.Equal(500_000, max!.GetRawConstantValue());
        Assert.Contains("ToolInterrupted", System.Enum.GetNames(typeof(NoticeKind)));
        Assert.Equal(new[] { "Interrupted", "Compacted", "ApiRetry", "TurnFailed", "ToolInterrupted" }, System.Enum.GetNames(typeof(NoticeKind)));
        Assert.Equal(typeof(string), typeof(ChatStrings).GetProperty("ToolInterrupted")?.PropertyType);
        Assert.Equal(typeof(string), typeof(ChatStrings).GetProperty("ToolRejected")?.PropertyType); // public, so it stays
        var ansi = typeof(ClaudeVisualChat.Protocol.AnsiText);
        Assert.True(ansi.IsAbstract && ansi.IsSealed && ansi.IsPublic);
        Assert.Equal(typeof(string), ansi.GetMethod("Strip", new[] { typeof(string) })?.ReturnType);
    }

    [Fact]
    public void The_agent_row_api_exists()
    {
        // the Agent row of 2.1.310 (0.6.0): the prompt, the wait for a permission, and the background line's texts; what the agents it
        // started are doing stays internal
        var agentPrompt = typeof(AgentInfo).GetProperty("Prompt");
        Assert.Equal(typeof(string), agentPrompt?.PropertyType);
        Assert.False(agentPrompt!.SetMethod!.IsPublic); // the conversation sets it
        var waiting = typeof(AgentInfo).GetProperty("IsWaitingForPermission");
        Assert.Equal(typeof(bool), waiting?.PropertyType);
        Assert.False(waiting!.SetMethod!.IsPublic); // the conversation sets it
        var prompt = typeof(ClaudeVisualChat.Protocol.AgentTaskStarted).GetProperty("Prompt");
        Assert.Equal(typeof(string), prompt?.PropertyType);
        Assert.Contains(prompt!.SetMethod!.ReturnParameter.GetRequiredCustomModifiers(), m => m.Name == "IsExternalInit"); // init only
        Assert.Equal(7, typeof(ClaudeVisualChat.Protocol.AgentTaskStarted).GetConstructors().Single().GetParameters().Length); // no new positional parameter
        Assert.Null(typeof(AgentInfo).GetProperty("Nested"));
        foreach (var text in new[]
        {
            "AgentWorkingInBackground", "AgentWorkingInBackgroundOn", "AgentWaitingForPermission", "AgentWaitingAlongWithNested",
            "AgentFailedWith", "AgentNestedWaiting", "AgentNestedWorking", "AgentNestedFailed", "AgentNestedStopped", "AgentTasksHint",
        })
        {
            Assert.Equal(typeof(string), typeof(ChatStrings).GetProperty(text)?.PropertyType);
        }
    }

    [Fact]
    public void The_plan_comment_api_exists()
    {
        // the comments on the plan (0.6.0): their texts; the controls and the comment itself stay internal
        foreach (var text in new[]
        {
            "PlanAddComment", "PlanCommentPlaceholder", "PlanCommentCancel", "PlanComments", "PlanRemoveComment",
            "PlanContinuePlanning", "PlanOneCommentIncluded", "PlanCommentsIncluded", "PlanSendFeedback",
        })
        {
            Assert.Equal(typeof(string), typeof(ChatStrings).GetProperty(text)?.PropertyType);
        }
        var names = Library.GetExportedTypes().Select(t => t.FullName).ToHashSet();
        Assert.DoesNotContain("ClaudeVisualChat.PlanComment", names);
        Assert.DoesNotContain("ClaudeVisualChat.Controls.PlanCommentBox", names);
        Assert.DoesNotContain("ClaudeVisualChat.Controls.PlanTextBlock", names);
        Assert.DoesNotContain("ClaudeVisualChat.Controls.PlanCommentRequest", names);
    }

    [Fact]
    public void The_dictation_interface_is_documented()
    {
        // the XML documentation travels in the package, beside the dll: a member without a summary shows nothing in the host's IntelliSense
        var xml = System.IO.Path.ChangeExtension(Library.Location, ".xml");
        Assert.True(System.IO.File.Exists(xml), xml);
        var summarized = System.Xml.Linq.XDocument.Load(xml).Descendants("member")
            .Where(m => !string.IsNullOrWhiteSpace((string?)m.Element("summary")))
            .Select(m => (string)m.Attribute("name")!)
            .ToHashSet();
        foreach (var member in new[]
        {
            "T:ClaudeVisualChat.IChatDictation",
            "P:ClaudeVisualChat.IChatDictation.IsAvailable",
            "E:ClaudeVisualChat.IChatDictation.AvailabilityChanged",
            "M:ClaudeVisualChat.IChatDictation.ListenAsync(System.IProgress{System.String},System.Threading.CancellationToken)",
        })
        {
            Assert.True(summarized.Contains(member), member + " has no summary");
        }
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
