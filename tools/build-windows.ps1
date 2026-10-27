param([switch]$Offline, [switch]$SkipTests, [string]$QtRoot, [switch]$Classic)
. "$PSScriptRoot\build-common.ps1"
Initialize-BuildEnvironment -Offline:$Offline
$logRoot = Join-Path $script:ProjectRoot 'artifacts\logs'
New-Item -ItemType Directory -Force $logRoot | Out-Null
Start-Transcript -Path (Join-Path $logRoot 'windows-build.log') -Force | Out-Null
try {
    $dotnet = Find-DotnetSdk
    $usbTools = New-UsbToolBundle
    $cmake = Find-Cmake
    $qt = $null
    if (-not $Classic) { $qt = Find-QtKit $QtRoot; Write-Host "Qt desktop kit: $qt" }
    $restore = @('--configfile', $script:NugetConfig)
    if ($Offline) { $restore += @('--ignore-failed-sources', '-p:NuGetAudit=false'); Write-Host 'Offline build: online NuGet vulnerability audit is unavailable.' }
    if (-not $SkipTests) {
        Invoke-Checked $dotnet (@('restore', 'tests/Dis.Core.Tests') + $restore)
        Invoke-Checked $dotnet @('run', '--project', 'tests/Dis.Core.Tests', '-c', 'Release', '--no-restore')
    }
    Invoke-Checked $cmake @('-S', 'native', '-B', 'artifacts/native', '-A', 'x64')
    Invoke-Checked $cmake @('--build', 'artifacts/native', '--config', 'Release', '--parallel')
    if (-not $SkipTests) { Invoke-Checked (Join-Path $script:ProjectRoot 'artifacts/native/Release/DisCodecSmoke.exe') @('artifacts/native-smoke.h264') }
    Invoke-Checked $dotnet (@('restore', 'src/Dis.Host', '-r', 'win-x64', '-p:SelfContained=true') + $restore)
    $destination = Reset-BuildOutput 'windows'
    $assembly = if ($Classic) { 'Dis' } else { 'Dis.Engine' }
    Invoke-Checked $dotnet @('publish', 'src/Dis.Host/Dis.Host.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', "-p:DisHostAssemblyName=$assembly", "-p:DisUsbToolsArchive=$usbTools", '--no-restore', '-o', $destination)
    Test-NativeExecutable (Join-Path $destination "$assembly.exe") '--verify-usb-tools'
    $licenses = Join-Path $destination 'licenses'
    New-Item -ItemType Directory -Force $licenses | Out-Null
    $usbZip = [IO.Compression.ZipFile]::OpenRead($usbTools)
    try { [IO.Compression.ZipFileExtensions]::ExtractToFile($usbZip.GetEntry('NOTICE.txt'), (Join-Path $licenses 'Android-Platform-Tools-NOTICE.txt'), $true) }
    finally { $usbZip.Dispose() }
    if (-not $Classic) {
        Invoke-Checked $cmake @('-S', 'ui', '-B', 'artifacts/qt-desktop', '-A', 'x64', "-DCMAKE_PREFIX_PATH=$qt", "-DQt6_DIR=$qt/lib/cmake/Qt6")
        Invoke-Checked $cmake @('--build', 'artifacts/qt-desktop', '--config', 'Release', '--parallel')
        Copy-Item -LiteralPath 'artifacts/qt-desktop/Release/Dis.exe' -Destination (Join-Path $destination 'Dis.exe') -Force
        Invoke-Checked (Join-Path $qt 'bin\windeployqt.exe') @('--release', '--verbose', '0', '--no-translations', '--no-opengl-sw', '--no-compiler-runtime', '--no-patchqt', '--qmldir', (Join-Path $script:ProjectRoot 'ui\qml'), '--dir', $destination, (Join-Path $destination 'Dis.exe'))
        "[Paths]`nPrefix=.`nPlugins=.`nQmlImports=qml" | Set-Content -LiteralPath (Join-Path $destination 'qt.conf') -Encoding ASCII
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
        $vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        $crt = Get-ChildItem -LiteralPath (Join-Path $vs 'VC\Redist\MSVC') -Directory | Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'x64\Microsoft.VC143.CRT' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if ($crt) { Get-ChildItem -LiteralPath $crt -Filter '*.dll' | Copy-Item -Destination $destination -Force }
        else { throw 'The Visual C++ redistributable DLLs could not be located for portable Qt deployment.' }
        $licenses = Join-Path $destination 'licenses'
        New-Item -ItemType Directory -Force $licenses | Out-Null
        Copy-Item -Path 'third_party/Qt/*' -Destination $licenses -Force
        if (Test-Path -LiteralPath (Join-Path $qt 'sbom')) { Copy-Item -LiteralPath (Join-Path $qt 'sbom') -Destination (Join-Path $licenses 'Qt-SBOM') -Recurse -Force }
    }
    Copy-Item -LiteralPath 'artifacts/native/Release/Dis.Codec.dll' -Destination $destination -Force
    $driver = Join-Path $destination 'driver'
    New-Item -ItemType Directory -Force $driver | Out-Null
    $zip = Get-VerifiedDownload 'https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/download/25.7.23/VirtualDisplayDriver-x86.Driver.Only.zip' 'vdd-25.7.23.zip' 'e24210692b442b39af763536330ce78b423f19342b7a7792c26de3944e418b3a'
    Copy-Item -LiteralPath $zip -Destination (Join-Path $driver 'vdd-25.7.23.zip') -Force
    Copy-Item -LiteralPath 'third_party/VDD/LICENSE' -Destination (Join-Path $driver 'LICENSE-VDD.txt') -Force
    Copy-Item -LiteralPath 'driver/LICENSE' -Destination (Join-Path $driver 'LICENSE-Microsoft-sample.txt') -Force
    Copy-Item -LiteralPath 'docs/QUICK_START.txt' -Destination (Join-Path $destination 'START_HERE.txt') -Force
    Write-BuildManifest $destination @{ dotnet = (& $dotnet --version); cmake = (& $cmake --version | Select-Object -First 1); frontend = $(if($Classic){'WinForms diagnostic'}else{'Qt Quick'}); qt = $qt }
    Write-Host "`nBuilt $destination\Dis.exe" -ForegroundColor Green
} finally { Stop-Transcript | Out-Null }
