using System.Diagnostics;
using static B01Recorder.Ui;

namespace B01Recorder;

internal sealed class AnimationEditor : Form
{
    private readonly Recorder recorder;
    private readonly CancellationTokenSource lifetime = new();
    private readonly FrameStrip thumbnails = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private const int HandleRadius = 4;
    private const int PreviewInset = HandleRadius + 2;
    private readonly PictureBox preview = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    private readonly FrameSlider timeline = new(scrollBar: false) { Dock = DockStyle.Fill, AccessibleName = T("Current frame", "현재 프레임"), Margin = Padding.Empty };
    private readonly QualitySlider quality = new() { Size = new(120, 36), AccessibleName = T("Export quality", "저장 품질"), Margin = new(0, 0, 8, 0) };
    private readonly Label qualityLabel = Caption("");
    private readonly Label sizeLabel = Caption(T("Preparing preview…", "미리보기 준비 중…"));
    private readonly Label frameLabel = Caption("");
    private readonly DarkButton save = Button(T("Save…", "저장…"), 96, glyph: "Floppy");
    private readonly DarkButton cancel = Button(T("Cancel", "취소"), 76);
    private readonly DarkButton play = Button("", 40, T("Play", "재생"), "Play");
    private readonly DarkButton resetCrop = Button(T("Reset crop", "크롭 초기화"), 108);
    private readonly ToolTip hints = new();
    private readonly System.Windows.Forms.Timer playback = new() { Interval = 16 };
    private readonly Stopwatch playClock = new();
    private readonly TableLayoutPanel layout = new() { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1 };
    private readonly FlowLayoutPanel transport = Row();
    private AnimationPreview? source;
    private Bitmap? previewFrame;
    private Rectangle crop;
    private Rectangle dragRectangle;
    private Point dragOrigin;
    private CropEdge dragEdges;
    private int firstFrame;
    private int lastFrame;
    private int playFrom;
    private bool saving;
    private Task initialize = Task.CompletedTask;
    private Task<string?> estimate = Task.FromResult<string?>(null);
    private CancellationTokenSource? estimateCancellation;
    private string? encodeError;
    public string? SavedPath { get; private set; }
    public bool PreviewFailed { get; private set; }
    public int Quality => quality.Value;

