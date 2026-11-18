using System.Diagnostics;
using static B01Recorder.Ui;

namespace B01Recorder;

internal sealed class MainForm : Form
{
    private readonly Settings settings;
    private readonly bool persistSettings;
    private readonly Dictionary<Control, (string English, string Korean)> captions = [];
    private readonly DarkCombo targets = new([]);
    private readonly DarkCombo format = new(["MP4", "GIF", "WEBP"]);
    private readonly DarkNumber fps = new() { Minimum = 1, Maximum = 120, Size = new(80, 36) };
    private readonly Label formatHint = Line();
    private readonly CheckBox audio = new DarkCheck();
    private readonly CheckBox cursor = new DarkCheck();
    private readonly Label folder = new()
    {
        Dock = DockStyle.Fill, AutoEllipsis = true, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft,
        Margin = Padding.Empty, ForeColor = Theme.Text
    };
    private readonly TextBox fileNamePattern = new() { BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text, MaxLength = 180 };
    private readonly Label fileNameExample = Line();
    private readonly ToolTip help = new() { AutoPopDelay = 30000, InitialDelay = 300, ReshowDelay = 100 };
    private readonly ContextMenuStrip pathMenu = new();
    private readonly Label targetLabel = Line();
    private readonly Label status = Line();
    private readonly Label badge = Line();
    private readonly DarkButton gear = new("") { Glyph = "Gear" };
    private readonly DarkButton start = new("") { Primary = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly DarkButton select = new("") { Dock = DockStyle.Fill, Margin = new(8, 0, 0, 0) };
    private readonly DarkButton[] modeButtons = [new("") { Glyph = "Desktop" }, new("") { Glyph = "Window" }, new("") { Glyph = "Crop" }];
    private readonly DarkButton browse = new("") { Glyph = "FolderOpen", Dock = DockStyle.Fill, Margin = new(8, 0, 0, 0) };
    private readonly DarkButton info = new("") { Glyph = "CircleInfo", Dock = DockStyle.Fill, Margin = new(8, 0, 0, 0) };
    private readonly TableLayoutPanel settingsPanel = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
    private readonly SelectionOverlay overlay = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 200 };
    private readonly Stopwatch clock = new();
    private CaptureTarget? target;
    private Recorder? recorder;
    private (string English, string Korean) statusText = ("Choose a source to start recording.", "녹화할 대상을 선택하세요.");
    private int captureMode;
    private bool wasMaximized;
    private bool busy, closing, picking, released, settingsSaved;

    public MainForm(Settings? initialSettings = null, bool persistSettings = true)
    {
        settings = initialSettings ?? Settings.Load();
        this.persistSettings = persistSettings;
        Ui.Language = settings.Language;
        Text = "B01 Recorder";
        ClientSize = new(512, 608);
        MinimumSize = new(480, 648);
        DoubleBuffered = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        AutoScaleMode = AutoScaleMode.Dpi;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);

        var root = Stack((BuildHeader(), 56), (settingsPanel, 0), (BuildFooter(), 40));
        root.Padding = new(16);
        Controls.Add(root);
        BuildRecordingControls();

