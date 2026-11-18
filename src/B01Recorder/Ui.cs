using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace B01Recorder;

internal enum UiLanguage { English, Korean }
internal enum ThemeMode { System, Light, Dark }

internal static class Ui
{
    // WinForms font sizes use points. These correspond to 15px and 13px at 96 DPI.
    public const float BodyPoints = 11.25f;
    public const float SmallPoints = 9.75f;
    private static readonly PrivateFontCollection fonts = new();
    private static readonly Dictionary<(float Size, FontStyle Style), Font> cachedFonts = [];
    private static readonly Dictionary<string, Bitmap> icons = [];
    public static UiLanguage Language { get; set; }
    public static string T(string english, string korean) => Language == UiLanguage.Korean ? korean : english;

    public static void InitializeFonts()
    {
        foreach (var weight in new[] { "Regular", "Bold" })
        {
            using var stream = Open($"Ui.Font.Pretendard-{weight}.ttf");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            // GDI and GDI+ both need the private font. GDI+ keeps the buffer until process exit.
            var fontData = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, fontData, bytes.Length);
            uint count = 0;
            AddFontMemResourceEx(fontData, (uint)bytes.Length, nint.Zero, ref count);
            fonts.AddMemoryFont(fontData, bytes.Length);
        }
        Application.SetDefaultFont(Font(BodyPoints));
    }

    public static Font Font(float size, FontStyle style = FontStyle.Regular)
    {
        var key = (size, style);
        if (!cachedFonts.TryGetValue(key, out var font))
        {
            font = new Font(fonts.Families[0], size, style);
            cachedFonts[key] = font;
        }
        return font;
    }

    public static void DrawIcon(Graphics canvas, string name, Rectangle bounds, Color color)
    {
        if (!icons.TryGetValue(name, out var bitmap))
        {
            using var stream = Open($"Ui.Icon.{name}.png");
            using var image = Image.FromStream(stream);
            bitmap = new Bitmap(image);
            icons[name] = bitmap;
        }
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        attributes.SetColorMatrix(new ColorMatrix([
            [0, 0, 0, 0, 0], [0, 0, 0, 0, 0], [0, 0, 0, 0, 0], [0, 0, 0, color.A / 255f, 0],
            [color.R / 255f, color.G / 255f, color.B / 255f, 0, 1]
        ]));
        var state = canvas.Save();
        try
        {
            canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
            canvas.PixelOffsetMode = PixelOffsetMode.Half;
            canvas.DrawImage(bitmap, bounds, 0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel, attributes);
        }
        finally { canvas.Restore(state); }
    }

    private static Stream Open(string name) => typeof(Ui).Assembly.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"Missing interface resource {name}.");

    [DllImport("gdi32.dll")]
    private static extern nint AddFontMemResourceEx(nint data, uint size, nint reserved, ref uint count);
}
