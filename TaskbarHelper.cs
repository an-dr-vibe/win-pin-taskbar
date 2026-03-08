using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WinPinTaskbar;

[SupportedOSPlatform("windows")]
static class TaskbarHelper
{
    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public int rcLeft, rcTop, rcRight, rcBottom; // RECT inline
        public nint lParam;
    }

    [DllImport("shell32.dll")]
    static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern nint FindWindow(string lpClassName, string? lpWindowName);

    const uint ABM_GETSTATE = 4;
    const uint ABM_SETSTATE = 10;
    const int ABS_AUTOHIDE = 1;
    const int ABS_ALWAYSONTOP = 2;

    static APPBARDATA Make() => new()
    {
        cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
        hWnd = FindWindow("Shell_TrayWnd", null),
    };

    public static bool IsAutoHide()
    {
        var abd = Make();
        return (SHAppBarMessage(ABM_GETSTATE, ref abd) & ABS_AUTOHIDE) != 0;
    }

    public static void SetAutoHide(bool value)
    {
        var abd = Make();
        abd.lParam = value ? ABS_AUTOHIDE : ABS_ALWAYSONTOP;
        SHAppBarMessage(ABM_SETSTATE, ref abd);
    }

    public static void Toggle() => SetAutoHide(!IsAutoHide());
}
