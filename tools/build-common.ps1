Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:ProjectRoot = Split-Path $PSScriptRoot -Parent
$script:ToolsRoot = Join-Path $script:ProjectRoot '.tools'
$script:OfflineBuild = $false
New-Item -ItemType Directory -Force $script:ToolsRoot | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable failed (exit $LASTEXITCODE)." }
}

function Reset-BuildOutput {
    param([ValidateSet('windows', 'Dis-Complete')][string]$Name)
    $root = [IO.Path]::GetFullPath((Join-Path $script:ProjectRoot 'artifacts'))
    $target = [IO.Path]::GetFullPath((Join-Path $root $Name))
    if ([IO.Path]::GetDirectoryName($target) -ne $root -or [IO.Path]::GetFileName($target) -ne $Name) { throw 'Refusing to clean a directory outside the generated build output.' }
    foreach ($path in @($root, $target)) {
        if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Build output must not be a link: $path" }
    }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    New-Item -ItemType Directory -Force $target | Out-Null
    return $target
}

function Get-VerifiedDownload {
    param([string]$Url, [string]$Name, [string]$Hash, [string]$Algorithm = 'SHA256')
    $file = Join-Path $script:ToolsRoot $Name
    if (Test-Path -LiteralPath $file) {
        if ((Get-FileHash -LiteralPath $file -Algorithm $Algorithm).Hash -eq $Hash) { return $file }
        if ($script:OfflineBuild) { throw "Cached $Name failed its checksum. Run once without -Offline to repair it." }
    } elseif ($script:OfflineBuild) { throw "$Name is not cached. Run once without -Offline." }
    Write-Host "Downloading $Name from $(([Uri]$Url).Host) ..."
    $partial = "$file.partial"
    if (Get-Command curl.exe -ErrorAction SilentlyContinue) {
        Invoke-Checked 'curl.exe' @('--fail', '--location', '--retry', '2', '--connect-timeout', '20', '--output', $partial, $Url)
    } else {
        $oldProgress = $ProgressPreference; $ProgressPreference = 'SilentlyContinue'
        try { Invoke-WebRequest -Uri $Url -OutFile $partial -UseBasicParsing } finally { $ProgressPreference = $oldProgress }
    }
    if ((Get-FileHash -LiteralPath $partial -Algorithm $Algorithm).Hash -ne $Hash) { throw "Checksum mismatch for $Name. The download was not used." }
    Move-Item -LiteralPath $partial -Destination $file -Force
    return $file
}

function Initialize-BuildEnvironment {
    param([switch]$Offline)
    Set-Location $script:ProjectRoot
    $script:OfflineBuild = $Offline.IsPresent
    $env:DOTNET_CLI_HOME = Join-Path $script:ToolsRoot 'dotnet-home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    $env:NUGET_PACKAGES = Join-Path $script:ToolsRoot 'nuget-packages'
    $env:GRADLE_USER_HOME = Join-Path $script:ToolsRoot 'gradle-home'
    $env:ANDROID_USER_HOME = Join-Path $script:ToolsRoot 'android-user'
    $script:NugetConfig = Join-Path $script:ToolsRoot 'NuGet.Build.Config'
    @'
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>
'@ | Set-Content -LiteralPath $script:NugetConfig -Encoding UTF8
}

function Find-DotnetSdk {
    $candidates = @((Join-Path $script:ToolsRoot 'dotnet\dotnet.exe'))
    $installed = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($installed) { $candidates += $installed.Source }
    foreach ($candidate in $candidates) {
        if ((Test-Path -LiteralPath $candidate) -and ((& $candidate --list-sdks) -match '^10\.0\.')) { return $candidate }
    }
    if ($script:OfflineBuild) { throw '.NET 10 SDK is not installed or cached.' }
    Write-Host 'Preparing portable .NET SDK 10.0.100...'
    $releases = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
    $sdk = @($releases.releases | ForEach-Object { $_.sdks } | Where-Object { $_.version -eq '10.0.100' })[0]
    $asset = $sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
    if (-not $asset) { throw 'Microsoft did not publish the pinned SDK archive metadata.' }
    $zip = Get-VerifiedDownload $asset.url 'dotnet-sdk-10.0.100.zip' $asset.hash 'SHA512'
    Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $script:ToolsRoot 'dotnet') -Force
    return Join-Path $script:ToolsRoot 'dotnet\dotnet.exe'
}

function Find-Cmake {
    $installed = Get-Command cmake.exe -ErrorAction SilentlyContinue
    if ($installed) { return $installed.Source }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if ($vs) {
            $cmake = Join-Path $vs 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
            if (Test-Path -LiteralPath $cmake) { return $cmake }
        }
    }
    throw 'Install Visual Studio Build Tools 2022 with Desktop development with C++ (including CMake and Windows SDK). Reopen this build; no Developer Command Prompt or GitHub account is needed.'
}

