using ClaudeVisualChat;
using Xunit;

namespace ClaudeVisualChat.Tests;

public class PathReferenceTests
{
    [Theory]
    [InlineData("src/app/main.css", "src/app/main.css", null)]
    [InlineData("src/app/main.css:42", "src/app/main.css", 42)]
    [InlineData("src/app/main.css:42:7", "src/app/main.css", 42)]
    [InlineData(@"src\app\Main.cs:9", @"src\app\Main.cs", 9)]
    [InlineData(@"C:\work\app\Main.cs:9", @"C:\work\app\Main.cs", 9)]
    [InlineData(@"C:\work\app\Main.cs", @"C:\work\app\Main.cs", null)]
    [InlineData("./a.txt", "./a.txt", null)]
    [InlineData("../lib/a.b.ts:3", "../lib/a.b.ts", 3)]
    [InlineData("/etc/nginx/nginx.conf", "/etc/nginx/nginx.conf", null)]
    [InlineData("  src/a.cs  ", "src/a.cs", null)]
    [InlineData("pages/[id].tsx", "pages/[id].tsx", null)]
    public void Inline_code_that_looks_like_a_path_is_one(string text, string path, int? line)
    {
        Assert.True(PathReference.TryFromCode(text, out var found, out var foundLine));
        Assert.Equal(path, found);
        Assert.Equal(line, foundLine);
    }

    [Theory]
    [InlineData("")]
    [InlineData("main.css")]                    // no separator
    [InlineData("src/app")]                     // no extension
    [InlineData("src/app/")]                    // a folder
    [InlineData("a / b.c")]                     // spaces
    [InlineData("see src/a.cs")]                // spaces
    [InlineData("https://example.com/a.html")]  // a URL
    [InlineData("file:///tmp/a.txt")]           // a URL
    [InlineData("src/*.cs")]                    // a pattern
    [InlineData("1.5/2.0")]                     // numbers
    [InlineData("a/b.")]                        // an empty extension
    [InlineData("src/a.cs:")]
    [InlineData("List<int>/x.y")]
    [InlineData("a/b.c = d")]
    public void Other_inline_code_is_not(string text)
    {
        Assert.False(PathReference.TryFromCode(text, out _, out _));
    }

    [Theory]
    [InlineData("src/app/main.css", "src/app/main.css", null)]
    [InlineData("src/app/main.css#L42", "src/app/main.css", 42)]
    [InlineData("src/app/main.css#L42-L51", "src/app/main.css", 42)]
    [InlineData("src/app/main.css#L42C3", "src/app/main.css", 42)]
    [InlineData("src/app/main.css:42", "src/app/main.css", 42)]
    [InlineData("src/app/main.css:42:7", "src/app/main.css", 42)]
    [InlineData("./a.md", "./a.md", null)]
    [InlineData("Makefile", "Makefile", null)]
    [InlineData("my%20notes/a.md#L3", "my notes/a.md", 3)]
    [InlineData(@"C:\work\a.cs:5", @"C:\work\a.cs", 5)]
    [InlineData("/abs/a.cs", "/abs/a.cs", null)]
    public void A_link_target_that_is_not_a_url_is_a_path(string target, string path, int? line)
    {
        Assert.True(PathReference.TryFromLink(target, out var found, out var foundLine));
        Assert.Equal(path, found);
        Assert.Equal(line, foundLine);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#section")]
    [InlineData("https://example.com/a.html")]
    [InlineData("http://x/y")]
    [InlineData("mailto:a@b.example")]
    [InlineData("file:///tmp/a.txt")]
    [InlineData("//cdn.example/a.js")]
    [InlineData("vscode://file/x")]
    public void A_url_or_an_anchor_is_not(string target)
    {
        Assert.False(PathReference.TryFromLink(target, out _, out _));
    }
}
