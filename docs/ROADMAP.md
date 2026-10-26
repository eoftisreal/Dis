# Roadmap and release gates

## Current milestone: verifiable baseline

Windows host, Android receiver, own protocol, pinned TLS, USB/adapter coexistence design, frame backpressure, touch/pen transport, and driver source. Passing source builds do not establish device behavior or performance.

## P0: prove the basic product on hardware

1. Compile the driver with matching Windows SDK/WDK; resolve InfVerif/driver verification findings. Use a dedicated development test machine for signing/installation validation. Prepare properly signed distribution before normal-user delivery.
2. Confirm a Dis monitor can extend the desktop, be captured, and disappear cleanly. Validate installation, uninstall, device loss, Windows sleep/resume, monitor moves, DPI changes, rotation, and GPU reset.
3. Pass every [network coexistence test](NETWORK_TESTS.md), especially USB tethering plus display on one cable. Record device, USB mode, adapter, routes, throughput, and reconnect behavior. Do not promise every OEM exposes ADB and tethering simultaneously.
4. Test actual Windows/Android touch, pressure, tilt, eraser, negative monitor coordinates, disconnect during dragging/drawing, app backgrounding, and multiple rapid reconnects.
5. Validate the generated Windows EXE and Android APK on real devices. A compile is not an end-to-end playback test.

## P0: reach competitive latency and quality

- Add D3D11 capture/driver texture export and a GPU-friendly encoder pipeline.
- Implement Media Foundation hardware H.264 encoding and Android MediaCodec decoding, explicit access-unit framing, keyframe recovery, bounded decoder queues, and bitrate adaptation.
- Retain the JPEG path as a diagnostic fallback; measure rather than assume a hardware encoder is faster on every GPU.
- Benchmark against SuperDisplay on the same PC, Android device, resolution, refresh rate, cable, and content. Record motion-to-photon/input latency P50/P95, dropped/repeated frames, CPU/GPU load, power/temperature, throughput, and readable-text quality.
- Verify full/limited range, BT.709/sRGB handling, chroma sampling, and color patches. HDR and wide gamut remain unsupported until a complete color pipeline is validated.

## P1: make setup and recovery simple

- Offline QR pairing, remembered device identities, explicit revocation, and stable certificate storage protected by Windows credentials.
- A signed driver installer and a narrow privileged monitor helper, so normal streaming remains unprivileged.
- Clear transport selection, adapter/link diagnostics, reconnection after USB re-enumeration, and automated tunnel recreation without replacing someone else's ADB mappings.
- Automatic virtual monitor attach/detach on authenticated device sessions, negotiated native portrait modes, and per-device profiles.
- Keyboard, scroll, multi-touch gestures, hover, pressure curves, and configurable pen buttons.

## P2: polish and expand

Dynamic resolution/refresh, high refresh support, multi-monitor or multi-device sessions, HEVC/AV1 where hardware permits, audio forwarding, battery/thermal controls, and an optional zero-setup USB transport only if it can preserve the required networking modes.

The project is not called “better than SuperDisplay” until the competitive benchmarks and coexistence acceptance tests support that conclusion.
