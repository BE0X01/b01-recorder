using System.Diagnostics;
using System.Globalization;
using System.Text;
using NAudio.Wave;

namespace B01Recorder;

internal sealed record RecordingOptions(CaptureTarget Target, int Fps, string Format, int Quality, bool Audio, bool Cursor, string Folder);

internal sealed class Recorder : IDisposable
{
    private readonly string ffmpeg;
    private Process? process;
    private TaskCompletionSource firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly StringBuilder log = new();
    private LoopbackAudio? audio;
    private RecordingOptions? options;
    private string session = "";
    public string RecoveryFolder => session;
    public bool HasExited => process?.HasExited ?? true;
    public Recorder(string ffmpeg) => this.ffmpeg = ffmpeg;

    public static string ResolveFfmpeg(string custom)
    {
        if (!string.IsNullOrWhiteSpace(custom))
        {
            if (!File.Exists(custom)) throw new FileNotFoundException("The selected FFmpeg executable does not exist.", custom);
            return Path.GetFullPath(custom);
        }
        foreach (var path in new[] { Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"), Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe") })
            if (File.Exists(path)) return path;
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var path = Path.Combine(folder.Trim('"'), "ffmpeg.exe");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("FFmpeg was not found. Run Install-FFmpeg.cmd from the app folder, or place ffmpeg.exe beside b01-recorder.exe.");
    }

    public async Task StartAsync(RecordingOptions value)
    {
        if (process is not null) throw new InvalidOperationException("Recording is already in progress.");
        if (value.Target.IsWindow && (!Windows.IsWindow(value.Target.Handle) || Windows.IsIconic(value.Target.Handle)))
            throw new InvalidOperationException("The selected window is closed or minimized. Select a visible window.");
        if (value.Target.Bounds.Width < 16 || value.Target.Bounds.Height < 16) throw new InvalidOperationException("The recording region is too small.");
        options = value;
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
            args.AddRange(["-an", "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "18", "-pix_fmt", "yuv420p", Path.Combine(session, "capture.mkv")]);
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
                throw new InvalidOperationException("Could not start screen capture.\n" + ReadLog());
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public async Task<string> StopAsync()
    {
        if (process is null || options is null) throw new InvalidOperationException("No recording is in progress.");
        var captureFailed = process.HasExited;
        if (!process.HasExited)
        {
            await process.StandardInput.WriteLineAsync("q");
            await process.StandardInput.FlushAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); throw new TimeoutException("Recording did not stop in time. Temporary files remain in the recovery folder."); }
        }
        await StopAudioAsync();
        if (captureFailed || process.ExitCode != 0) throw new InvalidOperationException("The recording process stopped unexpectedly.\n" + ReadLog());
        var output = Path.Combine(options.Folder, $"b01-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.{options.Format.ToLowerInvariant()}");
        var partial = Path.Combine(session, "export." + options.Format.ToLowerInvariant());
        var args = new List<string> { "-hide_banner", "-y", "-i", Path.Combine(session, "capture.mkv") };
        switch (options.Format)
        {
            case "MP4":
                if (audio is not null)
                    args.AddRange(["-i", Path.Combine(session, "audio.wav"), "-map", "0:v:0", "-map", "1:a:0", "-c:a", "aac", "-b:a", "192k", "-af", "apad", "-shortest"]);
                args.AddRange(["-c:v", "copy", "-movflags", "+faststart"]);
                break;
            case "GIF":
                var colors = Math.Clamp((int)Math.Round(16 + options.Quality * 2.4), 16, 256);
                var dither = options.Quality >= 60 ? "sierra2_4a" : "bayer:bayer_scale=3";
                args.AddRange(["-filter_complex", $"[0:v]split[a][b];[a]palettegen=max_colors={colors}:reserve_transparent=0[p];[b][p]paletteuse=dither={dither}", "-loop", "0"]);
                break;
            case "WEBP":
                args.AddRange(["-c:v", "libwebp_anim", "-quality", options.Quality.ToString(CultureInfo.InvariantCulture), "-compression_level", "4", "-loop", "0", "-an"]);
                break;
            default: throw new InvalidOperationException("Unsupported output format.");
        }
        args.Add(partial);
        var error = new StringBuilder();
        using var export = StartProcess(args, line => { lock (error) { error.AppendLine(line); if (error.Length > 20000) error.Remove(0, error.Length - 16000); } });
        await export.WaitForExitAsync();
        // WaitForExit also drains redirected event handlers.
        export.WaitForExit();
        if (export.ExitCode != 0 || !File.Exists(partial) || new FileInfo(partial).Length == 0)
            throw new InvalidOperationException("Export failed. Temporary files have been preserved.\n" + error);
        File.Move(partial, output);
        Dispose();
        try { Directory.Delete(session, true); } catch (IOException) { }
        return output;
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
            if (failure is not null) throw new InvalidOperationException("Could not capture system audio.", failure);
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
