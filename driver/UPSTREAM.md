# Driver provenance

The Dis driver is derived from Microsoft's Indirect Display Driver sample, under the Microsoft Public License (MS-PL). The complete license is in [LICENSE](LICENSE).

- Repository: https://github.com/microsoft/Windows-driver-samples
- Pinned revision: `bb68ed5e770bce04dc5395ec9258dafab034184d`
- Original directory: `video/IndirectDisplay/IddSampleDriver`
- Original `Driver.cpp` blob: `ae060298b725d4739c311ffe0f5711b224a76adb`

Dis replaces the sample's three panels and borrowed EDIDs with one EDID-less monitor, a stable container ID, landscape/portrait modes, explicit monitor cleanup, size-checked mode callbacks, a termination-aware surface consumer, and a separate driver group/hardware ID.

The driver is source-only and unvalidated on Windows in this milestone. Its frame consumer does not implement GPU export or hardware encoding. The host independently captures an active desktop display with GDI. Signing, installation, sleep/resume, capture compatibility, and teardown are release gates, not assumed successes.
