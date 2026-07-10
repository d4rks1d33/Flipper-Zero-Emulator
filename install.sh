#!/usr/bin/env bash
#
# Flipper Zero Emulator - dependency installer
#
# Installs everything needed to run the emulator:
#   - A PATCHED Renode (flash persistence) into ./tools/renode, either from a
#     shipped PREBUILT (fast, default) or built from the shipped SOURCE.
#   - System packages: dosfstools, mtools (SD image), python3, pip, build toolchain
#   - Python packages: PySDL2, pysdl2-dll, Pillow (display), heatshrink2 (SD resources)
#   - Optionally downloads an official Flipper firmware into ./firmware
#
# ─── WHY A PATCHED RENODE ─────────────────────────────────────────────────────
# The official Renode release binaries do NOT contain our patch that adds a
# disk-backing (persistence) file to MappedMemory. Without that patch the
# emulated STM32WB55 internal flash is volatile: everything written by the guest
# (settings, installed apps, and — critically — an OTA-flashed custom firmware)
# is LOST when Renode exits. The whole point of this project (persisting an OTA
# update so the custom firmware actually BOOTS on the next run) depends on it.
#
# ─── HOW RENODE IS PROVIDED ───────────────────────────────────────────────────
# This repo ships BOTH:
#   1. tools/renode-prebuilt/  — ready-to-run patched portable packages for
#      linux-x64 and linux-arm64. install.sh uses the one matching your arch by
#      default (instant, no build).
#   2. third_party/renode-src/ — the full Renode v1.16.1 source with the patch
#      already applied. If there is no matching prebuilt (or you pass
#      --force-build / --rebuild-renode), install.sh builds it for your OS/arch.
#      The build needs .NET 8 SDK + cmake/gcc/g++/make and takes ~10-40 min.
#      .NET 8 is bootstrapped locally via dotnet-install.sh if not present.
#
# The chosen Renode is extracted into ./tools/renode and marked with
# tools/renode/.patched so re-runs are instant.
#
# Works on Debian/Ubuntu/Kali (apt). For other distros install the equivalents
# of the listed packages manually.
#
# Usage:
#   ./install.sh                 # install everything (prebuilt Renode for your arch)
#   ./install.sh --with-firmware # also download the latest release firmware
#   ./install.sh --force-build   # ignore prebuilt; build patched Renode from source
#   ./install.sh --rebuild-renode  # wipe install + rebuild from the shipped source
#   ./install.sh --skip-renode-build
#                                # do NOT build/extract; assume tools/renode is set
#   RENODE_VERSION=1.16.1 ./install.sh
#
# Environment overrides:
#   RENODE_VERSION=1.16.1        # Renode version (prebuilt name / build tag)
#   RENODE_FORCE_BUILD=1         # same as --force-build
#   RENODE_REBUILD=1             # same as --rebuild-renode
#   RENODE_SKIP_BUILD=1          # same as --skip-renode-build
#   RENODE_ALLOW_OFFICIAL=1      # if the source build fails, fall back to the
#                                # official (UNPATCHED, non-persistent) binary
#   RENODE_ASSUME_UNPATCHED=1    # treat an unmarked existing tools/renode as
#                                # UNpatched and (re)install anyway
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

RENODE_VERSION="${RENODE_VERSION:-1.16.1}"
TOOLS_DIR="$SCRIPT_DIR/tools"
RENODE_DIR="$TOOLS_DIR/renode"
# The patched Renode SOURCE ships in the repo (third_party/renode-src) so it can
# be rebuilt for whatever OS/arch the user is on. We also ship ready-to-run
# PREBUILT portable packages for common arches in tools/renode-prebuilt/.
RENODE_SRC_DIR="$SCRIPT_DIR/third_party/renode-src"
RENODE_PREBUILT_DIR="$TOOLS_DIR/renode-prebuilt"
DOTNET_DIR="$TOOLS_DIR/dotnet"
PATCH_FILE="$TOOLS_DIR/renode-patches/0001-mappedmemory-persistence.patch"
PATCH_MARKER="$RENODE_DIR/.patched"
RENODE_FORCE_BUILD="${RENODE_FORCE_BUILD:-0}"
WITH_FIRMWARE=0
RENODE_REBUILD="${RENODE_REBUILD:-0}"
RENODE_SKIP_BUILD="${RENODE_SKIP_BUILD:-0}"
RENODE_ALLOW_OFFICIAL="${RENODE_ALLOW_OFFICIAL:-0}"