    public AnimationEditor(Recorder recorder, int initialQuality)
    {
        this.recorder = recorder;
        Text = T($"Edit {recorder.Format} recording", $"{recorder.Format} 녹화 편집");
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        ClientSize = new(912, 672);
        MinimumSize = new(768, 544);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MaximizeBox = true;
        quality.Value = initialQuality;
        save.Primary = true;
        save.Enabled = quality.Enabled = timeline.Enabled = false;
        transport.Enabled = false;
        CancelButton = cancel;
        cancel.Click += (_, _) => Close();
        save.Click += async (_, _) => await SaveAsync();
        quality.ValueChanged += (_, _) => { UpdateQualityLabel(); ScheduleEstimate(); };
        UpdateQualityLabel();

        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        header.ColumnStyles.Add(new(SizeType.Percent, 100));
        header.ColumnStyles.Add(new(SizeType.Absolute, 192));
        var exportOptions = Row();
        qualityLabel.AutoSize = false;
        qualityLabel.Size = new(104, 36);
        qualityLabel.Margin = new(0, 0, 8, 0);
        qualityLabel.TextAlign = ContentAlignment.MiddleLeft;
        exportOptions.Controls.AddRange([qualityLabel, quality, sizeLabel]);
        sizeLabel.AutoEllipsis = true;
        sizeLabel.AutoSize = false;
        sizeLabel.Size = new(212, 20);
        var actions = Row();
        actions.FlowDirection = FlowDirection.RightToLeft;
        actions.Controls.AddRange([save, cancel]);
        header.Controls.Add(exportOptions, 0, 0);
        header.Controls.Add(actions, 1, 0);
        AddRow(header, 44);
        preview.Margin = Padding.Empty;
        AddRow(preview, 0);

        var first = Button("", 40, T("First selected frame", "선택 구간의 처음"), "BackwardStep");
        var last = Button("", 40, T("Last selected frame", "선택 구간의 끝"), "ForwardStep");
        var setStart = Button(T("Set start", "시작 지정"), 84);
        var setEnd = Button(T("Set end", "끝 지정"), 84);
        hints.SetToolTip(first, first.AccessibleName);
        hints.SetToolTip(last, last.AccessibleName);
        hints.SetToolTip(play, play.AccessibleName);
        first.Click += (_, _) => Seek(firstFrame);
        last.Click += (_, _) => Seek(lastFrame);
        play.Click += (_, _) => { if (playClock.IsRunning) StopPlayback(); else Play(); };
        setStart.Click += (_, _) => SetTrim(timeline.Value, Math.Max(timeline.Value, lastFrame));
        setEnd.Click += (_, _) => SetTrim(Math.Min(firstFrame, timeline.Value), timeline.Value);
        transport.Padding = new(0, 8, 0, 0);
        resetCrop.Click += (_, _) => SetCrop(new Rectangle(Point.Empty, source!.SourceSize));
        transport.Controls.AddRange([first, play, last, setStart, setEnd, resetCrop, frameLabel]);
        frameLabel.AutoSize = false;
        frameLabel.AutoEllipsis = true;
        frameLabel.TextAlign = ContentAlignment.MiddleLeft;
        frameLabel.Height = 36;
        frameLabel.Margin = Padding.Empty;
        transport.SizeChanged += (_, _) => frameLabel.Width = Math.Max(1, transport.ClientSize.Width -
            transport.Controls.Cast<Control>().Where(control => control != frameLabel).Sum(control => control.Width + control.Margin.Horizontal));
        frameLabel.MouseEnter += (_, _) => hints.SetToolTip(frameLabel, frameLabel.Text);
        AddRow(transport, 44);
        AddRow(timeline, 28);
        AddRow(thumbnails, 84);
        Controls.Add(layout);

        timeline.ValueChanged += (_, _) => ShowFrame();
        timeline.UserScroll += (_, _) => StopPlayback();
        thumbnails.FrameSelected += Seek;
        playback.Tick += (_, _) => AdvancePlayback();
        preview.Paint += PaintCrop;
        preview.MouseDown += StartCrop;
        preview.MouseMove += DragCrop;
        preview.MouseUp += FinishCrop;
        preview.MouseCaptureChanged += (_, _) => { dragEdges = CropEdge.None; preview.Invalidate(); };
        Shown += (_, _) => initialize = InitializeAsync();
        FormClosing += (_, e) => e.Cancel = saving;
        FormClosed += (_, _) => { StopPlayback(); lifetime.Cancel(); estimateCancellation?.Cancel(); };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitle(this);
    }

    public async Task DrainAsync()
    {
        await initialize;
        await estimate;
    }

