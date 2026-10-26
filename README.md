# Dis — Windows to Android display

A developing alternative to SuperDisplay, built around using an Android screen while keeping Ethernet, Wi-Fi, and USB tethering available. Windows 11 x64 host; Android 9 or newer receiver.

**Status: 0.1 preview source, not a finished SuperDisplay replacement.** The first transport is software JPEG over pinned TLS. It has not yet been benchmarked against SuperDisplay or tested on physical Windows/Android devices. The virtual monitor driver is source-only and needs WDK compilation, signing, and device validation.

## Included

- Windows desktop app with monitor and adapter selection, capture, cursor, quality/FPS controls, and live throughput/FPS reporting.
- Android receiver with aspect-correct rendering, touch-to-mouse, pen pressure/tilt input, and bounded reconnect attempts.
- USB through an ADB reverse tunnel; ordinary IP transport over Ethernet, Wi-Fi, or a USB-tethering adapter.
- Certificate-pinned TLS and a random 256-bit pairing token. Stopping the host revokes the current link.
- Acknowledgement-controlled streaming: at most one unconsumed frame, bounded messages, handshake limits, and input release on disconnect.
- An adapted Microsoft IddCx virtual monitor driver with portrait/landscape modes and software-device lifecycle support in the host.
- Protocol/security tests in C# and Java, build scripts, and Windows CI.

Dis does not change routes, DNS, adapter priorities, tethering state, or USB accessory mode. Display traffic still shares finite link bandwidth with other traffic; zero interference is a hardware test goal, not a claim established by source inspection.

## Build on Windows

### Windows app

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), then run:

```bat
BUILD_WINDOWS.cmd
```

The self-contained executable is written to `artifacts\windows\Dis.exe`. No display driver is needed to stream an already-active monitor.

### Android app

Install Android Studio with SDK platform 35, build tools 35.0.0, and JDK 17. Set `JAVA_HOME` to JDK 17 and `ANDROID_HOME` to the Android SDK folder. Run:

```bat
BUILD_ANDROID.cmd
```

The build script verifies and downloads Gradle 8.9, then builds/lints the app. Output: `artifacts\android\Dis-preview.apk`. You can also open `android` in Android Studio. Preview APKs use a debug signing key; production signing and Play Store distribution are not set up.

### Virtual monitor driver

See [driver setup](docs/DRIVER.md). Building the normal Windows app does **not** build or install the driver. Mirroring works on an existing display; an extended desktop requires an active virtual display. The host's **Add virtual monitor** action currently needs administrator access and an installed, trusted Dis driver.

## Connect

1. Launch Dis on Windows and select an active display.
2. Choose either **USB cable (ADB)** or the intended network adapter. For ADB, install Android Platform Tools, enable USB debugging, authorize the PC, and provide `adb.exe` if it is not on PATH.
3. Start the display and copy the complete connection link into the Dis Android app. Treat the link as a secret; it grants viewing and, if enabled, input for that host session.
4. Connect. Stop on Windows to revoke the link. Reconnect with a fresh link after restarting the host.

For USB tethering without ADB, leave tethering enabled, select its Windows adapter in Dis, and connect Android to the Windows IP shown in the link. For Ethernet on the PC and Wi-Fi on Android, both devices must be reachable on the same LAN. Android-side Ethernet adapters can also provide IP connectivity, subject to the device's USB host/accessory limitations.

If Windows Firewall blocks network mode, allow Dis on the intended private network. The app does not create broad firewall rules. ADB mode listens only on loopback. Remote input targets ordinary desktop applications; Windows can block injection into elevated applications or the secure desktop.

### If Windows cannot find `adb.exe`

Download **SDK Platform-Tools for Windows** from [Google's official page](https://developer.android.com/tools/releases/platform-tools). Extract the **entire** `platform-tools` folder beside `Dis.exe`, including its DLLs. Dis now looks there automatically, also checks Android Studio's usual SDK location, `ANDROID_HOME`, `ANDROID_SDK_ROOT`, and PATH, and offers **Browse…** to select `adb.exe` explicitly.

In an older Dis build, enter the full executable path in **ADB executable**, for example `C:\Android\platform-tools\adb.exe`. Installing Android Studio or changing PATH is not required. USB debugging must still be enabled and the PC authorized on the phone after ADB starts.

For Ethernet, Wi-Fi, or a USB-tethering network connection, select that network adapter instead of **USB cable (ADB)**; those modes do not launch ADB.

## Project records

- [Current progress and verification](docs/STATUS.md)
- [SuperDisplay static research](docs/SUPERDISPLAY_RESEARCH.md)
- [Wire protocol](docs/PROTOCOL.md)
- [Network coexistence acceptance tests](docs/NETWORK_TESTS.md)
- [Roadmap and release gates](docs/ROADMAP.md)
- [Driver provenance and license](driver/UPSTREAM.md)

SuperDisplay code, APKs, installer payloads, icons, and private protocol implementations are not included. This implementation uses its own protocol and UI, standard Windows/Android APIs, and an attributed Microsoft driver sample. No interoperability with SuperDisplay is claimed.
