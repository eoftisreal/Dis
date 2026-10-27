# Second-display driver

Normal setup uses the signed x64 VirtualDrivers package from release 25.7.23:

- Source/project: https://github.com/VirtualDrivers/Virtual-Display-Driver
- Artifact: VirtualDisplayDriver-x86.Driver.Only.zip (despite its artifact name, the INF targets NTamd64 and the DLL is PE32+ x64)
- SHA-256: e24210692b442b39af763536330ce78b423f19342b7a7792c26de3944e418b3a
- Catalog publisher: SignPath Foundation; certificate chain to GlobalSign Code Signing Root R45.
- License: [VirtualDrivers MIT license](../third_party/VDD/LICENSE), plus the retained [Microsoft sample license](../driver/LICENSE).

Dis packages the unmodified archive and licenses. First-time setup verifies its checksum and Windows catalog trust, registers the Root\MttVDD software device if absent, and asks Windows to bind the signed package. The Windows administrator request and publisher prompt are part of this explicit setup action. Dis does not import certificates, disable signature enforcement, or modify Secure Boot. Existing upstream display settings are preserved.

The main Dis window runs without elevation. Setup leaves the adapter disabled. Every connection requests administrator approval for a session helper, which enables only the installed Root\\MttVDD adapter. Full adapter disabling while idle is permanent; legacy preferences cannot bypass it. Disconnect, USB removal, failed receiver startup, stalled playback, normal exit, or an unexpected engine exit ends the session and disables the adapter. Rotation retains the session and pairing credentials. Extend/Duplicate is managed through Windows display settings while connected; the application Mirror selector has been removed.

The controller requires a decoded-frame acknowledgement within 30 seconds over USB or 60 seconds for manual network pairing. After playback begins, a disconnected receiver gets three seconds to reconnect and a connected but stalled receiver gets ten seconds. The elevated helper additionally requires heartbeat renewal: its startup lease is 75 seconds, then 15 seconds after the first heartbeat. If the engine hangs and cannot perform its own cleanup, lease expiry disables the adapter independently. The helper retries adapter disabling for up to five seconds after releasing display resources.

CI uses a disposable Windows VM. Its driver test checks the catalog's normal Windows trust and imports only the known leaf certificate into that VM's TrustedPublisher store to simulate accepting the publisher prompt; it removes that entry afterward. This CI-only publisher setup is not part of the app installer and does not alter root trust. The test attempts a real virtual monitor and landscape/portrait mode change. See the actual Actions result for validation status.

The original `driver/DisIdd` source remains an independent development option derived from Microsoft's sample. It is not the signed default provider and has not been production-signed. Build it with matching Visual Studio/WDK tooling only if working on that driver implementation; normal users should use the app's signed-driver setup.
