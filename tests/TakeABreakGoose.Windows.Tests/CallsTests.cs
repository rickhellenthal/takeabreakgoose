using Xunit;

namespace TakeABreakGoose.Windows.Tests;

public class CallsTests
{
    [Theory]
    [InlineData("Zoom")]
    [InlineData("ms-teams")]
    [InlineData("Teams")]
    [InlineData("chrome")]
    [InlineData("msedge")]
    [InlineData("slack")]
    public void Meeting_apps_and_browsers_count(string program)
    {
        Assert.True(Calls.IsMeetingApp(program));
    }

    [Theory]
    [InlineData("Discord")]
    [InlineData("steam")]
    [InlineData("SoTGame")]
    [InlineData("Audacity")]
    [InlineData("javaw")]
    [InlineData("msedgewebview2")] // only counts through the app that started it
    public void Games_voice_chat_and_recording_dont_count(string program)
    {
        Assert.False(Calls.IsMeetingApp(program));
    }
}
