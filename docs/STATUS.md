# Progress checkpoint — 27 October 2026

## Version 0.2 changes

- Real second-display setup uses a pinned, catalog-verified VirtualDrivers/MikeTheTech x64 driver package. The original unsigned Dis driver source is retained for development.
- USB Connect opens the permission-protected Android handoff and supplies pairing automatically. A bundled APK installer button replaces separate package handling.
- Native Windows Media Foundation H.264 encoding prefers usable hardware MFTs and can select a software encoder; Android MediaCodec renders to a Surface. JPEG remains an explicit fallback.
- A bounded two-frame transport window removes per-frame stop-and-wait. Native encoding stays on an MTA worker and raw-frame lifetime is protected until the native call completes.
- Windows monitor orientation controls and Android orientation controls are separate. Controlled host rotation preserves the pairing session across its restart.
- Quality presets include 720p smooth and 1080p detailed, with a target up to 60 fps and actual encoder/FPS/throughput reporting.

## Verification records

At implementation checkpoint, the managed host compiles with zero warnings/errors, 19 local C# checks pass, and 44 independent Java checks pass. An Android clean debug build and lint pass. Windows CI adds actual ADB launch, native H.264 encode plus independent decode of 24 pictures, and signed virtual-display/rotation tests; these Windows-only gates must be read from the Actions run for the delivered revision.

No physical Windows/Android hardware performance benchmark, GPU-specific encoder coverage, HDR/color validation, or full SuperDisplay APK reverse engineering has been established. A 60 fps setting is a target, not a verified rate on the user's phone. The next validation priority is the user's actual connection and the Ethernet/tethering coexistence matrix.
