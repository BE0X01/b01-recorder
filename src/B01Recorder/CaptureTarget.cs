using System.Runtime.InteropServices;
using System.Text;

namespace B01Recorder;

internal sealed record CaptureTarget(string Label, Rectangle Bounds, nint Handle = 0)
{
    public override string ToString() => Label;
    public bool IsWindow => Handle != 0;
}

internal static class Windows
{
    private delegate bool EnumProc(nint handle, nint data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, nint data);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint handle);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint handle, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint handle, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint handle, ref Point point);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowDisplayAffinity(nint handle, uint affinity);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    internal static bool TryGetClientBounds(nint handle, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!IsWindow(handle) || IsIconic(handle) || !GetClientRect(handle, out var rect)) return false;
        var point = Point.Empty;
        if (!ClientToScreen(handle, ref point)) return false;
        bounds = new(point, new Size(rect.Right, rect.Bottom));
        return bounds.Width > 1 && bounds.Height > 1;
    }
    internal static List<CaptureTarget> List(nint ownHandle)
    {
        var result = new List<CaptureTarget>();
        EnumWindows((handle, _) =>
        {
            if (handle == ownHandle || !IsWindowVisible(handle) || IsIconic(handle)) return true;
            var title = new StringBuilder(512);
            GetWindowText(handle, title, title.Capacity);
            if (title.Length > 0 && TryGetClientBounds(handle, out var bounds))
                result.Add(new(title.ToString(), bounds, handle));
            return true;
        }, 0);
        return result.OrderBy(t => t.Label).ToList();
    }
}

internal sealed class RegionPicker : Form
{
    private readonly Bitmap snapshot;
    private Point? origin;
    private Rectangle selection;
    public Rectangle SelectedBounds { get; private set; }
    public RegionPicker()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
        snapshot = new Bitmap(Bounds.Width, Bounds.Height);
        using (var graphics = Graphics.FromImage(snapshot)) graphics.CopyFromScreen(Bounds.Location, Point.Empty, Bounds.Size);
        BackColor = Theme.Background;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
        MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { origin = e.Location; Capture = true; } };
        MouseMove += (_, e) =>
        {
            if (origin is not Point start) return;
            selection = Rectangle.FromLTRB(Math.Min(start.X, e.X), Math.Min(start.Y, e.Y), Math.Max(start.X, e.X), Math.Max(start.Y, e.Y));
            Invalidate();
        };
        MouseUp += (_, e) =>
        {
            if (origin is null || e.Button != MouseButtons.Left) return;
            Capture = false;
            if (selection.Width < 16 || selection.Height < 16) { origin = null; return; }
            SelectedBounds = new Rectangle(PointToScreen(selection.Location), selection.Size);
            DialogResult = DialogResult.OK;
            Close();
        };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.DrawImageUnscaled(snapshot, Point.Empty);
        using var pen = new Pen(Theme.Accent, 3);
        e.Graphics.DrawRectangle(pen, selection);
        using var font = new Font("Segoe UI", 14);
        using var brush = new SolidBrush(Theme.Background);
        e.Graphics.FillRectangle(brush, 20, 20, 425, 38);
        e.Graphics.DrawString("Drag to select a region  ·  Esc to cancel", font, Brushes.White, 30, 26);
        if (selection.Width > 0) e.Graphics.DrawString($"{selection.Width} × {selection.Height}", font, Brushes.White, selection.Left + 8, selection.Top + 8);
    }
    protected override void Dispose(bool disposing) { if (disposing) snapshot.Dispose(); base.Dispose(disposing); }
}
