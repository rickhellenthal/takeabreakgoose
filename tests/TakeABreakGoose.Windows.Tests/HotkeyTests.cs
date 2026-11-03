using Xunit;

namespace TakeABreakGoose.Windows.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("G", 0x47)]
    [InlineData("D", 0x44)]
    [InlineData("D5", 0x35)]
    [InlineData("NumPad7", 0x67)]
    [InlineData("F1", 0x70)]
    [InlineData("F12", 0x7B)]
    [InlineData("Space", 0x20)]
    [InlineData("OemPlus", 0xBB)]
    [InlineData("Oem3", 0xC0)]
    public void Key_names_from_the_settings_become_virtual_keys(string key, uint virtualKey)
    {
        Assert.Equal(virtualKey, WindowsHotkey.VirtualKey(key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("F25")]
    [InlineData("NumPad")]
    [InlineData("Goose")]
    public void Names_that_are_no_key_are_refused(string key)
    {
        Assert.Null(WindowsHotkey.VirtualKey(key));
    }
}
