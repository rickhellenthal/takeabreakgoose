using Xunit;

namespace TakeABreakGoose.Tests;

public class ReleaseHistoryTests
{
    private static GitHubRelease Release(string tag, bool preRelease = false) => new(tag, $"Notes of {tag}", preRelease);

    private static readonly GitHubRelease[] Releases =
    {
        Release("v1.13.0"), Release("v1.12.0"), Release("v1.12.0-spike.2", preRelease: true),
        Release("v1.11.0"), Release("v1.10.0"), Release("v1.9.0"),
    };

    [Fact]
    public void Gives_every_version_since_the_installed_one_newest_first()
    {
        var notes = ReleaseHistory.Since(Releases, installed: "1.10.0", target: "1.13.0", out bool more);

        Assert.Equal(new[] { "1.13.0", "1.12.0", "1.11.0" }, notes.Select(n => n.Version));
        Assert.Equal("Notes of v1.12.0", notes[1].Markdown);
        Assert.False(more);
    }

    [Fact]
    public void Leaves_out_versions_newer_than_the_one_updated_to()
    {
        var notes = ReleaseHistory.Since(Releases, installed: "1.10.0", target: "1.12.0", out _);

        Assert.Equal(new[] { "1.12.0", "1.11.0" }, notes.Select(n => n.Version));
    }

    [Fact]
    public void Gives_at_most_three_and_says_there_are_more()
    {
        var notes = ReleaseHistory.Since(Releases, installed: "1.8.0", target: "1.13.0", out bool more);

        Assert.Equal(new[] { "1.13.0", "1.12.0", "1.11.0" }, notes.Select(n => n.Version));
        Assert.True(more);
    }

    [Fact]
    public void Pre_releases_only_count_when_updating_to_one()
    {
        var spikes = new[] { Release("v1.11.0-spike.3", true), Release("v1.11.0-spike.2", true), Release("v1.10.0") };

        var toSpike = ReleaseHistory.Since(spikes, installed: "1.11.0-spike.1", target: "1.11.0-spike.3", out _);
        var toRelease = ReleaseHistory.Since(Releases, installed: "1.11.0", target: "1.12.0", out _);

        Assert.Equal(new[] { "1.11.0-spike.3", "1.11.0-spike.2" }, toSpike.Select(n => n.Version));
        Assert.Equal(new[] { "1.12.0" }, toRelease.Select(n => n.Version));
    }

    [Fact]
    public void Tags_that_are_not_versions_are_skipped()
    {
        var notes = ReleaseHistory.Since(new[] { Release("nightly"), Release("v1.11.0") }, "1.10.0", "1.11.0", out _);

        Assert.Equal(new[] { "1.11.0" }, notes.Select(n => n.Version));
    }

    [Fact]
    public void Reads_GitHubs_list_of_releases_without_drafts()
    {
        const string json = """
            [
              { "tag_name": "v1.12.0", "body": "## New", "prerelease": false, "draft": true },
              { "tag_name": "v1.11.0", "body": "## Newer things", "prerelease": false, "draft": false },
              { "tag_name": "v1.11.0-spike.2", "body": null, "prerelease": true, "draft": false }
            ]
            """;

        var releases = ReleaseHistory.Parse(json);

        Assert.Equal(new[]
        {
            new GitHubRelease("v1.11.0", "## Newer things", false),
            new GitHubRelease("v1.11.0-spike.2", "", true),
        }, releases);
    }

    [Theory]
    [InlineData("1.11.0", "1.10.0")]
    [InlineData("1.10.0", "1.9.0")]
    [InlineData("1.11.0", "1.11.0-spike.3")]
    [InlineData("1.11.0-spike.10", "1.11.0-spike.2")]
    [InlineData("v2.0.0", "1.99.99")]
    public void Versions_compare_as_semantic_versions(string newer, string older)
    {
        Assert.True(ReleaseVersion.Parse(newer).CompareTo(ReleaseVersion.Parse(older)) > 0);
        Assert.True(ReleaseVersion.Parse(older).CompareTo(ReleaseVersion.Parse(newer)) < 0);
    }
}
