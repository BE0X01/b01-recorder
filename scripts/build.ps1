$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskRoot
$project = [xml](Get-Content -LiteralPath (Join-Path $taskRoot 'src/B01Recorder/B01Recorder.csproj') -Raw)
$projectVersion = [version]$project.Project.PropertyGroup.Version
$taskVersion = if ($projectVersion.Build -gt 0) { $projectVersion.ToString(3) } else { $projectVersion.ToString(2) }
$taskBundle = Join-Path $taskRoot "artifacts/b01-recorder-v$taskVersion-win-x64"
New-Item -ItemType Directory -Force $taskBundle | Out-Null
& (Join-Path $PSScriptRoot 'create-icon.ps1')
dotnet publish src/B01Recorder/B01Recorder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $taskBundle
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md'), (Join-Path $taskRoot 'LICENSE'), (Join-Path $taskRoot 'THIRD-PARTY-NOTICES.md') -Destination $taskBundle
$taskImages = Join-Path $taskBundle 'docs/images'
New-Item -ItemType Directory -Force $taskImages | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/images/preview.png'), (Join-Path $taskRoot 'docs/images/animation-editor.png') -Destination $taskImages
$taskLicenses = Join-Path $taskBundle 'licenses'
New-Item -ItemType Directory -Force $taskLicenses | Out-Null
$taskPackageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages' }
Copy-Item -LiteralPath (Join-Path $taskPackageRoot 'naudio/2.2.1/license.txt') -Destination (Join-Path $taskLicenses 'NAudio-MIT.txt')
$taskRuntimeRoot = Get-ChildItem -LiteralPath (Join-Path $taskPackageRoot 'microsoft.netcore.app.runtime.win-x64') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
Copy-Item -LiteralPath (Join-Path $taskRuntimeRoot.FullName 'LICENSE.TXT') -Destination (Join-Path $taskLicenses 'dotnet-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $taskRuntimeRoot.FullName 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $taskLicenses 'dotnet-ThirdPartyNotices.txt')
Copy-Item -LiteralPath (Join-Path $taskRoot 'src/B01Recorder/assets/fonts/LICENSE.txt') -Destination (Join-Path $taskLicenses 'Pretendard-OFL.txt')
Copy-Item -LiteralPath (Join-Path $taskRoot 'src/B01Recorder/assets/icons/LICENSE.txt') -Destination (Join-Path $taskLicenses 'Remix-Icon-License.txt')
# Users install FFmpeg separately with the included setup script.
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-FFmpeg.ps1'), (Join-Path $PSScriptRoot 'Install-FFmpeg.cmd') -Destination $taskBundle
$taskFiles = @('b01-recorder.exe', 'Install-FFmpeg.ps1', 'Install-FFmpeg.cmd', 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'docs', 'licenses') | ForEach-Object { Join-Path $taskBundle $_ }
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
