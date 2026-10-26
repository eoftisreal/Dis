# Network and USB coexistence acceptance

The implementation opens sockets and, in USB ADB mode, adds one per-device reverse mapping. It does not call route, DNS, tethering, network-priority, USB accessory-mode, adapter-disable, or global ADB-reset operations. Those source properties do not prove that a particular device exposes compatible simultaneous USB functions.

Record the Windows build/GPU, Android model/version, USB cable/port/hub, transport, display mode/resolution, and app revision with every result. None of these physical-device tests has been claimed as passed in the initial source milestone.

| Setup | Procedure | Acceptance |
| --- | --- | --- |
| PC Ethernet + USB ADB display | Browse/download through Ethernet while streaming; compare Windows routes/adapter state before, during, after | Ethernet stays connected; routes/DNS unchanged by Dis; no display stall or unintended adapter reset |
| Phone USB tethering + ADB display, one cable | Enable tethering first, authorize ADB, start reverse mode, transfer data to the PC while streaming | Tethering remains enabled; both continue; any OEM incompatibility is recorded explicitly |
| Phone USB tethering + IP display, one cable | Select the tether adapter in Dis; connect Android to the displayed Windows adapter IP; use internet on the PC | Both traffic types work; compare throughput/latency under contention |
| PC Ethernet + Android Wi-Fi | Select PC Ethernet IPv4 on a reachable LAN and connect from Android Wi-Fi | No need to disable either adapter; frame acknowledgements continue |
| Android Ethernet adapter/hub | Connect Android to Ethernet and PC to the same LAN; use IP display mode | Display works over Ethernet; unrelated network activity remains functional |
| ADB + existing port mapping | Create an unrelated reverse mapping; start/stop Dis; test a conflicting same-port mapping | Unrelated mappings survive; conflicting mapping is rejected rather than overwritten |
| Cable removal while dragging/inking | Disconnect while a button or pen contact is down, then reconnect | No stuck mouse/pen contact; bounded timeout; a new session starts with no inherited stroke |
| Route/adapter change | Disable the selected adapter, reconnect cable, or change LAN | Receiver reports disconnect/retry; no route repair or global network reset; user can restart on the new adapter |
| Slow/contended link | Saturate available bandwidth while moving windows | At most one unacknowledged frame; no growing old-frame queue; stats expose lower frame rate |
| Repeated connect/stop | Repeat 50 times; include Android app backgrounding and rotation | No leaked sockets/threads/bitmaps, duplicate input writer, stale callbacks, or leaked Dis-owned tunnels |

For comparison, capture the same tests with SuperDisplay at equivalent settings. “No interference” should distinguish untouched network configuration from unavoidable shared-link bandwidth competition.
