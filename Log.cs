namespace WinPinTaskbar;

static class Log
{
    static readonly string Path = System.IO.Path.Combine(
        System.IO.Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "", "log.txt");

    public static void Write(string msg)
    {
        try { File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff}  {msg}\n"); }
        catch { }
    }
}
