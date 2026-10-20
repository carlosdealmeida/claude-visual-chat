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
}
