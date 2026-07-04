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
generate "scripts/run_updater.resc.in"        "scripts/run_updater.resc"

# Ensure runtime dirs exist.
mkdir -p logs sdcard firmware

log "setup complete for project dir: $SCRIPT_DIR"
