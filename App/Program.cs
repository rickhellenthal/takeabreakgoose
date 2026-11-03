using System;
using Avalonia;
using Avalonia.Controls;
using Velopack;

namespace TakeABreakGoose;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // First of all: when Velopack's installer or updater starts the app to let it know about an
        // install, update or uninstall, this handles that and quits.
        VelopackApp.Build().Run();

        // Before Avalonia starts, which can't quit again before its main loop runs.
        using var singleInstance = new SingleInstance(App.SettingsFolder());
        if (!singleInstance.IsFirst)
        {
            SingleInstance.AskFirstToOpen();
            return;
        }
        App.SingleInstance = singleInstance;

        // The app lives in the tray or the menu bar: closing the window doesn't quit it, only Quit does.
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // On a Mac, a menu bar app: no Dock icon (Info.plist's LSUIElement does the same once it's an .app).
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .LogToTrace();
}
