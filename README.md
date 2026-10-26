# Dis — Windows to Android second display

Dis 0.2 preview adds a real extended-display setup, automatic USB connection, H.264 streaming, and orientation controls. Windows 11 x64 host; Android 9+ receiver.

## Use the complete preview

Download the **Dis-Complete-Preview** artifact from a successful [Actions run](https://github.com/eoftisreal/Dis/actions). Extract the entire folder, retaining `Dis.exe`, `Dis.Codec.dll`, `Dis.apk`, and `driver`.

1. Authorize USB debugging on the phone and make Android Platform Tools available on Windows. The app detects normal SDK installations and a `platform-tools` folder beside Dis.exe; Browse is available under Advanced.
2. Press **Install/update Android app**. This installs the bundled APK over USB. An older preview with a different debug signing key may need to be uninstalled once on the phone before updating.
3. Press **Set up second display** once. Accept the administrator request and Windows publisher prompt if shown. This installs the pinned, signed Virtual Display Driver by VirtualDrivers/MikeTheTech, signed by SignPath Foundation.
4. Choose **USB**, **Second display**, and **Connect**. Dis opens the Android receiver and transfers pairing credentials automatically through the authorized USB connection. There is no connection-link copying in USB mode.

Choose **Mirror selected screen** to stream an existing monitor without installing a virtual display driver. Network connections over Ethernet, Wi-Fi, or USB tethering remain available under Connection; manual network pairing remains under Advanced.

## Smoothness and orientation

The default path is H.264: Windows Media Foundation selects a usable hardware encoder when available, with a software encoder fallback. Android MediaCodec renders directly to a Surface. A two-frame window replaces the old stop-and-wait transport. Buffers remain bounded; encoded reference frames are never silently dropped. The live status shows the encoder actually used, delivered frame rate, and throughput.

Start with **720p · smooth** and a 60 fps target. Use **1080p · detailed** after checking performance. If H.264 cannot initialize, the app reports **JPEG fallback**. A target frame rate is not a measured guarantee on a particular PC/phone.

**Windows orientation** changes the selected Windows monitor to landscape, portrait, or a flipped mode. Applying it restarts the stream with the same pairing session and automatically reconnects USB. The Android **Auto / Landscape / Portrait** selector controls the phone's own orientation separately. Portrait streams preserve aspect ratio and can use a full-height video mode.

## What is and is not established

This is an independent developing application, not recovered SuperDisplay source. Its official Windows installer was examined statically; its Android APK and private protocol were not reverse engineered. No SuperDisplay code or assets are included.

The original source-only Dis IddCx driver remains under `driver/DisIdd` for development. Normal second-display setup now uses the separately attributed, signed VirtualDrivers package instead. Setup verifies its pinned SHA-256 and catalog signature; it does not disable signature enforcement, change Secure Boot, or install root certificates. Installing the driver is an explicit first-setup action. The installed virtual monitor remains available in Windows after streaming stops.

There is no claim yet that Dis outperforms SuperDisplay on the user's hardware. GPU-specific hardware encoder behavior, Android decoder performance, USB bandwidth/thermal behavior, color accuracy, and comparison benchmarks require real-device measurements. Ethernet and tethering configuration are left untouched, but display traffic still consumes link bandwidth.

## Build on Windows

Install .NET 10 SDK and Visual Studio C++ tools/CMake, then run `BUILD_WINDOWS.cmd`. The native codec uses Windows Media Foundation and has no FFmpeg runtime dependency. For Android, install JDK 17 and Android SDK 35/build tools 35.0.0, set JAVA_HOME and ANDROID_HOME, and run `BUILD_ANDROID.cmd`.

The CI builds both apps, runs protocol/security/ADB tests, encodes and independently decodes an actual H.264 stream, checks the signed driver and virtual-monitor orientation, and assembles the complete USB setup package. Consult the actual run results rather than assuming these gates have passed for an arbitrary revision.

## Project records

- [Current status](docs/STATUS.md)
- [Quick start](docs/QUICK_START.txt)
- [Protocol](docs/PROTOCOL.md)
- [Driver setup and provenance](docs/DRIVER.md)
- [Network coexistence acceptance matrix](docs/NETWORK_TESTS.md)
- [Reference-product research](docs/SUPERDISPLAY_RESEARCH.md)
- [Roadmap](docs/ROADMAP.md)
