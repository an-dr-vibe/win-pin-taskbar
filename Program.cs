using System.Runtime.Versioning;
using WinPinTaskbar;

[SupportedOSPlatform("windows")]
static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        using var mutex = new System.Threading.Mutex(true, "WinPinTaskbar_SingleInstance", out bool isNew);
        if (!isNew) return;

        using var app = new TrayApp();
        Application.Run();
    }
}
