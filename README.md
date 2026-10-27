# Dis — Windows to Android second display

Dis 0.4 preview turns an Android phone or tablet into a Windows virtual monitor over USB or a local network. The desktop uses Qt Quick with dedicated Display, Devices, Settings, and About pages, live stream metrics, hardware H.264, GPU resizing, and an immersive Android receiver. Windows 11 x64 host; Android 9+ receiver.

## Use the complete preview

Run **BUILD_ALL.cmd** locally. On the first Android setup, review Google's SDK terms and run `BUILD_ALL.cmd -AcceptAndroidLicenses` to approve them. Open **`artifacts/Dis-Setup.exe`**: this single installer includes the desktop runtime, Android APK, ADB, codec, and signed driver archive. It installs for your Windows account, adds a Start menu shortcut and uninstall entry, offers sign-in startup, and opens Devices for first setup. Qt and .NET do not need to be installed. `artifacts/Dis-Complete.zip` remains the portable alternative; keep its whole folder together. See [local build instructions](docs/LOCAL_BUILD.md).

1. Authorize USB debugging on the phone. USB tools are embedded in the Windows engine and prepared automatically; no separate `adb.exe` download, copying, installation, or selection is needed. **Devices** shows whether your phone is authorized.
2. In **Devices**, press **Install receiver** to install the bundled APK over USB. An older preview with a different debug signing key may need to be uninstalled once on the phone before updating.
3. In **Devices**, press **Set up display** once. Accept the administrator request and Windows publisher prompt if shown. This installs the pinned, signed Virtual Display Driver by VirtualDrivers/MikeTheTech, signed by SignPath Foundation.
4. On **Display**, press **Connect display** and approve the administrator prompt. USB is the default transport. Dis opens the Android receiver and transfers pairing credentials automatically. There is no connection-link copying in USB mode.

Dis creates an Android second display. Use Windows display settings to choose Extend or Duplicate; the app no longer has a Mirror screen selector. Network connections over Ethernet, Wi-Fi, or USB tethering remain available under Connection; manual network pairing remains under Advanced.

**The virtual adapter is always disabled while idle.** This behavior is permanent, with no switch to turn it off; existing preferences are overridden. Windows asks for administrator approval on Connect so a helper can enable the adapter and disable it on Disconnect, USB removal, failed playback, app exit, or a crash. Setup also leaves the adapter disabled. Windows display settings can manage Extend/Duplicate while a session is active.

A connection is confirmed only when the phone acknowledges its first decoded frame. If no frame is acknowledged within 30 seconds on USB or 60 seconds for network pairing, Dis closes the session and disables the adapter. A lost receiver gets a three-second reconnect grace; an open connection without new frame acknowledgements times out after ten seconds. The elevated helper also has an independent lease: if the engine hangs before playback, it disables the adapter after at most 75 seconds; after playback heartbeats begin, the lease is 15 seconds. These timeouts preserve short Android surface reconnections while avoiding an abandoned virtual screen.

Dis stays in the system tray when you close its window. The tray menu offers Open, Connect, Disconnect, and Quit; Quit stops the display and exits. **Settings → Start with Windows** starts it quietly at sign-in without elevation. To connect automatically when plugging in USB, enable **Settings → USB autoconnect** on Windows and **Connection controls → Start display when USB connects** on Android. The phone option starts off. Dis must be running on the PC, USB debugging must be authorized, and Android may require unlocking the phone. Manual desktop Disconnect pauses automatic attempts until unplug/replug. This uses the existing authorized ADB transport, not Android accessory mode.

If an older Windows app reports **Windows could not apply this display mode (code -1)**, press **Win+P**, choose **Extend**, then retry Connect. The driver can be installed and running while its monitor is detached. Updated host builds try driver-advertised modes and fall back to Windows' extended-display topology when legacy activation fails. This fix is in the Windows host; rebuilding only the Android APK does not update it.

## Smoothness and orientation

The USB implementation still uses ADB internally. Its executable, supporting DLLs, and notice are embedded in `Dis.Engine.exe` and restored to a versioned private cache under `%LOCALAPPDATA%\Dis\UsbTools` as needed. Missing or modified cache files are repaired from the embedded bundle. Saved paths into deleted portable folders are ignored. Network mode does not require ADB. Android USB debugging authorization is still required for the direct USB mode.

