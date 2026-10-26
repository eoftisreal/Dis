$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location $projectRoot
if (-not $env:JAVA_HOME -or -not (Test-Path "$env:JAVA_HOME\bin\javac.exe")) {
    throw 'Install JDK 17 and set JAVA_HOME to its directory.'
}
if (-not $env:ANDROID_HOME) { $env:ANDROID_HOME = $env:ANDROID_SDK_ROOT }
if (-not $env:ANDROID_HOME) { $env:ANDROID_HOME = Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
if (-not (Test-Path "$env:ANDROID_HOME\platforms\android-35\android.jar")) {
    throw 'Install Android SDK platform 35 using Android Studio and set ANDROID_HOME.'
}
$toolsRoot = Join-Path $projectRoot '.tools'
$gradleRoot = Join-Path $toolsRoot 'gradle-8.9'
$gradleBatch = Join-Path $gradleRoot 'bin\gradle.bat'
if (-not (Test-Path $gradleBatch)) {
    New-Item -ItemType Directory -Force $toolsRoot | Out-Null
    $archive = Join-Path $toolsRoot 'gradle-8.9-bin.zip'
    Invoke-WebRequest 'https://services.gradle.org/distributions/gradle-8.9-bin.zip' -OutFile $archive -UseBasicParsing
    $expected = 'd725d707bfabd4dfdc958c624003b3c80accc03f7037b5122c4b1d0ef15cecab'
    if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
        Remove-Item $archive
        throw 'Gradle archive checksum mismatch.'
    }
    Expand-Archive $archive $toolsRoot -Force
    Remove-Item $archive
}
& $gradleBatch -p android assembleDebug lintDebug --console=plain
if ($LASTEXITCODE -ne 0) { throw 'Android build or lint failed.' }
$destination = Join-Path $projectRoot 'artifacts\android'
New-Item -ItemType Directory -Force $destination | Out-Null
Copy-Item 'android\app\build\outputs\apk\debug\app-debug.apk' "$destination\Dis-preview.apk" -Force
Write-Host "Built $destination\Dis-preview.apk"
