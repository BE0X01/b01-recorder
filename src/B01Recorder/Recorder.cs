using System.Diagnostics;
using System.Globalization;
using System.Text;
using NAudio.Wave;

namespace B01Recorder;

internal sealed record RecordingOptions(CaptureTarget Target, int Fps, string Format, int Quality, bool Audio, bool Cursor, string Folder, string FileNamePattern = RecordingFileName.DefaultPattern);
internal sealed record AnimationEdit(int FirstFrame, int LastFrame, Rectangle Crop, int Quality);
internal sealed record AnimationPreview(string Folder, Size SourceSize, int FrameCount)
{
    public Bitmap LoadFrame(int index)
    {
        using var image = Image.FromFile(Path.Combine(Folder, $"frame-{index:D8}.jpg"));
        return new Bitmap(image);
    }
}

internal sealed class Recorder : IDisposable
{
    private readonly string ffmpeg;
    private Process? process;
    private TaskCompletionSource firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly StringBuilder log = new();
    private LoopbackAudio? audio;
    private RecordingOptions? options;
    private string session = "";
    private DateTime startedAt;
    public string RecoveryFolder => session;
    public bool HasExited => process?.HasExited ?? true;
    public bool IsAnimation => options?.Format is "GIF" or "WEBP";
    public string Format => options!.Format;
    public int Fps => options!.Fps;
    public string SuggestedPath => RecordingFileName.AvailablePath(options!.Folder, options.FileNamePattern, startedAt, Format);
    private string CapturePath => Path.Combine(session, "capture.mkv");
    public Recorder(string ffmpeg) => this.ffmpeg = ffmpeg;

