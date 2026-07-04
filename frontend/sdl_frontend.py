#!/usr/bin/env python3
"""
SDL2 frontend for the Flipper Zero emulator.

- Reads the 128x64 1bpp framebuffer written by the ST7567 peripheral
  (default /tmp/flipper_fb.raw, 8192 bytes, one byte per pixel: 0/1)
- Renders it scaled (default 4x = 512x256) in an orange-on-black Flipper look
- Maps PC keyboard keys to Flipper buttons and injects them into Renode
  through its telnet monitor port (default localhost:1234), by driving the
  GPIO input lines of the emulated buttons.

Button mapping (matches furi_hal_resources.c):
    Up    = PB10  (arrow up)      inverted (active low)
    Down  = PC6   (arrow down)    inverted
    Left  = PB11  (arrow left)    inverted
    Right = PB12  (arrow right)   inverted
    OK    = PH3   (Enter)         non-inverted (active high)
    Back  = PC13  (Backspace)     inverted

For inverted buttons: not-pressed => line HIGH, pressed => line LOW.
For OK (non-inverted): not-pressed => line LOW, pressed => line HIGH.

Renode GPIO injection uses:  <port> OnGPIO <pin> <true|false>
where the GPIOPort's OnGPIO sets the input state of that pin.

Requirements: PySDL2 (pip install PySDL2 pysdl2-dll)
"""

import os
import sys
import socket
import time
import ctypes

try:
    import sdl2
    import sdl2.ext
except ImportError:
    print("ERROR: PySDL2 not installed. Run: pip install PySDL2 pysdl2-dll")
    sys.exit(1)

import tempfile
FB_PATH = os.environ.get(
    "FLIPPER_EMU_FB_PATH", os.path.join(tempfile.gettempdir(), "flipper_fb.raw"))
MONITOR_HOST = os.environ.get("FLIPPER_EMU_MONITOR_HOST", "localhost")
MONITOR_PORT = int(os.environ.get("FLIPPER_EMU_MONITOR_PORT", "1234"))

WIDTH = 128
HEIGHT = 64
SCALE = int(os.environ.get("FLIPPER_EMU_SCALE", "4"))

# Flipper LCD colors (approx): orange foreground, dark background
FG = (0xFF, 0x8C, 0x00)   # amber/orange
BG = (0x2A, 0x1A, 0x00)   # dark brown/black

# Button definitions: name -> (port, pin, inverted)
BUTTONS = {
    "up":    ("gpioPortB", 10, True),
    "down":  ("gpioPortC", 6,  True),
    "left":  ("gpioPortB", 11, True),
    "right": ("gpioPortB", 12, True),
    "ok":    ("gpioPortH", 3,  False),
    "back":  ("gpioPortC", 13, True),
}

# SDL keycode -> button name
KEYMAP = {
    sdl2.SDLK_UP: "up",
    sdl2.SDLK_DOWN: "down",
    sdl2.SDLK_LEFT: "left",
    sdl2.SDLK_RIGHT: "right",
    sdl2.SDLK_RETURN: "ok",
    sdl2.SDLK_KP_ENTER: "ok",
    sdl2.SDLK_BACKSPACE: "back",
    sdl2.SDLK_ESCAPE: "back",
}


class RenodeMonitor:
    """Simple client for the Renode telnet monitor to inject GPIO state."""

    def __init__(self, host, port):
        self.host = host
        self.port = port
        self.sock = None
        self.connect()

    def connect(self):
        try:
            self.sock = socket.create_connection((self.host, self.port), timeout=2)
            self.sock.settimeout(0.1)
            time.sleep(0.2)
            self._drain()
            print(f"Connected to Renode monitor at {self.host}:{self.port}")
        except Exception as e:
            print(f"WARNING: could not connect to Renode monitor: {e}")
            print("Buttons will not work. Start Renode with: machine StartGdbServer or "
                  "the monitor socket (see run.sh).")
            self.sock = None

    def _drain(self):
        if not self.sock:
            return
        try:
            while True:
                data = self.sock.recv(4096)
                if not data:
                    break
        except socket.timeout:
            pass
        except Exception:
            pass

    def send(self, line):
        if not self.sock:
            return
        try:
            self.sock.sendall((line + "\n").encode())
            self._drain()
        except Exception as e:
            print(f"monitor send failed: {e}")
            self.sock = None

    def set_button(self, name, pressed):
        port, pin, inverted = BUTTONS[name]
        # Determine the electrical line level for this state.
        if inverted:
            level = not pressed   # pressed => low
        else:
            level = pressed       # pressed => high
        val = "true" if level else "false"
        self.send(f"{port} OnGPIO {pin} {val}")


def load_framebuffer():
    try:
        with open(FB_PATH, "rb") as f:
            data = f.read()
        if len(data) < WIDTH * HEIGHT:
            return None
        return data
    except Exception:
        return None


def init_buttons(monitor):
    """Set all buttons to the not-pressed state."""
    for name in BUTTONS:
        monitor.set_button(name, False)


def main():
    sdl2.ext.init()
    window = sdl2.ext.Window("Flipper Zero Emulator", size=(WIDTH * SCALE, HEIGHT * SCALE))
    window.show()

    renderer = sdl2.ext.Renderer(window)

    monitor = RenodeMonitor(MONITOR_HOST, MONITOR_PORT)
    init_buttons(monitor)

    running = True
    last_fb = None
    pressed_keys = set()

    print("Flipper Zero SDL frontend running.")
    print("  Arrows = D-pad, Enter = OK, Backspace/Esc = Back, Q = quit")

    while running:
        events = sdl2.ext.get_events()
        for event in events:
            if event.type == sdl2.SDL_QUIT:
                running = False
            elif event.type == sdl2.SDL_KEYDOWN:
                key = event.key.keysym.sym
                if key == sdl2.SDLK_q:
                    running = False
                elif key in KEYMAP and key not in pressed_keys:
                    pressed_keys.add(key)
                    monitor.set_button(KEYMAP[key], True)
            elif event.type == sdl2.SDL_KEYUP:
                key = event.key.keysym.sym
                if key in KEYMAP and key in pressed_keys:
                    pressed_keys.discard(key)
                    monitor.set_button(KEYMAP[key], False)

        fb = load_framebuffer()
        if fb is not None:
            renderer.clear(BG)
            for y in range(HEIGHT):
                for x in range(WIDTH):
                    if fb[y * WIDTH + x]:
                        renderer.fill(
                            (x * SCALE, y * SCALE, SCALE, SCALE), FG)
            renderer.present()

        sdl2.SDL_Delay(33)  # ~30 fps

    sdl2.ext.quit()


if __name__ == "__main__":
    main()
