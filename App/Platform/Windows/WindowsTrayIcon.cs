using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Platform;

namespace TakeABreakGoose;

/// <summary>
/// The goose in the notification area, with Windows' own menu and balloons (Avalonia's tray icon
/// draws its menu itself, which doesn't look like the other icons' menus).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class WindowsTrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_NULL = 0x0000;

    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04, NIF_INFO = 0x10;

    private const uint MF_STRING = 0x0000, MF_CHECKED = 0x0008, MF_SEPARATOR = 0x0800;
    private const uint TPM_BOTTOMALIGN = 0x0020, TPM_NONOTIFY = 0x0080, TPM_RETURNCMD = 0x0100, TPM_RIGHTBUTTON = 0x0002;

    private enum Command { Open = 1, BreakNow, DoNotDisturb, StopSession, Quit }

    private readonly HiddenWindow _window = new();
    private readonly IntPtr _icon;
    // Windows sends this when Explorer (and with it the taskbar) has restarted: the icon has to be added again.
    private readonly uint _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");

    private string _toolTip = "Take a Break Goose";
    private bool _doNotDisturb;
    private bool _inCall;
    private bool _sessionRunning;
    private string _hotkeyText = "";

    public event Action? OpenRequested;
    public event Action? SummonRequested;
    public event Action? StopSessionRequested;
    public event Action? DoNotDisturbToggled;
    public event Action? QuitRequested;

    public WindowsTrayIcon()
    {
        _icon = LoadGooseIcon(GetSystemMetrics(SmallIconWidth));
        _window.Message += OnMessage;
        Add();
    }

    /// <param name="inCall">Do not disturb is on because the user is in a call.</param>
    /// <param name="hotkeyText">The global shortcut, shown next to Open.</param>
    public void Update(string status, bool doNotDisturb, bool inCall, bool sessionRunning, string hotkeyText)
    {
        _doNotDisturb = doNotDisturb;
        _inCall = inCall;
        _sessionRunning = sessionRunning;
        _hotkeyText = hotkeyText;
        // The menu is made each time it opens, from these; the tooltip changes right away.
        string toolTip = $"Take a Break Goose\n{status}";
        if (toolTip == _toolTip) return;
        _toolTip = toolTip;
        var data = NewData(NIF_TIP);
        Notify(NIM_MODIFY, ref data);
    }

    public void ShowBalloon(string message)
    {
        var data = NewData(NIF_INFO);
        Copy(message, data.Info, 256);
        Copy("Take a Break Goose", data.InfoTitle, 64);
        Notify(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        var data = NewData(0);
        Notify(NIM_DELETE, ref data);
        _window.Dispose();
        DestroyIcon(_icon);
    }

    private void Add()
    {
        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        Notify(NIM_ADD, ref data);
    }

    private void OnMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == _taskbarCreated)
        {
            Add();
            return;
        }
        if (message != CallbackMessage) return;
        switch ((uint)lParam)
        {
            case WM_LBUTTONUP:
                OpenRequested?.Invoke();
                break;
            case WM_RBUTTONUP:
                ShowMenu();
                break;
        }
    }

    private void ShowMenu()
    {
        IntPtr menu = CreatePopupMenu();
        try
        {
            string open = _hotkeyText.Length > 0 ? $"{Strings.MenuOpen}\t{_hotkeyText}" : Strings.MenuOpen;
            AppendMenuW(menu, MF_STRING, (nuint)Command.Open, open);
            // Breaks, Do not disturb and stopping only mean something during a focus session.
            // Do not disturb can outlast its session (the goose's goodbye waits for it), so it stays while on.
            if (_sessionRunning) AppendMenuW(menu, MF_STRING, (nuint)Command.BreakNow, Strings.TakeABreakNow);
            if (_sessionRunning || _doNotDisturb)
                AppendMenuW(menu, MF_STRING | (_doNotDisturb ? MF_CHECKED : 0), (nuint)Command.DoNotDisturb,
                    _inCall ? Strings.MenuDoNotDisturbInCall : Strings.MenuDoNotDisturb);
            if (_sessionRunning) AppendMenuW(menu, MF_STRING, (nuint)Command.StopSession, Strings.MenuStopSession);
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, MF_STRING, (nuint)Command.Quit, Strings.MenuQuit);
            // Open is what a click on the icon does: bold, like in other tray menus.
            SetMenuDefaultItem(menu, (uint)Command.Open, 0);

            GetCursorPos(out var cursor);
            // The menu only closes when clicking elsewhere if its window is in front (see TrackPopupMenu).
            SetForegroundWindow(_window.Handle);
            var command = (Command)TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_NONOTIFY | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN,
                cursor.X, cursor.Y, _window.Handle, IntPtr.Zero);
            PostMessageW(_window.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            switch (command)
            {
                case Command.Open: OpenRequested?.Invoke(); break;
                case Command.BreakNow: SummonRequested?.Invoke(); break;
                case Command.DoNotDisturb: DoNotDisturbToggled?.Invoke(); break;
                case Command.StopSession: StopSessionRequested?.Invoke(); break;
                case Command.Quit: QuitRequested?.Invoke(); break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private NotifyIconData NewData(uint flags)
    {
        var data = new NotifyIconData
        {
            Size = (uint)sizeof(NotifyIconData),
            Window = _window.Handle,
            Id = 1,
            Flags = flags,
            CallbackMessage = CallbackMessage,
            Icon = _icon,
        };
        if ((flags & NIF_TIP) != 0) Copy(_toolTip, data.Tip, 128);
        return data;
    }

    private static void Notify(uint message, ref NotifyIconData data)
    {
        fixed (NotifyIconData* pointer = &data) Shell_NotifyIconW(message, pointer);
    }

    /// <summary>Into one of the structure's fixed text fields, cut off to fit.</summary>
    private static void Copy(string text, char* field, int length)
    {
        int count = Math.Min(text.Length, length - 1);
        text.AsSpan(0, count).CopyTo(new Span<char>(field, length));
        field[count] = '\0';
    }

    /// <summary>The image in goose.ico nearest to the size the notification area wants.</summary>
    private static IntPtr LoadGooseIcon(int size)
    {
        using var stream = AssetLoader.Open(new Uri("avares://TakeABreakGoose/Assets/goose.ico"));
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        byte[] ico = memory.ToArray();

        // An .ico starts with a count of images, then 16 bytes per image: width (0 for 256), ..., size, offset.
        int count = BitConverter.ToUInt16(ico, 4);
        int best = -1, bestWidth = 0;
        for (int i = 0; i < count; i++)
        {
            int entry = 6 + 16 * i;
            int width = ico[entry] == 0 ? 256 : ico[entry];
            // The smallest that's at least as big, or else the biggest.
            bool better = best < 0
                          || (width >= size && (bestWidth < size || width < bestWidth))
                          || (width < size && bestWidth < size && width > bestWidth);
            if (better)
            {
                best = entry;
                bestWidth = width;
            }
        }
        if (best < 0) return IntPtr.Zero;
        int length = BitConverter.ToInt32(ico, best + 8);
        int offset = BitConverter.ToInt32(ico, best + 12);
        fixed (byte* image = &ico[offset])
            return CreateIconFromResourceEx(image, (uint)length, 1, 0x00030000, size, size, 0);
    }

    private const int SmallIconWidth = 49; // SM_CXSMICON

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        public fixed char Tip[128];
        public uint State;
        public uint StateMask;
        public fixed char Info[256];
        public uint TimeoutOrVersion;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Shell_NotifyIconW(uint message, NotifyIconData* data);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll")]
    private static partial IntPtr CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AppendMenuW(IntPtr menu, uint flags, nuint id, string? text);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPosition);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr parameters);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(IntPtr menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out CursorPoint point);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    private static partial IntPtr CreateIconFromResourceEx(byte* data, uint size, int isIcon, uint version,
        int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr icon);
}
