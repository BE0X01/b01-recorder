param([switch]$SkipDownload)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskRoot
$taskVersion = '0.2'
$taskFfmpegVersion = '9.0.2'
$taskArchiveHash = '60F467265B1E312373DBCD92200C2618A74850F98D3D078E94296BB3FA2047BA'
$taskTools = Join-Path $taskRoot '.tools'
$taskBundle = Join-Path $taskRoot "artifacts/b01-recorder-v$taskVersion-win-x64"
$taskArchive = Join-Path $taskTools 'ffmpeg.zip'
$taskFfmpegRoot = Join-Path $taskTools "ffmpeg/ffmpeg-$taskFfmpegVersion-essentials_build"
New-Item -ItemType Directory -Force $taskTools, $taskBundle | Out-Null
if (!(Test-Path -LiteralPath $taskArchive)) {
    if ($SkipDownload) { throw 'FFmpeg archive missing.' }
    Invoke-WebRequest "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-$taskFfmpegVersion-essentials_build.zip" -OutFile $taskArchive
}
if ((Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash -ne $taskArchiveHash) { throw 'FFmpeg archive checksum mismatch.' }
if (!(Test-Path -LiteralPath $taskFfmpegRoot)) { Expand-Archive -LiteralPath $taskArchive -DestinationPath (Join-Path $taskTools 'ffmpeg') -Force }
& (Join-Path $PSScriptRoot 'create-icon.ps1')
dotnet publish src/B01Recorder/B01Recorder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $taskBundle
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
Copy-Item -LiteralPath (Join-Path $taskFfmpegRoot 'bin/ffmpeg.exe') -Destination $taskBundle
Copy-Item -LiteralPath (Join-Path $taskFfmpegRoot 'bin/ffprobe.exe') -Destination $taskBundle
Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md'), (Join-Path $taskRoot 'LICENSE'), (Join-Path $taskRoot 'THIRD-PARTY-NOTICES.md') -Destination $taskBundle
$taskLicenses = Join-Path $taskBundle 'licenses'
New-Item -ItemType Directory -Force $taskLicenses | Out-Null
Copy-Item -LiteralPath (Join-Path $taskFfmpegRoot 'LICENSE') -Destination (Join-Path $taskLicenses 'FFmpeg-GPL-v3.txt')
Copy-Item -LiteralPath (Join-Path $taskFfmpegRoot 'README.txt') -Destination (Join-Path $taskLicenses 'FFmpeg-build.txt')
$taskPackageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages' }
Copy-Item -LiteralPath (Join-Path $taskPackageRoot 'naudio/2.2.1/license.txt') -Destination (Join-Path $taskLicenses 'NAudio-MIT.txt')
$taskRuntimeRoot = Get-ChildItem -LiteralPath (Join-Path $taskPackageRoot 'microsoft.netcore.app.runtime.win-x64') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
Copy-Item -LiteralPath (Join-Path $taskRuntimeRoot.FullName 'LICENSE.TXT') -Destination (Join-Path $taskLicenses 'dotnet-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $taskRuntimeRoot.FullName 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $taskLicenses 'dotnet-ThirdPartyNotices.txt')
Copy-Item -LiteralPath (Join-Path $taskPackageRoot 'microsoft.web.webview2/1.0.4258.31/LICENSE.txt') -Destination (Join-Path $taskLicenses 'WebView2-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $taskPackageRoot 'microsoft.web.webview2/1.0.4258.31/NOTICE.txt') -Destination (Join-Path $taskLicenses 'WebView2-NOTICE.txt')
# FFmpeg is kept in the local development folder, but is never bundled for distribution.
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-FFmpeg.ps1'), (Join-Path $PSScriptRoot 'Install-FFmpeg.cmd') -Destination $taskBundle
$taskFiles = @('b01-recorder.exe', 'Install-FFmpeg.ps1', 'Install-FFmpeg.cmd', 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'licenses') | ForEach-Object { Join-Path $taskBundle $_ }
$taskZip = Join-Path $taskRoot "artifacts/b01-recorder-v$taskVersion-win-x64.zip"
Compress-Archive -LiteralPath $taskFiles -DestinationPath $taskZip -Force
$taskDigest = (Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash.ToLowerInvariant()
"$taskDigest  $(Split-Path -Leaf $taskZip)" | Set-Content -LiteralPath (Join-Path $taskRoot 'artifacts/SHA256SUMS.txt') -Encoding utf8NoBOM
$taskShortcut = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $taskRoot 'B01 Recorder.lnk'))
$taskShortcut.TargetPath = Join-Path $taskBundle 'b01-recorder.exe'
$taskShortcut.WorkingDirectory = $taskBundle
$taskShortcut.IconLocation = "$($taskShortcut.TargetPath),0"
$taskShortcut.Description = 'B01 Recorder'
$taskShortcut.Save()
Write-Output "Built: $taskZip"
