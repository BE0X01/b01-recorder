using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace B01Recorder;

internal static class RecordingLibrary
{
    public static IReadOnlyList<string> List(string folder, IEnumerable<string> remembered)
    {
        var files = new List<string>(remembered);
        if (Directory.Exists(folder))
            files.AddRange(Directory.EnumerateFiles(folder, "b01-*", System.IO.SearchOption.TopDirectoryOnly));
        return files.Where(File.Exists).Where(f => new[] { ".mp4", ".gif", ".webp" }.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(File.GetLastWriteTimeUtc).Take(60).ToList();
    }
    public static string ThumbnailPath(string file)
    {
        var info = new FileInfo(file);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(file) + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks)));
        return Path.Combine(AppContext.BaseDirectory, "data", "thumbnails", key + ".png");
    }
    public static async Task<string?> ThumbnailAsync(string ffmpeg, string file, CancellationToken cancellation = default)
    {
        var image = ThumbnailPath(file);
        if (File.Exists(image)) return image;
        Directory.CreateDirectory(Path.GetDirectoryName(image)!);
        var partial = image + "." + Guid.NewGuid().ToString("N") + ".png";
        var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-hide_banner", "-v", "error", "-y", "-i", file, "-frames:v", "1", "-vf", "scale=480:270:force_original_aspect_ratio=decrease,pad=480:270:(ow-iw)/2:(oh-ih)/2", partial }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var error = process.StandardError.ReadToEndAsync(cancellation);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await error;
            if (process.ExitCode != 0 || !File.Exists(partial)) return null;
            File.Move(partial, image, true); return image;
        }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); return null; }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
    public static Image LoadThumbnail(string image)
    {
        // Detach from the file, so deleting a recording never locks its thumbnail.
        using var source = Image.FromFile(image); return new Bitmap(source);
    }
    public static void Trash(string file)
    {
        var thumbnail = ThumbnailPath(file);
        FileSystem.DeleteFile(file, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        if (File.Exists(thumbnail)) File.Delete(thumbnail);
    }
}
