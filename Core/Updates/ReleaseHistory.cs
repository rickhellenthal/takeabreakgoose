using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TakeABreakGoose;

/// <summary>A release on GitHub: its tag ("v1.11.0") and its description, which is its release notes.</summary>
public sealed record GitHubRelease(string Tag, string Notes, bool PreRelease);

/// <summary>The release notes of one version, as Markdown.</summary>
public sealed record VersionNotes(string Version, string Markdown);

/// <summary>The release notes to show after an update, newest first; <paramref name="More"/>: older ones were left out.</summary>
public sealed record UpdateNotes(IReadOnlyList<VersionNotes> Versions, bool More);

/// <summary>
/// The release notes to show after updating: those of every version since the one that was
/// installed, newest first, so nothing is missed when versions were skipped.
/// </summary>
public static class ReleaseHistory
{
    /// <summary>At most this many versions; the release page has the rest.</summary>
    public const int MaxVersions = 3;

    /// <summary>GitHub's list of releases (GET /repos/{owner}/{repo}/releases), without drafts.</summary>
    public static IReadOnlyList<GitHubRelease> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var releases = new List<GitHubRelease>();
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            releases.Add(new GitHubRelease(
                release.GetProperty("tag_name").GetString() ?? "",
                release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()));
        }
        return releases;
    }

    /// <summary>
    /// The versions after <paramref name="installed"/> up to and including <paramref name="target"/>,
    /// newest first, at most <see cref="MaxVersions"/>. Pre-releases only count when updating to one.
    /// </summary>
    /// <param name="more">Whether there were more versions in between than fit.</param>
    public static IReadOnlyList<VersionNotes> Since(IEnumerable<GitHubRelease> releases, string installed, string target, out bool more)
    {
        var from = ReleaseVersion.Parse(installed);
        var to = ReleaseVersion.Parse(target);
        var between = releases
            .Select(r => (Release: r, Version: ReleaseVersion.TryParse(r.Tag)))
            .Where(r => r.Version is { } v && v.CompareTo(from) > 0 && v.CompareTo(to) <= 0
                        && (to.IsPreRelease || !r.Release.PreRelease))
            .OrderByDescending(r => r.Version!.Value)
            .Select(r => new VersionNotes(r.Version!.Value.ToString(), r.Release.Notes))
            .ToList();
        more = between.Count > MaxVersions;
        return between.Take(MaxVersions).ToList();
    }
}

/// <summary>A version like "1.11.0" or "1.11.0-spike.3", compared the way semantic versioning does.</summary>
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch, string PreRelease) : IComparable<ReleaseVersion>
{
    public bool IsPreRelease => PreRelease.Length > 0;

    /// <summary>Also takes a tag ("v1.11.0").</summary>
    public static ReleaseVersion? TryParse(string text)
    {
        string version = text.Trim().TrimStart('v', 'V').Split('+')[0];
        int dash = version.IndexOf('-');
        string numbers = dash < 0 ? version : version[..dash];
        string pre = dash < 0 ? "" : version[(dash + 1)..];
        string[] parts = numbers.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor)
            || !int.TryParse(parts[2], out int patch))
            return null;
        return new ReleaseVersion(major, minor, patch, pre);
    }

    public static ReleaseVersion Parse(string text) =>
        TryParse(text) ?? throw new FormatException($"Not a version: {text}");

    public int CompareTo(ReleaseVersion other)
    {
        int numbers = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (numbers != 0) return numbers;
        // 1.11.0-spike.3 comes before 1.11.0 itself.
        if (IsPreRelease != other.IsPreRelease) return IsPreRelease ? -1 : 1;
        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    // "spike.2" before "spike.10": parts that are numbers compare as numbers.
    private static int ComparePreRelease(string a, string b)
    {
        string[] x = a.Split('.'), y = b.Split('.');
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            bool xNumber = int.TryParse(x[i], out int xn), yNumber = int.TryParse(y[i], out int yn);
            int part = xNumber && yNumber ? xn.CompareTo(yn)
                : xNumber ? -1
                : yNumber ? 1
                : string.CompareOrdinal(x[i], y[i]);
            if (part != 0) return part;
        }
        return x.Length.CompareTo(y.Length);
    }

    public override string ToString() => IsPreRelease ? $"{Major}.{Minor}.{Patch}-{PreRelease}" : $"{Major}.{Minor}.{Patch}";
}
