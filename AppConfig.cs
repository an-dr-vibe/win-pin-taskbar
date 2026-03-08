using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinPinTaskbar;

record ResolutionEntry(int Width, int Height, int RefreshRate)
{
    public override string ToString() => $"{Width} × {Height}  @{RefreshRate}Hz";
}

class AppConfig
{
    public List<ResolutionEntry> Resolutions { get; set; } = [];

    [JsonIgnore]
    public static string ConfigPath { get; } = Path.Combine(
        Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "", "config.json");

    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOpts) ?? Default();
        }
        catch { }
        var cfg = Default();
        cfg.Save();
        return cfg;
    }

    public void Save() =>
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOpts));

    static AppConfig Default() => new()
    {
        Resolutions = [new(1920, 1080, 60), new(2560, 1440, 60)],
    };
}
