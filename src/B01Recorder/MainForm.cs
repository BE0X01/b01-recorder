using System.Diagnostics;

namespace B01Recorder;

internal sealed class MainForm : Form
{
    private readonly Settings settings = Settings.Load();
    private readonly bool persistSettings;
    private readonly DarkCombo targets = new([]);
    private readonly DarkCombo format = new(["MP4", "GIF", "WEBP"]);
    private readonly DarkCombo preset = new(["Regular · 30 FPS", "Smooth · 60 FPS", "Light · 15 FPS", "Animation · 10 FPS", "Custom"]);
    private readonly DarkNumber fps = new() { Minimum = 1, Maximum = 120, Width = 76 };
    private readonly QualitySlider quality = new();
    private readonly Label qualityLabel = Label("");
    private readonly CheckBox audio = Check("System audio");
    private readonly CheckBox cursor = Check("Capture cursor");
    private readonly TextBox folder = new() { ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Input, ForeColor = Theme.Text, Dock = DockStyle.Fill };
    private readonly Label targetLabel = Label("", true);
    private readonly Label status = Label("", true);
    private readonly Label badge = Label("●  READY");
    private readonly DarkButton start = new("●  Record") { Primary = true, Width = 148, Height = 38 };
    private readonly DarkButton select = new("Refresh") { Width = 88, Dock = DockStyle.Fill, Margin = new(8, 0, 0, 0) };
    private readonly DarkButton[] modeButtons = [new("Full screen"), new("Window"), new("Region")];
    private readonly SlimFlowPanel settingsPanel = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new(0) };
    private readonly SlimFlowPanel gallery = new() { Dock = DockStyle.Fill, WrapContents = true, BackColor = Theme.Surface, Margin = new(0) };
    private readonly MediaPreview preview = new() { Dock = DockStyle.Fill };
    private readonly Label fileLabel = Label("No recording selected");
    private readonly Label countLabel = Label("0 files", true);
    private readonly DarkButton play = new("Play in app") { Width = 106 };
    private readonly DarkButton external = new("Open externally") { Width = 90 };
    private readonly DarkButton reveal = new("Folder") { Width = 64 };
    private readonly DarkButton delete = new("Delete") { Width = 64, Destructive = true };
    private readonly SelectionOverlay overlay = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 200 };
    private readonly Stopwatch clock = new();
    private CancellationTokenSource? libraryRefresh;
    private CaptureTarget? target;
    private Recorder? recorder;
    private string? selectedFile;
    private int captureMode;
    private bool busy, closing, changingPreset, picking, released, settingsSaved;

    public MainForm(Settings? initialSettings = null, bool persistSettings = true)
    {
        if (initialSettings is not null) settings = initialSettings;
        this.persistSettings = persistSettings;
        Text = "B01 Recorder";
        ClientSize = new(1180, 850); MinimumSize = new(1040, 780);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background; ForeColor = Theme.Text;
        Font = new("Segoe UI", 9); AutoScaleMode = AutoScaleMode.Dpi;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        settings.Recordings ??= [];

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(24), ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new(SizeType.Absolute, 80)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 68));
        root.Controls.Add(BuildHeader(), 0, 0);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new(0) };
        body.ColumnStyles.Add(new(SizeType.Percent, 58)); body.ColumnStyles.Add(new(SizeType.Percent, 42));
        body.RowStyles.Add(new(SizeType.Percent, 100));
        body.Controls.Add(BuildLibrary(), 0, 0); body.Controls.Add(settingsPanel, 1, 0);
        root.Controls.Add(body, 0, 1); root.Controls.Add(BuildFooter(), 0, 2); Controls.Add(root);
        BuildSettings();
        settingsPanel.SizeChanged += (_, _) => { foreach (Control card in settingsPanel.Controls) card.Width = Math.Max(350, settingsPanel.ClientSize.Width - 22); };

        format.SelectedItem = new[] { "MP4", "GIF", "WEBP" }.Contains(settings.Format) ? settings.Format : "MP4";
        fps.Value = Math.Clamp(settings.Fps, 1, 120); quality.Value = Math.Clamp(settings.Quality, 1, 100);
        audio.Checked = settings.Audio; cursor.Checked = settings.Cursor; folder.Text = settings.Folder;
        preset.SelectedIndex = settings.Fps switch { 30 => 0, 60 => 1, 15 => 2, 10 => 3, _ => 4 };
        targets.SelectedIndexChanged += (_, _) => { target = targets.SelectedItem as CaptureTarget; UpdateTarget(); };
        select.Click += (_, _) => { if (captureMode == 2) PickRegion(); else RefreshTargets(); };
        format.SelectedIndexChanged += (_, _) => UpdateFormat(); quality.ValueChanged += (_, _) => UpdateFormat();
        preset.SelectedIndexChanged += (_, _) =>
        {
            if (preset.SelectedIndex is >= 0 and < 4) { changingPreset = true; fps.Value = new[] { 30, 60, 15, 10 }[preset.SelectedIndex]; changingPreset = false; }
        };
        fps.ValueChanged += (_, _) => { if (!changingPreset) preset.SelectedIndex = 4; };
        start.Click += async (_, _) => { if (recorder is null) await StartRecording(); else await StopRecording(); };
        play.Click += async (_, _) => { if (selectedFile is not null) await preview.LoadAsync(selectedFile, true); };
        external.Click += (_, _) => SafeAction(() => { if (selectedFile is not null) Process.Start(new ProcessStartInfo(selectedFile) { UseShellExecute = true }); });
        reveal.Click += (_, _) => SafeAction(() =>
        {
            if (selectedFile is null) return;
            var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = true }; info.Arguments = "/select,\"" + selectedFile + "\""; Process.Start(info);
        });
        delete.Click += async (_, _) => await DeleteSelectedAsync();
        timer.Tick += async (_, _) =>
        {
            UpdateOverlay();
            if (recorder is null || busy) return;
            status.Text = $"● Recording  {clock.Elapsed:hh\\:mm\\:ss} · Click Stop & save when finished.";
            if (recorder.HasExited) await StopRecording();
        };
        Shown += async (_, _) => { timer.Start(); await RefreshLibraryAsync(); };
        FormClosing += async (_, e) =>
        {
            if (closing) return;
            if (busy) { e.Cancel = true; return; }
            overlay.Hide();
            if (recorder is not null) { e.Cancel = true; await StopRecording(); closing = true; Close(); return; }
        };
        FormClosed += (_, _) => SaveSettingsOnExit();
        SetMode(Math.Clamp(settings.CaptureMode, 0, 2)); UpdateFormat(); UpdateLibraryActions();
        status.Text = "Ready · Choose a source, then click Record.";
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitle(this); }
    private Control BuildHeader()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = new(0) };
        header.ColumnStyles.Add(new(SizeType.Absolute, 62)); header.ColumnStyles.Add(new(SizeType.Percent, 100)); header.ColumnStyles.Add(new(SizeType.Absolute, 106));
        header.RowStyles.Add(new(SizeType.Absolute, 38)); header.RowStyles.Add(new(SizeType.Absolute, 26));
        var logo = new PictureBox { Image = Theme.Logo(48), Size = new(48, 48), SizeMode = PictureBoxSizeMode.Zoom, Margin = new(0, 5, 0, 0) };
        header.Controls.Add(logo, 0, 0); header.SetRowSpan(logo, 2);
        var title = Label("B01 Recorder"); title.Font = new("Segoe UI Semibold", 23); header.Controls.Add(title, 1, 0);
        header.Controls.Add(Label("Choose a source. Record. Review.", true), 1, 1);
        badge.BackColor = Theme.AccentSurface; badge.ForeColor = Theme.Accent; badge.Padding = new(10, 7, 10, 7); badge.AutoSize = true;
        header.Controls.Add(badge, 2, 0); return header;
    }
    private Control BuildLibrary()
    {
        var card = new Card { Dock = DockStyle.Fill, Margin = new(0, 0, 20, 0), Padding = new(16) };
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = new(0) };
        content.RowStyles.Add(new(SizeType.Absolute, 34)); content.RowStyles.Add(new(SizeType.Percent, 58));
        content.RowStyles.Add(new(SizeType.Absolute, 30)); content.RowStyles.Add(new(SizeType.Absolute, 46)); content.RowStyles.Add(new(SizeType.Percent, 42));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new(0) };
        heading.ColumnStyles.Add(new(SizeType.Percent, 100)); heading.ColumnStyles.Add(new(SizeType.Absolute, 60));
        heading.Controls.Add(Label("RECENT RECORDINGS", true), 0, 0); heading.Controls.Add(countLabel, 1, 0);
        content.Controls.Add(heading, 0, 0); content.Controls.Add(preview, 0, 1);
        fileLabel.Dock = DockStyle.Fill; fileLabel.AutoEllipsis = true; fileLabel.AutoSize = false; fileLabel.TextAlign = ContentAlignment.MiddleLeft;
        content.Controls.Add(fileLabel, 0, 2); content.Controls.Add(Row(play, external, reveal, delete), 0, 3); content.Controls.Add(gallery, 0, 4);
        card.Controls.Add(content); return card;
    }
    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new(0, 16, 0, 0), Margin = new(0) };
        footer.ColumnStyles.Add(new(SizeType.Percent, 100)); footer.ColumnStyles.Add(new(SizeType.Absolute, 156)); footer.RowStyles.Add(new(SizeType.Percent, 100));
        status.AutoSize = false; status.AutoEllipsis = true; status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft;
        footer.Controls.Add(status, 0, 0); footer.Controls.Add(Row(start), 1, 0); return footer;
    }
    private void BuildSettings()
    {
        var modes = EqualRow(modeButtons);
        for (var i = 0; i < modeButtons.Length; i++) { var index = i; modeButtons[i].Radio = true; modeButtons[i].AccessibleRole = AccessibleRole.RadioButton; modeButtons[i].Click += (_, _) => SetMode(index); }
        targets.Dock = DockStyle.Fill;
        var picker = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new(0) };
        picker.ColumnStyles.Add(new(SizeType.Percent, 100)); picker.ColumnStyles.Add(new(SizeType.Absolute, 98)); picker.RowStyles.Add(new(SizeType.Percent, 100));
        picker.Controls.Add(targets, 0, 0); picker.Controls.Add(select, 1, 0);
        targetLabel.AutoSize = false; targetLabel.AutoEllipsis = true; targetLabel.Dock = DockStyle.Fill;
        AddCard("CAPTURE SOURCE", 152, Stack((modes, 36), (picker, 34), (targetLabel, 25)));

        preset.Width = 228; fps.Width = 76;
        AddCard("FRAME RATE", 88, Row(preset, fps, Label("FPS", true)));
        format.Width = 124;
        quality.Dock = DockStyle.Fill;
        qualityLabel.AutoSize = false; qualityLabel.Dock = DockStyle.Fill;
        AddCard("OUTPUT & QUALITY", 140, Stack((Row(format, Label("MP4  /  GIF  /  WEBP", true)), 35), (qualityLabel, 22), (quality, 28)));

        var browse = new DarkButton("Choose folder") { Width = 88 };
        var open = new DarkButton("Open folder") { Width = 88 };
        AddCard("OUTPUT FOLDER", 116, Stack((folder, 28), (Row(browse, open), 38)));
        browse.Click += async (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { InitialDirectory = folder.Text, Description = "Recording output folder" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            folder.Text = dialog.SelectedPath; await RefreshLibraryAsync();
        };
        open.Click += (_, _) => SafeAction(() => { Directory.CreateDirectory(folder.Text); Process.Start(new ProcessStartInfo(folder.Text) { UseShellExecute = true }); });
        AddCard("AUDIO & CURSOR", 78, Row(audio, cursor));
    }
    private void AddCard(string title, int height, Control content)
    {
        var card = new Card { Height = height, Width = 442, Margin = new(0, 0, 0, 10), Padding = new(14) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new(0) };
        layout.RowStyles.Add(new(SizeType.Absolute, 24)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.Controls.Add(Label(title, true), 0, 0); content.Dock = DockStyle.Fill; layout.Controls.Add(content, 0, 1); card.Controls.Add(layout); settingsPanel.Controls.Add(card);
    }
    private static TableLayoutPanel Stack(params (Control control, int height)[] rows)
    {
        var result = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows.Length, Margin = new(0) };
        for (var i = 0; i < rows.Length; i++) { result.RowStyles.Add(new(SizeType.Absolute, rows[i].height)); rows[i].control.Dock = DockStyle.Fill; result.Controls.Add(rows[i].control, 0, i); }
        return result;
    }
    private static TableLayoutPanel EqualRow(DarkButton[] buttons)
    {
        var result = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = buttons.Length, RowCount = 1, Margin = new(0) };
        result.RowStyles.Add(new(SizeType.Percent, 100));
        for (var i = 0; i < buttons.Length; i++) { result.ColumnStyles.Add(new(SizeType.Percent, 100f / buttons.Length)); buttons[i].Dock = DockStyle.Fill; buttons[i].Margin = new(i == 0 ? 0 : 5, 0, 0, 0); result.Controls.Add(buttons[i], i, 0); }
        return result;
    }
    private static FlowLayoutPanel Row(params Control[] children)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new(0) }; row.Controls.AddRange(children); return row;
    }
    private static Label Label(string text, bool muted = false) => new() { Text = text, UseMnemonic = false, ForeColor = muted ? Theme.Muted : Theme.Text, AutoSize = true, Margin = new(0, 2, 8, 0) };
    private static CheckBox Check(string text) => new DarkCheck { Text = text, ForeColor = Theme.Text, BackColor = Theme.Surface, Margin = new(0, 3, 16, 0) };
    private void SetMode(int mode)
    {
        captureMode = mode; target = null; overlay.Hide();
        for (var i = 0; i < modeButtons.Length; i++) { modeButtons[i].Selected = i == mode; modeButtons[i].Invalidate(); }
        targets.Enabled = mode != 2; select.Text = mode == 2 ? "Select region" : "Refresh";
        if (mode == 2) { targets.Items.Clear(); targets.Items.Add("Drag to select a region"); targets.SelectedIndex = 0; UpdateTarget(); }
        else RefreshTargets();
    }
    private void RefreshTargets()
    {
        targets.Items.Clear();
        if (captureMode == 0)
        {
            targets.Items.Add(new CaptureTarget("All monitors", SystemInformation.VirtualScreen));
            foreach (var screen in Screen.AllScreens) targets.Items.Add(new CaptureTarget($"{screen.DeviceName} · {screen.Bounds.Width} × {screen.Bounds.Height}" + (screen.Primary ? " · primary" : ""), screen.Bounds));
        }
        else foreach (var window in Windows.List(Handle)) targets.Items.Add(window);
        if (targets.Items.Count > 0) targets.SelectedIndex = 0; else { target = null; UpdateTarget(); }
    }
    private void PickRegion()
    {
        picking = true; overlay.Hide();
        try
        {
            using var picker = new RegionPicker();
            if (picker.ShowDialog(this) == DialogResult.OK) target = new("Selected region", picker.SelectedBounds);
        }
        finally { picking = false; Activate(); }
        UpdateTarget();
    }
    private void UpdateTarget()
    {
        var bounds = target?.Bounds ?? Rectangle.Empty;
        if (target?.IsWindow == true) Windows.TryGetClientBounds(target.Handle, out bounds);
        targetLabel.Text = target is null ? "Select a region. Recording starts only when you click Record." : $"{bounds.Width} × {bounds.Height}  ·  ({bounds.X}, {bounds.Y})  ·  {target.Label}";
        start.Text = recorder is null ? "●  Record" : "■  Stop & save";
        start.Enabled = !busy && (recorder is not null || target is not null);
        UpdateOverlay();
    }
    private void UpdateOverlay() => overlay.Update(target, captureMode != 0 && !picking && !closing && WindowState != FormWindowState.Minimized);
    private void UpdateFormat()
    {
        var animation = format.Text != "MP4"; audio.Enabled = !animation; quality.Enabled = animation;
        qualityLabel.Text = animation ? $"Quality {quality.Value}% · Higher means sharper" : "MP4 · High-quality H.264 / audio supported";
    }
    private async Task StartRecording()
    {
        if (target is null || busy || recorder is not null) return;
        busy = true; SetRecordingUi(true); status.Text = "Starting recording…";
        try
        {
            await preview.PauseAsync(); recorder = new(Recorder.ResolveFfmpeg(settings.Ffmpeg));
            await recorder.StartAsync(new(target, (int)fps.Value, format.Text, quality.Value, audio.Checked && format.Text == "MP4", cursor.Checked, folder.Text));
            clock.Restart(); badge.Text = "●  REC"; badge.ForeColor = Color.FromArgb(255, 125, 145);
            status.Text = "● Recording · Click Stop & save when finished.";
        }
        catch (Exception ex)
        {
            var recovery = recorder?.RecoveryFolder; recorder?.Dispose(); recorder = null; ShowError(ex, recovery); SetRecordingUi(false);
        }
        finally { busy = false; UpdateTarget(); }
    }
    private async Task StopRecording()
    {
        if (recorder is null || busy) return;
        busy = true; clock.Stop(); start.Enabled = false; badge.Text = "●  SAVING"; badge.ForeColor = Theme.Accent;
        status.Text = "Saving… GIF / WebP encoding may take a moment.";
        var current = recorder; string? file = null;
        try
        {
            file = await current.StopAsync();
            settings.Recordings = settings.Recordings.Prepend(file).Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList();
            status.Text = "Saved · " + Path.GetFileName(file);
        }
        catch (Exception ex) { ShowError(ex, current.RecoveryFolder); }
        finally
        {
            current.Dispose(); recorder = null; busy = false; SetRecordingUi(false); UpdateTarget();
        }
        if (file is not null && !closing) await RefreshLibraryAsync(file);
    }
    private void SetRecordingUi(bool recording)
    {
        settingsPanel.Enabled = !recording; gallery.Enabled = !recording; preview.Enabled = !recording; UpdateTarget();
        if (!recording) { badge.Text = "●  READY"; badge.ForeColor = Theme.Accent; }
        UpdateLibraryActions();
    }
    private void UpdateLibraryActions()
    {
        var enabled = selectedFile is not null && File.Exists(selectedFile) && !busy && recorder is null;
        play.Enabled = external.Enabled = reveal.Enabled = delete.Enabled = enabled;
    }
    private async Task RefreshLibraryAsync(string? preferred = null)
    {
        if (IsDisposed || Disposing) return;
        libraryRefresh?.Cancel(); libraryRefresh?.Dispose(); libraryRefresh = new(); var cancellation = libraryRefresh.Token;
        foreach (var old in gallery.Controls.Cast<Control>().ToArray()) { gallery.Controls.Remove(old); old.Dispose(); }
        try
        {
            var files = RecordingLibrary.List(folder.Text, settings.Recordings); countLabel.Text = $"{files.Count} files";
            var cards = new List<(string file, RecordingCard card)>();
            foreach (var file in files)
            {
                var card = new RecordingCard(file); card.Chosen += async (_, _) => { if (recorder is null && !busy) await SelectFileAsync(file); };
                gallery.Controls.Add(card); cards.Add((file, card));
            }
            if (files.Count == 0) gallery.Controls.Add(new Label { Text = "No recordings yet.\nYour recordings will appear here after saving.", AutoSize = true, ForeColor = Theme.Muted, Margin = new(8, 20, 8, 8) });
            var next = preferred ?? (files.Contains(selectedFile) ? selectedFile : files.FirstOrDefault());
            if (next is not null) await SelectFileAsync(next); else { selectedFile = null; fileLabel.Text = "No recording selected"; await preview.ClearAsync(); UpdateLibraryActions(); }
            string ffmpeg; try { ffmpeg = Recorder.ResolveFfmpeg(settings.Ffmpeg); } catch { return; }
            foreach (var (file, card) in cards)
            {
                if (cancellation.IsCancellationRequested || IsDisposed) return;
                try
                {
                    var image = await RecordingLibrary.ThumbnailAsync(ffmpeg, file, cancellation);
                    if (image is not null && !cancellation.IsCancellationRequested && !card.IsDisposed) card.SetThumbnail(RecordingLibrary.LoadThumbnail(image));
                }
                catch (IOException) { }
            }
        }
        catch (Exception ex) { if (!cancellation.IsCancellationRequested && !IsDisposed) status.Text = "Could not load recordings: " + ex.Message; }
    }
    private async Task SelectFileAsync(string file)
    {
        selectedFile = file; fileLabel.Text = Path.GetFileName(file); UpdateLibraryActions();
        foreach (var card in gallery.Controls.OfType<RecordingCard>()) { card.BorderColor = card.FilePath == file ? Theme.Accent : Theme.Border; card.Invalidate(); }
        await preview.LoadAsync(file);
    }
    private async Task DeleteSelectedAsync()
    {
        var file = selectedFile; if (file is null || recorder is not null || busy) return;
        if (DarkDialog.Show(this, "Move this recording to the Recycle Bin?\n\n" + Path.GetFileName(file), true) != DialogResult.Yes) return;
        try
        {
            await preview.ClearAsync(); RecordingLibrary.Trash(file); selectedFile = null;
            settings.Recordings.RemoveAll(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase));
            status.Text = "Recording moved to the Recycle Bin."; await RefreshLibraryAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void SaveSettingsOnExit()
    {
        if (settingsSaved || !persistSettings) return;
        settingsSaved = true; settings.CaptureMode = captureMode;
        settings.Folder = folder.Text; settings.Format = format.Text; settings.Fps = (int)fps.Value;
        settings.Quality = quality.Value; settings.Audio = audio.Checked; settings.Cursor = cursor.Checked;
        try { settings.Save(); } catch (Exception ex) { DarkDialog.Show(this, "Could not save setting.ini: " + ex.Message); }
    }
    private void SafeAction(Action action) { try { action(); } catch (Exception ex) { ShowError(ex); } }
    private void ShowError(Exception ex, string? recovery = null)
    {
        status.Text = "Error · Check your settings and try again.";
        DarkDialog.Show(this, ex.Message + (string.IsNullOrEmpty(recovery) ? "" : "\n\nRecovery folder: " + recovery));
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !released) { released = true; timer.Dispose(); overlay.Dispose(); libraryRefresh?.Cancel(); libraryRefresh?.Dispose(); libraryRefresh = null; recorder?.Dispose(); recorder = null; }
        base.Dispose(disposing);
    }
}

