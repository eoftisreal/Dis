param([switch]$Offline, [switch]$SkipTests, [switch]$AcceptAndroidLicenses)
. "$PSScriptRoot\build-common.ps1"
Initialize-BuildEnvironment -Offline:$Offline
$logRoot = Join-Path $script:ProjectRoot 'artifacts\logs'
New-Item -ItemType Directory -Force $logRoot | Out-Null
Start-Transcript -Path (Join-Path $logRoot 'android-build.log') -Force | Out-Null
try {
    $env:JAVA_HOME = Find-JavaSdk
    $env:PATH = (Join-Path $env:JAVA_HOME 'bin') + ';' + $env:PATH
    $signingRoot = Join-Path $script:ToolsRoot 'signing'
    $key = Join-Path $signingRoot 'Dis-preview.keystore'
    if (-not (Test-Path -LiteralPath $key)) {
        New-Item -ItemType Directory -Force $signingRoot | Out-Null
        $existingDebugKey = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.android\debug.keystore'
        if (Test-Path -LiteralPath $existingDebugKey) { Copy-Item -LiteralPath $existingDebugKey -Destination $key }
        else { Invoke-Checked (Join-Path $env:JAVA_HOME 'bin\keytool.exe') @('-genkeypair', '-keystore', $key, '-storepass', 'android', '-keypass', 'android', '-alias', 'androiddebugkey', '-keyalg', 'RSA', '-keysize', '2048', '-validity', '10000', '-dname', 'CN=Android Debug,O=Android,C=US') }
    }
    $sdk = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, (Join-Path $env:LOCALAPPDATA 'Android\Sdk'), (Join-Path $script:ToolsRoot 'android-sdk')) |
        Where-Object { $_ -and (Test-Path -LiteralPath (Join-Path $_ 'platforms\android-35\android.jar')) } | Select-Object -First 1
    if (-not $sdk) { $sdk = Join-Path $script:ToolsRoot 'android-sdk' }
    $env:ANDROID_HOME = $sdk; $env:ANDROID_SDK_ROOT = $sdk
    $manager = Join-Path $sdk 'cmdline-tools\latest\bin\sdkmanager.bat'
    if (-not (Test-Path -LiteralPath (Join-Path $sdk 'platforms\android-35\android.jar')) -or -not (Test-Path -LiteralPath (Join-Path $sdk 'build-tools\35.0.0\aapt2.exe'))) {
        if ($Offline) { throw 'Android SDK platform 35 and build-tools 35.0.0 are not cached.' }
        if (-not (Test-Path -LiteralPath $manager)) {
            $zip = Get-VerifiedDownload 'https://dl.google.com/android/repository/commandlinetools-win-11479570_latest.zip' 'android-tools.zip' '4dafc3f63c6d29066b7706778efdbe9c7439f3b1a8c749d3b6a3f878cb926a0a'
            $unpacked = Join-Path $script:ToolsRoot 'android-cli'
            Expand-Archive -LiteralPath $zip -DestinationPath $unpacked -Force
            $latest = Join-Path $sdk 'cmdline-tools\latest'
            New-Item -ItemType Directory -Force $latest | Out-Null
            Copy-Item -Path (Join-Path $unpacked 'cmdline-tools\*') -Destination $latest -Recurse -Force
        }
        if ($AcceptAndroidLicenses) {
            # Only this explicit switch accepts Google's licenses.
            $licenseLog = Join-Path $logRoot 'android-licenses.log'
            Write-Host "Accepting approved Android SDK licenses; details: $licenseLog"
            1..100 | ForEach-Object { 'y' } | & $manager "--sdk_root=$sdk" --licenses *> $licenseLog
            if ($LASTEXITCODE -ne 0) { throw 'Android SDK license acceptance failed.' }
        } elseif (-not (Test-Path -LiteralPath (Join-Path $sdk 'licenses\android-sdk-license'))) {
            throw 'Read https://developer.android.com/studio/terms, then rerun BUILD_ANDROID.cmd -AcceptAndroidLicenses (or use sdkmanager --licenses interactively).'
        }
        Invoke-Checked $manager @("--sdk_root=$sdk", 'platforms;android-35', 'build-tools;35.0.0', 'platform-tools')
    }
    $gradle = Find-Gradle
    if (-not $SkipTests) {
        $classes = Join-Path $script:ProjectRoot 'artifacts\java-tests'
        New-Item -ItemType Directory -Force $classes | Out-Null
        Invoke-Checked (Join-Path $env:JAVA_HOME 'bin\javac.exe') @('-d', $classes, 'android/app/src/main/java/io/github/eoftisreal/dis/Wire.java', 'android/app/src/main/java/io/github/eoftisreal/dis/ConnectionSpec.java', 'android/app/src/main/java/io/github/eoftisreal/dis/CertificatePin.java', 'android/app/src/main/java/io/github/eoftisreal/dis/H264ParameterSets.java', 'android/app/src/main/java/io/github/eoftisreal/dis/StreamStats.java', 'android/app/src/main/java/io/github/eoftisreal/dis/TouchScrollGesture.java', 'android/app/src/main/java/io/github/eoftisreal/dis/InputQueue.java', 'tests/java/InputQueueTest.java', 'tests/java/WireTest.java', 'tests/java/StreamStatsTest.java', 'tests/java/TouchScrollGestureTest.java')
        Invoke-Checked (Join-Path $env:JAVA_HOME 'bin\java.exe') @('-cp', $classes, 'io.github.eoftisreal.dis.WireTest')
        Invoke-Checked (Join-Path $env:JAVA_HOME 'bin\java.exe') @('-cp', $classes, 'io.github.eoftisreal.dis.StreamStatsTest')
        Invoke-Checked (Join-Path $env:JAVA_HOME 'bin\java.exe') @('-cp', $classes, 'io.github.eoftisreal.dis.TouchScrollGestureTest')
    }
    if (-not $SkipTests) { Invoke-Checked (Join-Path $env:JAVA_HOME 'bin\java.exe') @('-cp', $classes, 'io.github.eoftisreal.dis.InputQueueTest') }
    $arguments = @('-p', 'android', 'assembleDebug', '--console=plain', '--no-daemon')
    if (-not $SkipTests) { $arguments += @('lintDebug', 'testDebugUnitTest') }
    if ($Offline) { $arguments += '--offline' }
    Invoke-Checked $gradle $arguments
    $destination = Join-Path $script:ProjectRoot 'artifacts\android'
    New-Item -ItemType Directory -Force $destination | Out-Null
    Copy-Item -LiteralPath 'android/app/build/outputs/apk/debug/app-debug.apk' -Destination (Join-Path $destination 'Dis.apk') -Force
    Write-BuildManifest $destination @{ java = (& (Join-Path $env:JAVA_HOME 'bin\javac.exe') -version 2>&1).ToString(); gradle = '8.9'; androidPlatform = 35 }
    Write-Host "`nBuilt $destination\Dis.apk" -ForegroundColor Green
} finally { Stop-Transcript | Out-Null }