function Find-QtKit {
    param([string]$QtRoot)
    $candidates = @($QtRoot, $env:QT_ROOT, (Join-Path (Split-Path $script:ProjectRoot -Parent) 'EngineTest\deps\qt'), 'C:\Qt')
    foreach ($candidate in $candidates) {
        if (-not $candidate -or -not (Test-Path -LiteralPath $candidate)) { continue }
        $kits = @($candidate, (Join-Path $candidate 'msvc2022_64'))
        $kits += @(Get-ChildItem -LiteralPath $candidate -Directory | ForEach-Object { Join-Path $_.FullName 'msvc2022_64' })
        foreach ($kit in $kits) {
            if ((Test-Path -LiteralPath (Join-Path $kit 'lib\cmake\Qt6\Qt6Config.cmake')) -and (Test-Path -LiteralPath (Join-Path $kit 'bin\windeployqt.exe'))) { return (Resolve-Path -LiteralPath $kit).Path }
        }
    }
    throw 'Qt 6.8+ MSVC x64 was not found. Pass -QtRoot "C:\path\to\Qt\6.8.3\msvc2022_64", set QT_ROOT, or use -Classic for the diagnostic WinForms build.'
}

function Find-JavaSdk {
    $candidates = @($env:JAVA_HOME, (Join-Path $env:ProgramFiles 'Android\Android Studio\jbr'))
    $jdkRoot = Join-Path $script:ToolsRoot 'jdk'
    if (Test-Path -LiteralPath $jdkRoot) { $candidates += @(Get-ChildItem -LiteralPath $jdkRoot -Directory | ForEach-Object FullName) }
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath (Join-Path $candidate 'bin\javac.exe'))) {
            $version = & (Join-Path $candidate 'bin\javac.exe') -version 2>&1
            if ("$version" -match 'javac (17|21)\.') { return $candidate }
        }
    }
    $zip = Get-VerifiedDownload 'https://aka.ms/download-jdk/microsoft-jdk-17.0.17-windows-x64.zip' 'jdk-17.zip' '7bf59e0bab043c1869303aff89ce9c76982561ec9a0d74ae868ac65468822466'
    Expand-Archive -LiteralPath $zip -DestinationPath $jdkRoot -Force
    return (Get-ChildItem -LiteralPath $jdkRoot -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'bin\javac.exe') } | Select-Object -First 1).FullName
}

function Find-Gradle {
    $batch = Join-Path $script:ToolsRoot 'gradle-8.9\bin\gradle.bat'
    if (-not (Test-Path -LiteralPath $batch)) {
        $zip = Get-VerifiedDownload 'https://downloads.gradle.org/distributions/gradle-8.9-bin.zip' 'gradle-8.9-bin.zip' 'd725d707bfabd4dfdc958c624003b3c80accc03f7037b5122c4b1d0ef15cecab'
        Expand-Archive -LiteralPath $zip -DestinationPath $script:ToolsRoot -Force
    }
    return $batch
}

function Write-BuildManifest {
    param([string]$Directory, [hashtable]$Tools)
    $files = @(Get-ChildItem -LiteralPath $Directory -File -Recurse | Where-Object Name -ne 'build-manifest.json' | ForEach-Object {
        @{ path = $_.FullName.Substring($Directory.Length + 1).Replace('\', '/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    @{ builtUtc = [DateTime]::UtcNow.ToString('o'); tools = $Tools; files = $files } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Directory 'build-manifest.json') -Encoding UTF8
}

function New-UsbToolBundle {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $candidates = @()
    foreach ($sdk in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, (Join-Path $script:ToolsRoot 'android-sdk'))) {
        if ($sdk) { $candidates += Join-Path $sdk 'platform-tools' }
    }
    if ($env:LOCALAPPDATA) { $candidates += Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools' }
    $installed = Get-Command adb.exe -ErrorAction SilentlyContinue
    if ($installed) { $candidates += Split-Path $installed.Source -Parent }
    $required = @('adb.exe','AdbWinApi.dll','AdbWinUsbApi.dll','NOTICE.txt')
    foreach ($candidate in $candidates) {
        $missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $candidate $_)) })
        if ($missing.Count -eq 0) {
            $bundle = Join-Path $script:ToolsRoot 'Dis-usb-tools.zip'
            $files = @($required | ForEach-Object { Join-Path $candidate $_ })
            Compress-Archive -LiteralPath $files -DestinationPath $bundle -Force
            return $bundle
        }
    }
    throw 'Build-time Android Platform Tools were not found. Run BUILD_ANDROID.cmd once to prepare the SDK, then retry. Published apps contain the USB tools automatically.'
}

function Test-NativeExecutable {
    param([string]$Executable, [string]$Arguments)
    $check = New-Object System.Diagnostics.Process
    $check.StartInfo.FileName = $Executable
    $check.StartInfo.Arguments = $Arguments
    $check.StartInfo.UseShellExecute = $false
    $check.StartInfo.CreateNoWindow = $true
    $check.StartInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $check.StartInfo.RedirectStandardError = $true
    try {
        if (-not $check.Start()) { throw "Could not start $Executable" }
        $errorOutput = $check.StandardError.ReadToEndAsync()
        if (-not $check.WaitForExit(60000)) { $check.Kill(); throw "Verification timed out: $Executable" }
        if ($check.ExitCode -ne 0) { throw "Verification failed: $Executable ($($check.ExitCode)). $($errorOutput.Result)" }
    } finally { $check.Dispose() }
}
