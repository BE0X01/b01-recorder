using System.Text.Json;

namespace B01Recorder;

internal sealed class Settings
{
    public string Folder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public string Format { get; set; } = "MP4";
    public int Fps { get; set; } = 30;
    public int Quality { get; set; } = 80;
    public bool Audio { get; set; } = true;
    public bool Cursor { get; set; } = true;
    public string Ffmpeg { get; set; } = "";
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "b01-recorder", "settings.json");
    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