Windows Graphics Capture reads and resizes the selected monitor through D3D11 before CPU readback. GDI remains a fallback. H.264 uses a working Media Foundation hardware encoder when available, otherwise a software encoder. Android MediaCodec renders directly to a Surface with low-latency support where available. The bounded two-frame transport waits for capacity before capturing, so a freshly captured picture does not age while waiting for an older frame. Encoded reference frames are never silently dropped. Status reports the encoder, measured FPS, frame acknowledgement time, and throughput. Frame acknowledgement time includes capture/encoding, transport, decoder consumption, and acknowledgement; it is not motion-to-photon latency.

Start with **Fast · 720p / 60 fps**. **Balanced** targets 1080p at 30 fps, and **Sharp** targets 1080p at 60 fps. If H.264 cannot initialize, status reports **JPEG fallback**. A target frame rate is not a measured guarantee.

**Desktop size** is separate from streamed video quality: match the phone's shape, choose a Full HD desktop, or use a compact desktop for larger text. Only driver-advertised 60 Hz modes are selected. Preferences are remembered locally; pairing credentials are never saved in preferences. Optional startup USB connection is available. On Android, full screen hides the toolbar and system bars while preserving the picture's aspect ratio; tap the small Controls button to change orientation or disconnect.

**Windows orientation** changes the selected Windows monitor to landscape, portrait, or a flipped mode. Applying it restarts the stream with the same pairing session and automatically reconnects USB. The Android **Auto / Landscape / Portrait** selector controls the phone's own orientation separately. Portrait streams preserve aspect ratio and can use a full-height video mode.

On Android, **Controls** opens the connection dashboard while video continues playing. Reconnect reuses the current pairing; Disconnect stops the receiver. The dashboard shows received and decoded FPS, video Mbps, ping RTT, a recent FPS trace, negotiated resolution, and the actual decoder. An optional small overlay keeps live stats visible over the display. **Copy diagnostics** copies a local troubleshooting report without the pairing token, certificate pin, or connection link. Ping measures network roundtrip; decoder FPS counts consumed frames, not physical screen presentation or motion-to-photon latency. Metrics are unavailable until measured and reset on each transport connection.

## What is and is not established

This is an independent developing application, not recovered SuperDisplay source. Its Windows installer and the user-supplied Android 1.2.22 APK were examined statically; the reference findings are documented in docs/SUPERDISPLAY_APK_RESEARCH.md. The complete private protocol and Windows implementation have not been recovered. No SuperDisplay code or assets are included.

The original source-only Dis IddCx driver remains under `driver/DisIdd` for development. Normal second-display setup now uses the separately attributed, signed VirtualDrivers package instead. Setup verifies its pinned SHA-256 and catalog signature; it does not disable signature enforcement, change Secure Boot, or install root certificates. Installing the driver is an explicit first-setup action. The driver stays installed while its adapter is disabled between sessions.

There is no claim yet that Dis outperforms SuperDisplay on the user's hardware. GPU-specific hardware encoder behavior, Android decoder performance, USB bandwidth/thermal behavior, color accuracy, and comparison benchmarks require real-device measurements. Ethernet and tethering configuration are left untouched, but display traffic still consumes link bandwidth.

## Build on Windows

Run `BUILD_WINDOWS.cmd` for the Qt desktop, `BUILD_ANDROID.cmd` for the APK, or `BUILD_ALL.cmd` for the complete portable package. The scripts discover Qt 6.8+ MSVC x64, Visual Studio C++ tools, and CMake, and cache .NET/JDK/Gradle/Android prerequisites under `.tools`. Your sibling `EngineTest/deps/qt` kit is discovered automatically; `-QtRoot` or `QT_ROOT` supports other locations. No global PATH edits are needed. Run with `-Offline` after preparing dependencies. Keep the whole output folder, including `Dis.Engine.exe`, Qt DLLs, `qml`, and `platforms`. The portable app does not need Qt installed on its target PC. `-Classic` builds the diagnostic WinForms interface. [Details and prerequisites](docs/LOCAL_BUILD.md).

The historical GitHub workflow remains an optional verification path. Local builds compile the native codec from source, run protocol/security/ADB and Java checks, perform a real H.264 encoding smoke test, and run Android lint. Real-device capture and lifecycle checks are available separately; no local build changes the installed display driver or connected phone automatically.

## Project records

- [Current status](docs/STATUS.md)
- [Quick start](docs/QUICK_START.txt)
- [Protocol](docs/PROTOCOL.md)
- [Driver setup and provenance](docs/DRIVER.md)
- [Network coexistence acceptance matrix](docs/NETWORK_TESTS.md)
- [Reference-product research](docs/SUPERDISPLAY_RESEARCH.md)
- [Roadmap](docs/ROADMAP.md)
