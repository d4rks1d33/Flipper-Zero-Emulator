#!/usr/bin/env bash
# Flipper Zero Emulator Launcher
#
# Usage:
#   ./run.sh [firmware.bin] [options]
#
# Options:
#   --with-update TGZ   Prepare an SD card image with the given update package
#                       and boot into the OTA self-flash flow.
#   --no-gui            Do not launch the SDL display frontend.
#   --headless          Run Renode without any window and without GUI frontend.
#   --help              Show this help.
#
# Examples:
#   ./run.sh firmware/flipper-z-f7-full-1.4.3.bin
#   ./run.sh --with-update /path/to/flipper-z-f7-update-1.4.3.tgz
#
# Renode is used from ./tools/renode (installed by install.sh). You can override
# with the RENODE_PATH environment variable.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Locate Renode: bundled tools/renode first, then RENODE_PATH, then PATH.
if [ -n "${RENODE_PATH:-}" ] && [ -x "${RENODE_PATH}/renode" ]; then
    RENODE="${RENODE_PATH}/renode"
elif [ -x "$SCRIPT_DIR/tools/renode/renode" ]; then
    RENODE="$SCRIPT_DIR/tools/renode/renode"
elif command -v renode >/dev/null 2>&1; then
    RENODE="$(command -v renode)"
else
    echo "Error: Renode not found. Run ./install.sh first (installs into tools/renode)."
    exit 1
fi

FIRMWARE_BIN=""
UPDATE_TGZ=""
LAUNCH_GUI=1
HEADLESS=0
MONITOR_PORT="${FLIPPER_EMU_MONITOR_PORT:-1234}"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --with-update) UPDATE_TGZ="$2"; shift 2 ;;
        --no-gui)      LAUNCH_GUI=0; shift ;;
        --headless)    HEADLESS=1; LAUNCH_GUI=0; shift ;;
        --help|-h)
            grep '^#' "$0" | sed 's/^# \{0,1\}//' | head -22
            exit 0 ;;
        *) FIRMWARE_BIN="$1"; shift ;;
    esac
done

# Regenerate machine-specific files if missing (portable across clones/moves).
if [ ! -f platform/stm32wb55_flipper.repl ] || [ ! -f scripts/run_updater.resc ]; then
    ./setup.sh
fi

if [ -z "$FIRMWARE_BIN" ]; then
    # Prefer an emulator-patched build (boots fully: fixes battery/charger delays).
    FIRMWARE_BIN=$(find "$SCRIPT_DIR/firmware" -name "*EMULATOR*patched*.bin" -type f 2>/dev/null | head -1)
    if [ -z "$FIRMWARE_BIN" ]; then
        FIRMWARE_BIN=$(find "$SCRIPT_DIR/firmware" -name "*.bin" -type f 2>/dev/null | head -1)
    fi
    if [ -z "$FIRMWARE_BIN" ]; then
        echo "Error: no firmware specified and none found in firmware/"
        echo "  Put a flipper-z-f7-full-*.bin in firmware/, or run: ./install.sh --with-firmware"
        exit 1
    fi
fi
[ -f "$FIRMWARE_BIN" ] || { echo "Error: firmware not found: $FIRMWARE_BIN"; exit 1; }
FIRMWARE_BIN="$(cd "$(dirname "$FIRMWARE_BIN")" && pwd)/$(basename "$FIRMWARE_BIN")"

echo "=== Flipper Zero Emulator ==="
echo "Renode:   $RENODE"
echo "Firmware: $FIRMWARE_BIN"

export FLIPPER_EMU_LOG_DIR="$SCRIPT_DIR/logs"
export FLIPPER_EMU_FB_PATH="${FLIPPER_EMU_FB_PATH:-/tmp/flipper_fb.raw}"
export FLIPPER_EMU_MONITOR_PORT="$MONITOR_PORT"
mkdir -p "$FLIPPER_EMU_LOG_DIR"
rm -f "$FLIPPER_EMU_FB_PATH"

RESC="scripts/flash_firmware.resc"

if [ -n "$UPDATE_TGZ" ]; then
    echo "Preparing SD card with update: $UPDATE_TGZ"
    python3 "$SCRIPT_DIR/scripts/prepare_sdcard.py" --update-tgz "$UPDATE_TGZ"
    RESC="scripts/run_updater.resc"
fi

echo "Renode monitor socket: telnet localhost $MONITOR_PORT"
echo "USART1 debug console:  telnet localhost 3456"
echo "Logs: $FLIPPER_EMU_LOG_DIR/"
echo ""

# Renode runs headless (no X) and serves its monitor on a TCP port so the SDL
# frontend can inject buttons. --disable-xwt keeps it windowless either way.
"$RENODE" --disable-xwt --port "$MONITOR_PORT" \
    -e "\$firmware_bin=@${FIRMWARE_BIN}" \
    -e "include @${RESC}" &
RENODE_PID=$!

GUI_PID=""
cleanup() {
    kill "$RENODE_PID" 2>/dev/null || true
    [ -n "$GUI_PID" ] && kill "$GUI_PID" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

if [ "$LAUNCH_GUI" -eq 1 ]; then
    sleep 3
    if python3 -c "import sdl2" 2>/dev/null; then
        echo "Launching SDL display frontend..."
        python3 "$SCRIPT_DIR/frontend/sdl_frontend.py" &
        GUI_PID=$!
    else
        echo "PySDL2 not available; skipping GUI window."
        echo "Use the headless viewer:  python3 frontend/view_display.py --watch"
    fi
fi

wait "$RENODE_PID"