for arg in "$@"; do
    case "$arg" in
        --with-firmware) WITH_FIRMWARE=1 ;;
        --rebuild-renode) RENODE_REBUILD=1 ;;
        --force-build) RENODE_FORCE_BUILD=1 ;;
        --skip-renode-build) RENODE_SKIP_BUILD=1 ;;
        --allow-official) RENODE_ALLOW_OFFICIAL=1 ;;
        --help|-h)
            grep '^#' "$0" | sed 's/^# \{0,1\}//' | head -40
            exit 0 ;;
    esac
done

log()  { printf '\033[1;34m[install]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[install]\033[0m %s\n' "$*"; }
err()  { printf '\033[1;31m[install]\033[0m %s\n' "$*" >&2; }

# ─── 1. System packages ──────────────────────────────────────────────────────
install_system_packages() {
    # Runtime deps (dosfstools/mtools/python) + build toolchain for compiling
    # Renode's tlib cores and C# from source (cmake/gcc/g++/make/git). .NET 8 is
    # NOT installed via apt here — it is bootstrapped locally by dotnet-install.sh
    # inside install_renode_from_source() so the version is deterministic.
    local pkgs=(dosfstools mtools python3 python3-pip curl tar
                git cmake gcc g++ make)
    if command -v apt-get >/dev/null 2>&1; then
        log "Installing system packages via apt: ${pkgs[*]}"
        if [ "$(id -u)" -eq 0 ]; then SUDO=""; else SUDO="sudo"; fi
        $SUDO apt-get update -qq || warn "apt-get update failed (continuing)"
        $SUDO apt-get install -y -qq "${pkgs[@]}" || warn "some packages failed to install"
    else
        warn "apt-get not found. Please install manually: ${pkgs[*]}"
    fi
}

# ─── 2. Renode (built from source, WITH the persistence patch) ────────────────
#
# Entry point. Decides whether to (re)build the patched Renode, reuse an
# existing patched install, or — only if explicitly allowed — fall back to the
# official unpatched binary. See the header for WHY the source build matters.
install_renode() {
    mkdir -p "$TOOLS_DIR"

    # Idempotency: a fully-built, patched install is marked by $PATCH_MARKER.
    # Re-runs skip the (long) build unless the user forces --rebuild-renode.
    if [ "$RENODE_REBUILD" -ne 1 ] && renode_is_patched; then
        log "Patched Renode already present at $RENODE_DIR (marker $PATCH_MARKER) — skipping build."
        "$RENODE_DIR/renode" --version 2>/dev/null | head -1 || true
        return 0
    fi

    # Escape hatch: user manages tools/renode themselves (e.g. copied a patched
    # build over). Don't build, just sanity-check.
    if [ "$RENODE_SKIP_BUILD" -eq 1 ]; then
        if [ -x "$RENODE_DIR/renode" ]; then
            warn "--skip-renode-build set: using existing $RENODE_DIR WITHOUT verifying the persistence patch."
            warn "If flash does not persist across runs, drop this flag to build the patched Renode."
            return 0
        fi
        err "--skip-renode-build set but no Renode found at $RENODE_DIR. Aborting."
        return 1
    fi

    # FAST PATH: use the shipped prebuilt for this arch (patched, instant). This
    # is the normal case on Linux x86_64 / arm64 and needs no build at all.
    if [ "$RENODE_FORCE_BUILD" -ne 1 ] && [ "$RENODE_REBUILD" -ne 1 ] && install_renode_prebuilt; then
        return 0
    fi

    # SLOW PATH: build the patched Renode from the source shipped in the repo
    # (third_party/renode-src). Works on any OS/arch with .NET 8 + cmake + gcc.
    if install_renode_from_source; then
        return 0
    fi

    err "Building patched Renode from source FAILED."
    if [ "$RENODE_ALLOW_OFFICIAL" -eq 1 ]; then
        warn "RENODE_ALLOW_OFFICIAL=1 — falling back to the OFFICIAL prebuilt Renode."
        warn "WARNING: the official binary has NO persistence patch — flash (and OTA"
        warn "         updates / custom-firmware boot) will NOT persist across runs."
        install_renode_official
        return $?
    fi
    err "Fix the build environment and re-run, or set RENODE_ALLOW_OFFICIAL=1 to"
    err "install the unpatched official binary (persistence will NOT work)."
    return 1
}

