using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace B01Recorder;

// Local integration fixtures only; real settings and recordings are untouched.
internal static class UiVerification
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    public static void Run()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "verification", "ui"); Directory.CreateDirectory(output);
        try
        {
            var folder = Path.Combine(output, "recordings-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(folder);
            using var form = new MainForm(new Settings { Folder = folder, Fps = 10, Audio = false, Recordings = [] }, false);
            form.Shown += async (_, _) =>
            {
                try
                {
                    ShowWindow(form.Handle, 5); form.TopMost = true; form.Activate();
                    await VerifyAsync(form, folder, output);
                    File.WriteAllText(Path.Combine(output, "PASS.txt"), "Exit-only INI persistence and reload / one Record-Stop toggle / delete dialog buttons / portable data folder / English dark UI / custom dropdown, checkbox, FPS input and scrollbar / default and minimum layouts / no auto-start / no minimization / moving window and region frame without dimming / MP4-GIF-WebP thumbnails and embedded playback / local recycle deletion / library refresh.\n" + DateTimeOffset.Now);
                    File.Delete(Path.Combine(output, "FAIL.txt"));
                }
                catch (Exception ex)
                {
                    var message = Field<MediaPreview>(form, "preview").Controls.OfType<Label>().First().Text;
                    var state = await Field<MediaPreview>(form, "preview").MediaStateAsync();
                    File.WriteAllText(Path.Combine(output, "FAIL.txt"), ex + "\nPreview: " + message + "\nState: " + state); Environment.ExitCode = 1;
                    Capture(form, Path.Combine(output, "failure.png"));
                }
                finally { typeof(MainForm).GetField("closing", Private)!.SetValue(form, true); form.Close(); }
            };
            Application.Run(form);
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "FAIL.txt"), ex.ToString()); Environment.ExitCode = 1; }
    }
    private static async Task VerifyAsync(MainForm form, string folder, string output)
    {
        Assert(!Descendants(form).Any(c => c.Text == "FFmpeg"), "FFmpeg button remained visible.");
        using (var dialog = DarkDialog.Create("Move this recording to the Recycle Bin?\n\nb01-example.mp4", true))
        {
            dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new(form.Left + 80, form.Top + 150); dialog.TopMost = true;
            dialog.Show(form); dialog.Activate(); await Task.Delay(150);
            foreach (var button in Descendants(dialog).OfType<Button>())
                Assert(button.Parent!.ClientRectangle.Contains(button.Bounds), "Delete dialog buttons clipped.");
            Capture(dialog, Path.Combine(output, "delete-dialog.png")); dialog.Close();
        }
        var ini = Path.Combine(folder, "setting.ini");
        using (var preferences = new MainForm(new Settings { StoragePath = ini, Folder = folder, Fps = 24, Audio = false }, true))
        {
            preferences.Show();
            Field<DarkNumber>(preferences, "fps").Value = 48;
            Field<QualitySlider>(preferences, "quality").Value = 65;
            Field<DarkCombo>(preferences, "format").SelectedItem = "WEBP";
            FindButton(preferences, "Region").PerformClick();
            Assert(!File.Exists(ini), "Settings were written before exit.");
            preferences.Close();
            Assert(File.Exists(ini), "Exit did not create setting.ini.");
            var written = File.GetLastWriteTimeUtc(ini);
            Invoke(preferences, "SaveSettingsOnExit");
            Assert(File.GetLastWriteTimeUtc(ini) == written, "Settings saved more than once.");
        }
        var restored = Settings.Load(ini);
        Assert(restored.Fps == 48 && restored.Quality == 65 && restored.Format == "WEBP" && !restored.Audio && restored.CaptureMode == 2 && restored.Folder == folder, "INI settings did not round-trip.");
        var ffmpeg = Recorder.ResolveFfmpeg("");
        var results = new List<object>();
        using var fixture = new Form { Text = "B01 capture fixture", ClientSize = new(321, 241), StartPosition = FormStartPosition.Manual, Location = new(form.Left + 50, form.Top + 170), BackColor = Color.FromArgb(70, 170, 140), TopMost = true };
        fixture.Show(); await Task.Delay(100);
        FindButton(form, "Window").PerformClick();
        var targets = Field<DarkCombo>(form, "targets");
        var selected = targets.Items.Cast<object>().OfType<CaptureTarget>().First(t => t.Handle == fixture.Handle);
        targets.SelectedItem = selected;
        Assert(Field<Recorder?>(form, "recorder") is null, "Selecting a window auto-started recording.");
        var selection = Field<SelectionOverlay>(form, "overlay");
        ValidateMask(selection, selected.Bounds);
        fixture.Left += 40; Invoke(form, "UpdateOverlay");
        Assert(Windows.TryGetClientBounds(fixture.Handle, out var moved), "Fixture client bounds missing."); ValidateMask(selection, moved);
        results.Add(new { check = "window-selection-and-follow", bounds = moved.ToString() });

        var toggle = Field<DarkButton>(form, "start");
        toggle.PerformClick();
        await Until(() => Field<DarkButton>(form, "start").Text == "■  Stop & save" && Field<DarkButton>(form, "start").Enabled, "Recording did not start.");
        Assert(form.WindowState == FormWindowState.Normal && form.Visible, "Recorder minimized on start.");
        fixture.BackColor = Color.FromArgb(190, 105, 80); await Task.Delay(650);
        fixture.BackColor = Color.FromArgb(70, 170, 140); await Task.Delay(650);
        Assert(ReferenceEquals(toggle, FindButton(form, "■  Stop & save")), "Record and Stop used different buttons.");
        toggle.PerformClick();
        try { await Until(() => Field<Recorder?>(form, "recorder") is null && Field<string?>(form, "selectedFile") is not null, "Recording did not save."); }
        catch (Exception ex) { throw new Exception($"{ex.Message} busy={Field<bool>(form, "busy")}, recording={Field<Recorder?>(form, "recorder") is not null}, status={Field<Label>(form, "status").Text}", ex); }
        Assert(toggle.Text == "●  Record", "Toggle did not reset after saving.");
        Assert(form.WindowState == FormWindowState.Normal, "Recorder changed window state on stop.");
        var recorded = Field<string?>(form, "selectedFile")!;
        results.Add(new { check = "window-recording-without-minimization", file = recorded });

        FindButton(form, "Region").PerformClick();
        Assert(!FindButton(form, "●  Record").Enabled && Field<Recorder?>(form, "recorder") is null, "Region mode recorded without a selected target.");
        typeof(MainForm).GetField("target", Private)!.SetValue(form, new CaptureTarget("integration region", moved)); Invoke(form, "UpdateTarget");
        Assert(FindButton(form, "●  Record").Enabled && Field<Recorder?>(form, "recorder") is null, "Selecting a region auto-started recording.");
        ValidateMask(selection, moved); results.Add(new { check = "region-mask-and-manual-start", bounds = moved.ToString() });
        FindButton(form, "Full screen").PerformClick(); fixture.Hide();
        Assert(!Field<Form>(selection, "border").Visible, "Switching to full screen left a region frame.");

        var synthetic = Path.Combine(folder, "b01-ui-fixture.mp4");
        await Command(ffmpeg, ["-hide_banner", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=10", "-t", "1.2", "-c:v", "libx264", "-pix_fmt", "yuv420p", synthetic]);
        var gif = Path.ChangeExtension(synthetic, ".gif"); var webp = Path.ChangeExtension(synthetic, ".webp");
        await Command(ffmpeg, ["-hide_banner", "-v", "error", "-y", "-i", synthetic, "-loop", "0", gif]);
        await Command(ffmpeg, ["-hide_banner", "-v", "error", "-y", "-i", synthetic, "-c:v", "libwebp_anim", "-loop", "0", webp]);
        await InvokeTask(form, "RefreshLibraryAsync", synthetic);
        var preview = Field<MediaPreview>(form, "preview");
        foreach (var file in new[] { synthetic, gif, webp })
        {
            var thumb = await RecordingLibrary.ThumbnailAsync(ffmpeg, file);
            Assert(thumb is not null && Path.GetFullPath(thumb).StartsWith(Path.Combine(AppContext.BaseDirectory, "data"), StringComparison.OrdinalIgnoreCase), "Thumbnail stored outside portable data folder.");
            Assert(thumb is not null && File.Exists(thumb), "Thumbnail missing: " + file);
            using (var image = RecordingLibrary.LoadThumbnail(thumb!)) Assert(image.Width == 480 && image.Height == 270, "Thumbnail size incorrect.");
            await InvokeTask(form, "SelectFileAsync", file);
            var video = file.EndsWith(".mp4");
            await UntilAsync(async () =>
            {
                using var state = await MediaState(preview);
                return video ? state.RootElement.TryGetProperty("ready", out var ready) && ready.GetInt32() >= 2 : state.RootElement.TryGetProperty("image", out var width) && width.GetInt32() > 0;
            }, "Embedded media did not load: " + file);
            if (video)
            {
                using var before = await MediaState(preview); Assert(before.RootElement.GetProperty("paused").GetBoolean(), "Preview auto-played audio.");
                FindButton(form, "Play in app").PerformClick();
                await UntilAsync(async () => { using var state = await MediaState(preview); return state.RootElement.TryGetProperty("time", out var time) && time.GetDouble() > .2; }, "Embedded video did not advance after Play.");
                await preview.PauseAsync();
            }
            results.Add(new { check = "thumbnail-and-embedded-media", file, thumbnail = thumb });
        }
        await InvokeTask(form, "SelectFileAsync", synthetic); await Task.Delay(300);
        var preset = Field<DarkCombo>(form, "preset");
        Invoke(preset, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 12, 12, 0));
        var popup = Field<ToolStripDropDown>(preset, "popup"); Assert(popup.Visible, "Dark dropdown did not open.");
        Capture(form, Path.Combine(output, "ui-dropdown.png"));
        var list = ((ToolStripControlHost)popup.Items[0]).Control;
        Invoke(list, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 45, 0));
        Assert(preset.SelectedIndex == 1 && Field<DarkNumber>(form, "fps").Value == 60, "Dropdown preset selection failed.");
        var number = Field<DarkNumber>(form, "fps"); Field<TextBox>(number, "edit").Text = "24";
        Assert(number.Value == 24 && preset.SelectedIndex == 4, "Custom FPS text input failed.");
        var audio = Field<CheckBox>(form, "audio"); var previous = audio.Checked; Invoke(audio, "OnClick", EventArgs.Empty);
        Assert(audio.Checked != previous, "Custom checkbox did not toggle."); audio.Checked = previous;
        var gallery = Field<SlimFlowPanel>(form, "gallery");
        Invoke(gallery, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 20, 20, -120));
        Assert(Field<SlimScrollBar>(gallery, "scroll").Value > 0, "Custom gallery scrolling failed.");
        Field<SlimScrollBar>(gallery, "scroll").Value = 0;
        results.Add(new { check = "custom-dropdown-checkbox-fps-scrollbar" });
        AssertButtonsFit(form); Capture(form, Path.Combine(output, "ui-default.png"));
        form.Size = form.MinimumSize; await Task.Delay(200); AssertButtonsFit(form); Capture(form, Path.Combine(output, "ui-minimum.png"));
        results.Add(new { check = "footer-layout", defaultClient = "1180 × 850", minimumWindow = form.Size.ToString() });

        await preview.ClearAsync(); var thumbnailPath = RecordingLibrary.ThumbnailPath(synthetic);
        RecordingLibrary.Trash(synthetic);
        Assert(!File.Exists(synthetic) && !File.Exists(thumbnailPath), "Local recycle deletion failed.");
        await InvokeTask(form, "RefreshLibraryAsync", (object?)null);
        Assert(!RecordingLibrary.List(folder, []).Contains(synthetic), "Deleted recording remained in library.");
        results.Add(new { check = "recycle-local-fixture-and-refresh", file = synthetic });
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void ValidateMask(SelectionOverlay overlay, Rectangle selected)
    {
        var border = Field<Form>(overlay, "border");
        var point = new Point(selected.Left - border.Left + selected.Width / 2, selected.Top - border.Top + selected.Height / 2);
        Assert(border.Visible && border.Region is not null && !border.Region.IsVisible(point), "Border covered selected pixels.");
        Assert(!border.Region!.IsVisible(0, 0), "Frame covered outside pixels.");
        Assert(border.Region.IsVisible(selected.Left - border.Left - 2, selected.Top - border.Top + 10), "Region frame was missing.");
    }
    private static void AssertButtonsFit(MainForm form)
    {
        foreach (var button in new[] { Field<DarkButton>(form, "start") })
        {
            Assert(button.Width <= 160 && button.Height <= 45, "Record buttons too large.");
            var parent = button.Parent!; Assert(parent.ClientRectangle.Contains(button.Bounds), "Record buttons clipped by parent.");
            var position = form.PointToClient(button.PointToScreen(Point.Empty)); Assert(form.ClientRectangle.Contains(new Rectangle(position, button.Size)), "Record button outside window.");
        }
    }
    private static void Capture(Form form, string path)
    {
        form.Activate(); form.Refresh(); Application.DoEvents();
        using var bitmap = new Bitmap(form.Width, form.Height); using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(form.Location, Point.Empty, bitmap.Size); bitmap.Save(path);
    }
    private static async Task<JsonDocument> MediaState(MediaPreview preview)
    {
        var raw = await preview.MediaStateAsync(); var decoded = JsonSerializer.Deserialize<string>(raw); return JsonDocument.Parse(decoded ?? "{}");
    }
    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static Button FindButton(Form form, string text) => Descendants(form).OfType<Button>().First(b => b.Text == text);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static object? Invoke(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static Task InvokeTask(object target, string name, params object?[] args) => (Task)Invoke(target, name, args)!;
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Until(Func<bool> condition, string message) => await UntilAsync(() => Task.FromResult(condition()), message);
    private static async Task UntilAsync(Func<Task<bool>> condition, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (!await condition()) { if (DateTime.UtcNow > deadline) throw new Exception(message); await Task.Delay(50); }
    }
    private static async Task Command(string file, string[] args)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!; var error = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new Exception(await error); await error;
    }
}
