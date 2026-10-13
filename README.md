# B01 Recorder

A minimal screen recorder for Windows. Capture your full screen, a window, or a selected region as **MP4 · GIF · WebP**.

[**View latest release**](https://github.com/BE0X01/b01-recorder/releases/latest)

## Preview

![B01 Recorder — dark minimal interface](docs/images/preview.png)

## Features

- **Three capture modes** — full screen, individual window, or selected region
- **MP4 · GIF · WebP** — adjustable quality for GIF and WebP
- **Compact controls** — three settings sections with no settings scrolling at the minimum window size
- **Frame rate controls** — custom rates from 1 to 120 FPS
- **System audio** — include PC audio in MP4 recordings, with optional cursor capture
- **Clear capture outlines** — teal borders without dimming the desktop
- **Recording library** — thumbnails, in-app previews, external playback, folder access, and Recycle Bin deletion
- **Portable storage** — settings and caches stay beside the executable
- **Remembered window** — restores window size, position, and maximized state; recovers onto a connected display
- **Custom filenames** — date/time tags, automatic numbering, a live example, and hover help

## Getting started

1. Download the ZIP from the latest release and extract it.
2. Run `Install-FFmpeg.cmd` to download FFmpeg directly from its distributor. The script verifies the download before placing `ffmpeg.exe` and `ffprobe.exe` beside the app. Alternatively, download the [essentials build](https://www.gyan.dev/ffmpeg/builds/) yourself and copy those two files into the app folder.
3. Run `b01-recorder.exe`.
4. Choose `Full screen`, `Window`, or `Region`, then select your capture target.
5. Set the output format, frame rate, quality, and output folder. Use the `···` button to choose a folder; recordings save to your Desktop by default.
6. Click `Record` to start. The same button changes to `Stop & save`; click it to finish and save.

**Selecting a capture target does not start recording.** The app stays visible when recording begins.

Select a thumbnail to preview a saved recording. Use `Play in app` for in-app playback. The `More` menu contains external playback, folder access, and `Delete`, which moves the file to the Recycle Bin after confirmation.

The default filename pattern is `record_%y%m%d-%n`, producing names such as `record_261014-01.mp4`. `%n` selects an available number starting at `01`; existing files are never overwritten. Customize the pattern in **Save to** and hover over the information icon for supported tags. The extension is added automatically, and date/time tags use the recording's start time.

## Requirements and storage

Supports Windows 10/11 **x64**. The .NET runtime is included. FFmpeg is obtained separately using the setup step above. In-app previews require [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/).

| Item | Location |
| --- | --- |
| Recordings | Your chosen folder; Desktop by default |
| Last-used settings | `setting.ini` beside the executable; saved once when the app exits |
| Thumbnails and player cache | The `data` folder beside the executable |

System audio is supported in MP4 recordings. Keep the target window restored during window capture. Long GIF or WebP recordings may take time to save.

## License

[MIT](LICENSE). See [Third-party notices](THIRD-PARTY-NOTICES.md) for licenses covering FFmpeg and other included components.
