using System.Diagnostics;

namespace B01Recorder;

internal sealed class MainForm : Form
{
    private readonly Settings settings = Settings.Load();
    private readonly ComboBox mode = Combo(["전체 화면", "윈도우", "영역 지정"]);
    private readonly ComboBox targets = Combo([]);
    private readonly ComboBox format = Combo(["MP4", "GIF", "WEBP"]);
    private readonly ComboBox preset = Combo(["표준 · 30 FPS", "부드럽게 · 60 FPS", "가볍게 · 15 FPS", "애니메이션 · 10 FPS", "직접 설정"]);
    private readonly NumericUpDown fps = new() { Minimum = 1, Maximum = 120, Width = 100 };
    private readonly TrackBar quality = new() { Minimum = 1, Maximum = 100, TickStyle = TickStyle.None, AutoSize = false, Height = 32, Dock = DockStyle.Fill };
    private readonly Label qualityLabel = new() { AutoSize = true };
    private readonly CheckBox audio = new() { Text = "시스템 소리 녹음", AutoSize = true };
    private readonly CheckBox cursor = new() { Text = "마우스 커서 포함", AutoSize = true };
    private readonly TextBox folder = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly Label targetLabel = new() { AutoSize = true, MaximumSize = new(530, 0), ForeColor = Color.FromArgb(90, 100, 115) };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new(530, 0) };
    private readonly Button start = Button("●  녹화 시작");
    private readonly Button stop = Button("■  중지 및 저장");
    private readonly Button select = Button("다시 선택");
    private readonly Button open = Button("저장 폴더 열기");
    private readonly FlowLayoutPanel controls = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 500 };
    private readonly Stopwatch clock = new();
    private CaptureTarget? target;
    private Recorder? recorder;
    private bool busy;
    private bool closing;
    private bool changingPreset;

    public MainForm()
    {
        Text = "b01 recorder · v0.1";
        ClientSize = new(620, 850);
        MinimumSize = new(636, 889);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(248, 249, 251);
        ForeColor = Color.FromArgb(30, 38, 50);
        Font = new("맑은 고딕", 10);
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(30), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new(SizeType.Absolute, 75));
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Absolute, 52));
        root.RowStyles.Add(new(SizeType.Absolute, 64));
        var heading = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        heading.Controls.Add(new Label { Text = "b01 recorder", Font = new("Segoe UI Semibold", 24), AutoSize = true, Margin = new(0) });
        heading.Controls.Add(new Label { Text = "대상을 고르고, 준비되면 녹화를 시작하세요.", AutoSize = true, ForeColor = Color.FromArgb(105, 112, 125) });
        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(controls, 0, 1);
        AddSection("녹화 대상", Row(mode, select));
        targets.Width = 520;
        controls.Controls.Add(targets);
        targetLabel.Margin = new(3, 8, 3, 14);
        controls.Controls.Add(targetLabel);
        AddSection("출력 형식", Row(format, audio));
        AddSection("프레임", Row(preset, fps, new Label { Text = "FPS", AutoSize = true, Padding = new(0, 6, 0, 0) }));
        AddSection("GIF / WebP 품질", Row(qualityLabel));
        quality.Width = 520;
        quality.Dock = DockStyle.None;
        controls.Controls.Add(quality);
        controls.Controls.Add(cursor);
        var browse = Button("폴더 선택");
        folder.Width = 390;
        folder.Dock = DockStyle.None;
        AddSection("저장 위치", Row(folder, browse));
        var advanced = Button("FFmpeg 선택");
        controls.Controls.Add(Row(open, advanced));
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        actions.RowStyles.Add(new(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new(SizeType.Percent, 50));
        start.Dock = stop.Dock = DockStyle.Fill;
        start.AutoSize = stop.AutoSize = false;
        start.BackColor = Color.FromArgb(50, 104, 240);
        start.ForeColor = Color.White;
        start.FlatAppearance.BorderSize = 0;
        actions.Controls.Add(start, 0, 0);
        actions.Controls.Add(stop, 1, 0);
        stop.Enabled = false;
        root.Controls.Add(actions, 0, 3);
        status.Margin = new(3, 14, 3, 0);
        root.Controls.Add(status, 0, 4);
        Controls.Add(root);

        format.SelectedItem = new[] { "MP4", "GIF", "WEBP" }.Contains(settings.Format) ? settings.Format : "MP4";
        fps.Value = Math.Clamp(settings.Fps, 1, 120);
        quality.Value = Math.Clamp(settings.Quality, 1, 100);
        audio.Checked = settings.Audio;
        cursor.Checked = settings.Cursor;
        folder.Text = settings.Folder;
        preset.SelectedIndex = settings.Fps switch { 30 => 0, 60 => 1, 15 => 2, 10 => 3, _ => 4 };
        mode.SelectedIndexChanged += (_, _) => ChangeMode();
        targets.SelectedIndexChanged += (_, _) => { target = targets.SelectedItem as CaptureTarget; UpdateTarget(); };
        select.Click += (_, _) => { if (mode.SelectedIndex == 2) PickRegion(); else RefreshTargets(); };
        format.SelectedIndexChanged += (_, _) => UpdateFormat();
        quality.ValueChanged += (_, _) => UpdateFormat();
        preset.SelectedIndexChanged += (_, _) =>
        {
            if (preset.SelectedIndex is >= 0 and < 4) { changingPreset = true; fps.Value = new[] { 30, 60, 15, 10 }[preset.SelectedIndex]; changingPreset = false; }
        };
        fps.ValueChanged += (_, _) => { if (!changingPreset) preset.SelectedIndex = 4; };
        browse.Click += (_, _) => { using var dialog = new FolderBrowserDialog { InitialDirectory = folder.Text, Description = "녹화 저장 폴더" }; if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath; };
        open.Click += (_, _) => SafeAction(() => { Directory.CreateDirectory(folder.Text); Process.Start(new ProcessStartInfo(folder.Text) { UseShellExecute = true }); });
        advanced.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "FFmpeg 실행 파일|ffmpeg.exe", Title = "ffmpeg.exe 선택" };
            if (dialog.ShowDialog(this) == DialogResult.OK) { settings.Ffmpeg = dialog.FileName; SaveSettings(); status.Text = "FFmpeg 경로를 저장했습니다."; }
        };
        start.Click += async (_, _) => await StartRecording();
        stop.Click += async (_, _) => await StopRecording();
        timer.Tick += async (_, _) =>
        {
            if (recorder is null || busy) return;
            status.Text = $"● 녹화 중  {clock.Elapsed:hh\\:mm\\:ss}  ·  중지 버튼을 누르면 저장됩니다.";
            if (recorder.HasExited) await StopRecording();
        };
        FormClosing += async (_, e) =>
        {
            if (closing) return;
            if (busy) { e.Cancel = true; return; }
            if (recorder is not null) { e.Cancel = true; await StopRecording(); closing = true; Close(); return; }
            SaveSettings();
        };
        mode.SelectedIndex = 0;
        ChangeMode();
        UpdateFormat();
        status.Text = "준비됨 · 녹화 시작 버튼을 눌러야 녹화가 시작됩니다.";
    }

    private void AddSection(string title, Control content)
    {
        controls.Controls.Add(new Label { Text = title, AutoSize = true, Font = new(Font, FontStyle.Bold), Margin = new(3, 13, 3, 5) });
        controls.Controls.Add(content);
    }
    private static FlowLayoutPanel Row(params Control[] children)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new(0) };
        row.Controls.AddRange(children);
        return row;
    }
    private static ComboBox Combo(string[] items)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, FlatStyle = FlatStyle.Flat };
        combo.Items.AddRange(items);
        if (items.Length > 0) combo.SelectedIndex = 0;
        return combo;
    }
    private static Button Button(string text) => new() { Text = text, AutoSize = true, MinimumSize = new(112, 34), FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new(3, 3, 8, 3), Cursor = Cursors.Hand };
    private void ChangeMode()
    {
        target = null;
        targets.Visible = mode.SelectedIndex != 2;
        select.Text = mode.SelectedIndex == 2 ? "영역 선택" : "새로고침";
        if (mode.SelectedIndex == 2) UpdateTarget(); else RefreshTargets();
    }
    private void RefreshTargets()
    {
        targets.Items.Clear();
        if (mode.SelectedIndex == 0)
        {
            targets.Items.Add(new CaptureTarget("모든 모니터", SystemInformation.VirtualScreen));
            foreach (var screen in Screen.AllScreens)
                targets.Items.Add(new CaptureTarget($"{screen.DeviceName} · {screen.Bounds.Width} × {screen.Bounds.Height}" + (screen.Primary ? " · 기본" : ""), screen.Bounds));
        }
        else foreach (var window in Windows.List(Handle)) targets.Items.Add(window);
        if (targets.Items.Count > 0) targets.SelectedIndex = 0;
        else { target = null; UpdateTarget(); }
    }
    private void PickRegion()
    {
        Hide();
        try
        {
            using var picker = new RegionPicker();
            if (picker.ShowDialog() == DialogResult.OK) target = new("선택한 영역", picker.SelectedBounds);
        }
        finally { Show(); Activate(); }
        UpdateTarget();
    }
    private void UpdateTarget()
    {
        targetLabel.Text = target is null ? "녹화 대상을 선택하세요." : $"{target.Label} · {target.Bounds.Width} × {target.Bounds.Height}" + (target.IsWindow ? "" : $" · ({target.Bounds.X}, {target.Bounds.Y})");
        start.Enabled = target is not null && recorder is null && !busy;
    }
    private void UpdateFormat()
    {
        var animation = format.Text != "MP4";
        audio.Enabled = !animation;
        quality.Enabled = animation;
        qualityLabel.Text = animation ? $"{quality.Value}%  ·  높을수록 선명하고 파일이 커집니다." : "MP4는 고화질로 저장합니다. 품질 조절은 GIF / WebP에 적용됩니다.";
    }
    private async Task StartRecording()
    {
        if (target is null || busy || recorder is not null) return;
        busy = true;
        SetRecordingUi(true);
        status.Text = "녹화를 준비하고 있습니다…";
        try
        {
            SaveSettings();
            recorder = new(Recorder.ResolveFfmpeg(settings.Ffmpeg));
            var options = new RecordingOptions(target, (int)fps.Value, format.Text, quality.Value, audio.Checked && format.Text == "MP4", cursor.Checked, folder.Text);
            WindowState = FormWindowState.Minimized;
            await Task.Delay(350);
            await recorder.StartAsync(options);
            clock.Restart();
            timer.Start();
            status.Text = "● 녹화 중 · 작업 표시줄에서 열어 중지할 수 있습니다.";
        }
        catch (Exception ex)
        {
            var recovery = recorder?.RecoveryFolder;
            recorder?.Dispose(); recorder = null;
            WindowState = FormWindowState.Normal;
            ShowError(ex, recovery);
            SetRecordingUi(false);
        }
        finally { busy = false; stop.Enabled = recorder is not null; UpdateTarget(); }
    }
    private async Task StopRecording()
    {
        if (recorder is null || busy) return;
        busy = true;
        timer.Stop(); clock.Stop(); stop.Enabled = false;
        status.Text = "저장 중… 긴 GIF / WebP는 변환에 시간이 걸릴 수 있습니다.";
        var current = recorder;
        try
        {
            var file = await current.StopAsync();
            status.Text = $"저장 완료 · {Path.GetFileName(file)}";
        }
        catch (Exception ex) { ShowError(ex, current.RecoveryFolder); }
        finally
        {
            current.Dispose(); recorder = null; busy = false;
            WindowState = FormWindowState.Normal; Activate(); SetRecordingUi(false); UpdateTarget();
        }
    }
    private void SetRecordingUi(bool recording) { controls.Enabled = !recording; start.Enabled = !recording && target is not null; stop.Enabled = recording && !busy; }
    private void SaveSettings()
    {
        settings.Folder = folder.Text; settings.Format = format.Text; settings.Fps = (int)fps.Value;
        settings.Quality = quality.Value; settings.Audio = audio.Checked; settings.Cursor = cursor.Checked;
        try { settings.Save(); } catch (Exception ex) { status.Text = "설정 저장 실패: " + ex.Message; }
    }
    private void SafeAction(Action action) { try { action(); } catch (Exception ex) { ShowError(ex); } }
    private void ShowError(Exception ex, string? recovery = null)
    {
        status.Text = "오류 · 설정을 확인하고 다시 시도하세요.";
        MessageBox.Show(this, ex.Message + (string.IsNullOrEmpty(recovery) ? "" : "\n\n복구 폴더: " + recovery), "b01 recorder", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
    protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); recorder?.Dispose(); } base.Dispose(disposing); }
}
