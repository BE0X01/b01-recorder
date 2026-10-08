using System.Globalization;
using System.Text;
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
    public int CaptureMode { get; set; }
    public string Ffmpeg { get; set; } = "";
    public List<string> Recordings { get; set; } = [];
    internal string StoragePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "setting.ini");

    public static Settings Load(string? path = null)
    {
        var settings = new Settings();
        if (path is not null) settings.StoragePath = path;
        try
        {
            if (!File.Exists(settings.StoragePath))
            {
                // Read existing preferences once for migration; all future writes use setting.ini.
                if (path is null)
                {
                    var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "b01-recorder", "settings.json");
                    return JsonSerializer.Deserialize<Settings>(File.ReadAllText(legacy)) ?? settings;
                }
                return settings;
            }
            foreach (var line in File.ReadLines(settings.StoragePath))
            {
                var split = line.IndexOf('=');
                if (split < 1) continue;
                var key = line[..split].Trim(); var value = line[(split + 1)..];
                switch (key)
                {
                    case "OutputFolder": settings.Folder = value; break;
                    case "Format": settings.Format = value; break;
                    case "FrameRate" when int.TryParse(value, out var fps): settings.Fps = Math.Clamp(fps, 1, 120); break;
                    case "Quality" when int.TryParse(value, out var quality): settings.Quality = Math.Clamp(quality, 1, 100); break;
                    case "SystemAudio" when bool.TryParse(value, out var audio): settings.Audio = audio; break;
                    case "CaptureCursor" when bool.TryParse(value, out var cursor): settings.Cursor = cursor; break;
                    case "CaptureMode" when int.TryParse(value, out var mode): settings.CaptureMode = Math.Clamp(mode, 0, 2); break;
                    case "FFmpegPath": settings.Ffmpeg = value; break;
                    default: if (key.StartsWith("File", StringComparison.Ordinal) && File.Exists(value)) settings.Recordings.Add(value); break;
                }
            }
        }
        catch { }
        return settings;
    }
    public void Save()
    {
        var text = new StringBuilder("[Recording]\n");
        text.AppendLine("OutputFolder=" + Folder).AppendLine("Format=" + Format)
            .AppendLine("FrameRate=" + Fps.ToString(CultureInfo.InvariantCulture))
            .AppendLine("Quality=" + Quality.ToString(CultureInfo.InvariantCulture))
            .AppendLine("SystemAudio=" + Audio).AppendLine("CaptureCursor=" + Cursor)
            .AppendLine("CaptureMode=" + CaptureMode.ToString(CultureInfo.InvariantCulture))
            .AppendLine("FFmpegPath=" + Ffmpeg).AppendLine().AppendLine("[Library]");
        for (var i = 0; i < Recordings.Count; i++) text.AppendLine($"File{i}={Recordings[i]}");
        File.WriteAllText(StoragePath, text.ToString(), new UTF8Encoding(false));
    }
}