    private async Task InitializeAsync()
    {
        try
        {
            var prepared = await recorder.PrepareAnimationAsync(lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            source = prepared;
            lastFrame = source.FrameCount - 1;
            timeline.Maximum = lastFrame;
            timeline.PageStep = Math.Max(1, source.FrameCount / 20);
            crop = new Rectangle(Point.Empty, source.SourceSize);
            thumbnails.LoadFrames(source);
            ShowFrame();
            quality.Enabled = transport.Enabled = save.Enabled = true;
            timeline.Enabled = lastFrame > 0;
            ScheduleEstimate();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            sizeLabel.Text = T("Could not prepare preview", "미리보기를 열지 못했습니다.");
            DarkDialog.Show(this, ex.Message + T("\n\nRecovery folder: ", "\n\n복구 폴더: ") + recorder.RecoveryFolder);
            PreviewFailed = true;
            Close();
        }
    }

    private void ShowFrame()
    {
        if (source is null) return;
        var oldImage = previewFrame;
        previewFrame = source.LoadFrame(timeline.Value);
        oldImage?.Dispose();
        preview.Invalidate();
        UpdateFrameLabel();
        thumbnails.ShowFrame(timeline.Value);
    }

    private void UpdateFrameLabel()
    {
        var duration = (lastFrame - firstFrame + 1) / (double)recorder.Fps;
        frameLabel.Text = T("Frame", "프레임") + $" {timeline.Value + 1}/{source!.FrameCount}  ·  "
            + T("Keep", "구간") + $" {firstFrame + 1}–{lastFrame + 1}  ·  {duration:0.00}" + T("s", "초");
    }

    private void SetTrim(int first, int last)
    {
        StopPlayback();
        firstFrame = first;
        lastFrame = last;
        UpdateFrameLabel();
        thumbnails.SetTrim(first, last);
        ScheduleEstimate();
    }

    private void Seek(int index)
    {
        StopPlayback();
        timeline.Value = index;
    }

    private void Play()
    {
        if (playClock.IsRunning) return;
        playFrom = timeline.Value >= lastFrame ? firstFrame : Math.Max(firstFrame, timeline.Value);
        timeline.Value = playFrom;
        playClock.Restart();
        playback.Start();
        SetPlaying(true);
    }

    private void AdvancePlayback()
    {
        var frame = playFrom + (long)(playClock.Elapsed.TotalSeconds * recorder.Fps);
        var length = lastFrame - firstFrame + 1;
        timeline.Value = firstFrame + (int)((frame - firstFrame) % length);
    }

    private void StopPlayback()
    {
        playback.Stop();
        playClock.Reset();
        SetPlaying(false);
    }

    private void SetPlaying(bool playing)
    {
        play.Glyph = playing ? "Pause" : "Play";
        play.AccessibleName = playing ? T("Pause", "일시정지") : T("Play", "재생");
        hints.SetToolTip(play, play.AccessibleName);
        play.Invalidate();
    }

    private void UpdateQualityLabel() => qualityLabel.Text = T("Quality", "품질") + $" {quality.Value}%";

    private void ScheduleEstimate()
    {
        if (source is null) return;
        estimateCancellation?.Cancel();
        var pending = new CancellationTokenSource();
        estimateCancellation = pending;
        encodeError = null;
        sizeLabel.Text = T("Calculating size…", "용량 계산 중…");
        estimate = EstimateAsync(estimate, new(firstFrame, lastFrame, crop, quality.Value), source.SourceSize, pending);
    }

    private async Task<string?> EstimateAsync(Task<string?> previous, AnimationEdit edit, Size sourceSize, CancellationTokenSource pending)
    {
        try
        {
            await previous;
            await Task.Delay(275, pending.Token);
            var path = await recorder.EncodeAnimationAsync(edit, sourceSize, pending.Token);
            pending.Token.ThrowIfCancellationRequested();
            var bytes = new FileInfo(path).Length;
            var size = bytes < 1024 ? $"{bytes:N0} B" : bytes < 1048576 ? $"{bytes / 1024d:0.0} KB" : $"{bytes / 1048576d:0.00} MB";
            sizeLabel.Text = T("Estimated size ", "예상 용량 ") + size;
            return path;
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { return null; }
        catch (Exception ex)
        {
            if (!pending.IsCancellationRequested)
            {
                encodeError = ex.Message;
                sizeLabel.Text = T("Could not estimate size", "용량을 계산하지 못했습니다.");
            }
            return null;
        }
        finally
        {
            if (ReferenceEquals(estimateCancellation, pending)) estimateCancellation = null;
            pending.Dispose();
        }
    }

    private async Task SaveAsync()
    {
        StopPlayback();
        var suggested = recorder.SuggestedPath;
        var extension = recorder.Format.ToLowerInvariant();
        using var dialog = new SaveFileDialog { Title = T("Save recording", "녹화 저장"), InitialDirectory = Path.GetDirectoryName(suggested),
            FileName = Path.GetFileName(suggested), DefaultExt = extension, Filter = $"{recorder.Format} (*.{extension})|*.{extension}",
            AddExtension = true, OverwritePrompt = true, RestoreDirectory = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        saving = true;
        layout.Enabled = false;
        var staged = Path.Combine(Path.GetDirectoryName(dialog.FileName)!, $".b01-save-{Guid.NewGuid():N}.tmp");
        try
        {
            if (encodeError is not null) ScheduleEstimate();
            var encoded = await estimate ?? throw new InvalidOperationException(encodeError ?? T("The recording could not be encoded.", "녹화 파일을 인코딩하지 못했습니다."));
            var estimateText = sizeLabel.Text;
            sizeLabel.Text = T("Saving…", "저장 중…");
            await Task.Run(() =>
            {
                File.Copy(encoded, staged);
                File.Move(staged, dialog.FileName, true);
            });
            SavedPath = dialog.FileName;
            sizeLabel.Text = estimateText;
        }
        catch (Exception ex)
        {
            if (sizeLabel.Text == T("Saving…", "저장 중…")) sizeLabel.Text = T("Save failed", "저장에 실패했습니다.");
            DarkDialog.Show(this, ex.Message);
        }
        finally
        {
            if (File.Exists(staged))
            {
                try { File.Delete(staged); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            saving = false;
            layout.Enabled = true;
        }
        if (SavedPath is not null) Close();
    }

    private void SetCrop(Rectangle bounds)
    {
        if (crop == bounds) return;
        crop = bounds;
        preview.Invalidate();
        ScheduleEstimate();
    }

    private Rectangle ImageBounds()
    {
        var image = previewFrame!;
        var scale = Math.Min(Math.Max(1, preview.Width - 2 * PreviewInset) / (double)image.Width,
            Math.Max(1, preview.Height - 2 * PreviewInset) / (double)image.Height);
        var width = Math.Max(1, (int)(image.Width * scale));
        var height = Math.Max(1, (int)(image.Height * scale));
        return new((preview.Width - width) / 2, (preview.Height - height) / 2, width, height);
    }

    private RectangleF CropBounds(Rectangle selection)
    {
        var image = ImageBounds();
        var size = source!.SourceSize;
        return new(image.X + selection.X * image.Width / (float)size.Width, image.Y + selection.Y * image.Height / (float)size.Height,
            selection.Width * image.Width / (float)size.Width, selection.Height * image.Height / (float)size.Height);
    }

    [Flags]
    private enum CropEdge { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8, Move = 16 }

    private static (CropEdge Edges, PointF Point)[] CropHandles(RectangleF bounds)
    {
        var x = bounds.Left + bounds.Width / 2;
        var y = bounds.Top + bounds.Height / 2;
        return
        [
            (CropEdge.Left | CropEdge.Top, new(bounds.Left, bounds.Top)), (CropEdge.Top, new(x, bounds.Top)),
            (CropEdge.Right | CropEdge.Top, new(bounds.Right, bounds.Top)), (CropEdge.Right, new(bounds.Right, y)),
            (CropEdge.Right | CropEdge.Bottom, new(bounds.Right, bounds.Bottom)), (CropEdge.Bottom, new(x, bounds.Bottom)),
            (CropEdge.Left | CropEdge.Bottom, new(bounds.Left, bounds.Bottom)), (CropEdge.Left, new(bounds.Left, y))
        ];
    }

    private CropEdge HitCrop(Point point)
    {
        var bounds = CropBounds(crop);
        var nearest = CropEdge.None;
        var distance = 64f;
        foreach (var (edges, center) in CropHandles(bounds))
        {
            var dx = center.X - point.X;
            var dy = center.Y - point.Y;
            var candidate = dx * dx + dy * dy;
            if (candidate >= distance) continue;
            distance = candidate;
            nearest = edges;
        }
        return nearest == CropEdge.None && bounds.Contains(point) ? CropEdge.Move : nearest;
    }

    private void StartCrop(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || previewFrame is null) return;
        var edges = HitCrop(e.Location);
        if (edges == CropEdge.None) return;
        StopPlayback();
        dragEdges = edges;
        dragOrigin = e.Location;
        dragRectangle = crop;
        preview.Capture = true;
    }

    private void DragCrop(object? sender, MouseEventArgs e)
    {
        if (previewFrame is null) return;
        if (dragEdges == CropEdge.None)
        {
            preview.Cursor = HitCrop(e.Location) switch
            {
                CropEdge.Left or CropEdge.Right => Cursors.SizeWE,
                CropEdge.Top or CropEdge.Bottom => Cursors.SizeNS,
                (CropEdge.Left | CropEdge.Top) or (CropEdge.Right | CropEdge.Bottom) => Cursors.SizeNWSE,
                (CropEdge.Right | CropEdge.Top) or (CropEdge.Left | CropEdge.Bottom) => Cursors.SizeNESW,
                CropEdge.Move => Cursors.SizeAll,
                _ => Cursors.Default
            };
            return;
        }
        var image = ImageBounds();
        if (image.Width == 0 || image.Height == 0) return;
        var size = source!.SourceSize;
        var dx = (int)Math.Round((e.X - dragOrigin.X) * (double)size.Width / image.Width);
        var dy = (int)Math.Round((e.Y - dragOrigin.Y) * (double)size.Height / image.Height);
        if (dragEdges == CropEdge.Move)
            dragRectangle = new Rectangle(Math.Clamp(crop.X + dx, 0, size.Width - crop.Width),
                Math.Clamp(crop.Y + dy, 0, size.Height - crop.Height), crop.Width, crop.Height);
        else
            dragRectangle = Rectangle.FromLTRB(
                dragEdges.HasFlag(CropEdge.Left) ? Math.Clamp(crop.Left + dx, 0, crop.Right - 1) : crop.Left,
                dragEdges.HasFlag(CropEdge.Top) ? Math.Clamp(crop.Top + dy, 0, crop.Bottom - 1) : crop.Top,
                dragEdges.HasFlag(CropEdge.Right) ? Math.Clamp(crop.Right + dx, crop.Left + 1, size.Width) : crop.Right,
                dragEdges.HasFlag(CropEdge.Bottom) ? Math.Clamp(crop.Bottom + dy, crop.Top + 1, size.Height) : crop.Bottom);
        preview.Invalidate();
    }

    private void FinishCrop(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || dragEdges == CropEdge.None) return;
        DragCrop(sender, e);
        dragEdges = CropEdge.None;
        preview.Capture = false;
        SetCrop(dragRectangle);
        DragCrop(sender, e);
    }

    private void PaintCrop(object? sender, PaintEventArgs e)
    {
        e.Graphics.FillRectangle(Brushes.Black, Rectangle.Inflate(preview.ClientRectangle, -PreviewInset, -PreviewInset));
        if (previewFrame is null || source is null) return;
        var box = ImageBounds();
        e.Graphics.DrawImage(previewFrame, box);
        var kept = CropBounds(dragEdges == CropEdge.None ? crop : dragRectangle);
        using var outside = new Region(box);
        outside.Exclude(kept);
        using var shade = new SolidBrush(Color.FromArgb(155, Color.Black));
        using var line = new Pen(Theme.Accent);
        e.Graphics.FillRegion(shade, outside);
        e.Graphics.DrawRectangle(line, kept.X, kept.Y, kept.Width, kept.Height);
        using var handle = new SolidBrush(Theme.Accent);
        foreach (var (_, point) in CropHandles(kept))
        {
            var dot = new RectangleF(point.X - HandleRadius, point.Y - HandleRadius, 2 * HandleRadius, 2 * HandleRadius);
            e.Graphics.FillEllipse(handle, dot);
            e.Graphics.DrawEllipse(Pens.Black, dot);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            playback.Dispose();
            lifetime.Dispose();
            hints.Dispose();
            previewFrame?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void AddRow(Control control, int height)
    {
        var row = layout.RowCount++;
        layout.RowStyles.Add(height == 0 ? new(SizeType.Percent, 100) : new(SizeType.Absolute, height));
        layout.Controls.Add(control, 0, row);
    }

    private static DarkButton Button(string text, int width, string? name = null, string? glyph = null) =>
        new(text) { Width = width, Height = 36, AccessibleName = name ?? text, Glyph = glyph };

    private static FlowLayoutPanel Row() => new() { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };

    private static Label Caption(string text) =>
        new() { Text = text, AutoSize = true, ForeColor = Theme.Text, Margin = new(0, 8, 8, 0), UseMnemonic = false };
}

internal sealed class FrameStrip : Control
{
    private const int CardWidth = 84;
    private const int CardHeight = 60;
    private const int Pitch = 92;
    private readonly FrameSlider scroll = new(scrollBar: true) { Dock = DockStyle.Bottom, Height = 16, AccessibleName = T("Scroll frames horizontally", "프레임 가로 스크롤") };
    private readonly Dictionary<int, Image> visibleFrames = [];
    private AnimationPreview? source;
    private int current;
    private int first;
    private int last;
    private int VisibleCount => Math.Max(1, (ClientSize.Width + Pitch - CardWidth) / Pitch);
    private int LastOffset => Math.Max(0, (source?.FrameCount ?? 0) - VisibleCount);
    public event Action<int>? FrameSelected;

    public FrameStrip()
    {
        BackColor = Theme.Background;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        AccessibleName = T("Frame thumbnails", "프레임 썸네일");
        scroll.ValueChanged += (_, _) => Invalidate();
        Controls.Add(scroll);
    }

    public void LoadFrames(AnimationPreview preview)
    {
        source = preview;
        last = preview.FrameCount - 1;
        UpdateScrollRange();
    }

    public void ShowFrame(int index)
    {
        current = index;
        if (index < scroll.Value) scroll.Value = index;
        else if (index - scroll.Value >= VisibleCount) scroll.Value = index - VisibleCount + 1;
        Invalidate();
    }

    public void SetTrim(int start, int end)
    {
        first = start;
        last = end;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScrollRange();
    }

    private void UpdateScrollRange()
    {
        if (source is null) { scroll.Enabled = false; return; }
        scroll.Maximum = LastOffset;
        scroll.PageStep = VisibleCount;
        scroll.Enabled = LastOffset > 0;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (source is null) return;
        var start = scroll.Value;
        var end = Math.Min(start + VisibleCount, source.FrameCount);
        foreach (var index in visibleFrames.Keys.Where(i => i < start || i >= end).ToArray())
        {
            visibleFrames[index].Dispose();
            visibleFrames.Remove(index);
        }

        using var background = new SolidBrush(Theme.Input);
        using var shade = new SolidBrush(Color.FromArgb(145, Color.Black));
        using var outline = new Pen(Theme.Accent, 2);
        for (var index = start; index < end; index++)
        {
            if (!visibleFrames.TryGetValue(index, out var thumbnail))
            {
                using var frame = source.LoadFrame(index);
                var scale = Math.Min((CardWidth - 4d) / frame.Width, (CardHeight - 4d) / frame.Height);
                thumbnail = frame.GetThumbnailImage(Math.Max(1, (int)(frame.Width * scale)),
                    Math.Max(1, (int)(frame.Height * scale)), null, IntPtr.Zero);
                visibleFrames.Add(index, thumbnail);
            }

            var card = new Rectangle((index - start) * Pitch, 1, CardWidth, CardHeight);
            e.Graphics.FillRectangle(background, card);
            e.Graphics.DrawImageUnscaled(thumbnail, card.X + (card.Width - thumbnail.Width) / 2,
                card.Y + (card.Height - thumbnail.Height) / 2);
            if (index < first || index > last) e.Graphics.FillRectangle(shade, card);
            if (index == current) e.Graphics.DrawRectangle(outline, Rectangle.Inflate(card, -1, -1));
            TextRenderer.DrawText(e.Graphics, $"{index + 1}", Ui.Font(Ui.SmallPoints), new Point(card.X + 4, card.Bottom - 18), Theme.Text, Theme.Background);
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (source is null || e.Button != MouseButtons.Left || e.Y > CardHeight || e.X % Pitch >= CardWidth) return;
        var index = scroll.Value + e.X / Pitch;
        if (e.X / Pitch < VisibleCount && index < source.FrameCount) FrameSelected?.Invoke(index);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        scroll.Value = Math.Clamp(scroll.Value - Math.Sign(e.Delta) * 3, 0, LastOffset);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            foreach (var frame in visibleFrames.Values) frame.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class FrameSlider : Control
{
    private const int Inset = 8;
    private readonly bool scrollBar;
    private int maximum;
    private int value;
    private int? dragOffset;
    private int TrackWidth => Math.Max(1, Width - 2 * Inset);
    private int ThumbWidth => scrollBar
        ? Math.Min(TrackWidth, Math.Max(24, (int)((long)TrackWidth * PageStep / (maximum + (long)PageStep))))
        : Math.Min(TrackWidth, 10);
    private int Travel => TrackWidth - ThumbWidth;
    private int ThumbLeft => Inset + (maximum == 0 ? 0 : (int)Math.Round(Travel * (double)value / maximum));

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Maximum
    {
        get => maximum;
        set { maximum = Math.Max(0, value); Value = this.value; Invalidate(); }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => value;
        set
        {
            var next = Math.Clamp(value, 0, maximum);
            if (next == this.value) return;
            this.value = next;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int PageStep { get; set; } = 1;
    public event EventHandler? ValueChanged;
    public event EventHandler? UserScroll;

    public FrameSlider(bool scrollBar)
    {
        this.scrollBar = scrollBar;
        DoubleBuffered = true;
        BackColor = Theme.Background;
        AccessibleRole = scrollBar ? AccessibleRole.ScrollBar : AccessibleRole.Slider;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var middle = Height / 2f;
        using var track = new Pen(Theme.Border, scrollBar ? 3 : 4);
        track.StartCap = track.EndCap = System.Drawing.Drawing2D.LineCap.Round;
        e.Graphics.DrawLine(track, Inset, middle, Inset + TrackWidth, middle);
        if (!Enabled) return;

        var left = ThumbLeft;
        if (scrollBar)
        {
            using var thumb = new Pen(Focused || dragOffset.HasValue ? Theme.Accent : Theme.Muted, 5);
            thumb.StartCap = thumb.EndCap = System.Drawing.Drawing2D.LineCap.Round;
            e.Graphics.DrawLine(thumb, left + 2, middle, left + ThumbWidth - 2, middle);
        }
        else
        {
            var center = left + ThumbWidth / 2f;
            using var progress = new Pen(Theme.Accent, 4);
            progress.StartCap = progress.EndCap = System.Drawing.Drawing2D.LineCap.Round;
            e.Graphics.DrawLine(progress, Inset, middle, center, middle);
            using var thumb = new SolidBrush(Focused && ShowFocusCues ? Theme.Text : Theme.Accent);
            e.Graphics.FillEllipse(thumb, left, middle - ThumbWidth / 2f, ThumbWidth, ThumbWidth);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || maximum == 0 || Travel == 0) return;
        Focus();
        dragOffset = e.X >= ThumbLeft && e.X < ThumbLeft + ThumbWidth ? e.X - ThumbLeft : ThumbWidth / 2;
        UserScroll?.Invoke(this, EventArgs.Empty);
        Capture = true;
        DragTo(e.X);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragOffset.HasValue) DragTo(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !dragOffset.HasValue) return;
        DragTo(e.X);
        Capture = false;
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (Capture) return;
        dragOffset = null;
        Invalidate();
    }

    private void DragTo(int x)
    {
        var offset = Math.Clamp(x - Inset - dragOffset!.Value, 0, Travel);
        Value = (int)Math.Round(offset * (double)maximum / Math.Max(1, Travel));
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Down or Keys.Up or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown
        || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        long? next = e.KeyCode switch
        {
            Keys.Left or Keys.Down => (long)value - 1,
            Keys.Right or Keys.Up => (long)value + 1,
            Keys.Home => 0,
            Keys.End => maximum,
            Keys.PageUp => (long)value - PageStep,
            Keys.PageDown => (long)value + PageStep,
            _ => null
        };
        if (next is long position)
        {
            UserScroll?.Invoke(this, EventArgs.Empty);
            Value = (int)Math.Clamp(position, 0, maximum);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}
