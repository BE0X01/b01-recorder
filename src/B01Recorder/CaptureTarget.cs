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
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    internal static List<CaptureTarget> List(nint ownHandle)
    {
        var result = new List<CaptureTarget>();
        EnumWindows((handle, _) =>
        {
            if (handle == ownHandle || !IsWindowVisible(handle) || IsIconic(handle)) return true;
            var title = new StringBuilder(512);
            GetWindowText(handle, title, title.Capacity);
            if (title.Length > 0 && GetClientRect(handle, out var r) && r.Right > 1 && r.Bottom > 1)
                result.Add(new(title.ToString(), new(0, 0, r.Right, r.Bottom), handle));
            return true;
        }, 0);
        return result.OrderBy(t => t.Label).ToList();
    }
}

internal sealed class RegionPicker : Form
{
    private Point? origin;
    private Rectangle selection;
    public Rectangle SelectedBounds { get; private set; }
    public RegionPicker()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
        BackColor = Color.FromArgb(20, 25, 35);
        Opacity = .35;
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
        using var pen = new Pen(Color.White, 3);
        e.Graphics.DrawRectangle(pen, selection);
        using var font = new Font("맑은 고딕", 18);
        e.Graphics.DrawString("드래그해서 영역 선택  ·  Esc 취소", font, Brushes.White, 30, 30);
        if (selection.Width > 0) e.Graphics.DrawString($"{selection.Width} × {selection.Height}", font, Brushes.White, selection.Left + 8, selection.Top + 8);
    }
}
