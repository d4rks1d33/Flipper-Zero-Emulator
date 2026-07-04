#!/usr/bin/env python3
"""
Prepare a FAT32 SD card image for Flipper Zero emulator.

Usage:
  python3 scripts/prepare_sdcard.py [--update-tgz path/to/update.tgz] [--output sdcard/sdcard.img] [--size 64]

This creates a FAT32 image that can be used as the emulated SD card.
If --update-tgz is provided, it extracts the update package into
/update/<version>/ on the image and creates the /.fupdate pointer file.

Writing files onto the image is done via a real loop-mount (`mount -o loop`)
whenever possible -- this uses the kernel's own FAT driver, which is far
faster and more reliable than `mtools` for anything beyond a handful of tiny
files, and doesn't rely on subprocess pipes that can hang (see SDImage below).
If loop-mounting isn't available (no root / no loop devices, e.g. some
sandboxed CI environments), it falls back to `mtools` (mcopy/mmd) with strict
timeouts so a hang becomes a clear error instead of an infinite wait.
"""

import argparse
import os
import shutil
import subprocess
import sys
import tarfile
import tempfile

# Default timeout (seconds) for any external command we shell out to.
# If a command doesn't finish in this window, we abort loudly instead of
# hanging forever (this is what bit us with subprocess capture_output=True
# deadlocking on WSL2 pipes).
CMD_TIMEOUT = 60


def _run(cmd, timeout=CMD_TIMEOUT, check=True):
    """Run an external command without capturing stdout/stderr (so output is
    visible live and we never depend on a pipe being drained), with a hard
    timeout so a hang becomes a clear, actionable error.
    """
    printable = " ".join(cmd)
    try:
        return subprocess.run(cmd, check=check, timeout=timeout)
    except subprocess.TimeoutExpired:
        print(f"\n  ERROR: command timed out after {timeout}s: {printable}")
        print("         This previously happened due to a subprocess pipe "
              "deadlock (WSL2-related). Aborting instead of hanging forever.")
        sys.exit(1)
    except subprocess.CalledProcessError as e:
        print(f"\n  ERROR: command failed (exit {e.returncode}): {printable}")
        sys.exit(1)
    except FileNotFoundError:
        print(f"\n  ERROR: command not found: {cmd[0]}")
        sys.exit(1)


class SDImage:
    """Wraps a FAT32 image file and exposes mkdir/copy operations.

    Prefers a real loop-mount (kernel FAT driver via `mount -o loop` +
    shutil), which is both faster and more robust than mtools for anything
    beyond a couple of tiny files. Falls back to mtools (mcopy/mmd) if
    loop-mounting isn't available (e.g. not root, no loop devices).
    """

    def __init__(self, img_path):
        self.img_path = img_path
        self.mount_point = None
        self.use_loop = False

    def __enter__(self):
        if os.geteuid() != 0:
            print("  Not running as root: will use mtools (mcopy/mmd) "
                  "instead of a real loop-mount.")
            return self

        mount_point = tempfile.mkdtemp(prefix="sdcard_mnt_")
        try:
            _run(["mount", "-o", "loop", self.img_path, mount_point], timeout=30)
        except SystemExit:
            # _run already printed an error and called sys.exit(1); we don't
            # want a hard exit here, we want to fall back to mtools instead.
            os.rmdir(mount_point)
            print("  Loop-mount failed: will use mtools (mcopy/mmd) instead.")
            return self

        self.mount_point = mount_point
        self.use_loop = True
        print(f"  Loop-mounted image at {mount_point} (using kernel FAT driver).")
        return self

    def __exit__(self, exc_type, exc_val, exc_tb):
        if self.use_loop and self.mount_point:
            _run(["sync"], timeout=30, check=False)
            _run(["umount", self.mount_point], timeout=30)
            os.rmdir(self.mount_point)

    def mkdir(self, dir_path):
        """Create a directory on the image (dir_path is '/'-rooted)."""
        if self.use_loop:
            target = os.path.join(self.mount_point, dir_path.lstrip("/"))
            os.makedirs(target, exist_ok=True)
        else:
            # mmd fails silently if dir exists, which is fine.
            _run(["mmd", "-i", self.img_path, "::" + dir_path], check=False)

    def copy(self, host_src, img_dest):
        """Copy a file or directory from the host into the image.

        img_dest is '/'-rooted. If host_src is a directory, its contents are
        copied recursively into img_dest (matching the old mcopy -s behavior).
        """
        if self.use_loop:
            target = os.path.join(self.mount_point, img_dest.lstrip("/"))
            if os.path.isdir(host_src):
                os.makedirs(target, exist_ok=True)
                for entry in os.listdir(host_src):
                    s = os.path.join(host_src, entry)
                    d = os.path.join(target, entry)
                    if os.path.isdir(s):
                        shutil.copytree(s, d, dirs_exist_ok=True)
                    else:
                        os.makedirs(os.path.dirname(d), exist_ok=True)
                        shutil.copy2(s, d)
            else:
                os.makedirs(os.path.dirname(target), exist_ok=True)
                shutil.copy2(host_src, target)
        else:
            _run(["mcopy", "-i", self.img_path, "-o", "-s", host_src, "::" + img_dest])


