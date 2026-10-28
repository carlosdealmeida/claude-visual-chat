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
    public void The_long_prompt_api_exists()
    {
        // a long prompt folded behind "Show more" (0.6.0): the open state in the item, because the list recycles its rows
        var open = typeof(UserMessageItem).GetProperty("IsTextExpanded");
        Assert.Equal(typeof(bool), open?.PropertyType);
        Assert.True(open!.CanWrite && open.SetMethod!.IsPublic);
        Assert.True(typeof(System.ComponentModel.INotifyPropertyChanged).IsAssignableFrom(typeof(UserMessageItem)));
        foreach (var text in new[] { "ShowMore", "ShowLess" })
            Assert.Equal(typeof(string), typeof(ChatStrings).GetProperty(text)?.PropertyType);
        // the fold and the prompt pinned at the top stay internal
        var names = Library.GetExportedTypes().Select(t => t.FullName).ToHashSet();
        Assert.DoesNotContain("ClaudeVisualChat.Controls.FoldedText", names);
        Assert.DoesNotContain("ClaudeVisualChat.Controls.StickyPrompt", names);
    }

    [Fact]
    public void The_scroll_on_send_and_timestamps_api_exists()
    {
        // the extension's scrollToBottomOnSend and showMessageTimestamps (0.6.0): two properties of the view, both on by default
        var view = typeof(ClaudeVisualChat.Controls.ClaudeChatView);
        foreach (var name in new[] { "ScrollToEndOnSend", "ShowTimestamps" })
        {
            var property = view.GetProperty(name);
            Assert.Equal(typeof(bool), property?.PropertyType);
            Assert.True(property!.SetMethod!.IsPublic);
            var field = view.GetField(name + "Property");
            Assert.Equal(typeof(Avalonia.StyledProperty<bool>), field?.FieldType);
            Assert.True(((Avalonia.StyledProperty<bool>)field!.GetValue(null)!).GetDefaultValue(view));
        }
        // the date line's texts; the line itself stays internal
        foreach (var text in new[] { "MonthNames", "DateFormat", "DateWithYearFormat" })
        {
            var property = typeof(ChatStrings).GetProperty(text);
            Assert.Equal(typeof(string), property?.PropertyType);
            Assert.Contains(property!.SetMethod!.ReturnParameter.GetRequiredCustomModifiers(), m => m.Name == "IsExternalInit"); // init only
        }
        var names = Library.GetExportedTypes().Select(t => t.FullName).ToHashSet();
        Assert.DoesNotContain("ClaudeVisualChat.Controls.DateSeparator", names);
    }

    [Fact]
    public void The_response_actions_api_exists()
    {
        // the actions under a reply (0.6.0): the bookmark and the line's uuid on the reply, a link event for the host and the rating
        var bookmarked = typeof(AssistantTextItem).GetProperty("IsBookmarked");
        Assert.Equal(typeof(bool), bookmarked?.PropertyType);
        Assert.True(bookmarked!.SetMethod!.IsPublic);
        var uuid = typeof(AssistantTextItem).GetProperty("Uuid");
        Assert.Equal(typeof(string), uuid?.PropertyType);
        Assert.False(uuid!.SetMethod!.IsPublic); // the conversation sets it
        Assert.Null(typeof(AssistantTextItem).GetProperty("Rating")); // the rating shown stays internal
        var blockUuid = typeof(ClaudeVisualChat.Protocol.AssistantBlock).GetProperty("Uuid");
        Assert.Equal(typeof(string), blockUuid?.PropertyType);
        Assert.Contains(blockUuid!.SetMethod!.ReturnParameter.GetRequiredCustomModifiers(), m => m.Name == "IsExternalInit"); // init only
        Assert.Equal(3, typeof(ClaudeVisualChat.Protocol.AssistantBlock).GetConstructors().Single().GetParameters().Length); // no new positional parameter

        var conversation = typeof(ChatConversation);
        Assert.Equal(typeof(System.EventHandler<LinkRequestedEventArgs>), conversation.GetEvent("LinkRequested")?.EventHandlerType);
        Assert.Equal(typeof(System.EventHandler<FeedbackRequestedEventArgs>), conversation.GetEvent("FeedbackRequested")?.EventHandlerType);
        Assert.Equal(typeof(string), typeof(LinkRequestedEventArgs).GetProperty("Url")?.PropertyType);
        Assert.NotNull(typeof(LinkRequestedEventArgs).GetConstructor(new[] { typeof(string) }));
        Assert.Equal(typeof(AssistantTextItem), typeof(FeedbackRequestedEventArgs).GetProperty("Item")?.PropertyType);
        Assert.Equal(typeof(bool?), typeof(FeedbackRequestedEventArgs).GetProperty("Positive")?.PropertyType);
        Assert.Equal(typeof(string), typeof(FeedbackRequestedEventArgs).GetProperty("Details")?.PropertyType);
        Assert.NotNull(typeof(FeedbackRequestedEventArgs).GetConstructor(new[] { typeof(AssistantTextItem), typeof(bool?), typeof(string) }));
        // what the conversation does with them stays internal (the bookmarks list is the bookmarks panel's, later)
        foreach (var member in new[] { "Bookmarks", "ToggleBookmark", "RequestLink", "CanOpenLinks", "CanSendFeedback", "RateAnswer" })
            Assert.Empty(conversation.GetMember(member));
        var names = Library.GetExportedTypes().Select(t => t.FullName).ToHashSet();
        Assert.DoesNotContain("ClaudeVisualChat.Controls.ResponseActions", names);
        Assert.DoesNotContain("ClaudeVisualChat.Controls.FeedbackDialog", names);
        foreach (var text in new[]
        {
            "CopyResponse", "CopyResponseToClipboard", "BookmarkResponse", "RemoveBookmark", "GoodResponse", "BadResponse",
            "FeedbackLearnHow", "FeedbackThanks", "FeedbackLearnHowShort", "FeedbackTitle", "FeedbackPlaceholder", "FeedbackSkip",
            "FeedbackSend",
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
        Assert.NotNull(send.GetMethod("UserMessage", new[] { typeof(string), typeof(System.Collections.Generic.IReadOnlyList<ChatImage>) }));

        // the models come from the conversation now
        var view = typeof(ClaudeVisualChat.Controls.ClaudeChatView);
        Assert.Null(view.GetProperty("AvailableModels"));
        Assert.Null(view.GetField("AvailableModelsProperty"));
        Assert.NotNull(view.GetProperty("AvailableModes"));
    }

    [Fact]
    public void The_meta_line_and_the_copy_command_exist()
    {
        // the line the conversation says about its own commands (0.6.0): public, with a text that cannot be changed from outside
        var meta = Library.GetExportedTypes().SingleOrDefault(t => t.FullName == "ClaudeVisualChat.MetaLineItem");
        Assert.NotNull(meta);
        Assert.True(meta!.IsSealed);
        Assert.True(typeof(ChatItem).IsAssignableFrom(meta));
        Assert.NotNull(meta.GetConstructor(new[] { typeof(string) }));
        var text = meta.GetProperty("Text");
        Assert.Equal(typeof(string), text?.PropertyType);
        Assert.Null(text!.SetMethod);
        Assert.Contains("copy", ChatConversation.LocalCommands);

        // the copy and what it says stay between the conversation and its view
        var conversation = typeof(ChatConversation);
        Assert.Null(conversation.GetEvent("CopyRequested"));
        Assert.Null(conversation.GetMethod("ReportCopy"));
        Assert.Null(conversation.GetProperty("Strings"));
        foreach (var name in new[] { "CommandCopyDescription", "CopyDone", "CopyNothing", "CopyOnly", "CopyUsage", "CopyFailed", "CopyWaiting" })
        {
            var property = typeof(ChatStrings).GetProperty(name);
            Assert.Equal(typeof(string), property?.PropertyType);
            Assert.Contains(property!.SetMethod!.ReturnParameter.GetRequiredCustomModifiers(), m => m.Name == "IsExternalInit"); // init only
        }
    }

    [Fact]
    public void The_queue_of_messages_sent_while_claude_works_exists()
    {
        // the messages held until the claude takes them (0.6.0): a list the host can read and follow, not replace
        var queued = typeof(ChatConversation).GetProperty("QueuedMessages");
        Assert.Equal(typeof(System.Collections.ObjectModel.ObservableCollection<UserMessageItem>), queued?.PropertyType);
        Assert.Null(queued!.SetMethod);

        // each message sent has a uuid, which the new overload writes for the claude to tell what became of it; the old one stays
        var uuid = typeof(MessageSubmittedEventArgs).GetProperty("Uuid");
        Assert.Equal(typeof(string), uuid?.PropertyType);
        Assert.Null(uuid!.SetMethod);
        var send = typeof(ClaudeVisualChat.Protocol.StdinMessages);
        Assert.Equal(typeof(string), send.GetMethod("UserMessage", new[] { typeof(MessageSubmittedEventArgs) })?.ReturnType);
        Assert.NotNull(send.GetMethod("UserMessage", new[] { typeof(string), typeof(System.Collections.Generic.IReadOnlyList<ChatImage>) }));

        // what the claude says of it: command_lifecycle
        var lifecycle = Library.GetExportedTypes().SingleOrDefault(t => t.FullName == "ClaudeVisualChat.Protocol.CommandLifecycle");
        Assert.NotNull(lifecycle);
        Assert.True(typeof(ClaudeVisualChat.Protocol.ClaudeEvent).IsAssignableFrom(lifecycle));
        Assert.NotNull(lifecycle!.GetConstructor(new[] { typeof(string), typeof(string), typeof(bool) }));
        Assert.Equal(typeof(string), lifecycle.GetProperty("CommandUuid")?.PropertyType);
        Assert.Equal(typeof(string), lifecycle.GetProperty("State")?.PropertyType);
        Assert.Equal(typeof(bool), lifecycle.GetProperty("RanAhead")?.PropertyType);

        // the held notice is the conversation's business, and so is the list in the view
        Assert.Null(typeof(UserMessageItem).GetProperty("QueuedNotice"));
        Assert.DoesNotContain("ClaudeVisualChat.Controls.QueuedMessageList", Library.GetExportedTypes().Select(t => t.FullName));
        foreach (var name in new[] { "YouQueued", "MessageQueued", "RanAtOnce" })
        {
            var property = typeof(ChatStrings).GetProperty(name);
            Assert.Equal(typeof(string), property?.PropertyType);
            Assert.Contains(property!.SetMethod!.ReturnParameter.GetRequiredCustomModifiers(), m => m.Name == "IsExternalInit"); // init only
        }
    }
}
