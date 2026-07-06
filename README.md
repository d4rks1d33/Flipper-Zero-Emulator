# Flipper Zero Full-System Emulator (STM32WB55RG)

Runs **real Flipper Zero firmware** on an emulated STM32WB55 chip using
[Renode](https://renode.io). It boots the actual firmware to the desktop,
renders the 128×64 display in a window, maps your PC keyboard to the Flipper
buttons, exposes the debug console over TCP, and logs every SubGHz/NFC/RFID
transaction — all **without any physical Flipper**.

Use it to develop and test firmware / FAP apps, or to inspect what the Flipper
*would* do on the radios (the RF signal itself is not emulated).

---

## Quick start

### 1. Prerequisites

- **Linux** (or WSL2 on Windows)
- **Python 3** + `pip`
- **Git**
- **bash**

### 2. Clone

```bash
git clone <repo-url> flipper-emulator
cd flipper-emulator
```

### 3. Run setup

```bash
./setup.sh
```

This generates the machine-specific platform files and creates a 32 GB FAT32
SD card image at `sdcard/sdcard.img` with the firmware resources
(NFC/IR/SubGHz databases, apps, dolphin animations).

### 4. Get a firmware binary

You need a **RELEASE** firmware built with `-DFLIPPER_EMULATOR`. The repo ships
one at `firmware/flipper-z-f7-full-EMULATOR-patched.bin`.

If you're starting fresh, place any `.bin` file in `firmware/` and see
[§ Patching any firmware](#patching-any-firmware) to build a patched one.

### 5. Run the emulator

```bash
./run.sh
```

- An SDL window opens with the Flipper display (if `pip install pysdl2-dll PySDL2 Pillow` was run).
- Wait **~30–45 seconds** for the firmware to boot to the desktop.
- The dolphin animation should play. You should see the idle desktop.

### 6. Use the buttons

With the SDL window focused:

| Key | Flipper button |
|-----|----------------|
| Arrow keys | D-Pad (Up/Down/Left/Right) |
| Enter | OK |
| Backspace / Esc | Back |
| Q | Quit the frontend |

Press **OK** (Enter) on the desktop to open the main menu.

### 7. Debug console

```bash
telnet localhost 3456
```

This connects to the firmware's USART1 log (230400 baud).

---

## Screenshots:

<div align="center">
  <img src="assets/Screenshots/Boot.png" width="90%" /><br /><img src="assets/Screenshots/Boot2.png" width="90%" /><br />
</div>
<div align="center">
  <img src="assets/Screenshots/Boot3.png" width="90%" /><br /><img src="assets/Screenshots/Boot4.png" width="90%" /><br />
</div>
<div align="center">
  <img src="assets/Screenshots/Boot5.png" width="90%" /><br />
</div>

---

## Step-by-step from a fresh clone

Here is exactly what you need to do, in order:

```bash
# 1. Clone the repo
git clone <repo-url> flipper-emulator
cd flipper-emulator

# 2. Run the per-machine setup (generates .repl and .resc files)
./setup.sh

# 3. (Optional) Install Python deps for the SDL frontend
pip install pysdl2-dll PySDL2 Pillow

# 4. Make sure you have a firmware binary
#    The shipped one works:
ls -la firmware/flipper-z-f7-full-EMULATOR-patched.bin
#    If missing, copy one in (see "Patching any firmware" below)

# 5. Run the emulator
./run.sh

# 6. In another terminal, watch the firmware log:
telnet localhost 3456

# 7. Wait ~30-45 seconds for the desktop to appear in the SDL window.
#    Press Enter to open the main menu.
```

---

## Troubleshooting

| Symptom | Cause & fix |
|---------|-------------|
| `Renode not found` | Run `./setup.sh` first, or install Renode manually and set `RENODE_PATH` |
| No window / "no module named sdl2" | Run `pip install pysdl2-dll PySDL2 Pillow`, then `./run.sh --no-gui` + `python3 frontend/view_display.py --watch` |
| Boots to DFU / recovery screen | A button reads as pressed. The script sets buttons idle automatically; if using a custom launch, drive PB10/11/12/PC6/13 HIGH and PH3 LOW before `start` |
| Black / frozen screen | Wait 30–45 seconds — boot is slow under emulation |
| Buttons don't respond | Make sure the SDL window is focused. Try pressing a key and checking the terminal for `DEBUG: Button ...` messages |
| Port 3456 already in use | Kill the previous Renode process: `pkill -f renode` |
| Port 1234 already in use | Same: `pkill -f renode` |
| `FuriStatusErrorTimeout` / crash | Ensure you're using a RELEASE firmware built with `DEBUG=0` and `-DFLIPPER_EMULATOR`. A debug build hits asserts on internal storage access |

---

## Patching any firmware

To build your own patched firmware:

### 1. Clone the firmware source

```bash
git clone --recursive https://github.com/flipperdevices/flipperzero-firmware
cd flipperzero-firmware
```

### 2. Apply the emulator patch

```bash
git apply /path/to/flipper-emulator/firmware/flipper_emulator.patch
```

If `git apply` fails (version mismatch), open the `.patch` file and manually
add the `#ifdef FLIPPER_EMULATOR` blocks it describes. The patch is small and
readable.

### 3. Build in RELEASE mode

```bash
./fbt DEBUG=0 --extra-define=FLIPPER_EMULATOR fw_dist
```

`DEBUG=0` is **required** — a debug build hits `furi_assert(type == ST_EXT)`
and crashes. The release binary goes to `dist/f7/flipper-z-f7-full-local.bin`.

### 4. Copy to the emulator

```bash
cp dist/f7/flipper-z-f7-full-local.bin \
   /path/to/flipper-emulator/firmware/flipper-z-f7-full-EMULATOR-patched.bin
```

### 5. Run

```bash
cd /path/to/flipper-emulator
./run.sh
```

---

## Advanced: headless mode

```bash
./run.sh --no-gui &
python3 frontend/view_display.py --watch   # live ASCII view
python3 frontend/view_display.py --png shot.png   # PNG snapshot
```

## Advanced: injecting buttons via monitor

```bash
echo "gpioPortH OnGPIO 3 true"  | nc -q0 localhost 1234   # OK press
echo "gpioPortH OnGPIO 3 false" | nc -q0 localhost 1234   # OK release
```

Pin mapping: Up=PB10, Down=PC6, Left=PB11, Right=PB12, OK=PH3, Back=PC13.

---

## Repository layout

```
flipper-emulator/
├── run.sh              # launcher
├── setup.sh            # per-machine setup (generates .repl from .repl.in)
├── firmware/           # firmware .bin + patch file + resources.ths
│   ├── flipper-z-f7-full-EMULATOR-patched.bin   # prebuilt patched firmware
│   └── flipper_emulator.patch                   # describes all FLIPPER_EMULATOR changes
├── frontend/
│   ├── sdl_frontend.py          # SDL2 window + button injection
│   └── view_display.py          # headless viewer (ASCII / PNG)
├── peripherals/        # custom Renode peripheral models
├── platform/           # STM32WB55 platform description (.repl)
├── scripts/            # Renode launch scripts
├── bootrom/            # boot-mode helper
├── sdcard/             # 32 GB FAT32 SD image (generated)
├── logs/               # radio JSONL logs
└── tools/              # Renode binary (downloaded by install.sh)
```

## Limitations

- **No RF** — SubGHz/NFC/RFID/IR are stubs that log transactions (not signal-accurate).
- **No BLE / Thread** — Core2 (M0+) radio stack is not emulated; BT shows unavailable.
- **Boot is slow** — ~30–45 s to the desktop (functional emulation + I2C timeouts).
- **Internal storage (LittleFS)** is not functional — the emulator uses RELEASE builds so `/int` accesses fail gracefully instead of asserting.
- **SD over SPI** is experimental — the main config runs with the display on SPI2 (SD not attached) for reliable boot.

---

## How it works

Renode emulates the STM32WB55 Cortex-M4 and its peripherals. This project adds:

- A **platform description** with the exact memory map of the Flipper's MCU.
- **Custom peripherals**: ST7567 display, LP5562 LED driver, CC1101/ST25R3916 radios,
  correct WB55 RCC + EXTI, IPCC/HSEM/RTC stubs.
- A **launch script** that wires everything together, loads the firmware, sets buttons
  to "not pressed", and starts the CPU.

The firmware needs a small `-DFLIPPER_EMULATOR` patch to handle emulation-specific
quirks (see `flipper_emulator.patch` for details). Everything else is the real firmware.
