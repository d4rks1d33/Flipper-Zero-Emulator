#!/usr/bin/env bash
#
# Flipper Zero Emulator - per-machine setup
#
# Generates the concrete platform/script files from their *.in templates,
# substituting this machine's absolute project path. Renode resolves the
# PythonPeripheral `filename:` entries and the SD image path against absolute
# paths, so these are regenerated whenever the repo is moved to a new location.
#
# Run this once after cloning (install.sh calls it automatically), or any time
# you move the repository to a different directory.
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

log() { printf '\033[1;34m[setup]\033[0m %s\n' "$*"; }

generate() {
    local template="$1" output="$2"
    if [ ! -f "$template" ]; then
        printf '\033[1;31m[setup]\033[0m missing template: %s\n' "$template" >&2
        return 1
    fi
    sed "s#@PROJECT_DIR@#${SCRIPT_DIR}#g" "$template" > "$output"
    log "generated $output"
}

generate "platform/stm32wb55_flipper.repl.in" "platform/stm32wb55_flipper.repl"
generate "scripts/flash_firmware.resc.in"     "scripts/flash_firmware.resc"
generate "scripts/run_updater.resc.in"        "scripts/run_updater.resc"

# Ensure runtime dirs exist.
mkdir -p logs sdcard firmware

# ─── Seed the non-volatile internal flash image ──────────────────────────────
# The emulated STM32WB55 internal flash is a disk-backed, non-volatile 1 MB
# image (firmware/flash.img). Renode loads it at boot and writes it back on
# exit, so a firmware flashed by the OTA updater survives a power cycle and
# boots on its own -- exactly like the real chip. Seed it once from whatever
# stock .bin is bundled (the .bin maps 1:1 to flash at 0x08000000). Delete
# flash.img (or run ./run.sh --reset-flash) to re-flash the stock image.
seed_flash_image() {
    local img="firmware/flash.img"
    if [ -f "$img" ]; then
        log "internal flash image already present: $img"
        return
    fi
    local seed=""
    for cand in firmware/flipper-z-f7-full.bin firmware/flipper-z-f7-STOCK.bin; do
        [ -f "$cand" ] && { seed="$cand"; break; }
    done
    if [ -z "$seed" ]; then
        seed="$(find firmware -maxdepth 1 -name '*.bin' -type f 2>/dev/null | head -1)"
    fi
    if [ -z "$seed" ]; then
        printf '\033[1;33m[setup]\033[0m no firmware .bin found to seed firmware/flash.img; put one in firmware/ and re-run setup.sh (or ./run.sh will create it).\n'
        return
    fi
    log "seeding non-volatile flash image from $seed ..."
    # 1 MB image, stock .bin at offset 0, rest left as 0xFF (erased flash).
    python3 - "$seed" "$img" <<'PY'
import sys
seed, img = sys.argv[1], sys.argv[2]
SIZE = 0x100000
data = bytearray(b"\xff" * SIZE)
with open(seed, "rb") as f:
    b = f.read()
data[:len(b)] = b[:SIZE]
with open(img, "wb") as f:
    f.write(data)
PY
    log "internal flash image ready: $img (1 MB, seeded from $(basename "$seed"))"
}
seed_flash_image

# The emulator needs an SD card image (storage mounts it at boot). Create a
# 32 GB FAT32 image with the standard Flipper folders if one doesn't exist yet.
if [ ! -f sdcard/sdcard.img ]; then
    log "creating SD card image (32 GB sparse FAT32)..."
    if python3 scripts/prepare_sdcard.py >/dev/null 2>&1; then
        log "SD card image ready: sdcard/sdcard.img"
    else
        printf '\033[1;33m[setup]\033[0m could not auto-create SD image; run: python3 scripts/prepare_sdcard.py\n'
    fi
fi

log "setup complete for project dir: $SCRIPT_DIR"