    public static string ResolveFfmpeg(string custom)
    {
        if (!string.IsNullOrWhiteSpace(custom))
        {
            if (!File.Exists(custom)) throw new FileNotFoundException(Ui.T("The selected FFmpeg executable does not exist.", "선택한 FFmpeg 실행 파일을 찾을 수 없습니다."), custom);
            return Path.GetFullPath(custom);
        }
        foreach (var path in new[] { Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"), Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe") })
            if (File.Exists(path)) return path;
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var path = Path.Combine(folder.Trim('"'), "ffmpeg.exe");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException(Ui.T("FFmpeg was not found. Run Install-FFmpeg.cmd from the app folder, or place ffmpeg.exe beside b01-recorder.exe.", "FFmpeg를 찾을 수 없습니다. 앱 폴더에서 Install-FFmpeg.cmd를 실행하거나 b01-recorder.exe 옆에 ffmpeg.exe를 넣으세요."));
    }

    public async Task StartAsync(RecordingOptions value)
    {
        if (process is not null) throw new InvalidOperationException(Ui.T("Recording is already in progress.", "이미 녹화 중입니다."));
        if (value.Target.IsWindow && (!Windows.IsWindow(value.Target.Handle) || Windows.IsIconic(value.Target.Handle)))
            throw new InvalidOperationException(Ui.T("The selected window is closed or minimized. Select a visible window.", "선택한 창이 닫혔거나 최소화되었습니다. 화면에 표시된 창을 선택하세요."));
        if (value.Target.Bounds.Width < 16 || value.Target.Bounds.Height < 16) throw new InvalidOperationException(Ui.T("The recording region is too small.", "녹화 영역이 너무 작습니다."));
        options = value;
        startedAt = DateTime.Now;
        RecordingFileName.Format(value.FileNamePattern, startedAt, 1);
        Directory.CreateDirectory(value.Folder);
        session = Path.Combine(value.Folder, $".b01-session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(session);
        try
        {
            var args = new List<string> { "-hide_banner", "-y", "-nostats", "-stats_period", "0.25", "-progress", "pipe:2", "-f", "gdigrab", "-framerate", value.Fps.ToString(), "-draw_mouse", value.Cursor ? "1" : "0" };
            if (value.Target.IsWindow)
                args.AddRange(["-i", $"hwnd={value.Target.Handle.ToInt64()}"]);
            else
                args.AddRange(["-offset_x", value.Target.Bounds.X.ToString(), "-offset_y", value.Target.Bounds.Y.ToString(), "-video_size", $"{value.Target.Bounds.Width}x{value.Target.Bounds.Height}", "-i", "desktop"]);
            // Record an intermediate video: animated formats are encoded after Stop.
            args.AddRange(["-an", "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "18", "-pix_fmt", "yuv420p"]);
            if (IsAnimation) args.AddRange(["-fps_mode", "cfr"]);
            args.Add(CapturePath);
            if (value.Audio && value.Format == "MP4")
            {
                audio = new LoopbackAudio(Path.Combine(session, "audio.wav"));
                audio.Start();
            }
            firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
            process = StartProcess(args, line =>
            {
                lock (log) { log.AppendLine(line); if (log.Length > 20000) log.Remove(0, log.Length - 16000); }
                if (line.StartsWith("frame=", StringComparison.Ordinal) && int.TryParse(line.AsSpan(6), out var count) && count > 0) firstFrame.TrySetResult();
            });
            var winner = await Task.WhenAny(firstFrame.Task, process.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(15)));
            if (winner != firstFrame.Task || process.HasExited)
                throw new InvalidOperationException(Ui.T("Could not start screen capture.\n", "화면 녹화를 시작하지 못했습니다.\n") + ReadLog());
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public async Task FinishCaptureAsync()
    {
        if (process is null || options is null) throw new InvalidOperationException(Ui.T("No recording is in progress.", "현재 녹화 중이 아닙니다."));
        var captureFailed = process.HasExited;
        if (!process.HasExited)
        {
            await process.StandardInput.WriteLineAsync("q");
            await process.StandardInput.FlushAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); throw new TimeoutException(Ui.T("Recording did not stop in time. Temporary files remain in the recovery folder.", "녹화를 제때 종료하지 못했습니다. 임시 파일은 복구 폴더에 남아 있습니다.")); }
        }
        await StopAudioAsync();
        if (captureFailed || process.ExitCode != 0) throw new InvalidOperationException(Ui.T("The recording process stopped unexpectedly.\n", "녹화가 예기치 않게 종료되었습니다.\n") + ReadLog());
    }

    public async Task<AnimationPreview> PrepareAnimationAsync(CancellationToken cancellationToken)
    {
        var folder = Path.Combine(session, "preview");
        Directory.CreateDirectory(folder);
        var first = Path.Combine(folder, "source.jpg");
        const string scale = "scale=w='max(1,floor(iw*min(1,min(640/iw,360/ih))))':h='max(1,floor(ih*min(1,min(640/iw,360/ih))))',setsar=1";
        await RunFfmpegAsync(["-hide_banner", "-loglevel", "error", "-y", "-i", CapturePath,
            "-map", "0:v:0", "-frames:v", "1", "-update", "1", first,
            "-map", "0:v:0", "-vf", scale, "-fps_mode", "passthrough", "-q:v", "4", "-start_number", "0",
            Path.Combine(folder, "frame-%08d.jpg")], cancellationToken);
        using var image = Image.FromFile(first);
        var count = Directory.EnumerateFiles(folder, "frame-*.jpg").Count();
        if (count == 0) throw new InvalidOperationException(Ui.T("The recording contains no preview frames.", "미리 볼 수 있는 프레임이 없습니다."));
        return new(folder, image.Size, count);
    }

    public Task<string> EncodeAnimationAsync(AnimationEdit edit, Size sourceSize, CancellationToken cancellationToken)
    {
        // ffmpeg's end_frame is exclusive. The editor displays an inclusive range.
        var filter = $"trim=start_frame={edit.FirstFrame}:end_frame={edit.LastFrame + 1},setpts=PTS-STARTPTS";
        if (edit.Crop != new Rectangle(Point.Empty, sourceSize))
            filter += $",format=yuv444p,crop={edit.Crop.Width}:{edit.Crop.Height}:{edit.Crop.X}:{edit.Crop.Y}:exact=1,setsar=1";
        return EncodeAsync(edit.Quality, filter, cancellationToken);
    }

    public async Task<string> StopAsync()
    {
        await FinishCaptureAsync();
        var partial = await EncodeAsync(options!.Quality, null, CancellationToken.None);
        string output;
        while (true)
        {
            output = SuggestedPath;
            try { File.Move(partial, output); break; }
            catch (IOException) when (File.Exists(output) || Directory.Exists(output)) { }
        }
        CleanSession();
        return output;
    }

    private async Task<string> EncodeAsync(int quality, string? filter, CancellationToken cancellationToken)
    {
        var partial = Path.Combine(session, "export." + Format.ToLowerInvariant());
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-i", CapturePath };
        switch (Format)
        {
            case "MP4":
                if (audio is not null)
                    args.AddRange(["-i", Path.Combine(session, "audio.wav"), "-map", "0:v:0", "-map", "1:a:0", "-c:a", "aac", "-b:a", "192k", "-af", "apad", "-shortest"]);
                args.AddRange(["-c:v", "copy", "-movflags", "+faststart"]);
                break;
            case "GIF":
                var colors = Math.Clamp((int)Math.Round(16 + quality * 2.4), 16, 256);
                var dither = quality >= 60 ? "sierra2_4a" : "bayer:bayer_scale=3";
                var source = filter is null ? "[0:v]" : $"[0:v]{filter}[trimmed];[trimmed]";
                args.AddRange(["-filter_complex", $"{source}split[a][b];[a]palettegen=max_colors={colors}:reserve_transparent=0[p];[b][p]paletteuse=dither={dither}", "-loop", "0"]);
                break;
            case "WEBP":
                if (filter is not null) args.AddRange(["-vf", filter]);
                args.AddRange(["-c:v", "libwebp_anim", "-quality", quality.ToString(CultureInfo.InvariantCulture), "-compression_level", "4", "-loop", "0", "-an"]);
                break;
            default: throw new InvalidOperationException(Ui.T("Unsupported output format.", "지원하지 않는 저장 형식입니다."));
        }
        args.Add(partial);
        await RunFfmpegAsync(args, cancellationToken);
        if (!File.Exists(partial) || new FileInfo(partial).Length == 0)
            throw new InvalidOperationException(Ui.T("FFmpeg did not write an output file.", "FFmpeg가 영상 파일을 만들지 못했습니다."));
        return partial;
    }

    public void CleanSession()
    {
        Dispose();
        try { Directory.Delete(session, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task RunFfmpegAsync(IEnumerable<string> args, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var error = new StringBuilder();
        using var export = StartProcess(args, line => { lock (error) { error.AppendLine(line); if (error.Length > 20000) error.Remove(0, error.Length - 16000); } });
        try { await export.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException)
        {
            if (!export.HasExited) export.Kill(true);
            await export.WaitForExitAsync();
            throw;
        }
        export.WaitForExit();
        if (export.ExitCode != 0)
            throw new InvalidOperationException(Ui.T("FFmpeg failed.\n", "FFmpeg 처리에 실패했습니다.\n") + error);
    }

    private string ReadLog() { lock (log) return log.ToString(); }
    private Process StartProcess(IEnumerable<string> args, Action<string> onError)
    {
        var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        var result = new Process { StartInfo = info };
        result.ErrorDataReceived += (_, e) => { if (e.Data is not null) onError(e.Data); };
        result.Start();
        result.BeginErrorReadLine();
        return result;
    }
    private async Task StopAudioAsync() { if (audio is not null) await audio.StopAsync(); }
    public void Dispose()
    {
        if (process is not null) { if (!process.HasExited) process.Kill(true); process.Dispose(); process = null; }
        audio?.Dispose(); audio = null;
    }
}

internal sealed class LoopbackAudio : IDisposable
{
    private readonly WasapiLoopbackCapture capture = new();
    private readonly WaveFileWriter writer;
    private readonly Stopwatch clock = new();
    private readonly object gate = new();
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly byte[] silence = new byte[65536];
    private Exception? failure;
    private bool disposed;
    private bool stopping;
    public LoopbackAudio(string path)
    {
        writer = new WaveFileWriter(path, capture.WaveFormat);
        capture.DataAvailable += (_, e) =>
        {
            lock (gate)
            {
                if (disposed) return;
                try
                {
                    // Loopback omits buffers while the output device is silent. Fill gaps
                    // on the wall-clock timeline so later sound keeps its original position.
                    var expected = AlignedBytes(clock.Elapsed.TotalSeconds);
                    var missing = expected - writer.Length - e.BytesRecorded;
                    if (missing > capture.WaveFormat.AverageBytesPerSecond / 10) WriteSilence(missing);
                    writer.Write(e.Buffer, 0, e.BytesRecorded);
                }
                catch (Exception ex) { failure = ex; }
            }
        };
        capture.RecordingStopped += (_, e) => { failure ??= e.Exception; stopped.TrySetResult(); };
    }
    public void Start() { clock.Start(); capture.StartRecording(); }
    public async Task StopAsync()
    {
        if (stopping) return;
        stopping = true;
        var duration = clock.Elapsed.TotalSeconds;
        capture.StopRecording();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
        lock (gate)
        {
            if (failure is not null) throw new InvalidOperationException(Ui.T("Could not capture system audio.", "시스템 오디오를 녹음하지 못했습니다."), failure);
            WriteSilence(AlignedBytes(duration) - writer.Length);
            writer.Flush();
            writer.Dispose();
            disposed = true;
        }
    }
    private long AlignedBytes(double seconds) => (long)(seconds * capture.WaveFormat.SampleRate) * capture.WaveFormat.BlockAlign;
    private void WriteSilence(long bytes)
    {
        var chunkSize = silence.Length / capture.WaveFormat.BlockAlign * capture.WaveFormat.BlockAlign;
        while (bytes > 0) { var count = (int)Math.Min(bytes, chunkSize); writer.Write(silence, 0, count); bytes -= count; }
    }
    public void Dispose()
    {
        capture.Dispose();
        lock (gate) { if (!disposed) { writer.Dispose(); disposed = true; } }
    }
}
