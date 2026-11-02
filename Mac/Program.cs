using System;
using Avalonia;
using Avalonia.Controls;

namespace TakeABreakGoose;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        // The app lives in the menu bar: closing the window doesn't quit it, only Quit does.
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // A menu bar app: no Dock icon (Info.plist's LSUIElement does the same once it's an .app).
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .LogToTrace();
}
