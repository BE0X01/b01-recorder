using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace B01Recorder;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(13, 14, 19);
    public static readonly Color Surface = Color.FromArgb(19, 20, 28);
    public static readonly Color Input = Color.FromArgb(26, 27, 37);
    public static readonly Color Border = Color.FromArgb(43, 45, 57);
    public static readonly Color Text = Color.FromArgb(228, 232, 237);
    public static readonly Color Muted = Color.FromArgb(142, 146, 166);
    public static readonly Color Accent = Color.FromArgb(77, 225, 205);
    public static readonly Color AccentSurface = Color.FromArgb(20, 47, 45);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    public static void DarkTitle(Form form)
    {
        var enabled = 1;
        DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int));
    }
    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath(); var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
    public static Bitmap Logo(int size)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(size / 64f, size / 64f);
        using var shape = Rounded(new(2, 2, 60, 60), 16);
        using var back = new SolidBrush(AccentSurface); g.FillPath(back, shape);
        using var pen = new Pen(Accent, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        foreach (var points in new[] { new Point[] { new(26, 16), new(16, 16), new(16, 26) }, [new(38, 16), new(48, 16), new(48, 26)], [new(16, 38), new(16, 48), new(26, 48)], [new(38, 48), new(48, 48), new(48, 38)] }) g.DrawLines(pen, points);
        using var accent = new SolidBrush(Accent); g.FillEllipse(accent, 23, 23, 18, 18);
        return bitmap;
    }
}

internal class Card : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Theme.Border;
    public Card() { DoubleBuffered = true; BackColor = Theme.Surface; Padding = new(16); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Theme.Rounded(new(0, 0, Width - 1, Height - 1), 12);
        using var pen = new Pen(BorderColor); e.Graphics.DrawPath(pen, shape);
    }
}

internal sealed class DarkButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Primary { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Selected { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Destructive { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Radio { get; set; }
    private bool hovering;
    public DarkButton(string text)
    {
        Text = text; AutoSize = false; Size = new(106, 36); Margin = new(0, 0, 8, 0);
        FlatStyle = FlatStyle.Flat; Cursor = Cursors.Hand; ForeColor = Theme.Text; BackColor = Theme.Input;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovering = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var fill = !Enabled ? Theme.Surface : Primary ? Theme.Accent : Selected ? Theme.AccentSurface : hovering ? Color.FromArgb(35, 37, 49) : Theme.Input;
        using var shape = Theme.Rounded(new(0, 0, Width - 1, Height - 1), 8);
        using var brush = new SolidBrush(fill); e.Graphics.FillPath(brush, shape);
        using var pen = new Pen(Selected || Primary && Enabled ? Theme.Accent : Theme.Border); e.Graphics.DrawPath(pen, shape);
        var color = !Enabled ? Theme.Muted : Primary ? Theme.Background : Destructive ? Color.FromArgb(247, 131, 144) : Selected ? Theme.Accent : Theme.Text;
        var textBounds = ClientRectangle; if (Radio) textBounds.Width -= 14;
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (Radio) { using var dot = new SolidBrush(Selected ? Theme.Accent : Theme.Border); e.Graphics.FillEllipse(dot, Width - 18, Height / 2 - 3, 6, 6); }
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), color, fill);
    }
}

internal sealed class QualitySlider : Control
{
    private int value = 80;
    public event EventHandler? ValueChanged;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value { get => value; set { var next = Math.Clamp(value, 1, 100); if (next == this.value) return; this.value = next; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); } }
    public QualitySlider() { Size = new(320, 28); DoubleBuffered = true; Cursor = Cursors.Hand; TabStop = true; }
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
