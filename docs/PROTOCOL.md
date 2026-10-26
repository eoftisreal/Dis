# Dis protocol v1

Each connection is a TCP stream protected by TLS 1.2 or TLS 1.3. Android verifies SHA-256 of the exact server certificate against the connection link. This explicit certificate identity replaces public-CA/hostname authentication for the self-signed session certificate. Never accept an unverified certificate to work around a connection failure.

## Envelope

All multibyte values are big endian. The header is 16 bytes.

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 4 | ASCII `DIS1` |
| 4 | 1 | Message type |
| 5 | 1 | Flags, must be zero |
| 6 | 2 | Reserved, must be zero |
| 8 | 4 | Unsigned payload length |
| 12 | 4 | Unsigned sequence number |

Unknown magic, flags, types, invalid lengths, truncated packets, and unsupported session versions terminate the connection. Validate before allocating payload memory. Each direction must serialize complete packet writes. TCP fragmentation and coalescing must not change message boundaries.

| Type | Value | Payload |
| --- | --- | --- |
| Hello | 1 | UTF-8 JSON, 2–4096 bytes |
| Config | 2 | UTF-8 JSON, 2–4096 bytes |
| Frame | 3 | 13 bytes–8 MiB total |
| Input | 4 | Exactly 24 bytes |
| Ack | 5 | Empty; sequence acknowledges Frame |
| Ping | 6 | Exactly 8 bytes, opaque echo data |
| Pong | 7 | Exactly 8 bytes, unchanged echo data |
| Error | 8 | UTF-8 JSON, 2–4096 bytes |

## Negotiation

Android sends Hello with sequence 0:

```json
{"version":1,"token":"64 hexadecimal characters","maxWidth":1920,"maxHeight":1080,"fps":30,"codec":"jpeg"}
```

The host authenticates the 32-byte token with constant-time comparison, validates capabilities, and allows one authenticated display session. Handshake/Hello deadline: 10 seconds. At most four pending handshakes are accepted. A second authenticated device receives an Error. There is no account, remote relay, password recovery, or cloud dependency.

Host Config has sequence 0:

```json
{"version":1,"codec":"jpeg","width":1920,"height":1080,"fps":30,"input":true,"pen":true}
```

The source monitor's aspect ratio is preserved without upscaling. v1 receiver limits output to 1920×1080 and 30 fps; portrait screens fit inside that bounding box. `pen` indicates that Windows synthetic pen injection initialized, not that every application accepts pressure.

## Frames and backpressure

Frame payload: width (u16), height (u16), host monotonic capture timestamp in microseconds (i64), then a complete JPEG. JPEG dimensions must match the header and negotiated Config before allocating its decoded bitmap. No remote timestamps are treated as end-to-end latency without clock synchronization.

Frame sequences start at 1 and wrap modulo 2^32. Android acknowledges after consuming the frame, including deliberate discard during a surface transition. The host never sends the next frame until that exact sequence is acknowledged. An acknowledgement for a stale or future frame terminates the session. The next capture is fresh; there is no queue of old frames. Ack deadline: 8 seconds. This favors bounded buffering over maximum high-latency-link throughput.

## Input

| Offset | Size | Meaning |
| --- | --- | --- |
| 0 | 1 | Kind: mouse 0, pen 1 |
| 1 | 1 | Action: down 0, move 1, up 2, cancel 3, hover 4 |
| 2 | 2 | Buttons: bit 0 pen barrel, bit 1 eraser |
| 4 | 4 | x, float32 normalized 0…1 |
| 8 | 4 | y, float32 normalized 0…1 |
| 12 | 4 | pressure, float32 normalized 0…1 |
| 16 | 4 | tiltX, float32 −90…90 degrees |
| 20 | 4 | tiltY, float32 −90…90 degrees |

All floats must be finite. Letterboxed areas do not initiate input. Coordinates map to the chosen monitor, including negative desktop origins. Input packets are ignored when host input is disabled, but malformed input is still rejected. Finger input maps one primary contact to a mouse; multi-touch gestures are not implemented. Pen pressure/tilt use Windows synthetic pointer input. Barrel/eraser bits apply only to pen input.

The Android input queue is bounded to 128 packets. Overflow closes the session rather than silently losing an up event. The host releases held input on session teardown. The host closes peers exceeding 2000 received packets/second. Reconnection creates a new input session; it never continues a held stroke.

## Connection links

`dis://<Windows-IP>:<port>?fp=<64-hex-SHA256>&token=<64-hex-secret>`

For ADB reverse, the IP is `127.0.0.1` on Android and forwards to Windows loopback. Stopping/restarting the server creates a fresh certificate and token. The receiver does not persist the link or accept incoming deep links. Tokens are never written to application logs.

Future incompatible wire changes require a new magic/version and explicit negotiation. Hardware codec support must specify access-unit boundaries, codec configuration, color metadata, decoder recovery, and keyframe requests before being advertised.
