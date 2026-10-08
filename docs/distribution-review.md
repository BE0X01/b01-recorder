# Distribution review - 2026-10-09

Scope: public repository main branch, Version 0.1/0.2 release assets, resolved NuGet package inventory, included license notices, and direct FFmpeg supplier information. This is an engineering distribution review, not a legal opinion or a complete security/patent audit.

## Findings and corrections

1. Both original releases redistributed a GPL v3 FFmpeg essentials build. Its own FFmpeg revision was identified, but exact source and build materials for all external libraries in that binary were not established. Merely adding the main FFmpeg source ZIP would not resolve that gap. Corrected release archives exclude ffmpeg.exe and ffprobe.exe and instead contain a script that downloads them directly from the supplier for the user's local installation. The supplier's original license/build files remain with that download.
2. The WebView2 SDK notice was mislabeled as Microsoft software license terms. The exact 1.0.4258.31 NuGet package LICENSE.txt contains BSD 3-Clause terms. The notice now distinguishes that SDK from the separately installed Microsoft WebView2 Runtime.
3. README and release descriptions previously stated that FFmpeg was included. They now explain the separate setup requirement. Build packaging excludes both FFmpeg binaries even if they exist in the local development output.
4. The missing-FFmpeg error referenced a removed UI button. Its instruction now points to the setup script or manual placement beside the executable.

## Checks

- Public main matched the local reviewed commit before changes. GitHub recognizes the project license as MIT.
- Resolved packages: NAudio, NAudio.Asio, NAudio.Core, NAudio.Midi, NAudio.Wasapi, NAudio.WinForms, NAudio.WinMM (2.2.1), and Microsoft.Web.WebView2 (1.0.4258.31).
- NAudio MIT, .NET license/third-party notices, and WebView2 SDK license/notice files are retained in the corrected packages.
- No matches for common GitHub token or private-key-header patterns in tracked files. This limited check does not prove the absence of all secrets.
- Original release assets are backed up locally before replacement. Replacement checksums are generated and compared to GitHub's uploaded asset digests.
- The existing historical source tags remain unchanged; the correction commit is linked from both release notes.

References:
- FFmpeg supplier build details: https://www.gyan.dev/ffmpeg/builds/
- FFmpeg licensing: https://ffmpeg.org/legal.html
- WebView2 SDK package license: https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4258.31/License
- NAudio 2.2.1 license: https://github.com/naudio/NAudio/blob/v2.2.1/license.txt

Remaining limits: this change does not certify every dependency as vulnerability-free or resolve jurisdiction-specific codec patent questions. It also does not retroactively prove compliance of earlier downloads. Re-bundling FFmpeg requires a separate, complete corresponding-source review.
