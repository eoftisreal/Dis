param([switch]$Offline, [switch]$SkipTests, [switch]$AcceptAndroidLicenses, [string]$QtRoot, [switch]$Classic)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
Set-Location $project
$flags = @()
if ($Offline) { $flags += '-Offline' }
if ($SkipTests) { $flags += '-SkipTests' }
$androidFlags = @($flags)
if ($AcceptAndroidLicenses) { $androidFlags += '-AcceptAndroidLicenses' }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\build-android.ps1" @androidFlags
if ($LASTEXITCODE -ne 0) { throw 'Android build failed. See artifacts/logs/android-build.log.' }
$windowsFlags = @($flags)
if ($QtRoot) { $windowsFlags += @('-QtRoot', $QtRoot) }
if ($Classic) { $windowsFlags += '-Classic' }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\build-windows.ps1" @windowsFlags
if ($LASTEXITCODE -ne 0) { throw 'Windows build failed. See artifacts/logs/windows-build.log.' }
. "$PSScriptRoot\build-common.ps1"
Initialize-BuildEnvironment -Offline:$Offline
$bundle = Reset-BuildOutput 'Dis-Complete'
Copy-Item -Path 'artifacts/windows/*' -Destination $bundle -Recurse -Force
Copy-Item -LiteralPath 'artifacts/android/Dis.apk' -Destination (Join-Path $bundle 'Dis.apk') -Force
Copy-Item -LiteralPath 'docs/QUICK_START.txt' -Destination (Join-Path $bundle 'START_HERE.txt') -Force
Write-BuildManifest $bundle @{ build = 'local'; windows = 'win-x64'; android = 'debug'; source = 'tools/build-all.ps1' }
$zip = Join-Path $project 'artifacts\Dis-Complete.zip'
Compress-Archive -Path "$bundle\*" -DestinationPath $zip -Force
if ($Classic) { Write-Host "Diagnostic portable package: $zip (single installer uses the Qt desktop)."; return }
$dotnet = Find-DotnetSdk
$setupRestore = @('restore', 'src/Dis.Setup/Dis.Setup.csproj', '-r', 'win-x64', '--configfile', $script:NugetConfig)
if ($Offline) { $setupRestore += @('--ignore-failed-sources', '-p:NuGetAudit=false') }
Invoke-Checked $dotnet $setupRestore
Invoke-Checked $dotnet @('publish', 'src/Dis.Setup/Dis.Setup.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', "-p:DisPayload=$zip", '--no-restore', '-o', (Join-Path $project 'artifacts\setup'))
$setup = Join-Path $project 'artifacts\Dis-Setup.exe'
Copy-Item -LiteralPath 'artifacts/setup/Dis-Setup.exe' -Destination $setup -Force
Test-NativeExecutable $setup '--verify-payload'
Write-Host "`nSingle installer: $setup`nPortable: $bundle\Dis.exe`nAndroid: $bundle\Dis.apk`nPackage: $zip" -ForegroundColor Green