internal sealed class RecordingCard : Card
{
    private readonly PictureBox image = new() { Dock = DockStyle.Top, Height = 100, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Background };
    public string FilePath { get; }
    public event EventHandler? Chosen;
    public RecordingCard(string file)
    {
        FilePath = file; Size = new(190, 157); Padding = new(8); Margin = new(0, 6, 10, 4); Cursor = Cursors.Hand;
        image.Image = Theme.Logo(48);
        var name = new Label { Text = Path.GetFileName(file), Dock = DockStyle.Top, Height = 21, ForeColor = Theme.Text, AutoEllipsis = true };
        var info = new FileInfo(file);
        var metadata = new Label { Text = $"{info.LastWriteTime:MM.dd HH:mm} · {info.Length / 1048576.0:0.#} MB · {info.Extension.TrimStart('.').ToUpperInvariant()}", Dock = DockStyle.Top, Height = 18, ForeColor = Theme.Muted, Font = new("Segoe UI", 8), AutoEllipsis = true };
        Controls.Add(metadata); Controls.Add(name); Controls.Add(image);
        foreach (Control control in Controls) control.Click += (_, _) => Chosen?.Invoke(this, EventArgs.Empty);
        Click += (_, _) => Chosen?.Invoke(this, EventArgs.Empty);
    }
    public void SetThumbnail(Image bitmap) { var old = image.Image; image.Image = bitmap; old?.Dispose(); }
    protected override void Dispose(bool disposing) { if (disposing) image.Image?.Dispose(); base.Dispose(disposing); }
}
