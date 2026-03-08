using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace WinPinTaskbar;

[SupportedOSPlatform("windows")]
static class DisplayHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY;
        public int dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
        public int dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    static extern bool EnumDisplaySettings(string? device, int mode, ref DEVMODE dm);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    static extern int ChangeDisplaySettings(ref DEVMODE dm, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool SystemParametersInfo(uint action, uint param, string? data, uint flags);

    const int ENUM_CURRENT_SETTINGS = -1;
    const int DISP_CHANGE_SUCCESSFUL = 0;
    const int DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;
    const uint SPI_SETDESKWALLPAPER = 0x0014; // used as dummy broadcast trigger
    const uint SPIF_SENDCHANGE = 0x0002;

    static DEVMODE CurrentMode()
    {
        var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm);
        return dm;
    }

    public static (int Width, int Height, int RefreshRate) GetCurrentResolution()
    {
        var dm = CurrentMode();
        return (dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency);
    }

    public static bool SetResolution(int width, int height, int refreshRate)
    {
        var dm = CurrentMode();
        dm.dmPelsWidth = width;
        dm.dmPelsHeight = height;
        dm.dmDisplayFrequency = refreshRate;
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
        return ChangeDisplaySettings(ref dm, 0) == DISP_CHANGE_SUCCESSFUL;
    }

    public static int GetCurrentScale()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        return key?.GetValue("LogPixels") is int dpi ? DpiToPercent(dpi) : 100;
    }

    public static void SetScale(int percent)
    {
        int dpi = PercentToDpi(percent);
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
        key?.SetValue("LogPixels", dpi, RegistryValueKind.DWord);
        // Broadcast the change — full effect requires sign-out
        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, null, SPIF_SENDCHANGE);
    }

    static int DpiToPercent(int dpi) => (int)Math.Round(dpi * 100.0 / 96);
    static int PercentToDpi(int percent) => (int)Math.Round(percent * 96.0 / 100);
}
