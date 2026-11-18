# Third-party components

B01 Recorder source is licensed under [MIT](LICENSE). The application is free to download and use. Third-party components retain their own licenses; the MIT license does not replace them.

## Included in the application

- **NAudio 2.2.1** and its Asio, Core, Midi, Wasapi, WinForms, and WinMM modules: Mark Heath and contributors, MIT. Copyright and permission notice: `licenses/NAudio-MIT.txt`. Source: https://github.com/naudio/NAudio/tree/v2.2.1 .
- **.NET runtime and Windows Forms**: Microsoft and contributors, MIT with additional notices for incorporated components. Included files: `licenses/dotnet-LICENSE.txt` and `licenses/dotnet-ThirdPartyNotices.txt`. Source: https://github.com/dotnet/runtime and https://github.com/dotnet/winforms .
- **Pretendard 1.3.9**: Kil Hyung-jin and contributors, SIL Open Font License 1.1. The app embeds the official Regular and Bold TTF files. See `licenses/Pretendard-OFL.txt` and https://github.com/orioncactus/pretendard/releases/tag/v1.3.9 .
- **Remix Icon 4.9.1**: Remix Design, Remix Icon License v1.0. The app includes rasterized Line icons and uses `video-on-line` in the app icon. See `licenses/Remix-Icon-License.txt` and https://remixicon.com/ .

## Separately obtained runtime components

- **FFmpeg / FFprobe**: executed as separate processes. Corrected B01 Recorder release ZIPs do **not** include these binaries. `Install-FFmpeg.ps1` downloads the unmodified 9.0.2 essentials archive directly from https://www.gyan.dev/ffmpeg/builds/ and verifies its pinned SHA-256 before installation. The downloaded archive includes the supplier's GPL v3 license and build information under `data/ffmpeg/`. The corresponding FFmpeg revision advertised by the supplier is https://github.com/FFmpeg/FFmpeg/commit/946fcce07b .

The supplier's GPL v3 FFmpeg build incorporates external libraries. Do not redistribute the downloaded binaries as part of another package without meeting their license obligations, including providing complete corresponding source and applicable build materials. A link to the FFmpeg repository alone does not establish that all source for the supplier's complete build has been supplied. See https://ffmpeg.org/legal.html .

## Distribution correction

The original Version 0.1 and Version 0.2 archives bundled FFmpeg while complete corresponding source for the supplier build had not been verified. Those download assets were replaced with archives that exclude FFmpeg and include a separate setup script. Release notes and SHA-256 checksums identify the correction. This avoids continuing that unverified binary redistribution; it does not retroactively establish compliance for any copy distributed earlier.
