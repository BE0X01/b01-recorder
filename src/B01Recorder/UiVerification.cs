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
            using var form = new MainForm(new Settings { Folder = folder, Fps = 10, Audio = false, ThemeMode = ThemeMode.Dark }, false);
            form.Shown += async (_, _) =>
            {
                try
                {
                    ShowWindow(form.Handle, 5); form.TopMost = true; form.Activate();
                    await VerifyAsync(form, folder, output);
                    File.WriteAllText(Path.Combine(output, "PASS.txt"), "Pretendard / English and Korean / System, Light and Dark themes / settings persistence / compact interface / capture selection / MP4 recording / GIF-WebP frame navigation, playback, crop, encoding and cancellation.\n" + DateTimeOffset.Now);
                    File.Delete(Path.Combine(output, "FAIL.txt"));
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(output, "FAIL.txt"), ex.ToString()); Environment.ExitCode = 1;
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
        Assert(form.Font.Name == "Pretendard" && Math.Abs(form.Font.SizeInPoints - Ui.BodyPoints) < 0.01f,
            "The main window did not use the embedded 15px Pretendard font.");
        Assert(Math.Abs(Field<Label>(form, "badge").Font.SizeInPoints - Ui.SmallPoints) < 0.01f, "The status chip did not use 13px Pretendard.");
        Assert(!Descendants(form).Any(control => control.Text is "RECENT RECORDINGS" or "Play in app"), "The recent recordings pane remained visible.");
        var ini = Path.Combine(folder, "setting.ini");
        using (var preferences = new MainForm(new Settings { StoragePath = ini, Folder = folder, Fps = 24, Audio = false, Quality = 65, ThemeMode = ThemeMode.Dark }, true))
        {
            preferences.Show();
            Field<DarkNumber>(preferences, "fps").Value = 48;
            Field<DarkCombo>(preferences, "format").SelectedItem = "WEBP";
            ChangePreferences(preferences, UiLanguage.Korean, ThemeMode.Light, output);
            Assert(!Theme.IsDark && Field<DarkButton>(preferences, "start").Text == "녹화", "Korean or light theme was not applied immediately.");
            FindButton(preferences, "영역").PerformClick();
            AssertControlsFit(preferences);
            preferences.TopMost = true;
            Capture(preferences, Path.Combine(output, "main-korean-light.png"));
            Assert(!File.Exists(ini), "Settings were written before exit.");
            preferences.Close();
            Assert(File.Exists(ini), "Exit did not create setting.ini.");
        }
        var restored = Settings.Load(ini);
        Assert(restored.Fps == 48 && restored.Quality == 65 && restored.DimRegionPicker && restored.Format == "WEBP" &&
            !restored.Audio && restored.CaptureMode == 2 && restored.Folder == folder && restored.Language == UiLanguage.Korean &&
            restored.ThemeMode == ThemeMode.Light && restored.CompactLayout, "INI settings did not round-trip.");
        using (var reloaded = new MainForm(restored, false))
            Assert(Field<DarkButton>(reloaded, "start").Text == "녹화" && !Theme.IsDark, "Reloaded preferences did not reach the UI.");
        ChangePreferences(form, UiLanguage.English, ThemeMode.System);
        var expectedLight = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1) is int value && value != 0;
        Assert(Theme.IsDark != expectedLight, "The System theme did not match Windows.");
        ChangePreferences(form, UiLanguage.English, ThemeMode.Dark);
        Assert(Theme.IsDark && Field<DarkButton>(form, "start").Text == "Record", "English or dark theme was not applied.");

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
        await Until(() => Field<DarkButton>(form, "start").Text == "Stop & save" && Field<DarkButton>(form, "start").Enabled, "Recording did not start.");
        Assert(form.WindowState == FormWindowState.Normal && form.Visible, "Recorder minimized on start.");
        fixture.BackColor = Color.FromArgb(190, 105, 80); await Task.Delay(650);
        fixture.BackColor = Color.FromArgb(70, 170, 140); await Task.Delay(650);
        Assert(ReferenceEquals(toggle, FindButton(form, "Stop & save")), "Record and Stop used different buttons.");
        toggle.PerformClick();
        try { await Until(() => Field<Recorder?>(form, "recorder") is null && Directory.EnumerateFiles(folder, "*.mp4").Any(), "Recording did not save."); }
        catch (Exception ex) { throw new Exception($"{ex.Message} busy={Field<bool>(form, "busy")}, recording={Field<Recorder?>(form, "recorder") is not null}, status={Field<Label>(form, "status").Text}", ex); }
        Assert(toggle.Text == "Record", "Toggle did not reset after saving.");
        Assert(form.WindowState == FormWindowState.Normal, "Recorder changed window state on stop.");
        var recorded = Directory.EnumerateFiles(folder, "*.mp4").Single();
        results.Add(new { check = "window-recording-without-minimization", file = recorded });
        await VerifyAnimationEditorsAsync(form, fixture, output);

        FindButton(form, "Region").PerformClick();
        Assert(!FindButton(form, "Record").Enabled && Field<Recorder?>(form, "recorder") is null, "Region mode recorded without a selected target.");
        typeof(MainForm).GetField("target", Private)!.SetValue(form, new CaptureTarget("integration region", moved)); Invoke(form, "UpdateTarget");
        Assert(FindButton(form, "Record").Enabled && Field<Recorder?>(form, "recorder") is null, "Selecting a region auto-started recording.");
        ValidateMask(selection, moved); results.Add(new { check = "region-mask-and-manual-start", bounds = moved.ToString() });
        FindButton(form, "Full screen").PerformClick(); fixture.Hide();
        Assert(!Field<Form>(selection, "border").Visible, "Switching to full screen left a region frame.");

        var formatPicker = Field<DarkCombo>(form, "format");
        var click = new MouseEventArgs(MouseButtons.Left, 1, 12, 12, 0);
        Invoke(formatPicker, "OnMouseDown", click);
        Assert(Field<ToolStripDropDown?>(formatPicker, "popup")?.Visible != true, "The dropdown opened before the mouse button was released.");
        Invoke(formatPicker, "OnMouseUp", click);
        Assert(Field<ToolStripDropDown>(formatPicker, "popup").Visible, "The dropdown did not open after one click.");
        // Let WinForms process the next press as it does with a real click while the menu has focus.
        var secondPress = Message.Create(formatPicker.Handle, 0x0201, 1, (12 << 16) | 12);
        Application.FilterMessage(ref secondPress);
        Assert(!Field<ToolStripDropDown>(formatPicker, "popup").Visible, "The WinForms menu filter did not dismiss the popup before the second press.");
        Invoke(formatPicker, "OnMouseDown", click);
        Invoke(formatPicker, "OnMouseUp", click);
        Assert(!Field<ToolStripDropDown>(formatPicker, "popup").Visible, "The second click did not close the dropdown.");
        Invoke(formatPicker, "OnMouseDown", click);
        Invoke(formatPicker, "OnMouseUp", click);
        var popup = Field<ToolStripDropDown>(formatPicker, "popup");
        Assert(popup.Visible, "The dropdown did not reopen on the next click.");
        Capture(form, Path.Combine(output, "ui-dropdown.png"));
        var list = ((ToolStripControlHost)popup.Items[0]).Control;
        Invoke(list, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 45, 0));
        Assert(formatPicker.SelectedIndex == 1 && !Field<CheckBox>(form, "audio").Enabled, "Dropdown output format selection failed.");
        formatPicker.SelectedIndex = 0;
        var number = Field<DarkNumber>(form, "fps"); Field<TextBox>(number, "edit").Text = "24";
        Assert(number.Value == 24, "Custom FPS text input failed.");
        var audio = Field<CheckBox>(form, "audio"); var previous = audio.Checked; Invoke(audio, "OnClick", EventArgs.Empty);
        Assert(audio.Checked != previous, "Custom checkbox did not toggle."); audio.Checked = previous;
        AssertControlsFit(form); Capture(form, Path.Combine(output, "ui-default.png"));
        form.Size = form.MinimumSize; await Task.Delay(200); AssertControlsFit(form); Capture(form, Path.Combine(output, "ui-minimum.png"));
        results.Add(new { check = "footer-layout", defaultClient = "512 × 608", minimumWindow = form.Size.ToString() });

        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void ChangePreferences(MainForm form, UiLanguage language, ThemeMode theme, string? output = null)
    {
        var gear = Field<DarkButton>(form, "gear");
        form.BeginInvoke(() =>
        {
            var settingsDialog = form.OwnedForms.Single(window => window.Visible);
            var settings = Descendants(settingsDialog).OfType<DarkCombo>().ToArray();
            var languageBox = settings.Single(combo => combo.AccessibleName is "Language" or "언어");
            var themeBox = settings.Single(combo => combo.AccessibleName is "Theme" or "테마");
            languageBox.SelectedIndex = (int)language;
            themeBox.SelectedIndex = (int)theme;
            Descendants(settingsDialog).OfType<DarkCheck>().Single().Checked = true;
            if (output is not null)
            {
                using var screenshot = new Bitmap(settingsDialog.Width, settingsDialog.Height);
                settingsDialog.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
                screenshot.Save(Path.Combine(output, "settings.png"));
            }
            Descendants(settingsDialog).OfType<Button>().Single(button => button.DialogResult == DialogResult.OK).PerformClick();
        });
        gear.PerformClick();
    }


    private static async Task VerifyAnimationEditorsAsync(MainForm form, Form fixture, string output)
    {
        var toggle = Field<DarkButton>(form, "start");
        var formats = Field<DarkCombo>(form, "format");
        foreach (var format in new[] { "GIF", "WEBP" })
        {
            var korean = format == "WEBP";
            if (korean) ChangePreferences(form, UiLanguage.Korean, ThemeMode.Light);
            formats.SelectedItem = format;
            toggle.PerformClick();
            await Until(() => toggle.Text == Ui.T("Stop & edit", "종료 후 편집") && toggle.Enabled, $"{format} capture did not start.");
            using (var colors = new System.Windows.Forms.Timer { Interval = 75 })
            {
                var hue = 0;
                colors.Tick += (_, _) => fixture.BackColor = Color.FromArgb(hue++ * 23 % 256, 130, 205);
                colors.Start();
                await Task.Delay(1550);
            }
            toggle.PerformClick();
            await Until(() => Application.OpenForms.OfType<AnimationEditor>().Any(), $"{format} editor did not open.");
            var editor = Application.OpenForms.OfType<AnimationEditor>().Single();
            try
            {
                if (korean)
                    Assert(editor.Text == "WEBP 녹화 편집" && editor.BackColor == Theme.Background && !Theme.IsDark,
                        "The editor did not inherit Korean and Light preferences.");
                var timeline = Field<FrameSlider>(editor, "timeline");
                await Until(() => timeline.Enabled && timeline.Maximum >= 10, $"{format} timeline did not load.");
                VerifyQualitySliderHold(editor);
                var strip = Field<FrameStrip>(editor, "thumbnails");
                var scroll = strip.Controls.OfType<FrameSlider>().Single();
                Assert(scroll.Enabled && scroll.Maximum + scroll.PageStep - 1 == timeline.Maximum, "The frame strip cannot scroll through all frames.");
                DragSliderToEnd(scroll);
                Assert(scroll.Value == scroll.Maximum, "Dragging the scrollbar did not reach the final page.");
                Invoke(strip, "OnMouseClick", new MouseEventArgs(MouseButtons.Left, 1, (timeline.Maximum - scroll.Value) * 92 + 8, 15, 0));
                Assert(timeline.Value == timeline.Maximum, "The last frame was not individually selectable.");
                Invoke(timeline, "OnKeyDown", new KeyEventArgs(Keys.Home));
                Assert(timeline.Value == 0, "The scrubber did not respond to Home.");
                DragSliderToEnd(timeline);
                Assert(timeline.Value == timeline.Maximum, "Dragging the scrubber did not reach the final frame.");
                timeline.Value = 0;
                var play = Field<DarkButton>(editor, "play");
                play.PerformClick();
                Assert(play.Glyph == "Pause", "The Play button did not switch to Pause.");
                await Until(() => timeline.Value > 0, $"{format} playback did not advance.");
                play.PerformClick();
                var pausedAt = timeline.Value;
                await Task.Delay(160);
                Assert(timeline.Value == pausedAt && play.Glyph == "Play", $"{format} playback did not pause.");
                timeline.Value = 2;
                FindButton(editor, Ui.T("Set start", "시작 지정")).PerformClick();
                timeline.Value = 6;
                FindButton(editor, Ui.T("Set end", "끝 지정")).PerformClick();
                Descendants(editor).OfType<DarkButton>().Single(button => button.Glyph == "BackwardStep").PerformClick();
                Assert(timeline.Value == 2, "First selected frame did not seek.");
                Descendants(editor).OfType<DarkButton>().Single(button => button.Glyph == "ForwardStep").PerformClick();
                Assert(timeline.Value == 6, "Last selected frame did not seek.");
                var previous = 2;
                var loops = 0;
                timeline.ValueChanged += (_, _) =>
                {
                    var frame = timeline.Value;
                    if (frame < previous) loops++;
                    previous = frame;
                };
                play.PerformClick();
                await Until(() => loops >= 2 || play.Glyph == "Play", $"{format} playback did not complete multiple loops.");
                Assert(loops >= 2 && play.Glyph == "Pause", $"{format} playback stopped at the end of the selected range.");
                play.PerformClick();
                VerifyCropHandles(editor);
                Field<QualitySlider>(editor, "quality").Value = 43;
                var encoded = await Field<Task<string?>>(editor, "estimate").WaitAsync(TimeSpan.FromSeconds(20));
                Assert(encoded is not null && File.Exists(encoded), $"{format} edit did not encode: {Field<string?>(editor, "encodeError")}");
                Assert(Field<Label>(editor, "sizeLabel").Text.StartsWith(Ui.T("Estimated size", "예상 용량")), "No size estimate appeared.");
                File.Copy(encoded!, Path.Combine(output, $"editor-trim-crop.{format.ToLowerInvariant()}"), true);
                using var screenshot = new Bitmap(editor.Width, editor.Height);
                editor.DrawToBitmap(screenshot, editor.Bounds with { Location = Point.Empty });
                screenshot.Save(Path.Combine(output, $"editor-{format.ToLowerInvariant()}.png"));
                editor.Size = editor.MinimumSize;
                editor.PerformLayout();
                foreach (var button in Descendants(editor).OfType<DarkButton>())
                    Assert(button.Parent!.ClientRectangle.Contains(button.Bounds), $"Editor button clipped at minimum size: {button.AccessibleName}.");
                var frameLabel = Field<Label>(editor, "frameLabel");
                Assert(frameLabel.Parent!.ClientRectangle.Contains(frameLabel.Bounds) && frameLabel.GetPreferredSize(Size.Empty).Width <= frameLabel.Width,
                    "Frame status was clipped at minimum editor size.");
                using var minimum = new Bitmap(editor.Width, editor.Height);
                editor.DrawToBitmap(minimum, new Rectangle(Point.Empty, editor.Size));
                minimum.Save(Path.Combine(output, $"editor-{format.ToLowerInvariant()}-minimum.png"));
            }
            finally { FindButton(editor, Ui.T("Cancel", "취소")).PerformClick(); }
            await Until(() => Field<Recorder?>(form, "recorder") is null, $"Cancelling {format} did not return to the main window.");
            Assert(Field<Label>(form, "status").Text == Ui.T("Editing cancelled.", "편집을 취소했습니다."), "Cancelling the editor did not discard the export.");
        }
        ChangePreferences(form, UiLanguage.English, ThemeMode.Dark);
        formats.SelectedItem = "MP4";
    }

    private static void DragSliderToEnd(FrameSlider slider)
    {
        var y = slider.Height / 2;
        Invoke(slider, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 13, y, 0));
        Invoke(slider, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, slider.Width - 1, y, 0));
        Invoke(slider, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, slider.Width - 1, y, 0));
    }

    private static void VerifyQualitySliderHold(AnimationEditor editor)
    {
        var slider = Field<QualitySlider>(editor, "quality");
        slider.Value = 75;
        var at75 = slider.PointToScreen(Point.Empty);
        slider.Value = 76;
        var at76 = slider.PointToScreen(Point.Empty);
        var fixedPointer = new Point(at75.X + 87, at75.Y + slider.Height / 2);
        var press = slider.PointToClient(fixedPointer);
        Invoke(slider, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, press.X, press.Y, 0));
        var heldValue = slider.Value;
        try
        {
            for (var i = 0; i < 8; i++)
            {
                var pointer = slider.PointToClient(fixedPointer);
                Invoke(slider, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, pointer.X, pointer.Y, 0));
                Assert(slider.Value == heldValue, "Holding the pointer still changed the quality value.");
            }
        }
        finally { Invoke(slider, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, press.X, press.Y, 0)); }
        slider.Value = 100;
        Assert(slider.PointToScreen(Point.Empty) == at75 && at75 == at76, "The changing percentage label moved the quality slider under the pointer.");
    }

    private static void VerifyCropHandles(AnimationEditor editor)
    {
        var preview = Field<PictureBox>(editor, "preview");
        var image = (Rectangle)Invoke(editor, "ImageBounds")!;
        var reset = FindButton(editor, Ui.T("Reset crop", "크롭 초기화"));
        var full = Field<Rectangle>(editor, "crop");
        using (var screenshot = new Bitmap(preview.Width, preview.Height))
        {
            preview.DrawToBitmap(screenshot, preview.ClientRectangle);
            var mid = image.Left + image.Width / 2;
            Assert(screenshot.GetPixel(mid, image.Top - 2).ToArgb() == Theme.Accent.ToArgb() &&
                screenshot.GetPixel(mid, image.Top + 2).ToArgb() == Theme.Accent.ToArgb(),
                "The top crop handle did not straddle the preview border.");
        }
        foreach (var horizontal in new[] { -1, 0, 1 })
        foreach (var vertical in new[] { -1, 0, 1 })
        {
            if (horizontal == 0 && vertical == 0) continue;
            reset.PerformClick();
            Assert(Field<Rectangle>(editor, "crop") == full, "Reset crop did not restore the full frame.");
            var x = image.Left + (horizontal + 1) * image.Width / 2;
            var y = image.Top + (vertical + 1) * image.Height / 2;
            x = Math.Clamp(x, 5, preview.Width - 6);
            y = Math.Clamp(y, 5, preview.Height - 6);
            Invoke(preview, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
            Invoke(preview, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, x - horizontal * 24, y - vertical * 24, 0));
            Invoke(preview, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, x - horizontal * 24, y - vertical * 24, 0));
            var crop = Field<Rectangle>(editor, "crop");
            Assert((horizontal < 0 ? crop.Left > 0 : crop.Left == 0) &&
                (horizontal > 0 ? crop.Right < full.Right : crop.Right == full.Right) &&
                (vertical < 0 ? crop.Top > 0 : crop.Top == 0) &&
                (vertical > 0 ? crop.Bottom < full.Bottom : crop.Bottom == full.Bottom),
                "Dragging a crop handle did not move only its attached edges.");
        }
        var before = Field<Rectangle>(editor, "crop");
        var selection = (RectangleF)Invoke(editor, "CropBounds", before)!;
        var center = new Point((int)(selection.Left + selection.Width / 2), (int)(selection.Top + selection.Height / 2));
        var outside = new Point(center.X + image.Width, center.Y + image.Height);
        Invoke(preview, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, center.X, center.Y, 0));
        Invoke(preview, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, outside.X, outside.Y, 0));
        Invoke(preview, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, outside.X, outside.Y, 0));
        var moved = Field<Rectangle>(editor, "crop");
        Assert(moved.Size == before.Size && moved.Location != before.Location && moved.Right == full.Right && moved.Bottom == full.Bottom,
            "Moving the crop box changed its size or exceeded the source bounds.");
    }
    private static void ValidateMask(SelectionOverlay overlay, Rectangle selected)
    {
        var border = Field<Form>(overlay, "border");
        var point = new Point(selected.Left - border.Left + selected.Width / 2, selected.Top - border.Top + selected.Height / 2);
        Assert(border.Visible && border.Region is not null && !border.Region.IsVisible(point), "Border covered selected pixels.");
        Assert(!border.Region!.IsVisible(0, 0), "Frame covered outside pixels.");
        Assert(border.Region.IsVisible(selected.Left - border.Left - 2, selected.Top - border.Top + 10), "Region frame was missing.");
    }
    private static void AssertControlsFit(MainForm form)
    {
        foreach (var name in new[] { "start", "gear", "badge", "folder", "fileNamePattern", "browse", "info", "fps", "audio", "cursor" })
        {
            var control = Field<Control>(form, name);
            Assert(control.Width > 0 && control.Height > 0, $"{name} collapsed.");
            for (var child = control; child.Parent is { } parent; child = parent)
                Assert(parent.ClientRectangle.Contains(child.Bounds), $"{name} was clipped inside {parent.GetType().Name}.");
        }
        var buttons = Field<DarkButton[]>(form, "modeButtons").Append(Field<DarkButton>(form, "select")).Append(Field<DarkButton>(form, "start"));
        foreach (var button in buttons.Where(button => button.Enabled)) AssertButtonContentCentered(button);
    }

    private static void AssertButtonContentCentered(DarkButton button)
    {
        using var bitmap = new Bitmap(button.Width, button.Height);
        button.DrawToBitmap(bitmap, button.ClientRectangle);
        var foreground = button.Primary ? Theme.OnAccent : button.Selected ? Theme.Accent : Theme.Text;
        var left = bitmap.Width;
        var right = -1;
        for (var y = 8; y < bitmap.Height - 8; y++)
        for (var x = 6; x < bitmap.Width - 6; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            var difference = Math.Abs(pixel.R - foreground.R) + Math.Abs(pixel.G - foreground.G) + Math.Abs(pixel.B - foreground.B);
            if (difference > 100) continue;
            left = Math.Min(left, x);
            right = Math.Max(right, x);
        }
        Assert(right >= left && Math.Abs((left + right) / 2.0 - (bitmap.Width - 1) / 2.0) <= 5,
            $"Button content is off-center: {button.Text} ({left}–{right} in {bitmap.Width}px).");
    }
    private static void Capture(Form form, string path)
    {
        form.Activate(); form.Refresh(); Application.DoEvents();
        using var bitmap = new Bitmap(form.Width, form.Height); using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(form.Location, Point.Empty, bitmap.Size); bitmap.Save(path);
    }
    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    private static Button FindButton(Form form, string text) => Descendants(form).OfType<Button>().First(b => b.Text == text);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static object? Invoke(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Until(Func<bool> condition, string message) => await UntilAsync(() => Task.FromResult(condition()), message);
    private static async Task UntilAsync(Func<Task<bool>> condition, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (!await condition()) { if (DateTime.UtcNow > deadline) throw new Exception(message); await Task.Delay(50); }
    }
}
