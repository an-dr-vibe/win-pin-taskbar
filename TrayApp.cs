using Microsoft.Win32;
using Svg;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WinPinTaskbar;

[SupportedOSPlatform("windows")]
sealed class TrayApp : IDisposable
{
    readonly NotifyIcon _tray = new();
    AppConfig _config = AppConfig.Load();
    nint _hicon;
    bool _disposed;

    public TrayApp()
    {
        _tray.Visible = true;
        _tray.MouseClick += OnMouseClick;
        Refresh();
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
    }

    void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            TaskbarHelper.Toggle();
            Refresh();
        }
    }

    void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
            Refresh();
    }

    void Refresh()
    {
        bool pinned = !TaskbarHelper.IsAutoHide();
        UpdateTrayIcon(pinned);
        _tray.Text = pinned ? "Taskbar: Always visible" : "Taskbar: Auto-hide";
        RebuildMenu(pinned);
    }

    void RebuildMenu(bool pinned)
    {
        var menu = new ContextMenuStrip();

        // Taskbar toggle
        var taskbarItem = new ToolStripMenuItem("Taskbar is pinned") { Checked = pinned };
        taskbarItem.Click += (_, _) => { TaskbarHelper.Toggle(); Refresh(); };
        menu.Items.Add(taskbarItem);

        menu.Items.Add(new ToolStripSeparator());

        // Resolution submenu
        var resSub = new ToolStripMenuItem("Resolution");
        var (curW, curH, curHz) = DisplayHelper.GetCurrentResolution();
        foreach (var r in _config.Resolutions)
        {
            bool isCurrent = r.Width == curW && r.Height == curH && r.RefreshRate == curHz;
            var item = new ToolStripMenuItem(r.ToString()) { Checked = isCurrent };
            var res = r;
            item.Click += (_, _) =>
            {
                if (!DisplayHelper.SetResolution(res.Width, res.Height, res.RefreshRate))
                    _tray.ShowBalloonTip(3000, "Resolution", $"Failed to set {res}", ToolTipIcon.Warning);
                else
                    Refresh();
            };
            resSub.DropDownItems.Add(item);
        }
        menu.Items.Add(resSub);

        // Scale submenu
        var scaleSub = new ToolStripMenuItem("Scale");
        int curScale = DisplayHelper.GetCurrentScale();
        foreach (int s in _config.Scales)
        {
            bool isCurrent = s == curScale;
            var item = new ToolStripMenuItem($"{s}%") { Checked = isCurrent };
            int scale = s;
            item.Click += (_, _) =>
            {
                DisplayHelper.SetScale(scale);
                _tray.ShowBalloonTip(4000, "Scale", $"Scale set to {scale}%. Sign out to apply fully.", ToolTipIcon.Info);
                Refresh();
            };
            scaleSub.DropDownItems.Add(item);
        }
        menu.Items.Add(scaleSub);

        menu.Items.Add(new ToolStripSeparator());

        // Settings
        var settings = new ToolStripMenuItem("Settings...");
        settings.Click += (_, _) =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppConfig.ConfigPath,
                UseShellExecute = true,
            });
        };
        menu.Items.Add(settings);

        menu.Items.Add(new ToolStripSeparator());

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => { _tray.Visible = false; Application.Exit(); };
        menu.Items.Add(exit);

        _tray.ContextMenuStrip = menu;
    }

    void UpdateTrayIcon(bool pinned)
    {
        bool dark = IsDarkTheme();
        string variant = dark ? "light" : "dark";
        string state = pinned ? "pinned" : "unpinned";
        string resource = $"WinPinTaskbar.assets.sidebar-pin-{state}-{variant}.svg";

        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resource);
        if (stream is null) return;

        var doc = SvgDocument.Open<SvgDocument>(stream);
        using var bmp = doc.Draw(32, 32);

        nint newHicon = bmp.GetHicon();
        _tray.Icon = Icon.FromHandle(newHicon);

        if (_hicon != 0) DestroyIcon(_hicon);
        _hicon = newHicon;
    }

    static bool IsDarkTheme() =>
        Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "SystemUsesLightTheme", 1) is int v && v == 0;

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(nint hIcon);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        _tray.Visible = false;
        _tray.Dispose();
        if (_hicon != 0) DestroyIcon(_hicon);
    }
}
