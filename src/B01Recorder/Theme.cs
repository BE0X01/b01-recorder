using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace B01Recorder;

internal static class Theme
{
    private static readonly Color[] dark = [Rgb(0x101419), Rgb(0x181D23), Rgb(0x20272F), Rgb(0x333D48),
        Rgb(0xEFF3F7), Rgb(0xA7B3C0), Rgb(0x83A9DF), Rgb(0x27384F)];
    private static readonly Color[] light = [Rgb(0xEFF3F5), Rgb(0xFFFFFF), Rgb(0xF3F6F8), Rgb(0xCED8DF),
        Rgb(0x17262D), Rgb(0x516570), Rgb(0x365D94), Rgb(0xE8EFF8)];
    private static Color[] colors = dark;
    public static bool IsDark => colors == dark;
    public static Color Background => colors[0];
    public static Color Surface => colors[1];
    public static Color Input => colors[2];
    public static Color Border => colors[3];
    public static Color Text => colors[4];
    public static Color Muted => colors[5];
    public static Color Accent => colors[6];
    public static Color AccentSurface => colors[7];
    public static Color OnAccent => IsDark ? Background : Color.White;
    private static Color Rgb(int rgb) => Color.FromArgb(unchecked((int)0xff000000) | rgb);

    public static void Apply(ThemeMode mode, Form root)
    {
        var previous = colors;
        colors = mode == ThemeMode.Light || mode == ThemeMode.System && SystemUsesLightTheme() ? light : dark;
        if (previous == colors) return;
        Repaint(root, previous);
        if (root.IsHandleCreated) DarkTitle(root);
        foreach (var child in root.OwnedForms) { Repaint(child, previous); DarkTitle(child); }
    }

    private static bool SystemUsesLightTheme() =>
        Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1) is int value && value != 0;

    private static void Repaint(Control control, Color[] previous)
    {
        Color Restyle(Color color)
        {
            var index = Array.FindIndex(previous, old => old.ToArgb() == color.ToArgb());
            return index < 0 ? color : colors[index];
        }
        control.BackColor = Restyle(control.BackColor);
        control.ForeColor = Restyle(control.ForeColor);
        if (control is Card card) card.BorderColor = Restyle(card.BorderColor);
        foreach (Control child in control.Controls) Repaint(child, previous);
        control.Invalidate();
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    public static void DarkTitle(Form form)
    {
        var enabled = IsDark ? 1 : 0;
        DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int));
    }
    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath(); var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
}

internal class Card : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Theme.Border;
    public Card() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); BackColor = Theme.Surface; Padding = new(16); }
    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 4 || Height < 4) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Theme.Rounded(new(0, 0, Width - 1, Height - 1), 12);
        using var fill = new SolidBrush(BackColor);
        e.Graphics.FillPath(fill, shape);
        using var pen = new Pen(BorderColor); e.Graphics.DrawPath(pen, shape);
    }
}

internal sealed class DarkButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Primary { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Selected { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Destructive { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public string? Glyph { get; set; }
    private bool hovering;
    public DarkButton(string text)
    {
        Text = text; AutoSize = false; Size = new(104, 36); Margin = new(0, 0, 8, 0);
        FlatStyle = FlatStyle.Flat; Cursor = Cursors.Hand; ForeColor = Theme.Text; BackColor = Theme.Input;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovering = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var fill = !Enabled ? Theme.Surface : Primary ? Theme.Accent : Selected || hovering ? Theme.AccentSurface : Theme.Input;
        using var shape = Theme.Rounded(new(0, 0, Width - 1, Height - 1), 8);
        using var brush = new SolidBrush(fill); e.Graphics.FillPath(brush, shape);
        var border = Focused && ShowFocusCues ? Theme.Text : Selected || Primary && Enabled ? Theme.Accent : Theme.Border;
        using var pen = new Pen(border); e.Graphics.DrawPath(pen, shape);
        var color = !Enabled ? Theme.Muted : Primary ? Theme.OnAccent : Destructive ? Color.FromArgb(247, 131, 144) : Selected ? Theme.Accent : Theme.Text;
        const TextFormatFlags measureFlags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        const TextFormatFlags textFlags = measureFlags | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        if (Glyph is null)
        {
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, color, textFlags | TextFormatFlags.HorizontalCenter);
            return;
        }
        const int iconSize = 16;
        var gap = Text.Length == 0 ? 0 : 8;
        var textWidth = Text.Length == 0 ? 0 : Math.Min(
            TextRenderer.MeasureText(e.Graphics, Text, Font, new Size(int.MaxValue, int.MaxValue), measureFlags).Width,
            Math.Max(0, Width - iconSize - gap - 16));
        var left = (Width - iconSize - gap - textWidth) / 2;
        Ui.DrawIcon(e.Graphics, Glyph, new(left, (Height - iconSize) / 2, iconSize, iconSize), color);
        if (textWidth > 0)
        {
            var textLeft = left + iconSize + gap;
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(textLeft, 0, textWidth, Height), color, textFlags);
        }
    }
}

internal sealed class QualitySlider : Control
{
    private int value = 80;
    public event EventHandler? ValueChanged;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value { get => value; set { var next = Math.Clamp(value, 1, 100); if (next == this.value) return; this.value = next; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); } }
    public QualitySlider() { Size = new(320, 28); DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); Cursor = Cursors.Hand; TabStop = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var x = 8 + (Width - 16) * (Value - 1) / 99f;
        using var track = new Pen(Theme.Border, 4); e.Graphics.DrawLine(track, 8, Height / 2, Width - 8, Height / 2);
        using var fill = new Pen(Enabled ? Theme.Accent : Theme.Muted, 4); e.Graphics.DrawLine(fill, 8, Height / 2, x, Height / 2);
        using var dot = new SolidBrush(Enabled ? Theme.Accent : Theme.Muted); e.Graphics.FillEllipse(dot, x - 6, Height / 2 - 6, 12, 12);
    }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Capture = true; SetFromX(e.X); } }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) SetFromX(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { Capture = false; base.OnMouseUp(e); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Left) Value--; if (e.KeyCode == Keys.Right) Value++; base.OnKeyDown(e); }
    private void SetFromX(int x) => Value = 1 + (int)Math.Round((x - 8) * 99.0 / Math.Max(1, Width - 16));
}
