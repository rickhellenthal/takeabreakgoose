using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace TakeABreakGoose;

/// <param name="Name">How to show the version, when it's more than its numbers (e.g. "1.11.0-spike.3" for a test build).</param>
public sealed record AvailableUpdate(Version Version, string Url, string? Name = null)
{
    public string DisplayName => Name ?? UpdateChecker.Display(Version);
}

/// <summary>Checks GitHub for a newer release. Quietly gives up when offline or when anything goes wrong.</summary>
public static class UpdateChecker
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/rickhellenthal/takeabreakgoose/releases/latest";

    // The app's version, not this library's: the app is what gets released.
    public static Version CurrentVersion { get; } =
        (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version ?? new Version(0, 0, 0);

    /// <summary>"1.3.0" rather than "1.3.0.0".</summary>
    public static string Display(Version version) => version.ToString(3);

    /// <summary>The newer release, if there is one. The app decides how often to ask (UpdateSchedule).</summary>
    public static Task<AvailableUpdate?> CheckAsync() => FetchAsync();

    // Shell-executing a URL opens the default browser on Windows, and runs `open` on macOS.
    public static void OpenInBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private static async Task<AvailableUpdate?> FetchAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"TakeABreakGoose/{Display(CurrentVersion)}");
            using var json = JsonDocument.Parse(await http.GetStringAsync(LatestReleaseApi));
            string tag = json.RootElement.GetProperty("tag_name").GetString() ?? "";
            string url = json.RootElement.GetProperty("html_url").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return null;
            return Normalize(latest) > Normalize(CurrentVersion) ? new AvailableUpdate(latest, url) : null;
        }
        catch (Exception)
        {
            return null; // offline, rate-limited, or GitHub changed something: no news is fine
        }
    }

    // 1.3 and 1.3.0.0 should compare as equal.
    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
