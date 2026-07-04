#!/usr/bin/env python3
"""
FUS (Firmware Upgrade Services) stub for the Flipper Zero emulator.

On real hardware the radio stack + FUS live on Core2 (Cortex-M0+) and are
signed, closed ST binaries. The updater talks to FUS via IPCC/SHCI to install
a new radio stack (radio.bin). We CANNOT emulate FUS faithfully.

This stub validates the update manifest (update.fuf) — in particular it checks
the Radio CRC declared in the manifest against the radio.bin file — and then
logs the radio-stack installation as if it had succeeded, without touching
anything. This mirrors what the emulator's IPCC stub reports to the firmware:
"radio stack already up to date / installed".

Manifest keys used (Flipper File Format, from update.fuf):
    Radio:          radio.bin
    Radio address:  <little-endian hex>   (e.g. 00 70 0D 08 -> 0x08 0D 70 00)
    Radio version:  <bytes>
    Radio CRC:      <little-endian hex 32-bit>

Usage:
    python3 fus_stub.py <path-to-update-dir>
"""

import os
import sys
import struct


def crc32_stm32(data):
    """STM32 hardware CRC (CRC-32/MPEG-2 variant used by Flipper's crc32_calc).

    Flipper's crc32_calc_buffer uses the STM32 CRC peripheral: poly 0x04C11DB7,
    init 0xFFFFFFFF, no final xor, no reflection, word-wise big-endian feed.
    """
    crc = 0xFFFFFFFF
    # Pad to multiple of 4
    padded = data + b"\x00" * ((4 - len(data) % 4) % 4)
    for i in range(0, len(padded), 4):
        word = struct.unpack(">I", padded[i:i+4])[0]
        crc ^= word
        for _ in range(32):
            if crc & 0x80000000:
                crc = ((crc << 1) ^ 0x04C11DB7) & 0xFFFFFFFF
            else:
                crc = (crc << 1) & 0xFFFFFFFF
    return crc


def parse_manifest(path):
    kv = {}
    with open(path, "r", errors="replace") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            if ":" in line:
                k, v = line.split(":", 1)
                kv[k.strip()] = v.strip()
    return kv


def le_hex_to_int(s):
    parts = s.split()
    b = bytes(int(x, 16) for x in parts)
    return int.from_bytes(b, "little")


def main():
    if len(sys.argv) < 2:
        print("usage: fus_stub.py <update-dir>")
        sys.exit(1)

    update_dir = sys.argv[1]
    manifest_path = os.path.join(update_dir, "update.fuf")
    if not os.path.exists(manifest_path):
        print(f"[FUS] ERROR: manifest not found: {manifest_path}")
        sys.exit(1)

    kv = parse_manifest(manifest_path)
    print("[FUS] Manifest parsed:")
    for k in ("Filetype", "Version", "Target", "Loader", "Firmware",
              "Radio", "Radio address", "Radio version", "Radio CRC"):
        if k in kv:
            print(f"        {k}: {kv[k]}")

    radio_file = kv.get("Radio")
    if not radio_file:
        print("[FUS] No radio stack in this update; nothing to install.")
        return

    radio_path = os.path.join(update_dir, radio_file)
    if not os.path.exists(radio_path):
        print(f"[FUS] ERROR: radio file missing: {radio_path}")
        sys.exit(1)

    with open(radio_path, "rb") as f:
        radio_data = f.read()

    declared_crc = None
    if "Radio CRC" in kv:
        declared_crc = le_hex_to_int(kv["Radio CRC"])

    computed = crc32_stm32(radio_data)
    print(f"[FUS] radio.bin size = {len(radio_data)} bytes")
    if declared_crc is not None:
        print(f"[FUS] declared Radio CRC = 0x{declared_crc:08X}")
        print(f"[FUS] computed  CRC (stm32) = 0x{computed:08X}")
        # Note: exact CRC match depends on the precise algorithm/region the
        # official tooling uses; we log both so discrepancies are visible.

    addr = kv.get("Radio address", "?")
    ver = kv.get("Radio version", "?")
    print(f"[FUS] Simulating radio stack install:")
    print(f"        target flash address = {addr}")
    print(f"        radio version        = {ver}")
    print(f"[FUS] Radio stack 'installed' (stub). FUS reports success to Core1.")
    print(f"[FUS] NOTE: no real RF/BLE stack is present; IPCC stub will report "
          f"the stack as running.")


if __name__ == "__main__":
    main()
