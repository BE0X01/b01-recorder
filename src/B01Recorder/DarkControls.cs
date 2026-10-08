using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Diagnostics.CodeAnalysis;

namespace B01Recorder;

internal sealed class DarkCheck : CheckBox
{
    public DarkCheck() { AutoSize = true; Cursor = Cursors.Hand; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    public override Size GetPreferredSize(Size proposedSize) { var text = TextRenderer.MeasureText(Text, Font); return new(text.Width + 29, Math.Max(23, text.Height + 4)); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Surface); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var y = (Height - 17) / 2; using var shape = Theme.Rounded(new(0, y, 17, 17), 4);
        using var fill = new SolidBrush(Checked && Enabled ? Theme.Accent : Theme.Input); e.Graphics.FillPath(fill, shape);
        using var border = new Pen(Checked && Enabled ? Theme.Accent : Theme.Border); e.Graphics.DrawPath(border, shape);
        if (Checked) { using var check = new Pen(Enabled ? Theme.Background : Theme.Muted, 2); e.Graphics.DrawLines(check, [new(4, y + 8), new(7, y + 11), new(13, y + 5)]); }
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(26, 0, Width - 26, Height), Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1));
    }
}

internal sealed class DarkCombo : Control
{
    private int selectedIndex = -1;
    private ToolStripDropDown? popup;
    public event EventHandler? SelectedIndexChanged;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public ComboItems Items { get; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public int SelectedIndex
    {
        get => selectedIndex;
        set { if (value < -1 || value >= Items.Count) throw new ArgumentOutOfRangeException(nameof(value)); selectedIndex = value; Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); }
    }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public object? SelectedItem { get => selectedIndex >= 0 && selectedIndex < Items.Count ? Items[selectedIndex] : null; set => SelectedIndex = value is null ? -1 : Items.IndexOf(value); }
    [AllowNull] public override string Text { get => SelectedItem?.ToString() ?? ""; set { } }
    public DarkCombo(string[] items)
    {
        Items = new ComboItems(this); Items.AddRange(items); if (Items.Count > 0) SelectedIndex = 0;
        Size = new(200, 32); DoubleBuffered = true; Cursor = Cursors.Hand; TabStop = true;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Surface); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Theme.Rounded(new(0, 0, Width - 1, Height - 1), 7); using var fill = new SolidBrush(Theme.Input); e.Graphics.FillPath(fill, shape);
        using var edge = new Pen(Focused ? Theme.Accent : Theme.Border); e.Graphics.DrawPath(edge, shape);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(10, 0, Width - 36, Height), Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        using var arrow = new Pen(Enabled ? Theme.Muted : Theme.Border, 1.5f); e.Graphics.DrawLines(arrow, [new(Width - 22, Height / 2 - 2), new(Width - 18, Height / 2 + 2), new(Width - 14, Height / 2 - 2)]);
    }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Focus(); Open(); } }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter || e.Alt && e.KeyCode == Keys.Down) { Open(); e.Handled = true; }
        else if (Items.Count > 0 && e.KeyCode is Keys.Up or Keys.Down) { SelectedIndex = Math.Clamp(SelectedIndex + (e.KeyCode == Keys.Up ? -1 : 1), 0, Items.Count - 1); e.Handled = true; }
        base.OnKeyDown(e);
    }
    private void Open()
    {
        if (!Enabled || Items.Count == 0) return;
        if (popup?.Visible == true) { popup.Close(); return; }
        popup?.Dispose();
        var list = new PopupList(this) { Size = new(Width, Math.Min(9, Items.Count) * 30 + 2), Font = Font };
        popup = new ToolStripDropDown { Padding = Padding.Empty, Margin = Padding.Empty, BackColor = Theme.Input, AutoSize = false, Size = list.Size };
        popup.Items.Add(new ToolStripControlHost(list) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = list.Size });
        popup.Show(this, new Point(0, Height + 4)); list.Focus();
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void Dispose(bool disposing) { if (disposing) popup?.Dispose(); base.Dispose(disposing); }
    internal sealed class ComboItems(DarkCombo owner) : Collection<object>
    {
        public void AddRange(IEnumerable<object> values) { foreach (var value in values) Add(value); }
        protected override void ClearItems() { base.ClearItems(); owner.SelectedIndex = -1; }
    }
    private sealed class PopupList : Control
    {
        private readonly DarkCombo owner;
        private readonly SlimScrollBar scroll = new();
        private int hovered;
        public PopupList(DarkCombo owner)
        {
            this.owner = owner; hovered = Math.Max(0, owner.SelectedIndex); DoubleBuffered = true; BackColor = Theme.Input; ForeColor = Theme.Text; TabStop = true;
            Controls.Add(scroll); scroll.ValueChanged += (_, _) => Invalidate();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); scroll.Bounds = new(Width - 12, 1, 11, Height - 2); scroll.Viewport = Height; scroll.Maximum = Math.Max(0, owner.Items.Count * 30 - Height + 2); scroll.Visible = scroll.Maximum > 0; }
        protected override void OnPaint(PaintEventArgs e)
        {
            for (var i = Math.Max(0, scroll.Value / 30); i < owner.Items.Count; i++)
            {
                var y = 1 + i * 30 - scroll.Value; if (y > Height) break;
                var r = new Rectangle(1, y, Width - (scroll.Visible ? 13 : 2), 30);
                if (i == hovered || i == owner.SelectedIndex) { using var brush = new SolidBrush(Theme.AccentSurface); e.Graphics.FillRectangle(brush, r); }
                TextRenderer.DrawText(e.Graphics, owner.Items[i].ToString(), Font, Rectangle.Inflate(r, -10, 0), i == owner.SelectedIndex ? Theme.Accent : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            using var edge = new Pen(Theme.Border); e.Graphics.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);
        }
        protected override void OnMouseMove(MouseEventArgs e) { var next = (e.Y - 1 + scroll.Value) / 30; if (next != hovered) { hovered = next; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Choose((e.Y - 1 + scroll.Value) / 30); base.OnMouseDown(e); }
        protected override void OnMouseWheel(MouseEventArgs e) { scroll.Value -= Math.Sign(e.Delta) * 90; base.OnMouseWheel(e); }
        protected override bool IsInputKey(Keys keyData) => true;
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) owner.popup?.Close();
            if (e.KeyCode is Keys.Enter or Keys.Space) Choose(hovered);
            if (e.KeyCode is Keys.Up or Keys.Down)
            {
                hovered = Math.Clamp(hovered + (e.KeyCode == Keys.Up ? -1 : 1), 0, owner.Items.Count - 1);
                if (hovered * 30 < scroll.Value) scroll.Value = hovered * 30;
                if ((hovered + 1) * 30 > scroll.Value + Height) scroll.Value = (hovered + 1) * 30 - Height + 2;
                Invalidate();
            }
            e.Handled = true; base.OnKeyDown(e);
        }
        private void Choose(int index) { if (index < 0 || index >= owner.Items.Count) return; owner.SelectedIndex = index; owner.popup?.Close(); }
    }
}