# True if $RENODE_DIR holds a Renode we consider patched-and-ready.
#
# The authoritative signal is the marker file ($PATCH_MARKER, i.e.
# tools/renode/.patched) that we write after a successful patched build. Renode
# ships as a single-file bundle with the managed assemblies compressed inside,
# so grepping the binary for the patch's log strings is NOT reliable and is
# deliberately avoided.
#
# Special case: this repo already ships a PREBUILT, patched Renode that predates
# the marker convention. If we find an executable renode with no marker and the
# user hasn't asked to rebuild, we assume it is the shipped patched build,
# backfill the marker, and warn how to force a rebuild if that assumption is
# wrong. Set RENODE_ASSUME_UNPATCHED=1 to opt out and force a source build.
renode_is_patched() {
    [ -x "$RENODE_DIR/renode" ] || return 1
    if [ -f "$PATCH_MARKER" ]; then
        return 0
    fi
    if [ "${RENODE_ASSUME_UNPATCHED:-0}" -eq 1 ]; then
        return 1
    fi
    warn "Found an existing Renode at $RENODE_DIR without a $PATCH_MARKER marker."
    warn "Assuming it is this repo's prebuilt PATCHED Renode and skipping the build."
    warn "If flash does not persist, force a clean rebuild with: ./install.sh --rebuild-renode"
    warn "(or set RENODE_ASSUME_UNPATCHED=1 to always build from source instead)."
    printf 'renode v%s patched: mappedmemory-persistence (assumed prebuilt, %s)\n' \
        "$RENODE_VERSION" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$PATCH_MARKER" 2>/dev/null || true
    return 0
}

# Map `uname -m` to the arch token Renode's build.sh / package names expect.
# Returns "aarch64" or "i386" on stdout (i386 is Renode's label for x86_64 host).
renode_host_arch() {
    case "$(uname -m)" in
        aarch64|arm64) echo "aarch64" ;;
        x86_64|amd64)  echo "i386" ;;
        *)             echo "" ;;
    esac
}

# Ensure a usable `dotnet` (>=8) is available; echo the dotnet command to use.
# Prefers an already-installed system dotnet >=8, otherwise bootstraps .NET 8
# SDK into $DOTNET_DIR via the official dotnet-install.sh. Returns non-zero on
# failure. All human-readable logging goes to stderr so stdout stays clean.
ensure_dotnet() {
    if command -v dotnet >/dev/null 2>&1; then
        local major
        major="$(dotnet --version 2>/dev/null | cut -d. -f1)"
        if [ -n "$major" ] && [ "$major" -ge 8 ] 2>/dev/null; then
            log "Using system dotnet $(dotnet --version) from PATH." >&2
            echo "dotnet"
            return 0
        fi
        warn "System dotnet is < 8 ($(dotnet --version 2>/dev/null)); bootstrapping .NET 8 locally." >&2
    fi

    if [ -x "$DOTNET_DIR/dotnet" ]; then
        log "Using previously bootstrapped .NET at $DOTNET_DIR." >&2
        echo "$DOTNET_DIR/dotnet"
        return 0
    fi

    log "Bootstrapping .NET 8 SDK into $DOTNET_DIR via dotnet-install.sh ..." >&2
    mkdir -p "$DOTNET_DIR"
    local installer="$TOOLS_DIR/dotnet-install.sh"
    if ! curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"; then
        err "Could not download dotnet-install.sh." >&2
        return 1
    fi
    chmod +x "$installer"
    if ! "$installer" --channel 8.0 --install-dir "$DOTNET_DIR" >&2; then
        err "dotnet-install.sh failed." >&2
        return 1
    fi
    if [ ! -x "$DOTNET_DIR/dotnet" ]; then
        err ".NET SDK not found at $DOTNET_DIR/dotnet after install." >&2
        return 1
    fi
    echo "$DOTNET_DIR/dotnet"
    return 0
}

