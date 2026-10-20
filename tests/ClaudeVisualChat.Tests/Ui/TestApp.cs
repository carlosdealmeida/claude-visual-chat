using System.Collections.Generic;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Logging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(ClaudeVisualChat.Tests.Ui.TestApp))]

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>
/// The app the headless UI tests run in: Fluent in dark, and a log that keeps binding errors. Drawn
/// with Skia and the system fonts (Segoe UI, Consolas), so text is measured as on screen and a frame can be captured.
/// </summary>
public class TestApp : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        Logger.Sink = TestLog.Instance;
    }
}

/// <summary>Warnings and errors Avalonia logs about bindings and resources (a broken path, a missing property…).</summary>
public sealed class TestLog : ILogSink
{
    public static readonly TestLog Instance = new TestLog();

    private readonly List<string> _entries = new List<string>();

    public IReadOnlyList<string> Entries => _entries;

    public void Clear() => _entries.Clear();

    public bool IsEnabled(LogEventLevel level, string area) =>
        level >= LogEventLevel.Warning && (area == LogArea.Binding || area == LogArea.Property);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Log(level, area, source, messageTemplate, new object?[0]);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        if (!IsEnabled(level, area)) return;
        _entries.Add(area + " " + level + ": " + messageTemplate + " [" + string.Join(", ", propertyValues) + "] on " + source);
    }
}
