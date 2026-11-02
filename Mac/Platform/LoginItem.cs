namespace TakeABreakGoose;

/// <summary>
/// Starting the app when the user logs in. Not there yet: step 3 of docs/macos-port.md adds it with
/// SMAppService.
/// </summary>
public static class LoginItem
{
    public static bool IsEnabled => false;

    public static void SetEnabled(bool enabled)
    {
    }
}