def create_fat32_image(path, size_mb=32768):
    """Create a blank FAT32 image using mkfs.vfat.

    Default is 32 GB, matching a typical physical Flipper microSD. The image is
    created sparse (truncate), so it only consumes a few MB on disk until data
    is written. A realistic size is REQUIRED: Renode's SD-over-SPI model raises
    an out-of-bounds error with small images.
    """
    print(f"Creating {size_mb}MB (sparse) FAT32 image at {path}...")
    # Sparse allocation: seek to end and write one byte.
    with open(path, "wb") as f:
        f.seek(size_mb * 1024 * 1024 - 1)
        f.write(b"\x00")

    # Format as FAT32
    _run(["mkfs.vfat", "-F", "32", "-n", "FLIPPER", path], timeout=60)
    print("  Image created and formatted.")


def prepare_update(sd, tgz_path):
    """Extract update .tgz and place it on the SD card image."""
    print(f"Extracting update package: {tgz_path}")

    with tempfile.TemporaryDirectory() as tmpdir:
        # Extract .tgz
        with tarfile.open(tgz_path, "r:gz") as tar:
            tar.extractall(tmpdir)

        # Find the update directory (e.g., f7-update-1.4.3/)
        entries = os.listdir(tmpdir)
        if len(entries) != 1:
            print(f"  Warning: expected 1 top-level dir in tgz, found {len(entries)}")

        update_dir_name = entries[0]
        update_dir = os.path.join(tmpdir, update_dir_name)

        if not os.path.isdir(update_dir):
            print(f"  Error: {update_dir_name} is not a directory")
            sys.exit(1)

        # Parse version from directory name or update.fuf
        version = update_dir_name  # e.g. "f7-update-1.4.3"

        print(f"  Update version directory: {version}")

        # Create /update/<version>/ on the image
        sd.mkdir("/update")
        sd.mkdir(f"/update/{version}")

        # Copy all files from the update directory
        for fname in os.listdir(update_dir):
            src = os.path.join(update_dir, fname)
            dest = f"/update/{version}/{fname}"
            print(f"  Copying {fname} -> {dest}")
            sd.copy(src, dest)

        # Create the update pointer file /.fupdate
        # Content: absolute path to the manifest on the SD
        # Format: /ext/update/<version>/update.fuf
        # (firmware prepends /ext/ which maps to SD root)
        pointer_content = f"/ext/update/{version}/update.fuf"
        pointer_file = os.path.join(tmpdir, ".fupdate")
        with open(pointer_file, "w") as f:
            f.write(pointer_content)

        sd.copy(pointer_file, "/.fupdate")

        print(f"  Update pointer: {pointer_content}")
        print("  Update package installed on SD image.")


def create_default_dirs(sd):
    """Create the standard Flipper Zero SD card directory structure."""
    dirs = [
        "/update",
        "/badusb",
        "/dolphin",
        "/ibutton",
        "/infrared",
        "/lfrfid",
        "/nfc",
        "/subghz",
        "/wav_player",
        "/music_player",
        "/apps",
        "/apps_data",
    ]
    for d in dirs:
        sd.mkdir(d)


def _decompress_ths(ths_path):
    """Decompress a Flipper heatshrink '.ths' resources file into a .tar.

    Returns the path to a temporary .tar file. The '.ths' magic is 'HSDS'
    (heatshrink data stream). We read the header (window/lookahead) and stream
    it through heatshrink2. Requires the `heatshrink2` Python package.
    """
    try:
        import heatshrink2
    except ImportError:
        print("  ERROR: resources file is a .ths (heatshrink) but the "
              "'heatshrink2' Python package is not installed.")
        print("         Either: pip install heatshrink2")
        print("         Or: pass an already-decompressed .tar via --resources.")
        sys.exit(1)

    with open(ths_path, "rb") as f:
        data = f.read()

    if data[:4] != b"HSDS":
        # Already a plain tar? just return as-is.
        return ths_path

    # HeatshrinkDataStreamHeader (Flipper): struct "<IBBB" = 7 bytes:
    #   magic(4)='HSDS'  version(1)=1  window_sz2(1)  lookahead_sz2(1)
    import struct
    magic, version, window, lookahead = struct.unpack("<IBBB", data[:7])
    payload = data[7:]

    decompressed = heatshrink2.decompress(
        payload, window_sz2=window, lookahead_sz2=lookahead)

    out = tempfile.mktemp(suffix=".tar")
    with open(out, "wb") as f:
        f.write(decompressed)
    print(f"  Decompressed resources ({len(data)} -> {len(decompressed)} bytes)")
    return out


