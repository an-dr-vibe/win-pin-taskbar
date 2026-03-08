using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace WinPinTaskbar;

[SupportedOSPlatform("windows")]
static class DisplayHelper
{
    // ── Resolution structs ────────────────────────────────────────────────────

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

    // ── Scale structs ─────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public int type;
        public int size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        // Union padding: largest variant (DISPLAYCONFIG_TARGET_MODE) = 48 bytes.
        // Use value-type fields — byte[] would be a reference type and breaks [Out] marshaling.
        ulong _u0, _u1, _u2, _u3, _u4, _u5;
    }

    // ── P/Invoke ──────────────────────────────────────────────────────────────

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    static extern bool EnumDisplaySettings(string? device, int mode, ref DEVMODE dm);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    static extern int ChangeDisplaySettings(ref DEVMODE dm, int flags);

    [DllImport("user32.dll")]
    static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    static extern int QueryDisplayConfig(uint flags, ref uint pathCount,
        [Out] DISPLAYCONFIG_PATH_INFO[] paths, ref uint modeCount,
        [Out] DISPLAYCONFIG_MODE_INFO[] modes, nint currentTopologyId);

    // Raw IntPtr variants — bypass all struct marshaling to guarantee byte layout
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    static extern int DisplayConfigGetDeviceInfoRaw(nint packet);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")]
    static extern int DisplayConfigSetDeviceInfoRaw(nint packet);

    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(nint hMonitor, uint dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    // ── Constants ─────────────────────────────────────────────────────────────

    const int ENUM_CURRENT_SETTINGS = -1;
    const int DISP_CHANGE_SUCCESSFUL = 0;
    const int DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;
    const uint QDC_ONLY_ACTIVE_PATHS = 2;
    const uint MONITOR_DEFAULTTOPRIMARY = 1;
    const uint MDT_EFFECTIVE_DPI = 0;

    // All scale percentages Windows knows about
    static readonly int[] ScaleLevels = [100, 125, 150, 175, 200, 225, 250, 300, 350];

    // ── Resolution ────────────────────────────────────────────────────────────

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

    // ── Scale ─────────────────────────────────────────────────────────────────
    //
    // Memory layout (all ints = 4 bytes, no struct marshaling):
    //   Header: type(4) size(4) adapterLow(4) adapterHigh(4) id(4) = 20 bytes
    //   GET:    + min(4) cur(4) max(4)                               = 32 bytes
    //   SET:    + scaleRel(4)                                        = 24 bytes

    const int GET_SIZE = 32;
    const int SET_SIZE = 24;

    /// <summary>Returns (currentScale%, availableScales[]) from the display driver.</summary>
    public static (int Current, int[] Available) GetScaleInfo()
    {
        int curPercent = DpiToPercent(GetPrimaryDpi());
        Log.Write($"GetScaleInfo: dpi={GetPrimaryDpi()} percent={curPercent}");

        if (!GetPrimarySourceInfo(out var adapterId, out uint sourceId))
            return (curPercent, ScaleLevels);

        if (!RawGetDpiScale(adapterId, sourceId, out int getType, out int min, out int cur, out int max))
            return (curPercent, ScaleLevels);

        int curIdx = FindClosestIndex(curPercent);
        int recIdx = Math.Clamp(curIdx - cur, 0, ScaleLevels.Length - 1);
        int minIdx = Math.Max(0, recIdx + min);
        int maxIdx = Math.Min(ScaleLevels.Length - 1, recIdx + max);
        Log.Write($"curIdx={curIdx} recIdx={recIdx} minIdx={minIdx} maxIdx={maxIdx}");

        return (ScaleLevels[Math.Clamp(curIdx, minIdx, maxIdx)], ScaleLevels[minIdx..(maxIdx + 1)]);
    }

    /// <summary>Applies scale immediately (same API as Windows Settings). Falls back to registry if unsupported.</summary>
    public static bool SetScale(int percent)
    {
        Log.Write($"SetScale({percent}%)");

        if (!GetPrimarySourceInfo(out var adapterId, out uint sourceId))
            return SetScaleFallback(percent);

        if (!RawGetDpiScale(adapterId, sourceId, out int getType, out int min, out int cur, out int max))
            return SetScaleFallback(percent);

        int curIdx   = FindClosestIndex(DpiToPercent(GetPrimaryDpi()));
        int recIdx   = Math.Clamp(curIdx - cur, 0, ScaleLevels.Length - 1);
        int scaleRel = Math.Clamp(FindClosestIndex(percent) - recIdx, min, max);

        // SET type is always adjacent to GET type (convention: GET=-4 → SET=-3, GET=-3 → SET=-4)
        int setType = getType == -4 ? -3 : -4;
        Log.Write($"scaleRel={scaleRel}  getType={getType} setType={setType}");

        nint buf = Marshal.AllocHGlobal(SET_SIZE);
        try
        {
            for (int i = 0; i < SET_SIZE; i += 4) Marshal.WriteInt32(buf, i, 0);
            Marshal.WriteInt32(buf, 0,  setType);
            Marshal.WriteInt32(buf, 4,  SET_SIZE);
            Marshal.WriteInt32(buf, 8,  (int)adapterId.LowPart);
            Marshal.WriteInt32(buf, 12, adapterId.HighPart);
            Marshal.WriteInt32(buf, 16, (int)sourceId);
            Marshal.WriteInt32(buf, 20, scaleRel);
            int r = DisplayConfigSetDeviceInfoRaw(buf);
            Log.Write($"DisplayConfigSetDeviceInfoRaw(type={setType}) → {r}");
            if (r == 0) return true;
        }
        finally { Marshal.FreeHGlobal(buf); }

        return SetScaleFallback(percent);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Tries type=-4 then type=-3 for GET. Returns working type and min/cur/max.</summary>
    static bool RawGetDpiScale(LUID adapterId, uint sourceId,
        out int workingType, out int min, out int cur, out int max)
    {
        foreach (int t in new[] { -4, -3 })
        {
            nint buf = Marshal.AllocHGlobal(GET_SIZE);
            try
            {
                for (int i = 0; i < GET_SIZE; i += 4) Marshal.WriteInt32(buf, i, 0);
                Marshal.WriteInt32(buf, 0,  t);
                Marshal.WriteInt32(buf, 4,  GET_SIZE);
                Marshal.WriteInt32(buf, 8,  (int)adapterId.LowPart);
                Marshal.WriteInt32(buf, 12, adapterId.HighPart);
                Marshal.WriteInt32(buf, 16, (int)sourceId);
                int r = DisplayConfigGetDeviceInfoRaw(buf);
                min = Marshal.ReadInt32(buf, 20);
                cur = Marshal.ReadInt32(buf, 24);
                max = Marshal.ReadInt32(buf, 28);
                Log.Write($"RawGetDpiScale(type={t}) → {r}  min={min} cur={cur} max={max}");
                if (r == 0) { workingType = t; return true; }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        workingType = min = cur = max = 0;
        return false;
    }

    static bool SetScaleFallback(int percent)
    {
        Log.Write($"SetScaleFallback({percent}%) — registry + Explorer restart");
        try
        {
            int dpi = PercentToDpi(percent);
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
            key?.SetValue("LogPixels", dpi, RegistryValueKind.DWord);
            key?.SetValue("Win8DpiScaling", 1, RegistryValueKind.DWord);

            foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer"))
                try { p.Kill(); } catch { }
            System.Threading.Thread.Sleep(500);
            System.Diagnostics.Process.Start("explorer.exe");
            return true;
        }
        catch (Exception ex) { Log.Write($"SetScaleFallback failed: {ex.Message}"); return false; }
    }

    static bool GetPrimarySourceInfo(out LUID adapterId, out uint sourceId)
    {
        adapterId = default; sourceId = 0;
        int r1 = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pc, out uint mc);
        Log.Write($"GetDisplayConfigBufferSizes → {r1}  paths={pc} modes={mc}");
        if (r1 != 0) return false;

        var paths = new DISPLAYCONFIG_PATH_INFO[pc];
        var modes = new DISPLAYCONFIG_MODE_INFO[mc];
        int r2 = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pc, paths, ref mc, modes, nint.Zero);
        Log.Write($"QueryDisplayConfig → {r2}  paths={pc} modes={mc}");
        if (r2 != 0) return false;

        adapterId = paths[0].sourceInfo.adapterId;
        sourceId  = paths[0].sourceInfo.id;
        Log.Write($"Primary source: adapterId=({adapterId.LowPart},{adapterId.HighPart}) id={sourceId}");
        return true;
    }

    static int GetPrimaryDpi()
    {
        nint hMon = MonitorFromWindow(nint.Zero, MONITOR_DEFAULTTOPRIMARY);
        return GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0
            ? (int)dpiX : 96;
    }

    static int DpiToPercent(int dpi) => (int)Math.Round(dpi * 100.0 / 96);
    static int PercentToDpi(int percent) => (int)Math.Round(percent * 96.0 / 100);

    static int FindClosestIndex(int percent)
    {
        int best = 0;
        for (int i = 1; i < ScaleLevels.Length; i++)
            if (Math.Abs(ScaleLevels[i] - percent) < Math.Abs(ScaleLevels[best] - percent))
                best = i;
        return best;
    }
}
