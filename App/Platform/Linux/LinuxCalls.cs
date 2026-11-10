using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

namespace TakeABreakGoose;

/// <summary>
/// Tells whether the user is in a call on Linux: a program of the user's has a camera open
/// (/dev/video*), or a meeting app records from a microphone (PulseAudio's or PipeWire's list of
/// recording streams, through pactl).
/// </summary>
/// <remarks>
/// The microphone only counts for meeting apps: games, Discord and Steam use it for voice chat,
/// where the goose is still welcome. An app missing from the list means the goose shows up as
/// usual, which is better than it quietly staying away for hours.
/// </remarks>
[SupportedOSPlatform("linux")]
public static class LinuxCalls
{
    // By program name. Browsers count: Google Meet and Teams run in them.
    private static readonly string[] MeetingPrograms =
    {
        "zoom", "zoom.real", "teams", "teams-for-linux", "slack", "skypeforlinux", "webex", "gotomeeting",
        "chrome", "google-chrome", "chromium", "chromium-browser", "msedge", "microsoft-edge", "firefox",
        "firefox-bin", "brave", "brave-browser", "opera", "vivaldi-bin",
    };

    private static bool _noPactl;

    public static bool InProgress() => CameraInUse() || RecordingPrograms().Any(IsMeetingApp);

    internal static bool IsMeetingApp(string program) => MeetingPrograms.Contains(program.ToLowerInvariant());

    /// <summary>Whether any of the user's programs has a camera open. Other users' programs can't be seen, which is fine.</summary>
    private static bool CameraInUse()
    {
        try
        {
            foreach (string process in Directory.EnumerateDirectories("/proc"))
            {
                if (!char.IsAsciiDigit(Path.GetFileName(process)[0])) continue;
                try
                {
                    foreach (string fd in Directory.EnumerateFileSystemEntries(Path.Combine(process, "fd")))
                        if (new FileInfo(fd).LinkTarget is { } target && target.StartsWith("/dev/video", StringComparison.Ordinal))
                            return true;
                }
                catch (Exception)
                {
                    // Another user's process, or one that just ended.
                }
            }
        }
        catch (Exception)
        {
            // No /proc to look in.
        }
        return false;
    }

    /// <summary>The programs recording from a microphone right now, by name, e.g. "zoom".</summary>
    private static IEnumerable<string> RecordingPrograms()
    {
        if (_noPactl) return Array.Empty<string>();
        try
        {
            using var pactl = Process.Start(new ProcessStartInfo("pactl", "list source-outputs")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                Environment = { ["LC_ALL"] = "C" },
            });
            if (pactl == null) return Array.Empty<string>();
            string output = pactl.StandardOutput.ReadToEnd();
            if (!pactl.WaitForExit(2000)) pactl.Kill();
            return ParseRecordingPrograms(output);
        }
        catch (Exception)
        {
            _noPactl = true; // no pactl here: only the camera counts
            return Array.Empty<string>();
        }
    }

    /// <summary>From pactl's list, each stream's `application.process.binary = "zoom"`.</summary>
    internal static List<string> ParseRecordingPrograms(string pactlOutput)
    {
        var programs = new List<string>();
        foreach (string line in pactlOutput.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("application.process.binary", StringComparison.Ordinal)) continue;
            int start = trimmed.IndexOf('"');
            int end = trimmed.LastIndexOf('"');
            if (start >= 0 && end > start) programs.Add(trimmed[(start + 1)..end]);
        }
        return programs;
    }
}
