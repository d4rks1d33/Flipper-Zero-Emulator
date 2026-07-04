#!/usr/bin/env python3
"""
Prepare a FAT32 SD card image for Flipper Zero emulator.

Usage:
  python3 scripts/prepare_sdcard.py [--update-tgz path/to/update.tgz] [--output sdcard/sdcard.img] [--size 64]

This creates a FAT32 image that can be used as the emulated SD card.
If --update-tgz is provided, it extracts the update package into
/update/<version>/ on the image and creates the /.fupdate pointer file.
"""

import argparse
import os
import subprocess
import sys
import tarfile
import tempfile
import shutil


def create_fat32_image(path, size_mb=32768):
    """Create a blank FAT32 image using mkfs.vfat.

    Default is 32 GB, matching a typical physical Flipper microSD. The image is
    created sparse (truncate), so it only consumes a few MB on disk until data
    is written. A realistic size is REQUIRED: Renode's SD-over-SPI model raises
    an out-of-bounds error with small images.
    """
    print(f"Creating {size_mb}MB (sparse) FAT32 image at {path}...")
    # Sparse allocation: seek to end and write one byte.
    with open(path, 'wb') as f:
        f.seek(size_mb * 1024 * 1024 - 1)
        f.write(b'\x00')

    # Format as FAT32
    subprocess.run(['mkfs.vfat', '-F', '32', '-n', 'FLIPPER', path],
                   check=True, capture_output=True)
    print("  Image created and formatted.")


def copy_to_image(img_path, host_src, img_dest):
    """Copy a file from host into the FAT32 image using mcopy."""
    subprocess.run(['mcopy', '-i', img_path, '-o', '-s', host_src, '::' + img_dest],
                   check=True, capture_output=True)


def mkdir_in_image(img_path, dir_path):
    """Create a directory in the FAT32 image using mmd."""
    # mmd fails silently if dir exists, which is fine
    subprocess.run(['mmd', '-i', img_path, '::' + dir_path],
                   capture_output=True)


def prepare_update(img_path, tgz_path):
    """Extract update .tgz and place it on the SD card image."""
    print(f"Extracting update package: {tgz_path}")

    with tempfile.TemporaryDirectory() as tmpdir:
        # Extract .tgz
        with tarfile.open(tgz_path, 'r:gz') as tar:
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
        mkdir_in_image(img_path, '/update')
        mkdir_in_image(img_path, f'/update/{version}')

        # Copy all files from the update directory
        for fname in os.listdir(update_dir):
            src = os.path.join(update_dir, fname)
            dest = f'/update/{version}/{fname}'
            print(f"  Copying {fname} -> {dest}")
            copy_to_image(img_path, src, dest)

        # Create the update pointer file /.fupdate
        # Content: absolute path to the manifest on the SD
        # Format: /ext/update/<version>/update.fuf
        # (firmware prepends /ext/ which maps to SD root)
        pointer_content = f'/ext/update/{version}/update.fuf'
        pointer_file = os.path.join(tmpdir, '.fupdate')
        with open(pointer_file, 'w') as f:
            f.write(pointer_content)

        copy_to_image(img_path, pointer_file, '/.fupdate')

        print(f"  Update pointer: {pointer_content}")
        print("  Update package installed on SD image.")


def create_default_dirs(img_path):
    """Create the standard Flipper Zero SD card directory structure."""
    dirs = [
        '/update',
        '/badusb',
        '/dolphin',
        '/ibutton',
        '/infrared',
        '/lfrfid',
        '/nfc',
        '/subghz',
        '/wav_player',
        '/music_player',
        '/apps',
        '/apps_data',
    ]
    for d in dirs:
        mkdir_in_image(img_path, d)


def main():
    parser = argparse.ArgumentParser(description='Prepare Flipper Zero SD card image')
    parser.add_argument('--update-tgz', help='Path to update .tgz package')
    parser.add_argument('--output', default='sdcard/sdcard.img',
                        help='Output image path (default: sdcard/sdcard.img)')
    parser.add_argument('--size', type=int, default=32768,
                        help='Image size in MB (default: 32768 = 32 GB, sparse). '
                             'A realistic size is required for Renode SD-over-SPI.')
    args = parser.parse_args()

    # Resolve paths relative to script location
    script_dir = os.path.dirname(os.path.abspath(__file__))
    project_dir = os.path.dirname(script_dir)
    output = os.path.join(project_dir, args.output) if not os.path.isabs(args.output) else args.output

    os.makedirs(os.path.dirname(output), exist_ok=True)

    create_fat32_image(output, args.size)
    create_default_dirs(output)

    if args.update_tgz:
        tgz = os.path.abspath(args.update_tgz)
        if not os.path.exists(tgz):
            print(f"Error: update tgz not found: {tgz}")
            sys.exit(1)
        prepare_update(output, tgz)

    print(f"\nSD card image ready: {output}")
    print(f"Size: {os.path.getsize(output) / (1024*1024):.1f} MB")


if __name__ == '__main__':
    main()
