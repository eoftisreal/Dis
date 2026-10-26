# Virtual display driver development

The driver uses Windows IddCx/UMDF, adapted from Microsoft's sample under [MS-PL](../driver/LICENSE). It creates one EDID-less monitor with landscape and portrait desktop modes. It is **not a signed, production-ready installer**.

## Build on Windows 11 x64

Install Visual Studio 2022 C++ tools, the matching Windows SDK/WDK and WDK Visual Studio integration, plus Spectre-mitigated C++ libraries. From the corresponding developer command prompt:

```bat
msbuild driver\DisIdd\DisIdd.vcxproj /m /p:Configuration=Release /p:Platform=x64
```

Use the SDK/WDK versions actually installed together; override `WindowsTargetPlatformVersion` if required by that installation. Check InfVerif/Inf2Cat results and all build output before treating a package as installable. No WDK build has been verified in the initial milestone.

## Installation and activation

Driver installation requires a properly trusted/signable package. A development-signed package is only for an isolated Windows driver-development environment configured according to Microsoft's testing guidance. This project does not change Secure Boot, enable test mode, install certificates, or disable signature enforcement for you.

For a trusted package, stage the generated INF using an administrator terminal:

```bat
pnputil /add-driver path\to\DisIdd.inf /install
```

Staging alone does not instantiate a software device. Run Dis as administrator for **Add virtual monitor**; its `SwDeviceCreate` call uses hardware ID `DisDisplay`. The driver must already be available. In Windows Display settings, choose **Extend** for the Dis monitor, then **Refresh displays** in Dis and select it as the stream source.

The software device is handle-scoped: **Remove virtual monitor** or closing the host releases it. Disconnecting Android alone does not currently remove the virtual monitor. A narrow privileged helper and automatic session-scoped attach/detach are future work.

## Current capture path

The driver consumes/relinquishes IddCx surfaces; it does not export shared GPU textures. The host independently captures an active Windows monitor with GDI, scales it while retaining aspect ratio, and encodes JPEG. Confirm that capture path works with the installed driver on the target Windows build; it has not been physically tested here. The extension driver is not bundled in normal CI app artifacts.

## Required validation

Build/sign/install/uninstall; one active monitor only; portrait/landscape mode enumeration; insufficient callback-buffer handling; power cycles and sleep/resume; GPU/device removal; continuous-frame teardown; monitor context cleanup; display capture; DPI/layout changes; and Windows Driver Verifier/HLK checks appropriate to the eventual distribution.

Primary references:

- https://learn.microsoft.com/windows-hardware/drivers/display/indirect-display-driver-model-overview
- https://github.com/microsoft/Windows-driver-samples/tree/bb68ed5e770bce04dc5395ec9258dafab034184d/video/IndirectDisplay
- https://learn.microsoft.com/windows/win32/api/swdevice/nf-swdevice-swdevicecreate