        format.SelectedItem = new[] { "MP4", "GIF", "WEBP" }.Contains(settings.Format) ? settings.Format : "MP4";
        fps.Value = Math.Clamp(settings.Fps, 1, 120);
        audio.Checked = settings.Audio;
        cursor.Checked = settings.Cursor;
        folder.Text = settings.Folder;
        fileNamePattern.Text = settings.FileNamePattern;
        fileNamePattern.TextChanged += (_, _) => UpdateFileNameExample();
        folder.TextChanged += (_, _) => { UpdateFileNameExample(); UpdateFolderTooltip(); };
        targets.SelectedIndexChanged += (_, _) => { if (captureMode != 2) target = targets.SelectedItem as CaptureTarget; UpdateTarget(); };
        select.Click += (_, _) => { if (captureMode == 2) PickRegion(); else RefreshTargets(); };
        gear.Click += (_, _) => ShowSettings();
        format.SelectedIndexChanged += (_, _) => UpdateFormat();
        start.Click += async (_, _) => { if (recorder is null) await StartRecording(); else await StopRecording(); };
        timer.Tick += async (_, _) =>
        {
            UpdateOverlay();
            if (busy || closing || recorder is null) return;
            var duration = clock.Elapsed.ToString(@"hh\:mm\:ss");
            SetStatus($"Recording  {duration}", $"녹화 중  {duration}");
            if (recorder.HasExited) await StopRecording();
        };
        Shown += (_, _) => timer.Start();
        FormClosing += async (_, e) =>
        {
            if (closing) { RememberWindow(); return; }
            if (busy) { e.Cancel = true; return; }
            overlay.Hide();
            if (recorder is not null) { e.Cancel = true; await StopRecording(); closing = true; Close(); return; }
            RememberWindow();
        };
        FormClosed += (_, _) => SaveSettingsOnExit();
        Resize += (_, _) => { if (WindowState != FormWindowState.Minimized) wasMaximized = WindowState == FormWindowState.Maximized; };
        SetMode(Math.Clamp(settings.CaptureMode, 0, 2));
        ApplyPreferences();
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitle(this); }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg == 0x001A && settings?.ThemeMode == ThemeMode.System) Theme.Apply(ThemeMode.System, this);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (settings.WindowX is not int x || settings.WindowY is not int y || settings.WindowWidth <= 0 || settings.WindowHeight <= 0) return;
        var saved = new Rectangle(x, y, settings.WindowWidth, settings.WindowHeight);
        var screen = Screen.AllScreens.FirstOrDefault(s => s.WorkingArea.Contains(saved)) ?? Screen.FromRectangle(saved);
        var area = screen.WorkingArea;
        var legacyLayout = !settings.CompactLayout;
        var width = Math.Min(Math.Max(legacyLayout ? Width : saved.Width, MinimumSize.Width), area.Width);
        var height = Math.Min(Math.Max(legacyLayout ? Height : saved.Height, MinimumSize.Height), area.Height);
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(Math.Clamp(x, area.Left, area.Right - width), Math.Clamp(y, area.Top, area.Bottom - height), width, height);
        if (settings.WindowMaximized) WindowState = FormWindowState.Maximized;
    }

    private void RememberWindow()
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        settings.WindowX = bounds.X; settings.WindowY = bounds.Y;
        settings.WindowWidth = bounds.Width; settings.WindowHeight = bounds.Height;
        settings.WindowMaximized = WindowState == FormWindowState.Minimized ? wasMaximized : WindowState == FormWindowState.Maximized;
        settings.CompactLayout = true;
    }

    private Control BuildHeader()
    {
        var header = Grid(4);
        header.Margin = new(0, 0, 0, 16);
        header.ColumnStyles.Add(new(SizeType.Absolute, 40));
        header.ColumnStyles.Add(new(SizeType.Percent, 100));
        header.ColumnStyles.Add(new(SizeType.Absolute, 100));
        header.ColumnStyles.Add(new(SizeType.Absolute, 48));
        var logo = new PictureBox { Dock = DockStyle.Fill, Margin = Padding.Empty, AccessibleName = "B01 Recorder" };
        logo.Paint += (_, e) =>
        {
            var box = new Rectangle(0, (logo.Height - 40) / 2, 40, 40);
            using var shape = Theme.Rounded(box, 12);
            using var fill = new SolidBrush(Theme.AccentSurface);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.FillPath(fill, shape);
            Ui.DrawIcon(e.Graphics, "CameraRecord", new Rectangle(8, box.Y + 8, 24, 24), Theme.Accent);
        };
        var title = Line(muted: false);
        title.Text = "B01 Recorder";
        title.Font = Ui.Font(16, FontStyle.Bold);
        title.Margin = new(12, 0, 0, 0);
        var pill = new Card { Dock = DockStyle.Fill, Margin = new(0, 4, 0, 4), Padding = Padding.Empty, BackColor = Theme.AccentSurface };
        badge.TextAlign = ContentAlignment.MiddleCenter;
        badge.BackColor = Color.Transparent;
        badge.Font = Ui.Font(Ui.SmallPoints);
        badge.ForeColor = Theme.Accent;
        pill.Controls.Add(badge);
        gear.Dock = DockStyle.Fill;
        gear.Margin = new(8, 0, 0, 0);
        header.Controls.Add(logo, 0, 0);
        header.Controls.Add(title, 1, 0);
        header.Controls.Add(pill, 2, 0);
        header.Controls.Add(gear, 3, 0);
        return header;
    }

    private Control BuildFooter()
    {
        var footer = Grid(2);
        footer.ColumnStyles.Add(new(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new(SizeType.Absolute, 128));
        status.Margin = new(0, 0, 12, 0);
        footer.Controls.Add(status, 0, 0);
        footer.Controls.Add(start, 1, 0);
        return footer;
    }

    private void BuildRecordingControls()
    {
        var modes = Grid(3);
        for (var i = 0; i < modeButtons.Length; i++)
        {
            var index = i;
            modes.ColumnStyles.Add(new(SizeType.Percent, 100f / modeButtons.Length));
            var button = modeButtons[i];
            button.AccessibleRole = AccessibleRole.RadioButton;
            button.Dock = DockStyle.Fill;
            button.Margin = new(i == 0 ? 0 : 8, 0, 0, 0);
            button.Click += (_, _) => SetMode(index);
            modes.Controls.Add(button, i, 0);
        }
        Bind(modeButtons[0], "Full screen", "전체 화면");
        Bind(modeButtons[1], "Window", "창");
        Bind(modeButtons[2], "Region", "영역");
        var picker = Pair(targets, select, 136);
        picker.Margin = new(0, 8, 0, 0);
        var capture = Stack((modes, 36), (picker, 44), (targetLabel, 24));
        AddCard("Capture source", "캡처 대상", 160, capture);

        format.Width = 108;
        format.Height = 36;
        var fpsLabel = Line();
        fpsLabel.Text = "FPS";
        fpsLabel.Size = new(44, 36);
        var encodeRow = Row(format, fps, fpsLabel);
        var toggles = Row(audio, cursor);
        audio.Margin = cursor.Margin = new(0, 4, 16, 0);
        Bind(audio, "System audio", "시스템 오디오");
        Bind(cursor, "Capture cursor", "마우스 커서");
        AddCard("Recording", "녹화", 144, Stack((encodeRow, 36), (formatHint, 24), (toggles, 28)));

        var nameLabel = Line();
        Bind(nameLabel, "Filename pattern", "파일 이름 형식");
        var folderBox = new Card { BackColor = Theme.Input, Margin = Padding.Empty, Padding = new(12, 4, 12, 4) };
        folderBox.Controls.Add(folder);
        var folderRow = Pair(folderBox, browse, 44);
        var nameRow = Pair(InputBox(fileNamePattern), info, 44);
        AddCard("Save to", "저장 위치", 176, Stack((folderRow, 36), (nameLabel, 24), (nameRow, 36), (fileNameExample, 24)));
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { InitialDirectory = folder.Text, Description = T("Recording output folder", "녹화 파일을 저장할 폴더") };
            if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath;
        };
        folder.DoubleClick += (_, _) => OpenOutputFolder();
        var copyPath = pathMenu.Items.Add("");
        pathMenu.Opening += (_, _) =>
        {
            pathMenu.BackColor = Theme.Input;
            copyPath.ForeColor = Theme.Text;
            copyPath.Text = T("Copy path", "경로 복사");
        };
        copyPath.Click += (_, _) =>
        {
            if (folder.Text.Length == 0) return;
            try { Clipboard.SetText(folder.Text); }
            catch (System.Runtime.InteropServices.ExternalException ex) { DarkDialog.Show(this, ex.Message); }
        };
        folder.ContextMenuStrip = pathMenu;
        info.Click += (_, _) => DarkDialog.Show(this, RecordingFileName.Help);
        settingsPanel.RowStyles.Add(new(SizeType.Percent, 100));
    }

    private void OpenOutputFolder()
    {
        try
        {
            Directory.CreateDirectory(folder.Text);
            Process.Start(new ProcessStartInfo(folder.Text) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ShowSettings()
    {
        using var dialog = new Form { Text = T("Settings", "설정"), StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, ClientSize = new(388, 232), BackColor = Theme.Background,
            ForeColor = Theme.Text, ShowInTaskbar = false, MaximizeBox = false, MinimizeBox = false };
        var language = new DarkCombo(["English", "한국어"]) { SelectedIndex = (int)settings.Language, AccessibleName = T("Language", "언어") };
        var theme = new DarkCombo([T("System", "시스템"), T("Light", "라이트"), T("Dark", "다크")])
            { SelectedIndex = (int)settings.ThemeMode, AccessibleName = T("Theme", "테마") };
        var dim = new DarkCheck { Text = T("Dim background during region selection", "영역을 지정할 때 배경 어둡게"), Checked = settings.DimRegionPicker };
        var save = new DarkButton(T("Apply", "적용")) { Primary = true, Width = 88, DialogResult = DialogResult.OK };
        var cancel = new DarkButton(T("Cancel", "취소")) { Width = 88, DialogResult = DialogResult.Cancel };
        var actions = Row(save, cancel);
        actions.FlowDirection = FlowDirection.RightToLeft;
        actions.Margin = new(0, 12, 0, 0);
        var languageLabel = Line(); languageLabel.Text = T("Language", "언어");
        var themeLabel = Line(); themeLabel.Text = T("Theme", "테마");
        var body = Stack((languageLabel, 24), (language, 36), (themeLabel, 28), (theme, 36), (dim, 28), (actions, 48));
        body.Padding = new(16);
        dialog.Controls.Add(body);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        Theme.DarkTitle(dialog);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        settings.Language = (UiLanguage)language.SelectedIndex;
        settings.ThemeMode = (ThemeMode)theme.SelectedIndex;
        settings.DimRegionPicker = dim.Checked;
        ApplyPreferences();
    }

    private void ApplyPreferences()
    {
        Ui.Language = settings.Language;
        Theme.Apply(settings.ThemeMode, this);
        foreach (var (control, label) in captions) control.Text = T(label.English, label.Korean);
        gear.AccessibleName = T("Settings", "설정");
        browse.AccessibleName = T("Choose output folder", "저장 폴더 선택");
        info.AccessibleName = T("Filename format help", "파일 이름 형식 도움말");
        targets.AccessibleName = T("Capture target", "캡처 대상");
        format.AccessibleName = T("Output format", "저장 형식");
        fps.AccessibleName = T("Frames per second", "초당 프레임 수");
        fileNamePattern.AccessibleName = T("Recording filename pattern", "파일 이름 형식");
        help.SetToolTip(gear, gear.AccessibleName);
        help.SetToolTip(browse, browse.AccessibleName);
        help.SetToolTip(info, info.AccessibleName);
        UpdateFolderTooltip();
        if (captureMode == 2)
        {
            if (target is not null) target = target with { Label = T("Selected region", "선택 영역") };
            SetRegionHint();
        }
        else RefreshTargets(target);
        UpdateFormat();
        UpdateTarget();
        SetBadge(T("Ready", "준비"));
        status.Text = T(statusText.English, statusText.Korean);
    }

    private void AddCard(string english, string korean, int height, Control content)
    {
        var card = new Card { Dock = DockStyle.Fill, Margin = new(0, 0, 0, 8), Padding = new(12) };
        var title = Line(muted: false);
        title.Font = Ui.Font(Ui.BodyPoints, FontStyle.Bold);
        Bind(title, english, korean);
        card.Controls.Add(Stack((title, 24), (content, 0)));
        var index = settingsPanel.Controls.Count;
        settingsPanel.RowStyles.Add(new(SizeType.Absolute, height));
        settingsPanel.Controls.Add(card, 0, index);
    }

    private void Bind(Control control, string english, string korean) => captions.Add(control, (english, korean));

    private static TableLayoutPanel Stack(params (Control control, int height)[] rows)
    {
        var result = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows.Length, Margin = Padding.Empty };
        for (var i = 0; i < rows.Length; i++)
        {
            var (control, height) = rows[i];
            result.RowStyles.Add(height == 0 ? new(SizeType.Percent, 100) : new(SizeType.Absolute, height));
            control.Dock = DockStyle.Fill;
            result.Controls.Add(control, 0, i);
        }
        return result;
    }

    private static TableLayoutPanel Grid(int columns)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = 1, Margin = Padding.Empty };
        grid.RowStyles.Add(new(SizeType.Percent, 100));
        return grid;
    }

    private static Control Pair(Control left, Control right, int width)
    {
        var row = Grid(2);
        row.ColumnStyles.Add(new(SizeType.Percent, 100));
        row.ColumnStyles.Add(new(SizeType.Absolute, width));
        left.Dock = DockStyle.Fill;
        left.Margin = Padding.Empty;
        row.Controls.Add(left, 0, 0);
        row.Controls.Add(right, 1, 0);
        return row;
    }

    private static FlowLayoutPanel Row(params Control[] children)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        foreach (var child in children) child.Margin = new(0, 0, 8, 0);
        row.Controls.AddRange(children);
        return row;
    }

    private static Label Line(bool muted = true) => new()
        { Dock = DockStyle.Fill, AutoEllipsis = true, UseMnemonic = false, Margin = Padding.Empty,
          TextAlign = ContentAlignment.MiddleLeft, ForeColor = muted ? Theme.Muted : Theme.Text };

    private static Control InputBox(TextBox edit)
    {
        var box = new Card { BackColor = Theme.Input, Margin = Padding.Empty, Padding = Padding.Empty };
        box.Controls.Add(edit);
        box.Layout += (_, _) => edit.Bounds = new Rectangle(12, (box.Height - edit.PreferredHeight) / 2,
            Math.Max(1, box.Width - 24), edit.PreferredHeight);
        edit.GotFocus += (_, _) => { box.BorderColor = Theme.Accent; box.Invalidate(); };
        edit.LostFocus += (_, _) => { box.BorderColor = Theme.Border; box.Invalidate(); };
        return box;
    }

    private void SetMode(int mode)
    {
        captureMode = mode;
        target = null;
        overlay.Hide();
        for (var i = 0; i < modeButtons.Length; i++) { modeButtons[i].Selected = i == mode; modeButtons[i].Invalidate(); }
        if (mode == 2) SetRegionHint(); else RefreshTargets();
        UpdateTarget();
    }

    private void SetRegionHint()
    {
        targets.Items.Clear();
        targets.Items.Add(T("Drag to select a region", "드래그하여 영역 지정"));
        targets.SelectedIndex = 0;
    }

    private void RefreshTargets(CaptureTarget? preferred = null)
    {
        targets.Items.Clear();
        if (captureMode == 0)
        {
            targets.Items.Add(new CaptureTarget(T("All monitors", "모든 모니터"), SystemInformation.VirtualScreen));
            foreach (var screen in Screen.AllScreens)
                targets.Items.Add(new CaptureTarget($"{screen.DeviceName} · {screen.Bounds.Width} × {screen.Bounds.Height}"
                    + (screen.Primary ? T(" · primary", " · 주 모니터") : ""), screen.Bounds));
        }
        else foreach (var window in Windows.List(Handle)) targets.Items.Add(window);
        var match = targets.Items.OfType<CaptureTarget>().FirstOrDefault(item => preferred is not null &&
            (item.IsWindow ? item.Handle == preferred.Handle : item.Bounds == preferred.Bounds));
        if (match is not null) targets.SelectedItem = match;
        else if (targets.Items.Count > 0) targets.SelectedIndex = 0;
        else target = null;
        UpdateTarget();
    }

    private void PickRegion()
    {
        picking = true;
        overlay.Hide();
        try
        {
            using var picker = new RegionPicker(settings.DimRegionPicker);
            if (picker.ShowDialog(this) == DialogResult.OK) target = new(T("Selected region", "선택 영역"), picker.SelectedBounds);
        }
        finally { picking = false; Activate(); }
        UpdateTarget();
    }

    private void UpdateTarget()
    {
        var bounds = target?.Bounds ?? Rectangle.Empty;
        if (target?.IsWindow == true) Windows.TryGetClientBounds(target.Handle, out bounds);
        targetLabel.Text = target is null
            ? captureMode == 2 ? T("No region selected", "지정된 영역 없음") : T("No capture target available", "캡처 가능한 대상 없음")
            : $"{bounds.Width} × {bounds.Height}  ·  ({bounds.X}, {bounds.Y})  ·  {target.Label}";
        targets.Enabled = captureMode != 2;
        select.Text = captureMode == 2 ? T("Select region", "영역 지정") : T("Refresh", "새로 고침");
        select.Glyph = captureMode == 2 ? "Crop" : "Refresh";
        start.Text = recorder is null ? T("Record", "녹화") : recorder.IsAnimation ? T("Stop & edit", "종료 후 편집") : T("Stop & save", "종료 후 저장");
        start.Glyph = recorder is null ? "Record" : "Stop";
        start.Enabled = !busy && (recorder is not null || target is not null);
        UpdateOverlay();
    }

    private void UpdateOverlay() => overlay.Update(target, captureMode != 0 && !picking && !closing && WindowState != FormWindowState.Minimized);

    private void UpdateFormat()
    {
        UpdateFileNameExample();
        var animation = format.Text != "MP4";
        audio.Enabled = !animation;
        formatHint.Text = animation ? T("Trim, crop and set quality after recording", "녹화 후 길이, 크롭, 품질을 편집합니다.")
            : T("H.264 video with optional system audio", "H.264 영상과 시스템 오디오를 녹화할 수 있습니다.");
    }

    private void UpdateFileNameExample()
    {
        try { fileNameExample.Text = T("Example: ", "예시: ") + Path.GetFileName(RecordingFileName.AvailablePath(folder.Text, fileNamePattern.Text, DateTime.Now, format.Text)); }
        catch (ArgumentException ex) { fileNameExample.Text = ex.Message; }
        help.SetToolTip(fileNameExample, fileNameExample.Text);
    }

    private void UpdateFolderTooltip() => help.SetToolTip(folder, folder.Text + "\n" +
        T("Double-click to open the output folder", "두 번 클릭하면 저장 폴더 열기"));

    private void SetBadge(string text) => badge.Text = text;

    private void SetStatus(string english, string korean)
    {
        statusText = (english, korean);
        status.Text = T(english, korean);
        help.SetToolTip(status, status.Text);
    }

    private async Task StartRecording()
    {
        if (target is null || busy || recorder is not null) return;
        busy = true;
        SetRecordingUi(true);
        SetStatus("Starting recording…", "녹화를 시작하는 중…");
        try
        {
            recorder = new(Recorder.ResolveFfmpeg(settings.Ffmpeg));
            await recorder.StartAsync(new(target, (int)fps.Value, format.Text, settings.Quality, audio.Checked && format.Text == "MP4", cursor.Checked, folder.Text, fileNamePattern.Text));
            clock.Restart();
            SetBadge(T("Recording", "녹화 중"));
            SetStatus("Recording", "녹화 중");
        }
        catch (Exception ex)
        {
            var recovery = recorder?.RecoveryFolder;
            recorder?.Dispose();
            recorder = null;
            ShowError(ex, recovery);
            SetRecordingUi(false);
        }
        finally { busy = false; UpdateTarget(); }
    }

    private async Task StopRecording()
    {
        if (recorder is null || busy) return;
        var current = recorder;
        busy = true;
        clock.Stop();
        start.Enabled = false;
        SetBadge(current.IsAnimation ? T("Editing", "편집 중") : T("Saving", "저장 중"));
        if (current.IsAnimation) SetStatus("Preparing editor…", "편집기를 준비하는 중…");
        else SetStatus("Saving…", "저장 중…");
        try
        {
            string? file;
            if (current.IsAnimation)
            {
                await current.FinishCaptureAsync();
                using var editor = new AnimationEditor(current, settings.Quality);
                editor.ShowDialog(this);
                await editor.DrainAsync();
                file = editor.SavedPath;
                settings.Quality = editor.Quality;
                if (editor.PreviewFailed)
                {
                    SetStatus("Preview failed. The recording is in the recovery folder.", "미리보기를 열지 못했습니다. 녹화 파일은 복구 폴더에 있습니다.");
                    return;
                }
                current.CleanSession();
                if (file is null) { SetStatus("Editing cancelled.", "편집을 취소했습니다."); return; }
            }
            else file = await current.StopAsync();
            var name = Path.GetFileName(file);
            SetStatus("Saved · " + name, "저장 완료 · " + name);
        }
        catch (Exception ex) { ShowError(ex, current.RecoveryFolder); }
        finally
        {
            current.Dispose();
            recorder = null;
            busy = false;
            SetRecordingUi(false);
        }
    }

    private void SetRecordingUi(bool recording)
    {
        settingsPanel.Enabled = gear.Enabled = !recording;
        if (!recording) SetBadge(T("Ready", "준비"));
        UpdateTarget();
    }

    private void SaveSettingsOnExit()
    {
        if (settingsSaved || !persistSettings) return;
        settingsSaved = true;
        settings.CaptureMode = captureMode;
        settings.Folder = folder.Text; settings.Format = format.Text; settings.Fps = (int)fps.Value;
        settings.Audio = audio.Checked; settings.Cursor = cursor.Checked;
        settings.FileNamePattern = fileNamePattern.Text;
        try { settings.Save(); } catch (Exception ex) { DarkDialog.Show(this, T("Could not save setting.ini: ", "설정 파일을 저장하지 못했습니다: ") + ex.Message); }
    }

    private void ShowError(Exception ex, string? recovery = null)
    {
        SetStatus("Recording error. Check your settings.", "녹화 오류가 발생했습니다. 설정을 확인하세요.");
        DarkDialog.Show(this, ex.Message + (string.IsNullOrEmpty(recovery) ? "" : T("\n\nRecovery folder: ", "\n\n복구 폴더: ") + recovery));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !released)
        {
            released = true;
            help.Dispose();
            pathMenu.Dispose();
            timer.Dispose();
            overlay.Dispose();
            recorder?.Dispose();
            recorder = null;
        }
        base.Dispose(disposing);
    }
}
