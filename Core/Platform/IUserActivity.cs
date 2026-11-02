using System;

namespace TakeABreakGoose;

/// <summary>
/// Tells how long ago the user last used the mouse or keyboard. Each platform asks its own system
/// (GetLastInputInfo on Windows).
/// </summary>
public interface IUserActivity
{
    TimeSpan IdleTime();
}
