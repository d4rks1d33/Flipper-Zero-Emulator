#!/usr/bin/env python3
"""
Headless framebuffer viewer for the Flipper Zero emulator.

Reads the 128x64 1bpp framebuffer written by the ST7567 peripheral and either:
  - prints it as ASCII art to the terminal (default), or
  - saves it as a PNG (--png out.png), or
  - watches the file and refreshes the ASCII view (--watch).

Useful on headless/WSL2 setups where SDL cannot open a window.

Usage:
  python3 view_display.py                 # one-shot ASCII dump
  python3 view_display.py --watch         # live ASCII (updates ~5 fps)
  python3 view_display.py --png shot.png  # write a PNG snapshot
"""

import argparse
import os
import sys
import time

import tempfile
FB_PATH = os.environ.get(
    "FLIPPER_EMU_FB_PATH", os.path.join(tempfile.gettempdir(), "flipper_fb.raw"))
WIDTH = 128
HEIGHT = 64


def load_fb(path):
    try:
        with open(path, "rb") as f:
            data = f.read()
        if len(data) < WIDTH * HEIGHT:
            return None
        return data
    except FileNotFoundError:
        return None


def to_ascii(fb):
    # Two vertical pixels per character using half-block glyphs.
    lines = []
    for y in range(0, HEIGHT, 2):
        row = []
        for x in range(WIDTH):
            top = fb[y * WIDTH + x]
            bot = fb[(y + 1) * WIDTH + x] if y + 1 < HEIGHT else 0
            if top and bot:
                row.append("\u2588")   # full block
            elif top:
                row.append("\u2580")   # upper half
            elif bot:
                row.append("\u2584")   # lower half
            else:
                row.append(" ")
        lines.append("".join(row))
    return "\n".join(lines)


def save_png(fb, path):
    try:
        from PIL import Image
    except ImportError:
        print("ERROR: Pillow not installed. Run: pip install Pillow")
        sys.exit(1)
    img = Image.new("RGB", (WIDTH, HEIGHT))
    px = img.load()
    fg = (0xFF, 0x8C, 0x00)
    bg = (0x2A, 0x1A, 0x00)
    for y in range(HEIGHT):
        for x in range(WIDTH):
            px[x, y] = fg if fb[y * WIDTH + x] else bg
    img = img.resize((WIDTH * 4, HEIGHT * 4), Image.NEAREST)
    img.save(path)
    print(f"Saved {path}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--png", help="save a PNG snapshot to this path")
    ap.add_argument("--watch", action="store_true", help="live ASCII view")
    ap.add_argument("--path", default=FB_PATH, help="framebuffer file path")
    args = ap.parse_args()

    if args.png:
        fb = load_fb(args.path)
        if fb is None:
            print(f"No framebuffer at {args.path}")
            sys.exit(1)
        save_png(fb, args.png)
        return

    if args.watch:
        try:
            while True:
                fb = load_fb(args.path)
                # Clear screen + home
                sys.stdout.write("\033[2J\033[H")
                if fb is None:
                    sys.stdout.write(f"(waiting for framebuffer at {args.path})\n")
                else:
                    sys.stdout.write(to_ascii(fb) + "\n")
                sys.stdout.flush()
                time.sleep(0.2)
        except KeyboardInterrupt:
            return

    fb = load_fb(args.path)
    if fb is None:
        print(f"No framebuffer at {args.path}")
        sys.exit(1)
    print(to_ascii(fb))


if __name__ == "__main__":
    main()
