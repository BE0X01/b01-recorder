using System.Globalization;
using System.Text;

namespace B01Recorder;

internal static class RecordingFileName
{
    public const string DefaultPattern = "record_%y%m%d-%n";
    public static string Help => Ui.T("Default: record_%y%m%d-%n → record_261014-01.mp4\n" +
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
        "Invalid filename characters become _.\n" +
        "Without %n, a duplicate name gets -01, -02, …",
        "기본값: record_%y%m%d-%n → record_261014-01.mp4\n" +
        "확장자는 자동으로 붙습니다.\n\n" +
        "%y  연도 2자리 (26)     %Y  연도 4자리 (2026)\n" +
        "%m  월 (01–12)         %d  일 (01–31)\n" +
        "%H  24시간제 (00–23)    %I  12시간제 (01–12)\n" +
        "%M  분 (00–59)         %S / %s  초 (00–59)\n" +
        "%p  AM / PM            %n  사용 가능한 번호 (01, 02, …)\n" +
        "%a  요일 약칭 (Wed)     %A  요일 (Wednesday)\n" +
        "%b  월 약칭 (Oct)       %B  월 (October)\n" +
        "%w  요일 번호 (일요일 0, 토요일 6)\n" +
        "%c  날짜와 시간         %x  날짜          %X  시간\n" +
        "%%  퍼센트 기호\n\n" +
        "파일명에 쓸 수 없는 문자는 _로 바뀝니다.\n" +
        "%n 없이 이름이 겹치면 -01, -02, …를 붙입니다.");

    public static string Format(string pattern, DateTime date, int number)
    {
        if (string.IsNullOrWhiteSpace(pattern)) throw new ArgumentException(Ui.T("Enter a recording filename pattern.", "파일 이름 형식을 입력하세요."));
        var result = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] != '%') { result.Append(pattern[i]); continue; }
            if (++i == pattern.Length) throw new ArgumentException(Ui.T("Use %% for a literal percent sign.", "퍼센트 기호를 쓰려면 %%를 입력하세요."));
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
                _ => throw new ArgumentException(Ui.T($"Unknown filename tag %{pattern[i]}. Open the information button for supported tags.",
                    $"알 수 없는 파일 이름 태그입니다 (%{pattern[i]}). 도움말 버튼에서 지원 태그를 확인하세요."))
            });
        }
        var name = result.ToString();
        foreach (var character in Path.GetInvalidFileNameChars()) name = name.Replace(character, '_');
        name = name.Trim().TrimEnd('.', ' ');
        if (name.Length == 0) throw new ArgumentException(Ui.T("The filename pattern must produce a name.", "파일 이름 형식으로 이름을 만들 수 없습니다."));
        if (name.Length > 240) throw new ArgumentException(Ui.T("The filename pattern produces a name that is too long.", "파일 이름이 너무 깁니다."));
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
