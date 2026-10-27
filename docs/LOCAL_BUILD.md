# Local builds

No GitHub account, repository creation, push, Actions secret, or artifact download is needed.

From an extracted source folder, run:

```bat
BUILD_ALL.cmd -AcceptAndroidLicenses
```

Use the license flag only after reviewing https://developer.android.com/studio/terms. Subsequent builds can use `BUILD_ALL.cmd` alone. Windows-only builds never need the Android license flag.

| Command | Output |
| --- | --- |
| `BUILD_WINDOWS.cmd` | `artifacts/windows/Dis.exe`, codec built from source, signed-driver archive |
| `BUILD_ANDROID.cmd` | `artifacts/android/Dis.apk` |
| `BUILD_ALL.cmd` | `artifacts/Dis-Complete/Dis.exe`, APK, codec, embedded USB tools, driver, instructions, manifest; also `artifacts/Dis-Complete.zip` and `artifacts/Dis-Setup.exe` |

The package includes the Qt runtime and a self-contained .NET engine. Keep the whole portable folder: `Dis.exe`, `Dis.Engine.exe`, codec, Qt DLLs, `qml`, `platforms`, APK, and driver. USB tools are embedded in the engine; there are no loose ADB files to supply. Target PCs do not need Qt or .NET installed. The APK is a development preview, signed with the local debug key.

The complete build also produces **`artifacts/Dis-Setup.exe`**, a single installer containing the full portable payload. It installs under `%LOCALAPPDATA%\Dis\Application` for the current account, creates a Start menu shortcut and Windows uninstall entry, and offers tray startup at sign-in. Driver setup remains an explicit one-time elevated action in Devices. Installer payload verification runs as part of the complete build. Offline builds skip the online NuGet vulnerability audit.

## Prerequisites and discovery

Published Windows builds embed the USB tool bundle in the engine executable. Building requires an Android Platform Tools installation with `adb.exe`, its two Windows DLLs, and `NOTICE.txt`. The complete build prepares Android first, then embeds those files automatically; a Windows-only developer build can use the SDK already prepared by `BUILD_ANDROID.cmd`. End users do not install or select ADB. The build verifies the embedded tools by restoring the private runtime cache and running the bundled executable's version check.

- Windows 11 x64. Install Visual Studio Build Tools 2022 with **Desktop development with C++**, CMake tools, and the Windows SDK. The scripts find these with `vswhere`, so a Developer Command Prompt and PATH changes are unnecessary.
- Qt 6.8+ MSVC x64 with Qt Quick and Quick Controls (6.8.3 is tested). The desktop build discovers the supplied sibling `EngineTest/deps/qt/6.8.3/msvc2022_64` kit. For another location, use `BUILD_ALL.cmd -QtRoot "C:\Qt\6.8.3\msvc2022_64"`, or set `QT_ROOT`. The Qt installation is read only; all generated files remain in the Dis workspace. `windeployqt` copies the runtime, and Qt notices and licenses are packaged under `licenses`. `BUILD_WINDOWS.cmd -Classic` is the optional diagnostic WinForms build.
- The scripts use an installed .NET 10 SDK or cache Microsoft's pinned 10.0.100 SDK under `.tools/dotnet`. A .NET runtime alone cannot compile an EXE.
- Android builds find JDK 17/21 or Android Studio's compatible JDK; otherwise they cache Microsoft OpenJDK 17.0.17. Gradle 8.9 and Android command-line tools 13 are pinned and verified. SDK platform 35, build tools 35.0.0, and platform-tools are installed in `.tools/android-sdk` if a compatible SDK is not already available.
- Initial downloads require internet access. Download checksums are checked before extraction; a mismatched archive is not used. NuGet and Gradle dependencies are cached locally. Robolectric additionally caches its Android test-runtime JARs in the standard user Maven cache (`~/.m2/repository/org/robolectric`); prepare them with one successful online test run before using offline builds. The virtual driver remains pinned to the reviewed 25.7.23 archive and SHA-256.
- Portable prerequisites and caches stay under `.tools`; normal builds do not install a display driver, modify system trust, change the user's network configuration, or deploy to the phone.

## Repeat builds and troubleshooting

`BUILD_ALL.cmd -Offline` uses cached prerequisites, NuGet packages, Gradle plugins, and Android packages. Prepare caches with one successful online build first. `-SkipTests` is available for development iterations; use a normal build before distributing a package.

Inspect `artifacts/logs/windows-build.log` or `android-build.log` after a failure. SDK license output is recorded separately. Build manifests include tool versions and SHA-256 hashes for all packaged files.

With no Dis instance running and tray operation enabled, run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-desktop-lifecycle.ps1` to check window close, tray persistence, single-instance reopen, Quit, and engine cleanup. It disables autoconnect for the check and does not install a driver or receiver.

Keep `.tools/signing/Dis-preview.keystore` backed up: it gives repeated local APKs the same identity. On first setup the script reuses an existing standard Android debug key when available. Moving from a GitHub-signed preview to a different local key requires uninstalling the old Dis receiver once, which removes its preferences. Future builds using the same local key update normally. Never commit the key or distribute it in the portable ZIP.

Optional real-device checks from the project root:

```bat
artifacts\Dis-Complete\Dis.exe --display-lifecycle-check
artifacts\Dis-Complete\Dis.exe --display-capture-check
```

These checks require the already installed display driver and administrator approval. They temporarily activate the virtual monitor. The lifecycle check tests attach, detach, reconnect, and hiding while preserving physical display layouts. The capture check verifies downscaled screen quadrants and encodes 24 pictures. Results are written under `artifacts`. Regular builds do not run these interactive checks.

GitHub CI does not cache or commit private signing keys. Its current receiver APK uses a temporary preview key, so updating from an APK built by another CI run can require uninstalling the old receiver first. Local builds preserve their existing private .tools/signing/Dis-preview.keystore and update normally. A stable distributed signing identity requires a separately configured secure signing service or encrypted CI secret.
