using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace TakeABreakGoose;

/// <summary>
/// Updates the app from inside it, with Velopack and GitHub Releases as the source. Only for an app
/// that Velopack installed (its Setup.exe or .pkg, or its portable zip); a build folder or an older
/// install falls back to UpdateChecker's link to the release page.
/// </summary>
public static class Updater
{
    private const string RepoUrl = "https://github.com/rickhellenthal/takeabreakgoose";
    private const string ReleasesUrl = RepoUrl + "/releases";
    private const string ReleasesApi = "https://api.github.com/repos/rickhellenthal/takeabreakgoose/releases?per_page=30";

    /// <summary>Started again after an update.</summary>
    public const string UpdatedArgument = "--updated";

    private static UpdateManager? _manager;
    private static UpdateInfo? _update;

    /// <summary>Whether this copy of the app can update itself.</summary>
    public static bool CanUpdate => Manager?.IsInstalled == true;

    /// <summary>The installed version, as Velopack knows it (with "-spike.3" for a test build); null when not installed by Velopack.</summary>
    public static string? CurrentVersionName => CanUpdate ? Manager!.CurrentVersion?.ToString() : null;

    /// <summary>The newer release, if there is one. Quietly gives up when offline or when anything goes wrong.</summary>
    public static async Task<AvailableUpdate?> CheckAsync()
    {
        if (Manager is not { } manager) return null;
        try
        {
            _update = await manager.CheckForUpdatesAsync();
            if (_update == null) return null;
            var version = _update.TargetFullRelease.Version;
            return new AvailableUpdate(new Version(version.Major, version.Minor, version.Patch), ReleasesUrl,
                version.IsPrerelease ? version.ToString() : null);
        }
        catch (Exception)
        {
            return null; // offline, rate-limited, or GitHub changed something: no news is fine
        }
    }

    /// <summary>
    /// Downloads the update found by <see cref="CheckAsync"/>, then quits and starts the new version.
    /// Only returns when that didn't work; throws OperationCanceledException when cancelled.
    /// </summary>
    /// <param name="progress">How far the download is, 0 to 100. Called from a background thread.</param>
    /// <param name="beforeRestart">Once downloaded, just before the app quits.</param>
    public static async Task DownloadAndRestartAsync(Action<int> progress, Action beforeRestart, CancellationToken cancel)
    {
        if (Manager is not { } manager || _update is not { } update)
            throw new InvalidOperationException("There's no update to install.");
        // Meanwhile: the release notes of every version since this one, for the new one to show.
        var notes = NotesSinceAsync(manager.CurrentVersion?.ToString(), update.TargetFullRelease, cancel);
        await manager.DownloadUpdatesAsync(update, progress, cancel);
        SaveReleaseNotes(await notes);
        cancel.ThrowIfCancellationRequested();
        beforeRestart();
        manager.ApplyUpdatesAndRestart(update, new[] { UpdatedArgument });
    }

    /// <summary>
    /// From GitHub's list of releases, so versions that were skipped aren't missed. When that doesn't
    /// work out, only those of the new version, which Velopack brings along.
    /// </summary>
    private static async Task<UpdateNotes> NotesSinceAsync(string? installed, VelopackAsset target, CancellationToken cancel)
    {
        string targetVersion = target.Version.ToString();
        var newest = new VersionNotes(targetVersion, target.NotesMarkdown ?? "");
        try
        {
            if (installed == null) return new UpdateNotes(new[] { newest }, More: false);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"TakeABreakGoose/{installed}");
            string json = await http.GetStringAsync(ReleasesApi, cancel);
            var versions = ReleaseHistory.Since(ReleaseHistory.Parse(json), installed, targetVersion, out bool more)
                // A release without a description on GitHub: Velopack's copy of its notes, if it's the new one.
                .Select(v => v.Version == targetVersion && string.IsNullOrWhiteSpace(v.Markdown) ? newest : v)
                .ToList();
            return versions.Count > 0 ? new UpdateNotes(versions, more) : new UpdateNotes(new[] { newest }, More: false);
        }
        catch (Exception)
        {
            return new UpdateNotes(new[] { newest }, More: false);
        }
    }

    // The release notes, kept for the new version until it has started and said what's new.
    private static string ReleaseNotesFile => Path.Combine(App.SettingsFolder(), "whats-new.json");

    private static void SaveReleaseNotes(UpdateNotes notes)
    {
        try
        {
            File.WriteAllText(ReleaseNotesFile, JsonSerializer.Serialize(notes));
        }
        catch (Exception)
        {
            // Without them, what's new only links to the release page.
        }
    }

    /// <summary>The release notes the previous version saved just before updating to this one, once.</summary>
    public static UpdateNotes? TakeReleaseNotes()
    {
        try
        {
            if (!File.Exists(ReleaseNotesFile)) return null;
            string json = File.ReadAllText(ReleaseNotesFile);
            File.Delete(ReleaseNotesFile);
            return JsonSerializer.Deserialize<UpdateNotes>(json);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The release page of a version, e.g. ".../releases/tag/v1.11.0".</summary>
    public static string ReleaseUrl(string version) => $"{ReleasesUrl}/tag/v{version}";

    /// <summary>The list of all releases.</summary>
    public static string AllReleasesUrl => ReleasesUrl;

    private static UpdateManager? Manager
    {
        get
        {
            if (_manager != null) return _manager;
            try
            {
                // A pre-release (a test build) also looks for newer pre-releases; a normal release only for releases.
                _manager = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: IsPreRelease()));
            }
            catch (Exception)
            {
                // Not installed by Velopack (e.g. run from a build folder): nothing to update.
            }
            return _manager;
        }
    }

    // "1.11.0-spike.3+abc123" for a test build, "1.11.0+abc123" for a release.
    private static bool IsPreRelease()
    {
        string? version = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return version?.Split('+')[0].Contains('-') == true;
    }
}
