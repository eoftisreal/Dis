"""Read-only APK/DEX inventory, optionally linked to an existing JADX output folder.

This script does not execute, modify, re-sign, or extract the APK. API references
and decompiled source locations describe static evidence, not observed behavior.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import struct
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path


PACKAGE = "com.kelocube.mirrorclient"
FOCUS = (
    "Decoder", "AsyncDecoder", "SyncDecoder", "YUVVideoStream", "Transport",
    "TransportKt", "TCPTransport", "TCPTransportListener", "AOAPTransport",
    "AOAPPipe", "AOAPTransportListener", "MirrorClient", "MirrorSession",
    "Input", "Pointer", "CanvasScaler", "PrefsKt", "MirrorActivity",
)
API_CLASSES = {
    "Landroid/media/MediaCodec;", "Landroid/media/MediaCodecInfo$VideoCapabilities;",
    "Landroid/media/MediaCodecInfo$CodecCapabilities;", "Landroid/media/MediaFormat;",
    "Landroid/hardware/usb/UsbManager;", "Ljava/nio/channels/ServerSocketChannel;",
    "Ljava/nio/channels/SocketChannel;", "Lnet/jpountz/lz4/LZ4Factory;",
    "Lnet/jpountz/lz4/LZ4FastDecompressor;",
}
MARKERS = (
    "video/avc", "low-latency", "operating-rate", "max-input-size",
    "KELOCUBE_MIRR_", "pref_force_software_encoder", "pref_remain_connected",
)
SOURCE_PATTERNS = {
    "AsyncDecoder": ("createDecoderByType", "setCallback", "releaseOutputBuffer", "operating-rate", "low-latency", "isSizeSupported"),
    "SyncDecoder": ("dequeueOutputBuffer", "releaseOutputBuffer", "operating-rate"),
    "YUVVideoStream": ("new AsyncDecoder", "new SyncDecoder", "surfaceCreated", "surfaceDestroyed"),
    "Transport": ("SUPPORTED_HOST_VERSIONS", "ByteOrder.LITTLE_ENDIAN", "readFromSocket(buffer, 3)", "getShort()", "getOnData().invoke"),
    "TransportKt": ("HANDSHAKE_BASE", "SUPPORTED_HOST_VERSIONS", "TIMEOUT_"),
    "TCPTransportListener": ("ServerSocketChannel.open", "bind(new InetSocketAddress"),
    "AOAPTransportListener": ("openAccessory", "getAccessoryList", "UsbManager"),
    "MirrorSession": ("onFrameDone()", "DataType.FrameDone", "PREF_FRAMERATE", "PREF_RESOLUTION", "DataType.Pen", "DataType.Keyboard"),
    "Input": ("onHover(", "onTouch(", "getPressure(", "getPointerCount(", "getAxisValue("),
    "Pointer": ("fastestJavaInstance", "lz4.decompress", "onShapeData(", "onMoveData(", "glTexSubImage2D"),
    "MirrorActivity": ("onConfigurationChanged(",),
    "PrefsKt": ("APP_DEFAULT_PORT", "PREF_FORCE_SOFTWARE_ENCODER", "PREF_FRAMERATE_DEFAULT"),
}


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def dex_inventory(data: bytes) -> dict:
    if len(data) < 112 or not re.fullmatch(rb"dex\n0[34][0-9]\0", data[:8]):
        raise ValueError("Unsupported or truncated DEX header")

    def u32(offset: int) -> int:
        return struct.unpack_from("<I", data, offset)[0]

    if u32(32) != len(data) or u32(36) != 112 or u32(40) != 0x12345678:
        raise ValueError("Invalid DEX length, header size, or endianness")

    def table(header: int, width: int) -> tuple[int, int]:
        count, offset = u32(header), u32(header + 4)
        if count > 1_000_000 or offset + count * width > len(data):
            raise ValueError("DEX table exceeds the input")
        return count, offset

    string_count, string_offset = table(56, 4)
    type_count, type_offset = table(64, 4)
    method_count, method_offset = table(88, 8)
    class_count, class_offset = table(96, 32)
    strings = []
    for n in range(string_count):
        start = u32(string_offset + n * 4)
        for _ in range(5):  # Skip the bounded ULEB128 UTF-16 character count.
            if start >= len(data):
                raise ValueError("Truncated DEX string")
            byte = data[start]
            start += 1
            if byte < 128:
                break
        else:
            raise ValueError("Malformed DEX string length")
        end = data.find(b"\0", start)
        if end < 0:
            raise ValueError("Unterminated DEX string")
        strings.append(data[start:end].decode("utf-8", errors="replace"))
    types = [strings[u32(type_offset + n * 4)] for n in range(type_count)]
    classes = []
    prefix = "L" + PACKAGE.replace(".", "/") + "/"
    app_classes = 0
    for n in range(class_count):
        offset = class_offset + n * 32
        name = types[u32(offset)]
        if name.startswith(prefix):
            app_classes += 1
            if name[len(prefix):-1] in FOCUS:
                classes.append({"descriptor": name, "class_def_offset": offset})
    references = []
    for n in range(method_count):
        owner, _, name = struct.unpack_from("<HHI", data, method_offset + n * 8)
        if types[owner] in API_CLASSES:
            references.append({"owner": types[owner], "method": strings[name], "method_id": n})
    return {
        "bytes": len(data), "sha256": digest(data),
        "dex_version": data[4:7].decode("ascii"),
        "string_count": string_count, "method_reference_count": method_count,
        "class_count": class_count, "application_class_count": app_classes,
        "focus_classes": classes, "selected_api_references": references,
        "selected_string_markers": [value for value in MARKERS if value in strings],
    }


def jadx_inventory(root: Path) -> dict:
    namespace = "{http://schemas.android.com/apk/res/android}"
    manifest_path = root / "resources/AndroidManifest.xml"
    manifest_bytes = manifest_path.read_bytes()
    manifest = ET.fromstring(manifest_bytes)
    sdk = manifest.find("uses-sdk")
    application = manifest.find("application")
    if sdk is None or application is None:
        raise ValueError("Incomplete decoded manifest")
    decoded = {
        "sha256": digest(manifest_bytes), "package": manifest.get("package"),
        "version_name": manifest.get(namespace + "versionName"),
        "version_code": manifest.get(namespace + "versionCode"),
        "min_sdk": sdk.get(namespace + "minSdkVersion"),
        "target_sdk": sdk.get(namespace + "targetSdkVersion"),
        "permissions": [p.get(namespace + "name") for p in manifest.findall("uses-permission")],
        "activities": [{key: p.get(namespace + key) for key in ("name", "exported", "configChanges", "launchMode")}
                       for p in application.findall("activity") if (p.get(namespace + "name") or "").startswith(PACKAGE)],
    }
    evidence = []
    for name, patterns in SOURCE_PATTERNS.items():
        path = root / "sources" / PACKAGE.replace(".", "/") / (name + ".java")
        data = path.read_bytes()
        text = data.decode("utf-8")
        lines = text.splitlines()
        evidence.append({
            "path": str(path.relative_to(root)).replace("\\", "/"),
            "sha256": digest(data), "incomplete_method_count": text.count("Method not decompiled"),
            "locations": {pattern: [i + 1 for i, line in enumerate(lines)
                                     if pattern in line and "@Metadata(" not in line]
                          for pattern in patterns},
        })
    return {"decoded_manifest": decoded, "decompiled_source_evidence": evidence}


def inspect(path: Path, jadx: Path | None = None) -> dict:
    data = path.read_bytes()
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        names = [entry.filename for entry in entries]
        if len(names) != len(set(names)):
            raise ValueError("Ambiguous APK: duplicate ZIP entry names")
        dex = {}
        for entry in entries:
            if re.fullmatch(r"classes(?:[2-9]|[1-9][0-9]+)?\.dex", entry.filename):
                if entry.file_size > 128 * 1024 * 1024:
                    raise ValueError("DEX exceeds the research memory limit")
                dex[entry.filename] = dex_inventory(archive.read(entry))
        manifest = archive.read("AndroidManifest.xml")
        result = {
            "filename": path.name, "bytes": len(data), "sha256": digest(data),
            "zip_entry_count": len(entries), "binary_manifest_sha256": digest(manifest),
            "native_libraries": [name for name in names if name.endswith(".so")],
            "dex": dex,
            "limits": [
                "The uploader supplied this artifact; publisher authenticity and APK signature trust have not been verified.",
                "API references and decompiled locations are static evidence, not proof of runtime execution or performance.",
                "The optional JADX folder must be generated from this same APK; source hashes make that output reproducible but do not independently bind it to the APK.",
                "The Windows service, driver implementation, USB descriptor behavior and hardware latency are outside this APK inventory.",
            ],
        }
    if jadx is not None:
        result["jadx"] = jadx_inventory(jadx)
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("apk", type=Path)
    parser.add_argument("--jadx-output", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        report = inspect(args.apk, args.jadx_output)
    except (OSError, ValueError, KeyError, IndexError, struct.error, zipfile.BadZipFile, ET.ParseError) as error:
        parser.exit(1, f"Cannot inventory APK: {error}\n")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Inventoried {report['filename']}: {len(report['dex'])} DEX files; {len(report['native_libraries'])} packaged native libraries.")


if __name__ == "__main__":
    main()
