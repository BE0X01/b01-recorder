# Downloads FFmpeg directly from its upstream distributor for local use.
# FFmpeg binaries are not included in B01 Recorder release archives.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$setupRoot = $PSScriptRoot
$setupVersion = '9.0.2'
$setupHash = '60F467265B1E312373DBCD92200C2618A74850F98D3D078E94296BB3FA2047BA'
$setupUrl = "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-$setupVersion-essentials_build.zip"
$setupData = Join-Path $setupRoot 'data/ffmpeg'
$setupArchive = Join-Path $setupData "ffmpeg-$setupVersion.zip"
New-Item -ItemType Directory -Force -Path $setupData | Out-Null
if (!(Test-Path -LiteralPath $setupArchive)) {
    Write-Host 'Downloading FFmpeg directly from gyan.dev...'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $setupUrl -OutFile $setupArchive -UseBasicParsing
}
if ((Get-FileHash -LiteralPath $setupArchive -Algorithm SHA256).Hash -ne $setupHash) {
    throw "FFmpeg checksum mismatch. No executables were installed. Remove $setupArchive and retry."
}
$setupExtracted = Join-Path $setupData "ffmpeg-$setupVersion-essentials_build"
# Re-extract the verified archive rather than trusting previously extracted files.
Expand-Archive -LiteralPath $setupArchive -DestinationPath $setupData -Force
Copy-Item -LiteralPath (Join-Path $setupExtracted 'bin/ffmpeg.exe'), (Join-Path $setupExtracted 'bin/ffprobe.exe') -Destination $setupRoot -Force
Write-Host 'FFmpeg installed. You can now run b01-recorder.exe.'
Write-Host "FFmpeg GPL license and supplier build information: $setupExtracted"
