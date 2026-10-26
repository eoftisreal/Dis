# SuperDisplay research — initial static pass

Research date: 27 October 2026. The goal is to understand the reference product's architecture and the networking/USB coexistence requirement, then implement Dis independently. This is not a recovered SuperDisplay source project or a completed reverse engineering effort.

## Artifact actually examined

- Official download: https://superdisplay.app/download/SuperDisplay_Installer_v1.1.10.exe
- Filename: `SuperDisplay_Installer_v1.1.10.exe`
- Size: **29,446,384 bytes**
- SHA-256: `bd7b2d30b633986f5eabfd4e1bb561e7cf7e79ee7be5db03bee270c79298d252`
- Source of the link: https://superdisplay.app/

The installer was downloaded and inspected as data. It was not executed or installed. Its full contents are not committed to this repository. The Android APK was not supplied or inspected.

## Confirmed evidence

The outer executable is a PE32 x86 bootstrapper with five PE sections and an overlay. Its top-level import table contains `KERNEL32.dll`; that is not the complete set of dependencies of the application payloads.

Installer data contains these component names, with exact byte offsets recorded in [the machine-readable inventory](research/superdisplay-installer.json):

| Embedded names | What the evidence establishes |
| --- | --- |
| `MirrorService.exe`, `MirrorSettings.exe` | These service/settings component names occur in the installer data |
| `superdisplay_display.dll`, `superdisplay_display.cat` | Display-driver component names occur |
| `superdisplay_hid.dll`, `superdisplay_hid.cat`, `superdisplay_hidbus.cat` | HID/input component names occur |
| `superdisplay_aoap.cat`, `superdisplay_wpdfilter.cat` | USB accessory/filter component names occur |
| `adb.exe`, `AdbWinApi.dll`, `AdbWinUsbApi.dll` | ADB component names occur |
| `Wintab32.dll` | Wintab component name occurs |
| `Sentry.dll`, `sentry_native.dll`, `crashpad_handler.exe` | Diagnostic component names occur |

Names alone do not establish that a component was loaded, that every file is installed on every machine, how the private protocol works, or which encoder is used. Two embedded cabinets were also read statically; their manifest/file lists identify Visual C++ runtime prerequisite content, not recovered display-service code.

## Publicly documented behavior

The official [help page](https://superdisplay.app/help/) documents:

- USB communication using Android Open Accessory Protocol (AOAP), with ADB as another connection method.
- AOAP takes exclusive USB-interface access. For simultaneous file transfer, the developer describes using ADB and disabling AOAP in SuperDisplay settings.
- Network mode uses the Android device's IP on the same network; the developer describes that mode as insecure and unsuitable for untrusted networks.
- Windows sees the device as a display that can be extended or duplicated.
- Windows Ink and Wintab are used for drawing-application compatibility.

This corroborates a multi-component design involving display, input, USB/network transport, and session/service coordination. It does **not** prove that every phone supports tethering and display simultaneously, or that no bandwidth contention occurs.

## Dis design decisions

Dis uses its own framed protocol over certificate-pinned TLS, with an independent Windows capture/streaming host and Android receiver. Its first wired path uses ADB reverse, without switching to AOAP or changing routes/adapter state. IP mode can instead use a USB-tether adapter or ordinary Ethernet/Wi-Fi. The driver source is derived from Microsoft's publicly licensed IddCx sample, not SuperDisplay's driver.

This preserves the user's coexistence requirement as a design and acceptance-test condition. Hardware support and measured performance remain to be proven.

## Still unverified

Complete installer payload extraction; code-level analysis of its service/drivers; APK analysis; USB descriptors on a real device; observed private transport framing; codec selection and zero-copy behavior; input latency; driver recovery; network coexistence on the target phone; and comparative latency/color/CPU/GPU benchmarks.

No private encoder, proprietary protocol, zero-copy implementation, or universal conflict-free USB behavior is inferred as a confirmed finding.

## Reproduce this inventory

On your Windows development machine, install Python and the research-only dependency, then run against your downloaded official installer:

```bat
py -m pip install -r tools\research-requirements.txt
py tools\inspect_installer.py path\to\SuperDisplay_Installer_v1.1.10.exe --output installer-inventory.json
```

The script reads PE metadata and exact embedded-name offsets. It never launches, installs, or patches the executable. Match the hash before comparing results; a different installer is a different research artifact.
