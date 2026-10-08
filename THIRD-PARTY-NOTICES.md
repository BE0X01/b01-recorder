# Third-party components

b01-recorder source is MIT licensed. Separate bundled tools retain their own licenses.

- **FFmpeg / FFprobe 9.0.2**: unmodified Windows essentials build from https://www.gyan.dev/ffmpeg/builds/ . GPL v3. The full license and build configuration are in `licenses/FFmpeg-GPL-v3.txt` and `licenses/FFmpeg-build.txt`. The corresponding FFmpeg source is https://github.com/FFmpeg/FFmpeg/commit/946fcce07b . FFmpeg is executed as a separate process. Its enabled dependency list is included in the build configuration; upstream source repositories are linked from the supplier's builds page.
- **NAudio 2.2.1**: Mark Heath and contributors, MIT. The license is included in `licenses/NAudio-MIT.txt`. Source: https://github.com/naudio/NAudio/tree/v2.2.1 .
- **.NET 10 runtime and Windows Forms**: Microsoft and contributors, MIT with third-party notices. Licenses and notices are included in `licenses/dotnet-LICENSE.txt` and `licenses/dotnet-ThirdPartyNotices.txt`. Source: https://github.com/dotnet/runtime and https://github.com/dotnet/winforms .
- **Microsoft WebView2 SDK 1.0.4258.31**: Microsoft, Microsoft software license terms. Terms and notices are included in `licenses/WebView2-LICENSE.txt` and `licenses/WebView2-NOTICE.txt`. The separately installed Microsoft Edge WebView2 Runtime is used for in-app media playback. Documentation: https://learn.microsoft.com/microsoft-edge/webview2/ .