# Use a ready-to-run PREBUILT patched Renode shipped in tools/renode-prebuilt/
# for the current architecture. Returns 0 on success (installed into $RENODE_DIR
# with the .patched marker), non-zero if there is no matching prebuilt.
install_renode_prebuilt() {
    local pkg=""
    case "$(uname -m)" in
        x86_64|amd64)
            pkg="$RENODE_PREBUILT_DIR/renode-${RENODE_VERSION}.linux-x64-portable-dotnet.tar.gz" ;;
        aarch64|arm64)
            pkg="$RENODE_PREBUILT_DIR/renode-${RENODE_VERSION}.linux-arm64-portable-dotnet.tar.gz" ;;
        *)
            return 1 ;;
    esac
    if [ ! -f "$pkg" ]; then
        log "No shipped prebuilt for $(uname -m) at $pkg — will build from source."
        return 1
    fi
    log "Installing shipped PREBUILT patched Renode: $(basename "$pkg")"
    rm -rf "$RENODE_DIR"
    mkdir -p "$RENODE_DIR"
    if ! tar -xzf "$pkg" -C "$RENODE_DIR" --strip-components=1; then
        err "Failed to extract prebuilt package $pkg."
        return 1
    fi
    if [ ! -x "$RENODE_DIR/renode" ]; then
        err "Prebuilt extraction did not yield an executable at $RENODE_DIR/renode."
        return 1
    fi
    printf 'renode v%s patched: mappedmemory-persistence (shipped prebuilt, %s)\n' \
        "$RENODE_VERSION" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$PATCH_MARKER"
    log "Patched Renode installed from prebuilt at $RENODE_DIR"
    "$RENODE_DIR/renode" --version 2>/dev/null | head -1 || true
    return 0
}

# Build the patched Renode from the source shipped in the repo
# (third_party/renode-src, patch already applied), producing a portable package
# extracted into $RENODE_DIR. Falls back to cloning if the shipped source is
# missing. Idempotent and re-entrant.
install_renode_from_source() {
    local arch
    arch="$(renode_host_arch)"
    if [ -z "$arch" ]; then
        err "Unsupported architecture for source build: $(uname -m)."
        return 1
    fi

    if [ ! -f "$PATCH_FILE" ]; then
        err "Persistence patch not found at $PATCH_FILE — cannot build patched Renode."
        return 1
    fi

    # A forced rebuild wipes only the install and the build artifacts inside the
    # shipped source tree (NOT the shipped source itself, which is patched and
    # tracked in the repo).
    if [ "$RENODE_REBUILD" -eq 1 ]; then
        log "--rebuild-renode: removing previous install and build artifacts ..."
        rm -rf "$RENODE_DIR"
        rm -rf "$RENODE_SRC_DIR/output" 2>/dev/null || true
    fi

    # 2a. Ensure the .NET SDK. Captured separately so we can bail early.
    local dotnet_cmd dotnet_bin_dir
    if ! dotnet_cmd="$(ensure_dotnet)"; then
        err "No usable .NET 8 SDK; cannot build Renode."
        return 1
    fi
    # Renode's build.sh invokes `dotnet` from PATH, so make ours visible.
    dotnet_bin_dir="$(dirname "$dotnet_cmd")"
    if [ "$dotnet_cmd" != "dotnet" ]; then
        export PATH="$dotnet_bin_dir:$PATH"
        export DOTNET_ROOT="$dotnet_bin_dir"
    fi

    # 2b. Source tree. Normally the PATCHED source ships in the repo at
    # $RENODE_SRC_DIR (third_party/renode-src) — no clone, no network, patch
    # already applied. If it is missing (e.g. someone stripped it), clone
    # upstream and apply the patch.
    local infra_dir="$RENODE_SRC_DIR/src/Infrastructure"
    local mm_file="$infra_dir/src/Emulator/Main/Peripherals/Memory/MappedMemory.cs"

    if [ -f "$mm_file" ]; then
        log "Using Renode source shipped in the repo: $RENODE_SRC_DIR"
        if grep -q "PersistWriteThrough" "$mm_file" 2>/dev/null; then
            log "Persistence patch already present in shipped source."
        else
            warn "Shipped source is missing the persistence patch; applying it now."
            if [ -d "$infra_dir/.git" ]; then
                git -C "$infra_dir" apply "$PATCH_FILE" || { err "Patch apply failed."; return 1; }
            else
                ( cd "$infra_dir" && patch -p1 < "$PATCH_FILE" ) || { err "patch(1) apply failed."; return 1; }
            fi
        fi
    else
        # Fallback: shipped source absent -> clone upstream + patch.
        log "Shipped Renode source not found; cloning v$RENODE_VERSION (shallow, recursive) ..."
        rm -rf "$RENODE_SRC_DIR"
        if ! git clone --depth 1 --branch "v$RENODE_VERSION" --recursive \
                https://github.com/renode/renode.git "$RENODE_SRC_DIR"; then
            err "git clone of Renode v$RENODE_VERSION failed (network?)."
            return 1
        fi
        if [ ! -d "$infra_dir" ]; then
            err "Expected submodule dir not found after clone: $infra_dir"
            return 1
        fi
        if git -C "$infra_dir" apply --reverse --check "$PATCH_FILE" >/dev/null 2>&1; then
            log "Persistence patch already applied — skipping."
        elif git -C "$infra_dir" apply "$PATCH_FILE"; then
            log "Applied persistence patch to src/Infrastructure."
        else
            err "Persistence patch does not apply cleanly to the cloned tree."
            return 1
        fi
    fi

    # 2d. Build the portable package.
    # build.sh flags: --net (dotnet build), --host-arch <arch>, -t (tar the
    # portable dotnet package into output/packages/). This is the exact command
    # already proven to work in this repo.
    log "Building Renode from source (host-arch=$arch). This takes ~10-40 min ..."
    if ! ( cd "$RENODE_SRC_DIR" && ./build.sh --net --host-arch "$arch" -t ); then
        err "Renode build.sh failed."
        return 1
    fi

    # 2e. Locate the produced portable tarball and extract it into $RENODE_DIR.
    local pkg
    pkg="$(ls -1t "$RENODE_SRC_DIR"/output/packages/renode-*."${arch}"-portable-dotnet.tar.gz 2>/dev/null | head -1)"
    if [ -z "$pkg" ]; then
        # Be lenient about the exact arch label in the filename.
        pkg="$(ls -1t "$RENODE_SRC_DIR"/output/packages/renode-*portable-dotnet.tar.gz 2>/dev/null | head -1)"
    fi
    if [ -z "$pkg" ] || [ ! -f "$pkg" ]; then
        err "Build finished but no portable package found under $RENODE_SRC_DIR/output/packages/."
        return 1
    fi
    log "Extracting portable package: $(basename "$pkg")"
    rm -rf "$RENODE_DIR"
    mkdir -p "$RENODE_DIR"
    tar -xzf "$pkg" -C "$RENODE_DIR" --strip-components=1

    if [ ! -x "$RENODE_DIR/renode" ]; then
        err "Extraction did not yield an executable at $RENODE_DIR/renode."
        return 1
    fi

    # Drop the marker so future runs know this is a patched build.
    printf 'renode v%s patched: mappedmemory-persistence (%s)\n' \
        "$RENODE_VERSION" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$PATCH_MARKER"

    log "Patched Renode installed at $RENODE_DIR"
    "$RENODE_DIR/renode" --version 2>/dev/null | head -1 || true
    return 0
}

