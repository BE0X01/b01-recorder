namespace B01Recorder;

internal sealed class SelectionOverlay : IDisposable
{
    private readonly OverlayWindow border = new() { BackColor = Theme.Accent };
    private Rectangle last, desktop;
    public void Update(CaptureTarget? target, bool enabled)
    {
        var bounds = target?.Bounds ?? Rectangle.Empty;
        if (target?.IsWindow == true && !Windows.TryGetClientBounds(target.Handle, out bounds)) enabled = false;
        bounds.Intersect(SystemInformation.VirtualScreen);
        if (!enabled || bounds.Width < 1 || bounds.Height < 1) { Hide(); return; }
        if (bounds != last || desktop != SystemInformation.VirtualScreen)
        {
            desktop = SystemInformation.VirtualScreen; border.Bounds = desktop;
            var local = new Rectangle(bounds.X - desktop.X, bounds.Y - desktop.Y, bounds.Width, bounds.Height);
            var ring = new Region(Rectangle.Inflate(local, 3, 3)); ring.Exclude(local);
            var old = border.Region; border.Region = ring; old?.Dispose(); last = bounds;
        }
        if (!border.Visible) border.Show();
    }
    public void Hide() => border.Hide();
    public void Dispose() => border.Dispose();
    private sealed class OverlayWindow : Form
    {
        public OverlayWindow() { FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; TopMost = true; Opacity = .99; }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var result = base.CreateParams; result.ExStyle |= 0x20 | 0x80 | 0x08000000; return result; } }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Windows.SetWindowDisplayAffinity(Handle, 0x11); }
        protected override void WndProc(ref Message m) { if (m.Msg == 0x84) { m.Result = -1; return; } base.WndProc(ref m); }
    }
}
