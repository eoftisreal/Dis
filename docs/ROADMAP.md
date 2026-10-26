# Roadmap and release gates

## Shipped in the 0.2 preview

- Signed virtual-display installation and Windows extended-desktop activation.
- Windows Graphics Capture through D3D11, Media Foundation H.264 encoding, and Android MediaCodec Surface decoding, with explicit fallbacks.
- Bounded transport and decoder queues, complete H.264 access units, and automatic USB pairing without copying a link.
- A combined Windows/Android download, APK installation from the host, and separate Windows-monitor and Android orientation controls.

The [verification record](STATUS.md) includes virtual-display installation, portrait rotation, actual captured pixels, and independent H.264 decoding on a Windows VM. Real-device behavior and comparative performance remain separate acceptance gates.

## P0: prove the basic product on hardware

1. Validate the signed VirtualDrivers package on physical PCs and document supported Windows/GPU combinations. Development of the original Dis IddCx driver still requires matching SDK/WDK verification and a separate signing plan.
2. Validate installation, uninstall, device loss, Windows sleep/resume, monitor moves, DPI changes, rotation, and GPU reset. The current monitor intentionally remains installed after streaming stops; automatic session attach/detach is future work.
3. Pass every [network coexistence test](NETWORK_TESTS.md), especially USB tethering plus display on one cable. Record device, USB mode, adapter, routes, throughput, and reconnect behavior. Do not promise every OEM exposes ADB and tethering simultaneously.
4. Test actual Windows/Android touch, pressure, tilt, eraser, negative monitor coordinates, disconnect during dragging/drawing, app backgrounding, and multiple rapid reconnects.
5. Validate the generated Windows EXE and Android APK on real devices. A compile is not an end-to-end playback test.

## P0: reach competitive latency and quality

- Reduce CPU readback and color-conversion work by feeding captured D3D11 textures into compatible hardware encoders.
- Validate hardware encoder/decoder combinations, improve keyframe recovery, and add adaptive bitrate and per-device quality profiles.
- Retain the JPEG path as a diagnostic fallback; measure rather than assume a hardware encoder is faster on every GPU.
- Benchmark against SuperDisplay on the same PC, Android device, resolution, refresh rate, cable, and content. Record motion-to-photon/input latency P50/P95, dropped/repeated frames, CPU/GPU load, power/temperature, throughput, and readable-text quality.
- Verify full/limited range, BT.709/sRGB handling, chroma sampling, and color patches. HDR and wide gamut remain unsupported until a complete color pipeline is validated.

## P1: make setup and recovery simple

- Offline QR pairing, remembered device identities, explicit revocation, and stable certificate storage protected by Windows credentials.
- Improve installation diagnostics and uninstall handling for the signed driver. Normal streaming already runs without elevation.
- Clear transport selection, adapter/link diagnostics, reconnection after USB re-enumeration, and automated tunnel recreation without replacing someone else's ADB mappings.
- Automatic virtual monitor attach/detach on authenticated device sessions, more native display modes, and per-device profiles.
- Keyboard, scroll, multi-touch gestures, hover, pressure curves, and configurable pen buttons.

## P2: polish and expand

Dynamic resolution/refresh, high refresh support, multi-monitor or multi-device sessions, HEVC/AV1 where hardware permits, audio forwarding, battery/thermal controls, and an optional zero-setup USB transport only if it can preserve the required networking modes.

The project is not called “better than SuperDisplay” until the competitive benchmarks and coexistence acceptance tests support that conclusion.
