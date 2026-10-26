"""Read-only PE/installer inventory. Never executes, installs, or patches the input."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

import pefile


NAMES = [
    "MirrorService.exe", "MirrorSettings.exe", "driver_installer.exe",
    "superdisplay_display.dll", "superdisplay_hid.dll", "superdisplay_aoap.cat",
    "superdisplay_display.cat", "superdisplay_hid.cat", "superdisplay_hidbus.cat",
    "superdisplay_wpdfilter.cat", "Wintab32.dll", "adb.exe", "AdbWinApi.dll",
    "AdbWinUsbApi.dll", "Sentry.dll", "sentry_native.dll", "crashpad_handler.exe",
    "Newtonsoft.Json.dll", "xaudio2_9redist.dll", "updater.exe",
]


def inspect(path: Path) -> dict:
    data = path.read_bytes()
    lowered = data.lower()
    pe = pefile.PE(data=data, fast_load=False)
    findings = []
    for name in NAMES:
        matches = []
        for encoding in ("ascii", "utf-16-le"):
            needle = name.encode(encoding)
            start = 0
            while (offset := lowered.find(needle.lower(), start)) >= 0:
                matches.append({"offset": offset, "encoding": encoding})
                start = offset + len(needle)
        if matches:
            findings.append({"name": name, "occurrences": matches})
    versions = {}
    for info in getattr(pe, "FileInfo", []):
        for group in info:
            for table in getattr(group, "StringTable", []):
                for key, value in table.entries.items():
                    versions[key.decode(errors="replace")] = value.decode(errors="replace")
    imports = [module.dll.decode(errors="replace") for module in getattr(pe, "DIRECTORY_ENTRY_IMPORT", [])]
    security = pe.OPTIONAL_HEADER.DATA_DIRECTORY[pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_SECURITY"]]
    return {
        "filename": path.name,
        "bytes": len(data),
        "sha256": hashlib.sha256(data).hexdigest(),
        "machine": hex(pe.FILE_HEADER.Machine),
        "pe_magic": hex(pe.OPTIONAL_HEADER.Magic),
        "versions": versions,
        "sections": [{"name": section.Name.rstrip(b"\0").decode(errors="replace"),
                      "file_offset": section.PointerToRawData, "raw_size": section.SizeOfRawData,
                      "virtual_size": section.Misc_VirtualSize} for section in pe.sections],
        "top_level_imports": imports,
        "overlay_offset": pe.get_overlay_data_start_offset(),
        "certificate_table": {"offset": security.VirtualAddress, "bytes": security.Size,
                              "signature_verified": False},
        "embedded_name_evidence": findings,
        "limits": [
            "Embedded names establish occurrences in installer data, not runtime execution or a verified installed-file inventory.",
            "No Windows execution, APK analysis, traffic capture, encoder identification, signature trust verification, or complete disassembly was performed.",
        ],
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("installer", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    report = inspect(args.installer)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Inventoried {report['filename']}: {len(report['embedded_name_evidence'])} embedded component names.")


if __name__ == "__main__":
    main()
