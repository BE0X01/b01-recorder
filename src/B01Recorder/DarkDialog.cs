namespace B01Recorder;

internal static class DarkDialog
{
    public static DialogResult Show(IWin32Window owner, string message, bool confirm = false)
    {
        using var dialog = Create(message, confirm);
        return dialog.ShowDialog(owner);
    }
    internal static Form Create(string message, bool confirm = false)
    {
        var dialog = new Form { Text = "B01 Recorder", ClientSize = new(560, 260), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, BackColor = Theme.Background, ForeColor = Theme.Text, Font = new("Segoe UI", 10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 60));
        var text = new SlimFlowPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.TopDown, BackColor = Theme.Background };
        text.Controls.Add(new Label { Text = message, AutoSize = true, MaximumSize = new(495, 0), ForeColor = Theme.Text, Margin = new(0) }); layout.Controls.Add(text, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new(0, 8, 0, 0), Margin = new(0) };
        var accept = new DarkButton(confirm ? "Move to Trash" : "OK") { Primary = true, Width = confirm ? 138 : 90, DialogResult = confirm ? DialogResult.Yes : DialogResult.OK };
        actions.Controls.Add(accept); dialog.AcceptButton = accept;
        if (confirm) { var cancel = new DarkButton("Cancel") { Width = 90, DialogResult = DialogResult.No }; actions.Controls.Add(cancel); dialog.CancelButton = cancel; }
        else dialog.CancelButton = accept;
        layout.Controls.Add(actions, 0, 1); dialog.Controls.Add(layout); dialog.Shown += (_, _) => Theme.DarkTitle(dialog);
        return dialog;
    }
}


