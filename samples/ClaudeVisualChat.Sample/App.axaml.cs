using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace ClaudeVisualChat.Sample;

public partial class App : Application
{
    internal static SampleOptions Options { get; set; } = SampleOptions.Parse(Array.Empty<string>());

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var options = Options;
            var window = new MainWindow(options);
            if (options.Screenshot is { } path)
            {
                // drawn off screen and never activated: the screenshot takes no focus and shows nothing on the desktop
                window.ShowActivated = false;
                window.ShowInTaskbar = false;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Position = new PixelPoint(-20000, -20000);
                window.Opened += async (_, _) =>
                {
                    var code = 1;
                    try
                    {
                        await window.SaveScreenshotAsync(path, options.History);
                        code = 0;
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine("screenshot failed: " + e);
                    }
                    desktop.Shutdown(code);
                };
            }
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
