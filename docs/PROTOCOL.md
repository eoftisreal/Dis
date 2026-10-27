# Dis protocol — v1 compatibility and v2 streaming

All packets retain the 16-byte DIS1 big-endian envelope: magic (4), type (1), zero flags (1), zero reserved (2), payload length (4), sequence (4). TLS 1.2/1.3 authenticates the exact server certificate using SHA-256 from the pairing link. A random 256-bit session token authenticates the receiver. Headers, payload limits, and pointer values are validated before use.

## Negotiation

Hello carries version, token, maxWidth, maxHeight, fps, and codec. Version 1 supports JPEG only. Version 2 supports h264 or jpeg, up to a 1920-by-1920 bounding box with a 1920-by-1080 maximum pixel area, including portrait. The host selects an even-sized aspect-preserving mode within the selected quality budget. Config returns the negotiated version, codec, width, height, fps, input/pen availability, and actual encoder label.

For H.264 the host sends CodecConfig (type 9, sequence 0, 8–65536 bytes) immediately after Config: Annex B SPS and PPS NAL units with start codes. The receiver separates these into MediaCodec csd-0/csd-1. Frame packets then carry complete Annex B access units. Baseline profile, no B pictures, and low-latency settings are requested from the Media Foundation encoder. Hardware support is discovered, not assumed.

## Message types and limits

| Type | Number | Payload |
| --- | --- | --- |
| Hello | 1 | JSON 2–4096 bytes |
| Config | 2 | JSON 2–4096 bytes |
| Frame | 3 | 13 bytes–8 MiB: width u16, height u16, timestamp i64, encoded data |
| Input | 4 | 24-byte pointer data |
| Ack | 5 | Empty; envelope sequence acknowledges a consumed frame |
| Ping / Pong | 6 / 7 | 8 opaque bytes |
| Error | 8 | JSON 2–4096 bytes |
| CodecConfig | 9 | Annex B parameter sets, 8–65536 bytes |

Frame timestamps use the host's monotonic microsecond clock and are retained through Media Foundation and MediaCodec. They identify decoder output for acknowledgements; clocks on different devices are not subtracted to claim end-to-end latency.

## Bounded pipelining

Version 1 permits one outstanding frame; version 2 permits two. A frame slot is reserved before sending. Only an acknowledgement matching an outstanding sequence releases that slot. Wrong/stale acknowledgements are rejected. The sender waits when the window is full; it never queues an unbounded series of old frames or drops encoded H.264 reference pictures. Raw captures that cannot be submitted to a busy asynchronous encoder can be skipped safely.

For JPEG, ACK follows frame consumption. For H.264, ACK follows release of decoder output to its Surface; this is not a promise of exact physical presentation time. The decoder's input/output queues are bounded and a failed/surface-lost session reconnects. A frame-slot/send timeout closes a stalled peer.

## Pointer input

The 24-byte Input payload is kind (u8: mouse 0 / pen 1), action (u8: down 0 / move 1 / up 2 / cancel 3 / hover 4), buttons (u16: barrel bit 0 / eraser bit 1), then five float32 values: x, y, pressure (0…1), tiltX and tiltY (−90…90). All floats must be finite. Coordinates map through the aspect-correct display view to the selected Windows monitor. The input queue is bounded; session teardown releases held mouse/pen contacts.

In the 0.3 implementation, pen Hover means in range without contact. Pen Cancel releases contact, if present, and clears range; cancelling an already out-of-range pen does nothing. A pen replacing a finger sends a mouse Cancel before its pen Down. No wire-format change is needed. Below eight queued packets, movement samples are retained; at eight queued packets or more, only adjacent Move/Move or Hover/Hover packets with identical kind and buttons may be replaced with the newest sample. Contact/button/tool transitions retain their order.

## USB pairing and rotation

After creating its ADB reverse tunnel and loopback TLS listener, Windows launches the permission-protected UsbConnectActivity via authorized ADB shell. The URI is base64url-encoded before Android shell argument transport, so its ampersands are never shell operators. The activity validates it and transfers it inside the app process; the public launcher activity does not accept arbitrary pairing extras. USB debugging authorization is still required.

Ordinary Stop revokes the certificate/token session. Applying a Windows orientation change preserves that session only across its controlled server restart, avoiding another manual pairing. Network pairing remains manual in this milestone.