internal sealed class SlimScrollBar : Control
{
    private int value, maximum, viewport = 100, dragOffset;
    private bool dragging;
    public event EventHandler? ValueChanged;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public int Maximum { get => maximum; set { maximum = Math.Max(0, value); Value = this.value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public int Viewport { get => viewport; set { viewport = Math.Max(1, value); Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public int Value { get => value; set { var next = Math.Clamp(value, 0, maximum); if (next == this.value) return; this.value = next; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); } }
    private int ThumbHeight => Math.Min(Height, Math.Max(28, (int)((long)Height * Viewport / (Maximum + Viewport))));
    private int ThumbY => Maximum == 0 ? 0 : (int)((long)(Height - ThumbHeight) * Value / Maximum);
    public SlimScrollBar() { DoubleBuffered = true; Cursor = Cursors.Hand; Width = 12; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; if (Maximum == 0) return;
        using var shape = Theme.Rounded(new((Width - 5) / 2f, ThumbY, 5, Math.Max(5, ThumbHeight)), 2.5f);
        using var brush = new SolidBrush(dragging ? Theme.Accent : Color.FromArgb(76, 80, 98)); e.Graphics.FillPath(brush, shape);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        dragging = true; Capture = true; dragOffset = e.Y >= ThumbY && e.Y <= ThumbY + ThumbHeight ? e.Y - ThumbY : ThumbHeight / 2; MoveTo(e.Y); base.OnMouseDown(e);
    }
    protected override void OnMouseMove(MouseEventArgs e) { if (dragging) MoveTo(e.Y); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnMouseWheel(MouseEventArgs e) { Value -= Math.Sign(e.Delta) * 60; base.OnMouseWheel(e); }
    private void MoveTo(int y) => Value = (int)((long)(y - dragOffset) * Maximum / Math.Max(1, Height - ThumbHeight));
}

internal sealed class SlimFlowPanel : UserControl
{
    private readonly FlowLayoutPanel flow = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new(0), Padding = new(0) };
    private readonly SlimScrollBar scroll = new();
    private bool arranging;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public new ControlCollection Controls => flow.Controls;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public FlowDirection FlowDirection { get => flow.FlowDirection; set => flow.FlowDirection = value; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool WrapContents { get => flow.WrapContents; set => flow.WrapContents = value; }
    public SlimFlowPanel()
    {
        DoubleBuffered = true; base.Controls.Add(flow); base.Controls.Add(scroll);
        scroll.ValueChanged += (_, _) => flow.Top = -scroll.Value;
        flow.SizeChanged += (_, _) => Arrange();
        flow.MouseWheel += (_, e) => { scroll.Value -= Math.Sign(e.Delta) * 60; };
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); Arrange(); }
    protected override void OnBackColorChanged(EventArgs e) { base.OnBackColorChanged(e); if (flow is not null) flow.BackColor = BackColor; }
    protected override void OnMouseWheel(MouseEventArgs e) { scroll.Value -= Math.Sign(e.Delta) * 60; base.OnMouseWheel(e); }
    private void Arrange()
    {
        if (arranging || ClientSize.Width < 16) return;
        arranging = true;
        try
        {
            var width = ClientSize.Width - 14; flow.MinimumSize = new(width, 0); flow.MaximumSize = new(width, 0); flow.Width = width;
            scroll.Bounds = new(ClientSize.Width - 12, 0, 12, ClientSize.Height); scroll.Viewport = ClientSize.Height;
            scroll.Maximum = Math.Max(0, flow.Height - ClientSize.Height); scroll.Visible = scroll.Maximum > 0;
            flow.Location = new(0, -scroll.Value);
        }
        finally { arranging = false; }
    }
}

internal sealed class DarkNumber : Control
{
    private readonly TextBox edit = new() { BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text };
    private decimal value = 1, minimum = 1, maximum = 120;
    private bool updating;
    public event EventHandler? ValueChanged;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public decimal Minimum { get => minimum; set => minimum = value; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public decimal Maximum { get => maximum; set => maximum = value; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public decimal Value
    {
        get => value;
        set { var next = Math.Clamp(value, Minimum, Maximum); var changed = next != this.value; this.value = next; updating = true; edit.Text = next.ToString("0"); updating = false; if (changed) ValueChanged?.Invoke(this, EventArgs.Empty); }
    }
    public DarkNumber()
    {
        Size = new(76, 32); DoubleBuffered = true; Controls.Add(edit); edit.Text = "1";
        edit.TextChanged += (_, _) => { if (!updating && decimal.TryParse(edit.Text, out var next) && next >= Minimum && next <= Maximum) { value = next; ValueChanged?.Invoke(this, EventArgs.Empty); } };
        edit.Leave += (_, _) => Value = decimal.TryParse(edit.Text, out var next) ? next : value;
        edit.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
        edit.KeyDown += (_, e) => { if (e.KeyCode is Keys.Up or Keys.Down) { Value += e.KeyCode == Keys.Up ? 1 : -1; e.Handled = true; } };
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); edit.Bounds = new(9, Math.Max(4, (Height - edit.PreferredHeight) / 2), Math.Max(1, Width - 34), edit.PreferredHeight); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Surface); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Theme.Rounded(new(0, 0, Width - 1, Height - 1), 7); using var fill = new SolidBrush(Theme.Input); using var border = new Pen(Theme.Border);
        e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(border, shape);
        using var pen = new Pen(Enabled ? Theme.Muted : Theme.Border, 1.2f);
        e.Graphics.DrawLines(pen, [new(Width - 20, 12), new(Width - 16, 8), new(Width - 12, 12)]);
        e.Graphics.DrawLines(pen, [new(Width - 20, Height - 12), new(Width - 16, Height - 8), new(Width - 12, Height - 12)]);
    }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.X >= Width - 28) Value += e.Y < Height / 2 ? 1 : -1; else edit.Focus(); base.OnMouseDown(e); }
}
