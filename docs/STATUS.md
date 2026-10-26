# Progress checkpoint — 27 October 2026

## Implemented in source

| Component | Progress |
| --- | --- |
| Windows app | WinForms UI; display/adapter selection; GDI capture; aspect-preserving scaling; JPEG; cursor; frame-rate/quality controls; throughput/FPS statistics |
| Android app | Surface rendering; bounded frame decode; finger-to-mouse and pen pressure/tilt transport; bounded reconnect; lifecycle disconnect |
| Network/USB | Adapter-specific TCP listener or loopback ADB reverse; existing tunnel conflicts rejected; Dis-owned mapping cleanup; no route/DNS/USB accessory changes |
| Security | TLS 1.2/1.3; SHA-256 server certificate pin; random 256-bit session token; handshake/message/rate/queue limits; link revocation when host stops |
| Framing | Versioned protocol; big-endian fields; exact reads; strict validation; one unconsumed frame; whole-frame send/ack deadline; input release on teardown |
| Virtual monitor | Microsoft sample adapted to one EDID-less panel, stable identity and portrait/landscape modes; host software-device lifecycle; source-only, not installed or tested |
| Reference research | Official installer obtained; PE metadata and embedded component-name offsets inventoried; public USB/network behavior documented; no APK disassembly |

## Verification

- Windows host Release compilation: **passed, zero warnings and errors**.
- Self-contained Windows x64 executable publication: **passed**. The executable has not been launched on Windows in this environment.
- C# executable test suite: **11 checks passed**, including fragmented/truncated reads, invalid lengths/flags, pointer validation, ack behavior, token checks, and framed TLS loopback traffic.
- Independent Java suite: **40 checks passed**, using the same framing/pointer vectors, rejecting malformed connection links, and checking the real certificate-pinning implementation against correct, unrelated, empty, and missing certificates. The committed certificate is public test data; its private key is not included.
- Android debug APK build (`assembleDebug`) and Android lint (`lintDebug`): **passed** with Android SDK 35, AGP 8.7.3, Gradle 8.9 and JDK 17. This verifies compilation/packaging and lint, not physical-device behavior.
- Native driver compilation/signing and real Windows/Android display/input/network testing: **not performed**.
- Competitive SuperDisplay benchmarks: **not performed**.

## Highest-priority next work

Validate the Android build and Windows app on physical devices, compile/sign/test the virtual display driver, and complete the [coexistence acceptance matrix](NETWORK_TESTS.md). Then implement a hardware video pipeline and run matched-hardware comparisons before claiming this is faster or better than SuperDisplay.

See the [roadmap](ROADMAP.md) for specific release gates. This checkpoint is a substantial first implementation, not a finished or benchmarked replacement.
