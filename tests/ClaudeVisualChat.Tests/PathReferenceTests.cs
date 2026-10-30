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
    [InlineData("arquivo-15.txt:1", "arquivo-15.txt", 1)]   // a bare file name of a known extension
    [InlineData("aaa-primeiro.css", "aaa-primeiro.css", null)]
    [InlineData("README.md:42", "README.md", 42)]
    [InlineData("Program.CS", "Program.CS", null)]
    [InlineData("EditorDaAba.cs", "EditorDaAba.cs", null)]
    [InlineData("package.json", "package.json", null)]
    [InlineData("index.d.ts", "index.d.ts", null)]
    [InlineData("appsettings.Development.json", "appsettings.Development.json", null)]
    [InlineData("button.test.ts:3", "button.test.ts", 3)]
    [InlineData("a.spec.ts", "a.spec.ts", null)]
    [InlineData("app.min.js", "app.min.js", null)]
    [InlineData("webpack.config.js", "webpack.config.js", null)]
    [InlineData("src/System.Text.Json", "src/System.Text.Json", null)] // with a folder it is as written: a path
    [InlineData("Default.aspx", "Default.aspx", null)]      // classic web sites
    [InlineData("global.asa", "global.asa", null)]
    [InlineData("web.config:12", "web.config", 12)]
    [InlineData("menu.asp", "menu.asp", null)]
    [InlineData("topo.inc", "topo.inc", null)]
    [InlineData("Login.ascx", "Login.ascx", null)]
    [InlineData("pom.xml", "pom.xml", null)]                // short stems with a file-only extension
    [InlineData("lib.rs", "lib.rs", null)]
    [InlineData("log.txt", "log.txt", null)]
    [InlineData("bad.py", "bad.py", null)]
    [InlineData("MainWindow.axaml.cs", "MainWindow.axaml.cs", null)]
    [InlineData("App.xaml.cs", "App.xaml.cs", null)]
    [InlineData("Index.razor.cs", "Index.razor.cs", null)]
    [InlineData("docker-compose.override.yml", "docker-compose.override.yml", null)]
    [InlineData("tsconfig.app.json", "tsconfig.app.json", null)]
    [InlineData(".gitignore", ".gitignore", null)]          // dot files
    [InlineData(".env", ".env", null)]
    [InlineData(".env.local", ".env.local", null)]
    [InlineData(".eslintrc.json", ".eslintrc.json", null)]
    [InlineData("yarn.lock", "yarn.lock", null)]
    [InlineData("Cargo.lock", "Cargo.lock", null)]
    public void Inline_code_that_looks_like_a_path_is_one(string text, string path, int? line)
    {
        Assert.True(PathReference.TryFromCode(text, out var found, out var foundLine));
        Assert.Equal(path, found);
        Assert.Equal(line, foundLine);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Node.js")]                     // prose words that end in a file extension
    [InlineData("Vue.js")]
    [InlineData("process.env")]
    [InlineData("import.meta.env")]
    [InlineData("System.Text.Json")]
    [InlineData("Newtonsoft.Json")]
    [InlineData("mutex.lock")]
    [InlineData("Avalonia.Xaml")]
    [InlineData("res.json")]
    [InlineData("obj.cs")]
    [InlineData("df.r")]
    [InlineData("foo.h")]
    [InlineData("ptr.c")]                       // a short variable with a member-like extension
    [InlineData(".then")]                       // a call, not a dot file
    [InlineData(".NET")]
    [InlineData("name.foo")]                    // no separator, an extension nobody opens
    [InlineData("e.Path")]                      // a member
    [InlineData("console.log")]
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
    [InlineData("length/2.0f")]                 // arithmetic
    [InlineData("user@host:/etc/x.conf")]       // scp syntax
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
    [InlineData("README.md:42", "README.md", 42)]
    [InlineData("main.css:42:7", "main.css", 42)]
    [InlineData("src/a.cs#section", "src/a.cs", null)]
    [InlineData("src/a.cs?x=1", "src/a.cs", null)]
    [InlineData("src/a.cs#L42?x=1", "src/a.cs", 42)]
    [InlineData("src/a.cs?x=1#L3", "src/a.cs", 3)]
    [InlineData("src/a.cs#L3:4", "src/a.cs", 3)]
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
