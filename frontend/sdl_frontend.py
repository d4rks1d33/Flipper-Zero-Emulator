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
    sdl2.SDLK_w: "up",
    sdl2.SDLK_DOWN: "down",
    sdl2.SDLK_s: "down",
    sdl2.SDLK_LEFT: "left",
    sdl2.SDLK_a: "left",
    sdl2.SDLK_RIGHT: "right",
    sdl2.SDLK_d: "right",
    sdl2.SDLK_RETURN: "ok",
    sdl2.SDLK_KP_ENTER: "ok",
    sdl2.SDLK_SPACE: "ok",
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

        # Drive the GPIO port pin. The port-aware EXTI model (SYSCFG_EXTICR
        # routing) forwards this to the correct EXTI line automatically, so we do
        # NOT poke `exti` directly any more -- doing so would inject on the wrong
        # port's line (e.g. `exti OnGPIO 3` = PA3, not PH3) and be rejected by the
        # EXTICR port mux.
        gpio_cmd = f"{port} OnGPIO {pin} {val}"
        print(f"DEBUG: Button {name} {'pressed' if pressed else 'released'} -> {gpio_cmd}")
        self.send(gpio_cmd)


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
    # A window can only receive keyboard events if there is a display server and
    # the window is focused. Without DISPLAY/WAYLAND_DISPLAY (e.g. pure SSH, or a
    # headless box) the SDL window never gets keyboard focus, so button presses
    # never reach the emulator -- warn clearly instead of silently "not working".
    if not (os.environ.get("DISPLAY") or os.environ.get("WAYLAND_DISPLAY")):
        print("WARNING: no DISPLAY/WAYLAND_DISPLAY is set. The SDL window cannot")
        print("         receive keyboard focus, so buttons will NOT reach the")
        print("         emulator. Run on a desktop session, or use headless mode:")
        print("           python3 frontend/view_display.py --watch")

    # Connect to the Renode monitor BEFORE creating the renderer. If the renderer
    # ever fails to initialize (missing GPU/render driver), we still want the
    # button-injection path connected -- and any connection warning shown -- so
    # the buttons keep working and the failure mode is obvious.
    monitor = RenodeMonitor(MONITOR_HOST, MONITOR_PORT)
    init_buttons(monitor)

    sdl2.ext.init()
    window = sdl2.ext.Window("Flipper Zero Emulator", size=(WIDTH * SCALE, HEIGHT * SCALE))
    window.show()
    # Pull the window to the front and give it focus so keystrokes land here.
    try:
        sdl2.SDL_RaiseWindow(window.window)
    except Exception:
        pass

    # Hardware-accelerated renderer + a single streaming texture we update once
    # per frame (instead of ~8192 per-pixel fills, which throttled the event loop
    # to ~12 Hz and made buttons feel unresponsive).
    sdl_renderer = sdl2.SDL_CreateRenderer(
        window.window, -1,
        sdl2.SDL_RENDERER_ACCELERATED | sdl2.SDL_RENDERER_PRESENTVSYNC)
    if not sdl_renderer:
        # Fall back to software so the window still renders.
        sdl_renderer = sdl2.SDL_CreateRenderer(window.window, -1, sdl2.SDL_RENDERER_SOFTWARE)
    texture = sdl2.SDL_CreateTexture(
        sdl_renderer, sdl2.SDL_PIXELFORMAT_ARGB8888,
        sdl2.SDL_TEXTUREACCESS_STREAMING, WIDTH, HEIGHT)

    # Pre-computed 32-bit ARGB pixels for on/off.
    fg_px = (0xFF << 24) | (FG[0] << 16) | (FG[1] << 8) | FG[2]
    bg_px = (0xFF << 24) | (BG[0] << 16) | (BG[1] << 8) | BG[2]
    pixel_buf = (ctypes.c_uint32 * (WIDTH * HEIGHT))()

    running = True
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
            for i in range(WIDTH * HEIGHT):
                pixel_buf[i] = fg_px if fb[i] else bg_px
            sdl2.SDL_UpdateTexture(texture, None, pixel_buf, WIDTH * 4)
            sdl2.SDL_RenderClear(sdl_renderer)
            sdl2.SDL_RenderCopy(sdl_renderer, texture, None, None)
            sdl2.SDL_RenderPresent(sdl_renderer)

        sdl2.SDL_Delay(16)  # ~60 fps; event loop stays responsive

    sdl2.SDL_DestroyTexture(texture)
    sdl2.SDL_DestroyRenderer(sdl_renderer)
    sdl2.ext.quit()


if __name__ == "__main__":
    main()
