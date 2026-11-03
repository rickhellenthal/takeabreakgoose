using Xunit;

namespace TakeABreakGoose.Tests;

public class ReleaseNotesTests
{
    // Shaped like release-notes/v1.10.0.md.
    private const string Notes = """
        ## 🪿 One goose for Windows and Mac

        Under the hood, the Windows goose is now the same app as the Mac one. On Windows it works just like before.

        What you might notice:

        - **A smaller download**: about a third smaller than before.
        - **Clicks go right past the goose.** Only the goose itself catches clicks.

        ## 🍎 On a Mac

        - Check boxes and buttons are the goose's own orange, instead of the accent colour in *System Settings*.

        ## 🇳🇱 The goose speaks Dutch

        Take a Break Goose now speaks English and Dutch. Pick one under **Language** in the Settings tab.

        ## Install

        - **`TakeABreakGoose-Setup-1.10.0.exe`**: run it to install or update.
        """;

    [Fact]
    public void Each_heading_becomes_a_section_and_the_Install_section_is_left_out()
    {
        var sections = ReleaseNotes.Highlights(Notes);

        Assert.Equal(new[] { "🪿 One goose for Windows and Mac", "🍎 On a Mac", "🇳🇱 The goose speaks Dutch" },
            sections.Select(s => s.Title));
    }

    [Fact]
    public void A_point_with_a_bold_start_is_shortened_to_it()
    {
        var points = ReleaseNotes.Highlights(Notes)[0].Points;

        Assert.Equal(new[] { "A smaller download", "Clicks go right past the goose." }, points);
    }

    [Fact]
    public void A_point_without_a_bold_start_is_kept_whole_without_its_markdown()
    {
        var points = ReleaseNotes.Highlights(Notes)[1].Points;

        Assert.Equal(new[] { "Check boxes and buttons are the goose's own orange, instead of the accent colour in System Settings." },
            points);
    }

    [Fact]
    public void A_section_without_points_gets_its_first_sentence()
    {
        var points = ReleaseNotes.Highlights(Notes)[2].Points;

        Assert.Equal(new[] { "Take a Break Goose now speaks English and Dutch." }, points);
    }

    [Fact]
    public void Links_become_their_text()
    {
        var sections = ReleaseNotes.Highlights("## New\n\n- See the [README](https://example.com/readme) for more.");

        Assert.Equal("See the README for more.", sections[0].Points[0]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Just a line without a heading.")]
    public void Notes_without_headings_give_nothing_to_show(string? markdown)
    {
        Assert.Empty(ReleaseNotes.Highlights(markdown));
    }
}
