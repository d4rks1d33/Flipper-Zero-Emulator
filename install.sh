#!/usr/bin/env bash
#
# Flipper Zero Emulator - dependency installer
#
# Installs everything needed to run the emulator:
#   - Renode (portable, bundled .NET runtime) into ./tools/renode
#   - System packages: dosfstools, mtools (SD image), python3, pip
#   - Python packages: PySDL2, pysdl2-dll, Pillow (display frontend)
#   - Optionally downloads an official Flipper firmware into ./firmware
#
# Works on Debian/Ubuntu/Kali (apt). For other distros install the equivalents
# of dosfstools + mtools manually; the rest is self-contained.
#
# Usage:
#   ./install.sh                 # install everything (no firmware download)
#   ./install.sh --with-firmware # also download the latest release firmware
#   RENODE_VERSION=1.16.1 ./install.sh
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

RENODE_VERSION="${RENODE_VERSION:-1.16.1}"
TOOLS_DIR="$SCRIPT_DIR/tools"
RENODE_DIR="$TOOLS_DIR/renode"
WITH_FIRMWARE=0

for arg in "$@"; do
    case "$arg" in
        --with-firmware) WITH_FIRMWARE=1 ;;
        --help|-h)
            grep '^#' "$0" | sed 's/^# \{0,1\}//' | head -20
            exit 0 ;;
    esac
done

log()  { printf '\033[1;34m[install]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[install]\033[0m %s\n' "$*"; }
err()  { printf '\033[1;31m[install]\033[0m %s\n' "$*" >&2; }

# ─── 1. System packages ──────────────────────────────────────────────────────
install_system_packages() {
    local pkgs=(dosfstools mtools python3 python3-pip curl tar)
    if command -v apt-get >/dev/null 2>&1; then
        log "Installing system packages via apt: ${pkgs[*]}"
        if [ "$(id -u)" -eq 0 ]; then SUDO=""; else SUDO="sudo"; fi
        $SUDO apt-get update -qq || warn "apt-get update failed (continuing)"
        $SUDO apt-get install -y -qq "${pkgs[@]}" || warn "some packages failed to install"
    else
        warn "apt-get not found. Please install manually: ${pkgs[*]}"
    fi
}

# ─── 2. Renode (portable, arch-aware) ────────────────────────────────────────
install_renode() {
    if [ -x "$RENODE_DIR/renode" ]; then
        log "Renode already present at $RENODE_DIR"
        return
    fi
    mkdir -p "$TOOLS_DIR"

    local arch tarball
    arch="$(uname -m)"
    case "$arch" in
        aarch64|arm64)
            tarball="renode-${RENODE_VERSION}.linux-arm64-portable-dotnet.tar.gz" ;;
        x86_64|amd64)
            tarball="renode-${RENODE_VERSION}.linux-portable-dotnet.tar.gz" ;;
        *)
            err "Unsupported architecture: $arch. Install Renode manually into $RENODE_DIR."
            return 1 ;;
    esac

    local url="https://github.com/renode/renode/releases/download/v${RENODE_VERSION}/${tarball}"
    log "Downloading Renode $RENODE_VERSION ($arch) ..."
    curl -fL "$url" -o "$TOOLS_DIR/renode.tar.gz"
    log "Extracting Renode ..."
    mkdir -p "$RENODE_DIR"
    tar -xzf "$TOOLS_DIR/renode.tar.gz" -C "$RENODE_DIR" --strip-components=1
    rm -f "$TOOLS_DIR/renode.tar.gz"
    log "Renode installed at $RENODE_DIR"
    "$RENODE_DIR/renode" --version 2>/dev/null | head -1 || true
}

# ─── 3. Python packages ──────────────────────────────────────────────────────
install_python_packages() {
    log "Installing Python packages (PySDL2, pysdl2-dll, Pillow) ..."
    local pipflags="--quiet"
    # Newer pip on Debian/Kali needs --break-system-packages for global installs.
    if pip3 install $pipflags PySDL2 pysdl2-dll Pillow 2>/dev/null; then
        :
    elif pip3 install $pipflags --break-system-packages PySDL2 pysdl2-dll Pillow 2>/dev/null; then
        :
    elif pip3 install $pipflags --user PySDL2 pysdl2-dll Pillow 2>/dev/null; then
        :
    else
        warn "Could not install Python packages automatically."
        warn "The SDL window and PNG export need: pip install PySDL2 pysdl2-dll Pillow"
        warn "The headless ASCII viewer (view_display.py) works without them."
    fi
}

# ─── 4. Optional firmware download ───────────────────────────────────────────
download_firmware() {
    log "Fetching latest official release firmware ..."
    mkdir -p "$SCRIPT_DIR/firmware"
    local dir_json ver url
    dir_json="$(curl -fsSL https://update.flipperzero.one/firmware/directory.json)" || {
        warn "Could not reach the Flipper update server."; return; }
    read -r ver url < <(python3 - "$dir_json" <<'PY'
import json, sys
d = json.loads(sys.argv[1])
for ch in d["channels"]:
    if ch["id"] == "release":
        v = ch["versions"][0]
        for f in v["files"]:
            if f["target"] == "f7" and f["type"] == "full_bin":
                print(v["version"], f["url"]); break
        break
PY
)
    if [ -n "${url:-}" ]; then
        log "Downloading firmware $ver ..."
        curl -fL "$url" -o "$SCRIPT_DIR/firmware/flipper-z-f7-full-${ver}.bin"
        log "Firmware saved to firmware/flipper-z-f7-full-${ver}.bin"
    else
        warn "Could not determine firmware URL."
    fi
}

# ─── Run ──────────────────────────────────────────────────────────────────────
install_system_packages
install_renode
install_python_packages
[ "$WITH_FIRMWARE" -eq 1 ] && download_firmware

# Generate machine-specific files from templates.
log "Generating platform files for this machine ..."
"$SCRIPT_DIR/setup.sh"

log "Done."
echo ""
echo "Next steps:"
echo "  1. Put a firmware .bin in ./firmware/ (or re-run: ./install.sh --with-firmware)"
echo "  2. Run:   ./run.sh"
echo "     Headless: ./run.sh --no-gui   then   python3 frontend/view_display.py --watch"