# Fallback ONLY: download the official prebuilt (UNPATCHED) Renode. Used only
# when the source build fails AND the user opted in with RENODE_ALLOW_OFFICIAL=1.
# Flash persistence will NOT work with this binary.
install_renode_official() {
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
    log "Downloading OFFICIAL (unpatched) Renode $RENODE_VERSION ($arch) ..."
    curl -fL "$url" -o "$TOOLS_DIR/renode.tar.gz"
    log "Extracting Renode ..."
    rm -rf "$RENODE_DIR"
    mkdir -p "$RENODE_DIR"
    tar -xzf "$TOOLS_DIR/renode.tar.gz" -C "$RENODE_DIR" --strip-components=1
    rm -f "$TOOLS_DIR/renode.tar.gz"
    # No .patched marker — this build is deliberately not marked as patched.
    log "Official Renode installed at $RENODE_DIR (NO persistence patch)."
    "$RENODE_DIR/renode" --version 2>/dev/null | head -1 || true
}

# ─── 3. Python packages ──────────────────────────────────────────────────────
install_python_packages() {
    log "Installing Python packages (PySDL2, pysdl2-dll, Pillow) ..."
    local pipflags="--quiet"
    # Newer pip on Debian/Kali needs --break-system-packages for global installs.
    if pip3 install $pipflags PySDL2 pysdl2-dll Pillow heatshrink2 2>/dev/null; then
        :
    elif pip3 install $pipflags --break-system-packages PySDL2 pysdl2-dll Pillow heatshrink2 2>/dev/null; then
        :
    elif pip3 install $pipflags --user PySDL2 pysdl2-dll Pillow heatshrink2 2>/dev/null; then
        :
    else
        warn "Could not install Python packages automatically."
        warn "The SDL window and PNG export need: pip install PySDL2 pysdl2-dll Pillow heatshrink2"
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
