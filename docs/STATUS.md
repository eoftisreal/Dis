# Progress checkpoint — 27 October 2026

## Version 0.3 changes and verification

The supplied SuperDisplay 1.2.22 APK has now been statically analyzed. See the [findings and resulting implementation decisions](SUPERDISPLAY_APK_RESEARCH.md) and [reproducible APK/DEX inventory](research/superdisplay-apk-1.2.22.json). The reference application was not executed and no proprietary implementation or assets are distributed with Dis.

- Android H.264 requests realtime operation at the negotiated FPS and supported low-latency mode, retries without optional hints if initialization fails, and reports the selected decoder/configuration.
- Android Surface presentation uses the phone's clock; host PTS still identifies decoded frames for ACKs.
- Recognized stylus input now includes hover/range exit, pressure history, tilt, eraser and barrel state. Pen takeover cancels a finger drag; a suppressed palm does not block a subsequent pen contact.
- Input retains normal samples and coalesces stale adjacent movement only after the queue reaches eight packets, preserving contact/button/tool transitions.
- ADB/TLS, signed second-display setup, Windows capture/encoding and orientation controls remain the working baseline. Install both updated endpoints for complete hover-exit behavior.

The [successful 0.3 verification run](https://github.com/eoftisreal/Dis/actions/runs/40545864513) tests revision `4486174b40a203d811b7268e48a916fbbb75dee2`; Windows, Android, and combined-package jobs all passed. Later documentation-only changes do not alter these binaries. The [complete Windows/Android package](https://github.com/eoftisreal/Dis/actions/runs/40545864513/artifacts/12752239502) includes the updated 0.3 quick start.

- 20 Windows protocol/security/geometry/queue/ADB checks passed.
- Native H.264 encoding and independent decoding passed for two streams of 24 pictures each, including pictures captured from the actual virtual monitor.
- The Windows VM installed the signed driver, activated the second monitor, rotated it to portrait, checked captured color pixels, and successfully injected pen hover/contact/movement/release/range-exit sequences.
- 44 Java protocol/connection checks and a 100,000-movement bounded-queue stress test passed.
- Eight Android MotionEvent tests passed in Robolectric's API 28 environment: hover, range exit, palm suppression, finger-to-pen takeover, contact with an already suppressed palm, cancellation with a missing pointer, eraser/barrel state, historical pressure/coordinates, and input/pen disable behavior. Some tests cover more than one behavior.
- Android APK assembly and lint passed. Test dependencies are not included in the delivered APK.
- Repeating the APK inventory without the decompiler metadata produced matching original DEX hashes/tables; malformed/truncated DEX headers were rejected.

These checks do not measure Android hardware decoder behavior, physical pen fidelity, end-to-end latency, frame drops, GPU encoder coverage or Ethernet/tethering coexistence on the user's PC/phone. Separate cursor rendering, Windows multi-touch/keyboard support, Wintab, native GPU-to-encoder textures and comparative SuperDisplay benchmarks remain future work.

## Version 0.2 changes

- Real second-display setup uses a pinned, catalog-verified VirtualDrivers/MikeTheTech x64 driver package. The original unsigned Dis driver source is retained for development.
- USB Connect opens the permission-protected Android handoff and supplies pairing automatically. A bundled APK installer button replaces separate package handling.
- Native Windows Media Foundation H.264 encoding prefers usable hardware MFTs and can select a software encoder; Android MediaCodec renders to a Surface. JPEG remains an explicit fallback.
- Windows Graphics Capture uses D3D11 to capture virtual-monitor pixels; GDI remains a fallback. The current path still copies pixels to CPU memory for conversion and encoding.
- A bounded two-frame transport window removes per-frame stop-and-wait. Native encoding stays on an MTA worker and raw-frame lifetime is protected until the native call completes.
- Windows monitor orientation controls and Android orientation controls are separate. Controlled host rotation preserves the pairing session across its restart.
- Quality presets include 720p smooth and 1080p detailed, with a target up to 60 fps and actual encoder/FPS/throughput reporting.

## Earlier 0.2 verification

The [successful verification run](https://github.com/eoftisreal/Dis/actions/runs/40403126579) tests source revision `9848e588c849af875cb6ee6506a9e1c01076ea32`. All three jobs passed: Windows, Android, and complete-package assembly. Later documentation-only changes do not alter the tested binaries.

- 20 Windows protocol, security, geometry, queue, and ADB checks passed, including launching an installed ADB executable from an unrelated working directory.
- The native codec encoded 24 H.264 pictures, all independently decoded by libavcodec.
- A disposable Windows VM installed the signed driver, activated a real second monitor at 800 × 600, and rotated it to 600 × 800. A known-color window was painted on that monitor and its captured pixel was checked. Another 24 pictures captured from the virtual monitor were encoded and independently decoded.
- 44 independent Java checks, an Android clean debug build, and Android lint passed.
- The combined download contains the self-contained Windows app, native codec, pinned signed driver archive and licenses, Android APK, and quick-start instructions.

No physical Windows/Android hardware performance benchmark, GPU-specific encoder coverage, HDR/color validation, or full SuperDisplay APK reverse engineering has been established. A 60 fps setting is a target, not a verified rate on the user's phone. The next validation priority is the user's actual connection and the Ethernet/tethering coexistence matrix.
