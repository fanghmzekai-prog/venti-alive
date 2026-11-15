using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Aeris.WindowProbe;

internal static class Native
{
    internal const int MouseActivate = 0x0021;
    internal const int MouseMove = 0x0200;
    internal const int LeftButtonDown = 0x0201;
    internal const int CancelMode = 0x001f;
    internal const int DisplayChanged = 0x007e;
    internal const int DpiChanged = 0x02e0;
    internal const int PowerBroadcast = 0x0218;
    internal const int ResumeAutomatic = 0x0012;
    internal const int SessionChanged = 0x02b1;
    internal const int SessionUnlock = 0x0008;
    internal const int NoActivate = 3;
    internal const int EscapeKey = 0x1b;
    internal const int LeftMouseKey = 0x01;
    private const int ExtendedStyle = -20;
    private const int Transparent = 0x0020;
    private const int NoActivateStyle = 0x08000000;
    private const uint KeepSize = 0x0001;
    private const uint KeepZOrder = 0x0004;
    private const uint KeepActivation = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    internal record struct Point(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    internal record struct Rect(int Left, int Top, int Right, int Bottom);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(nint window, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter,
        int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref Point point);

    internal static Point ScreenPosition(nint window, nint messagePosition)
    {
        int coordinates = unchecked((int)messagePosition);
        var point = new Point(unchecked((short)coordinates), unchecked((short)(coordinates >> 16)));
        if (!ClientToScreen(window, ref point))
        {
            throw new InvalidOperationException("Unable to translate the mouse message coordinates.");
        }
        return point;
    }

    internal static bool KeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    internal static void SetInputStyle(nint window, bool passThrough, bool noActivate = true)
    {
        int style = GetWindowLong(window, ExtendedStyle);
        style = noActivate ? style | NoActivateStyle : style & ~NoActivateStyle;
        style = passThrough ? style | Transparent : style & ~Transparent;
        Marshal.SetLastPInvokeError(0);
        if (SetWindowLong(window, ExtendedStyle, style) == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            throw new Win32Exception();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        internal int Size;
        internal Rect Bounds;
        internal Rect WorkArea;
        internal uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSRegisterSessionNotification(nint window, uint flags);

    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSUnRegisterSessionNotification(nint window);

    internal static bool TryGetWorkArea(Point point, bool primary, out Rect area)
    {
        // The origin is on the primary screen. Otherwise choose the screen nearest the anchor.
        nint monitor = MonitorFromPoint(primary ? default : point, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        bool found = GetMonitorInfo(monitor, ref info);
        area = info.WorkArea;
        return found;
    }

    internal static bool Move(nint window, int x, int y) =>
        SetWindowPos(window, 0, x, y, 0, 0, KeepSize | KeepZOrder | KeepActivation);
}
