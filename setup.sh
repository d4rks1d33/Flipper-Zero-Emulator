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

# The emulator needs an SD card image (storage mounts it at boot). Create a
# 32 GB FAT32 image with the standard Flipper folders if one doesn't exist yet.
if [ ! -f sdcard/sdcard.img ]; then
    log "creating SD card image (32 GB sparse FAT32)..."
    if python3 scripts/prepare_sdcard_2.py >/dev/null 2>&1; then
        log "SD card image ready: sdcard/sdcard.img"
    else
        printf '\033[1;33m[setup]\033[0m could not auto-create SD image; run: python3 scripts/prepare_sdcard_2.py\n'
    fi
fi

log "setup complete for project dir: $SCRIPT_DIR"
