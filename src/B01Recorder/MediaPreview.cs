using System.Net;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace B01Recorder;

internal sealed class MediaPreview : Panel
{
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Theme.Background, Visible = false };
    private readonly Label placeholder = new() { Text = "Select a recording\n\nClick a thumbnail to preview it here.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted };
    private int generation;
    private bool initialized;
    private Task? initializing;
    public MediaPreview() { BackColor = Theme.Background; Controls.Add(browser); Controls.Add(placeholder); }
    private Task InitializeAsync()
    {
        if (initialized) return Task.CompletedTask;
        if (initializing is null || initializing.IsFaulted) initializing = InitializeCoreAsync();
        return initializing;
    }
    private async Task InitializeCoreAsync()
    {
        if (initialized) return;
        var dataFolder = Path.Combine(AppContext.BaseDirectory, "data", "webview");
        var environment = await CoreWebView2Environment.CreateAsync(null, dataFolder, new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
        await browser.EnsureCoreWebView2Async(environment);
        var settings = browser.CoreWebView2.Settings;
        settings.AreDefaultContextMenusEnabled = false; settings.AreDevToolsEnabled = false; settings.IsStatusBarEnabled = false;
        browser.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
        initialized = true;
    }
    public async Task LoadAsync(string file, bool play = false)
    {
        var request = ++generation;
        try
        {
            await InitializeAsync();
            if (request != generation || IsDisposed) return;
            browser.CoreWebView2.SetVirtualHostNameToFolderMapping("media.b01.local", Path.GetDirectoryName(Path.GetFullPath(file))!, CoreWebView2HostResourceAccessKind.DenyCors);
            var url = "https://media.b01.local/" + Uri.EscapeDataString(Path.GetFileName(file));
            var content = Path.GetExtension(file).Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                ? $"<video src=\"{WebUtility.HtmlEncode(url)}\" controls playsinline preload=\"auto\" {(play ? "autoplay" : "")}></video>"
                : $"<img src=\"{WebUtility.HtmlEncode(url)}\" alt=\"Recording preview\">";
            placeholder.Visible = false; browser.Visible = true; browser.BringToFront();
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Complete(object? sender, CoreWebView2NavigationCompletedEventArgs e) { if (e.IsSuccess) loaded.TrySetResult(); else loaded.TrySetException(new IOException("Could not load preview: " + e.WebErrorStatus)); }
            browser.CoreWebView2.NavigationCompleted += Complete;
            try
            {
                browser.NavigateToString("<!doctype html><html><head><meta charset='utf-8'><meta name='color-scheme' content='dark'><style>html,body{margin:0;width:100%;height:100%;background:#0d0e13;overflow:hidden}body{display:flex;align-items:center;justify-content:center}video,img{width:100%;height:100%;object-fit:contain}</style></head><body>" + content + "</body></html>");
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
            }
            finally { browser.CoreWebView2.NavigationCompleted -= Complete; }
        }
        catch (Exception ex)
        {
            if (request != generation || IsDisposed) return;
            browser.Visible = false; placeholder.Visible = true;
            placeholder.Text = "Could not open the in-app player.\nUse Open externally to play this recording.\n\n" + (ex is WebView2RuntimeNotFoundException ? "Microsoft Edge WebView2 Runtime is required." : ex.Message);
        }
    }
    public async Task PauseAsync()
    {
        if (initialized) await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('video')?.pause();");
    }
    public async Task ClearAsync()
    {
        generation++;
        if (initialized)
        {
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Complete(object? sender, CoreWebView2NavigationCompletedEventArgs e) => loaded.TrySetResult();
            browser.CoreWebView2.NavigationCompleted += Complete;
            try { browser.CoreWebView2.Navigate("about:blank"); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
            finally { browser.CoreWebView2.NavigationCompleted -= Complete; }
        }
        browser.Visible = false; placeholder.Text = "Select a recording\n\nClick a thumbnail to preview it here."; placeholder.Visible = true;
    }
    internal async Task<string> MediaStateAsync() => initialized ? await browser.CoreWebView2.ExecuteScriptAsync("JSON.stringify({video:!!document.querySelector('video'),ready:document.querySelector('video')?.readyState,time:document.querySelector('video')?.currentTime,paused:document.querySelector('video')?.paused,error:document.querySelector('video')?.error?.message,network:document.querySelector('video')?.networkState,src:document.querySelector('video')?.currentSrc,image:document.querySelector('img')?.naturalWidth,url:location.href})") : "null";
}
