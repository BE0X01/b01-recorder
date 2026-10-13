using System.Globalization;
using System.Text;

namespace B01Recorder;

internal static class RecordingFileName
{
    public const string DefaultPattern = "record_%y%m%d-%n";
    public const string Help = "Default: record_%y%m%d-%n → record_261014-01.mp4\n" +
        "Extension is added automatically.\n\n" +
        "%y  Two-digit year (26)     %Y  Full year (2026)\n" +
        "%m  Month (01–12)           %d  Day (01–31)\n" +
        "%H  Hour, 24-hour (00–23)   %I  Hour, 12-hour (01–12)\n" +
        "%M  Minute (00–59)          %S / %s  Second (00–59)\n" +
        "%p  AM / PM                %n  Available number (01, 02, …)\n" +
        "%a  Short weekday (Wed)    %A  Full weekday (Wednesday)\n" +
        "%b  Short month (Oct)      %B  Full month (October)\n" +
        "%w  Weekday (0 = Sunday, 6 = Saturday)\n" +
        "%c  Date and time          %x  Date          %X  Time\n" +
        "%%  Literal %\n\n" +
        "Invalid filename characters become _. Existing files are never overwritten.\n" +
        "Without %n, a duplicate name gets -01, -02, …";

    public static string Format(string pattern, DateTime date, int number)
    {
        if (string.IsNullOrWhiteSpace(pattern)) throw new ArgumentException("Enter a recording filename pattern.");
        var result = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] != '%') { result.Append(pattern[i]); continue; }
            if (++i == pattern.Length) throw new ArgumentException("Use %% for a literal percent sign.");
            result.Append(pattern[i] switch
            {
                'a' => date.ToString("ddd", CultureInfo.InvariantCulture),
                'A' => date.ToString("dddd", CultureInfo.InvariantCulture),
                'b' => date.ToString("MMM", CultureInfo.InvariantCulture),
                'B' => date.ToString("MMMM", CultureInfo.InvariantCulture),
                'c' => date.ToString("MM/dd/yy HH:mm:ss", CultureInfo.InvariantCulture),
                'd' => date.ToString("dd", CultureInfo.InvariantCulture),
                'H' => date.ToString("HH", CultureInfo.InvariantCulture),
                'I' => date.ToString("hh", CultureInfo.InvariantCulture),
                'M' => date.ToString("mm", CultureInfo.InvariantCulture),
                'm' => date.ToString("MM", CultureInfo.InvariantCulture),
                'p' => date.ToString("tt", CultureInfo.InvariantCulture),
                'S' or 's' => date.ToString("ss", CultureInfo.InvariantCulture),
                'w' => ((int)date.DayOfWeek).ToString(CultureInfo.InvariantCulture),
                'x' => date.ToString("MM/dd/yy", CultureInfo.InvariantCulture),
                'X' => date.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                'Y' => date.ToString("yyyy", CultureInfo.InvariantCulture),
                'y' => date.ToString("yy", CultureInfo.InvariantCulture),
                'n' => number.ToString("D2", CultureInfo.InvariantCulture),
                '%' => "%",
                _ => throw new ArgumentException($"Unknown filename tag %{pattern[i]}. Hover over the information icon for supported tags.")
            });
        }
        var name = result.ToString();
        foreach (var character in Path.GetInvalidFileNameChars()) name = name.Replace(character, '_');
        name = name.Trim().TrimEnd('.', ' ');
        if (name.Length == 0) throw new ArgumentException("The filename pattern must produce a name.");
        if (name.Length > 240) throw new ArgumentException("The filename pattern produces a name that is too long.");
        var stem = name.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase) ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(stem[3])))
            name = "_" + name;
        return name;
    }

    public static string AvailablePath(string folder, string pattern, DateTime date, string format)
    {
        // Look for a real tag; %%n is literal text, not a counter.
        var hasNumber = false;
        for (var i = 0; i + 1 < pattern.Length; i++)
            if (pattern[i] == '%') { if (pattern[++i] == 'n') hasNumber = true; }
        for (var number = 1; number < int.MaxValue; number++)
        {
            var name = Format(pattern, date, number);
            if (!hasNumber && number > 1) name += "-" + (number - 1).ToString("D2", CultureInfo.InvariantCulture);
            var file = Path.Combine(folder, name + "." + format.ToLowerInvariant());
            if (!File.Exists(file) && !Directory.Exists(file)) return file;
        }
        throw new IOException("No available recording filename.");
    }
}
