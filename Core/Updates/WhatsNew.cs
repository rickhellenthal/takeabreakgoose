namespace TakeABreakGoose;

/// <summary>Whether to show what's new when the app starts: only the first time a new version runs.</summary>
public static class WhatsNew
{
    /// <param name="lastRunVersion">The version that ran before (GooseSettings.LastRunVersion), if it was known.</param>
    /// <param name="currentVersion">This version, e.g. "1.11.0" or "1.11.0-spike.3".</param>
    /// <param name="hadSettings">
    /// Whether there was a settings file when the app started. Without one this is a new install, with
    /// nothing to compare to; with one but no version in it, a version from before the app remembered it.
    /// </param>
    public static bool ShouldShow(string? lastRunVersion, string currentVersion, bool hadSettings) =>
        lastRunVersion == null ? hadSettings : lastRunVersion != currentVersion;
}
