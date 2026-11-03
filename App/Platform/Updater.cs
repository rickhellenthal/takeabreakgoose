using System;
using System.Reflection;
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
    /// Only returns when that didn't work.
    /// </summary>
    public static async Task DownloadAndRestartAsync()
    {
        if (Manager is not { } manager || _update is not { } update)
            throw new InvalidOperationException("There's no update to install.");
        await manager.DownloadUpdatesAsync(update);
        manager.ApplyUpdatesAndRestart(update, new[] { UpdatedArgument });
    }

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