def prepare_resources(sd, resources_path):
    """Extract the firmware 'resources' (.ths or .tar) onto the SD image.

    Flipper Zero ships its compiled firmware separately from its resources
    (NFC parser plugins, IR universal remotes, SubGHz maps, app FAPs, the
    dolphin animations, etc.). On real hardware qFlipper copies these to the SD
    when updating. Without them the firmware shows "No SD card or database
    found". This lays the resources onto the SD image so the emulated Flipper
    has a complete filesystem.
    """
    print(f"Adding firmware resources from: {resources_path}")

    tar_path = resources_path
    cleanup = None
    with open(resources_path, "rb") as f:
        magic = f.read(4)
    if resources_path.endswith(".ths") or magic == b"HSDS":
        tar_path = _decompress_ths(resources_path)
        if tar_path != resources_path:
            cleanup = tar_path

    with tempfile.TemporaryDirectory() as tmpdir:
        with tarfile.open(tar_path, "r:") as tar:
            try:
                tar.extractall(tmpdir, filter="data")  # py>=3.12
            except TypeError:
                tar.extractall(tmpdir)

        # The tar root usually contains apps/, apps_data/, dolphin/, infrared/,
        # nfc/, subghz/, Manifest, etc. Copy each top-level entry to the SD root.
        entries = sorted(os.listdir(tmpdir))
        for name in entries:
            src = os.path.join(tmpdir, name)
            dest = "/" + name
            if os.path.isdir(src):
                sd.mkdir(dest)
                sd.copy(src, dest)
            else:
                sd.copy(src, dest)
            print(f"  + {name}")

    if cleanup and os.path.exists(cleanup):
        os.remove(cleanup)
    print("  Resources installed on SD image.")


def _autodetect_resources():
    """Try to find a resources.ths/.tar to lay onto the SD image.

    Priority: a copy shipped in the repo's firmware/ dir, then a local firmware
    build's dist output.
    """
    here = os.path.dirname(os.path.abspath(__file__))
    project = os.path.dirname(here)
    candidates = [
        os.path.join(project, "firmware", "resources.ths"),
        os.path.join(project, "firmware", "resources.tar"),
        "/opt/emu-test/flipperzero-firmware/dist/f7/f7-update-local/resources.ths",
        "/opt/emu-test/flipperzero-firmware/dist/f7-D/f7-update-local/resources.ths",
    ]
    for candidate in candidates:
        if os.path.exists(candidate):
            return candidate
    return None


def main():
    parser = argparse.ArgumentParser(description="Prepare Flipper Zero SD card image")
    parser.add_argument("--update-tgz", help="Path to update .tgz package")
    parser.add_argument("--resources",
                        help="Path to firmware resources (resources.ths or .tar). "
                             "Extracted onto the SD so the firmware has its NFC/IR/"
                             "SubGHz databases and app FAPs (fixes 'No SD card or "
                             "database found'). Auto-detected from a local build if "
                             "not given.")
    parser.add_argument("--no-resources", action="store_true",
                        help="Do not add firmware resources even if auto-detected.")
    parser.add_argument("--output", default="sdcard/sdcard.img",
                        help="Output image path (default: sdcard/sdcard.img)")
    parser.add_argument("--size", type=int, default=32768,
                        help="Image size in MB (default: 32768 = 32 GB, sparse). "
                             "A realistic size is required for Renode SD-over-SPI.")
    args = parser.parse_args()

    # Resolve paths relative to script location
    script_dir = os.path.dirname(os.path.abspath(__file__))
    project_dir = os.path.dirname(script_dir)
    output = os.path.join(project_dir, args.output) if not os.path.isabs(args.output) else args.output

    os.makedirs(os.path.dirname(output), exist_ok=True)

    create_fat32_image(output, args.size)

    with SDImage(output) as sd:
        create_default_dirs(sd)

        # Firmware resources (NFC/IR/SubGHz databases, app FAPs, dolphin animations).
        if not args.no_resources:
            res = args.resources or _autodetect_resources()
            if res and os.path.exists(res):
                prepare_resources(sd, res)
            elif args.resources:
                print(f"Error: resources file not found: {args.resources}")
                sys.exit(1)
            else:
                print("Note: no firmware resources found (build with "
                      "'./fbt DEBUG=0 updater_package' and pass --resources, or place "
                      "them where prepare_sdcard.py auto-detects). The firmware will "
                      "show 'No SD card or database found' without them.")

        if args.update_tgz:
            tgz = os.path.abspath(args.update_tgz)
            if not os.path.exists(tgz):
                print(f"Error: update tgz not found: {tgz}")
                sys.exit(1)
            prepare_update(sd, tgz)

    print(f"\nSD card image ready: {output}")
    print(f"Size: {os.path.getsize(output) / (1024*1024):.1f} MB")


if __name__ == "__main__":
    main()
