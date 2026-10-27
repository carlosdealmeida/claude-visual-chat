using System;
using Avalonia;

namespace ClaudeVisualChat.Sample;

internal static class Program
{
    /// <summary>
    /// Opens the sample window. With <c>--touch</c> the window is a phone's screen (412×892) with the view alone in it,
    /// in the touch layout. With <c>--screenshot &lt;file.png&gt; --scenario &lt;id&gt; [--lang pt] [--history]</c> it
    /// plays the recording to the end instead, answering yes to every request (or, with <c>--history</c>, loads its
    /// session file), saves the window (1280×800, or 412×892 with <c>--touch</c>) to the file and exits (0 when saved).
    /// Nothing is clicked or typed.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            App.Options = SampleOptions.Parse(args);
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine(SampleOptions.Usage);
            return 2;
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // used by the designer too
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

/// <summary>The command line: nothing for the window, the touch layout, or the screenshot mode.</summary>
internal sealed class SampleOptions
{
    public const string Usage =
        "usage: ClaudeVisualChat.Sample [--touch] [--screenshot <file.png> --scenario <id> [--lang pt] [--history]]";

    private SampleOptions(string? screenshot, string scenario, bool portuguese, bool history, bool touch)
    {
        Screenshot = screenshot;
        Scenario = scenario;
        Portuguese = portuguese;
        History = history;
        Touch = touch;
    }

    /// <summary>Where to save the screenshot; null opens the window as usual.</summary>
    public string? Screenshot { get; }

    public string Scenario { get; }

    public bool Portuguese { get; }

    /// <summary>The screenshot shows the session file instead of the stream.</summary>
    public bool History { get; }

    /// <summary>The window is a phone's screen (412×892) and the view is in the touch layout.</summary>
    public bool Touch { get; }

    public static SampleOptions Parse(string[] args)
    {
        string? screenshot = null;
        string? scenario = null;
        var portuguese = false;
        var history = false;
        var touch = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--touch":
                    touch = true;
                    break;
                case "--screenshot":
                    screenshot = Value(args, ref i);
                    break;
                case "--scenario":
                    scenario = Value(args, ref i);
                    break;
                case "--lang":
                    var lang = Value(args, ref i);
                    if (lang != "pt" && lang != "en") throw new ArgumentException("--lang takes pt or en");
                    portuguese = lang == "pt";
                    break;
                case "--history":
                    history = true;
                    break;
                default:
                    throw new ArgumentException("unknown argument: " + args[i]);
            }
        }
        if (scenario != null && Array.IndexOf(Recordings.Scenarios, scenario) < 0)
            throw new ArgumentException("unknown scenario: " + scenario + " (" + string.Join(", ", Recordings.Scenarios) + ")");
        if (screenshot != null && scenario == null) throw new ArgumentException("--screenshot needs --scenario");
        if (history && screenshot == null) throw new ArgumentException("--history goes with --screenshot");
        return new SampleOptions(screenshot, scenario ?? Recordings.Scenarios[0], portuguese, history, touch);
    }

    private static string Value(string[] args, ref int i)
    {
        if (i + 1 >= args.Length) throw new ArgumentException(args[i] + " needs a value");
        return args[++i];
    }
}
