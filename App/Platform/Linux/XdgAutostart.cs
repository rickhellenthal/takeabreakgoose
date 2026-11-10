using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

namespace TakeABreakGoose;

/// <summary>
/// Starts the app when the user logs in to their Linux desktop: a .desktop file in
/// ~/.config/autostart, which GNOME, KDE and the other desktops all read.
/// </summary>
[SupportedOSPlatform("linux")]
public static class XdgAutostart
{
    public const string Argument = "--autostart";

    private static string File => Path.Combine(ConfigFolder(), "autostart", AppIdentity.Name + ".desktop");

    public static bool IsEnabled
    {
        get
        {
            try
            {
                if (!System.IO.File.Exists(File)) return false;
                // Turned off in the desktop's own settings: the file stays, with one of these in it.
                var lines = System.IO.File.ReadAllLines(File).Select(l => l.Trim()).ToList();
                return !lines.Contains("Hidden=true") && !lines.Contains("X-GNOME-Autostart-enabled=false");
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public static void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            if (System.IO.File.Exists(File)) System.IO.File.Delete(File);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        System.IO.File.WriteAllText(File,
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Name=Take a Break Goose\n" +
            "Comment=A friendly goose that reminds you to take a break\n" +
            $"Exec={Quote(ExecutablePath())} {Argument}\n" +
            "Terminal=false\n" +
            "X-GNOME-Autostart-enabled=true\n");
    }

    /// <summary>The AppImage when running from one (the running file is inside it, in a temporary mount), otherwise this program.</summary>
    private static string ExecutablePath() =>
        Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage
            ? appImage
            : Environment.ProcessPath ?? throw new InvalidOperationException("Can't tell where the app is.");

    private static string ConfigFolder() =>
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } config
            ? config
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

    // The Exec key's quoting: in double quotes, with ", `, $ and \ escaped.
    private static string Quote(string path) =>
        "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$") + "\"";
}
