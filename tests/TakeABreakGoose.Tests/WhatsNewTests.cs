using Xunit;

namespace TakeABreakGoose.Tests;

public class WhatsNewTests
{
    [Fact]
    public void Shown_the_first_time_a_new_version_runs()
    {
        Assert.True(WhatsNew.ShouldShow("1.11.0", "1.12.0", hadSettings: true));
        Assert.True(WhatsNew.ShouldShow("1.11.0-spike.1", "1.11.0-spike.2", hadSettings: true));
    }

    [Fact]
    public void Not_shown_again_for_the_same_version()
    {
        Assert.False(WhatsNew.ShouldShow("1.12.0", "1.12.0", hadSettings: true));
    }

    [Fact]
    public void Not_shown_on_a_new_install()
    {
        Assert.False(WhatsNew.ShouldShow(null, "1.12.0", hadSettings: false));
    }

    [Fact]
    public void Shown_after_updating_from_a_version_that_did_not_remember_its_version()
    {
        Assert.True(WhatsNew.ShouldShow(null, "1.12.0", hadSettings: true));
    }
}
