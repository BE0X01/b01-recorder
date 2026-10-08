using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace B01Recorder;

// Opt-in integration checks. Nothing records until --verify is explicitly supplied.
internal static class Verification
{
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint hwnd, nint dc, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    public static void Run(bool previewOnly = false)
    {
        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "verification"));
        Directory.CreateDirectory(output);
        foreach (var marker in new[] { "PASS.txt", "FAIL.txt" }) File.Delete(Path.Combine(output, marker));
        try
        {
            using var form = new MainForm();
            form.Show();
            ShowWindow(form.Handle, 5);
            form.TopMost = true;
            form.Refresh();
            Application.DoEvents();
            using (var image = new Bitmap(form.Width, form.Height))
            {
                using var graphics = Graphics.FromImage(image);
                var dc = graphics.GetHdc();
                try { if (!PrintWindow(form.Handle, dc, 0)) throw new Exception("Test window could not be rendered."); }
                finally { graphics.ReleaseHdc(dc); }
                image.Save(Path.Combine(output, "ui.png"));
            }
            if (previewOnly) return;
            var hwnd = form.Handle;
            var region = new Rectangle(form.Left + 32, form.Top + 64, 321, 241);
            using var animation = new Panel { Width = 40, Height = 40, Left = 300, Top = 100, BackColor = Color.RoyalBlue };
            form.Controls.Add(animation);
            animation.BringToFront();
            var task = Task.Run(async () =>
            {
                var ffmpeg = Recorder.ResolveFfmpeg("");
                var probe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
                var tests = new List<object>();
                foreach (var (target, format, quality, audio) in new[]
                {
                    (new CaptureTarget("desktop", SystemInformation.VirtualScreen), "MP4", 80, true),
                    (new CaptureTarget("region", region), "GIF", 25, false),
                    (new CaptureTarget("region", region), "GIF", 95, false),
                    (new CaptureTarget("window", new Rectangle(0, 0, 620, 850), hwnd), "WEBP", 25, false),
                    (new CaptureTarget("window", new Rectangle(0, 0, 620, 850), hwnd), "WEBP", 95, false)
                })
                {
                    using var recorder = new Recorder(ffmpeg);
                    await recorder.StartAsync(new(target, 10, format, quality, audio, true, output));
                    await Task.Delay(750);
                    if (audio)
                    {
                        using var tone = new WasapiOut();
                        tone.Init(new SignalGenerator { Frequency = 440, Gain = .15, Type = SignalGeneratorType.Sin }.ToWaveProvider());
                        tone.Play();
                        await Task.Delay(1000);
                        tone.Stop();
                    }
                    else await Task.Delay(1000);
                    await Task.Delay(750);
                    var file = await recorder.StopAsync();
                    var bytes = new FileInfo(file).Length;
                    if (bytes < 100) throw new Exception($"Empty {format}");
                    var info = await Command(probe, ["-v", "error", "-show_streams", "-show_format", "-of", "json", file]);
                    using var metadata = JsonDocument.Parse(info);
                    if (format == "MP4")
                    {
                        var streams = metadata.RootElement.GetProperty("streams").EnumerateArray().ToArray();
                        if (!streams.Any(s => s.GetProperty("codec_type").GetString() == "audio")) throw new Exception("Audio stream missing");
                        var volume = await Command(ffmpeg, ["-i", file, "-vn", "-af", "volumedetect", "-f", "null", "-"], true);
                        if (volume.Contains("max_volume: -inf")) throw new Exception("Audio is silent");
                        File.WriteAllText(Path.Combine(output, "audio-check.txt"), volume);
                    }
                    else
                    {
                        // Animated WebP frame decoding is checked separately with Pillow.
                        if (format == "GIF") await Command(ffmpeg, ["-v", "error", "-i", file, "-f", "null", "-"]);
                    }
                    tests.Add(new { target = target.Label, format, quality, audio, bytes, file, metadata = metadata.RootElement.Clone() });
                }
                File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(tests, new JsonSerializerOptions { WriteIndented = true }));
            });
            var frame = 0;
            while (!task.IsCompleted)
            {
                animation.Left = 50 + frame++ % 400;
                Application.DoEvents();
                Thread.Sleep(20);
            }
            task.GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(output, "PASS.txt"), "Desktop MP4 + audible system loopback; odd-size region GIF at two qualities; HWND WebP at two qualities.\n" + DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "FAIL.txt"), ex.ToString());
            Environment.ExitCode = 1;
        }
    }
    private static async Task<string> Command(string executable, string[] args, bool stderr = false)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var result = await stdout; var error = await errors;
        if (process.ExitCode != 0) throw new Exception(error);
        return stderr ? error : result;
    }
}
