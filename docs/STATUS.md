# Progress checkpoint — 27 October 2026

## Version 0.2 changes

- Real second-display setup uses a pinned, catalog-verified VirtualDrivers/MikeTheTech x64 driver package. The original unsigned Dis driver source is retained for development.
- USB Connect opens the permission-protected Android handoff and supplies pairing automatically. A bundled APK installer button replaces separate package handling.
- Native Windows Media Foundation H.264 encoding prefers usable hardware MFTs and can select a software encoder; Android MediaCodec renders to a Surface. JPEG remains an explicit fallback.
- Windows Graphics Capture uses D3D11 to capture virtual-monitor pixels; GDI remains a fallback. The current path still copies pixels to CPU memory for conversion and encoding.
- A bounded two-frame transport window removes per-frame stop-and-wait. Native encoding stays on an MTA worker and raw-frame lifetime is protected until the native call completes.
- Windows monitor orientation controls and Android orientation controls are separate. Controlled host rotation preserves the pairing session across its restart.
- Quality presets include 720p smooth and 1080p detailed, with a target up to 60 fps and actual encoder/FPS/throughput reporting.

## Verification records

The [successful verification run](https://github.com/eoftisreal/Dis/actions/runs/40403126579) tests source revision `9848e588c849af875cb6ee6506a9e1c01076ea32`. All three jobs passed: Windows, Android, and complete-package assembly. Later documentation-only changes do not alter the tested binaries.

- 20 Windows protocol, security, geometry, queue, and ADB checks passed, including launching an installed ADB executable from an unrelated working directory.
- The native codec encoded 24 H.264 pictures, all independently decoded by libavcodec.
- A disposable Windows VM installed the signed driver, activated a real second monitor at 800 × 600, and rotated it to 600 × 800. A known-color window was painted on that monitor and its captured pixel was checked. Another 24 pictures captured from the virtual monitor were encoded and independently decoded.
- 44 independent Java checks, an Android clean debug build, and Android lint passed.
- The combined download contains the self-contained Windows app, native codec, pinned signed driver archive and licenses, Android APK, and quick-start instructions.

No physical Windows/Android hardware performance benchmark, GPU-specific encoder coverage, HDR/color validation, or full SuperDisplay APK reverse engineering has been established. A 60 fps setting is a target, not a verified rate on the user's phone. The next validation priority is the user's actual connection and the Ethernet/tethering coexistence matrix.
