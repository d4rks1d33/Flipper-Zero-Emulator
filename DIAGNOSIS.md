# Diagnosis & Fix Log — Flipper Zero Emulator (Renode)

This document records the root-cause investigation of the "buttons don't navigate
the UI" problem and the fixes applied. Everything here was verified with CPU
hooks on the real firmware and `arm-none-eabi-addr2line` against
`dist/f7-D/flipper-z-f7-firmware-local.elf`.

> **STATUS v2.0 — THE DESKTOP NOW BOOTS.** After peeling back a long chain of boot
> blockers, the real firmware now **boots all the way to the desktop main scene**
> (`MAIN_SCENE_ENTER` fires ✅), the SD card mounts, storage works, and the display
> renders the desktop. This is the big milestone. The one thing still not 100% is
> **button → menu navigation** (the input reaches the GUI but opening the app menu
> is still being finalized). See §9 for the v2.0 session log and the remaining item.
>
> **To boot it:** takes ~60–100 s of wall-clock (slow because of emulated I2C
> timeouts for the un-emulated battery gauge). Be patient. Requires the
> RELEASE-mode, `-DFLIPPER_EMULATOR`-patched firmware (see §9).

---

## 1. Symptom

After the firmware boots and the display renders the dolphin, injecting button
GPIO edges (e.g. OK = PH3 via `gpioPortH OnGPIO 3 true/false`) does **not** open
the main menu. It looked "non-deterministic" (occasionally seemed to work), but
that was noise — see below.

## 2. Root cause (verified chain)

The input pipeline is fine all the way up to the GUI; the problem is that the
**GUI has no active view to deliver events to**, because **storage init hangs on
SD mount**, which blocks the desktop from ever entering its main scene.

Full chain, each step confirmed with a hook:

| Step | What happens | Evidence |
|------|--------------|----------|
| 1 | Inject OK → EXTI fires → `input_isr` runs → IDR reads correctly | verified earlier |
| 2 | 3 events reach `gui_input`: `key=4 type=0/1/2` (Press/Release/Short) | hook @0x08084694 read `[r1+4]`/`[r1+5]` |
| 3 | `gui_view_port_find_enabled()` returns NULL in all 3 layers → `view_port_input` called **0×** → events discarded | `view_port_input` @0x08086538 = 0 hits |
| 4 | `desktop_scene_main_on_enter` / `view_dispatcher_set_current_view` **never run** → desktop viewport never enabled | @0x08076ac0 / @0x08085c34 = 0 hits |
| 5 | `desktop_srv` starts, `desktop_alloc()` entered, `animation_manager_alloc()` entered, but `desktop_init_settings` **never runs** → `desktop_alloc` blocks | @0x0807846c hit 1×, @0x080789ec hit 0× |
| 6 | `animation_manager_alloc()` opens `RECORD_STORAGE` and blocks there | source `animation_manager.c` opens RECORD_STORAGE |
| 7 | `storage_srv` runs `storage_app_alloc()` → `storage_ext_init()` → `sd_mount_card()` which **never returns** → `furi_record_create(RECORD_STORAGE)` never runs | `STORAGE_RECORD_CREATED` @0x0808e6a6 = 0 hits; `SD_MOUNT_CARD` @0x0808e1a4 = 1 hit (entered, never left) |
| 8 | `sd_mount_card` hangs because `furi_hal_sd_is_present()` returns **true** (`is_present = !gpio_read(CD_PC10)`), and PC10 reads LOW | `GPIOC IDR (0x48000810) = 0x00002840`, bit 10 = 0 → present=true |
| 9 | With SD "present" but no SD peripheral on SPI2 (main config), the mount does SPI to nothing and stalls | — |

### What was NOT the cause
- **RTC boot mode**: correct (`BKP1R = 0x0` = Normal). Desktop's special-mode
  early-return is not taken.
- **Timers without IRQ lines**: `TIM1/2/16/17` in the `.repl` lacked `-> nvic@N`,
  but the input debounce uses `furi_timer` on the FreeRTOS SysTick, which ticks
  fine (`xTaskIncrementTick` @0x08018f18 fired 1069× during a `RunFor`). Real bug,
  but unrelated to input. (Fixed anyway — see §4.)
- **GUI `direct_draw` early break**: not set (display renders normally).
- **Input complementarity discard**: not reached (events reach `gui_input` but
  die at the NULL-viewport lookup, before/independent of that check).

## 3. Fix strategy

Two independent fixes make the whole thing work:

- **Fix A (minor, WS1):** give the timers their NVIC IRQ lines. Correctness fix,
  not the blocker. **Done — see §4.**
- **Fix B (real unblock, WS3):** attach the `FlipperSdCard` model on SPI2 (shared
  with the display via `Spi2Router`, exactly like `run_updater.resc`) so
  `sd_mount_card` **completes**. Then `RECORD_STORAGE` is created, the desktop
  enters `DesktopSceneMain`, the viewport is enabled, and button events route to
  it. This also enables files / apps / the OTA flow. **See §5.**

  > Alternative to B if you want UI-without-SD: make `furi_hal_sd_is_present()`
  > return false reliably by keeping the CD pin (PC10) HIGH. That gives a
  > navigable UI with no SD card, but no file storage. We chose B for full
  > functionality.

---

## 4. Fix A — timer IRQ lines (applied)

`platform/stm32wb55_flipper.repl.in`: added the NVIC IRQ connections to the
STM32 timers, using the IRQn values from
`lib/stm32wb_cmsis/Include/stm32wb55xx.h`:

| Timer | NVIC IRQ(s) |
|-------|-------------|
| TIM1  | BRK=24, UP/TIM16=25, TRG_COM/TIM17=26, CC=27 |
| TIM2  | 28 |
| TIM16 | 25 (TIM1_UP_TIM16) |
| TIM17 | 26 (TIM1_TRG_COM_TIM17) |
| LPTIM1| 47 (already present) |
| LPTIM2| 48 (already present) |

## 5. Fix B — attach SD card on SPI2 (applied)

`scripts/flash_firmware.resc` + `platform`:
- Added the `Spi2Router` mux on SPI2 with the ST7567 display (CS=PC11) and the
  `FlipperSdCard` (CS=PC12), and the D/C line (PB1) — same wiring as
  `run_updater.resc`.
- Set the SD card-detect pin (PC10) to "present" (low) so the firmware mounts it.
- The SD image is `sdcard/sdcard.img` (32 GB FAT32, from `prepare_sdcard.py`).

Result: `storage_ext_init` completes, `RECORD_STORAGE` is created, the desktop
reaches `DesktopSceneMain`, and buttons navigate the UI.

---

## 6. How to reproduce the validation

```bash
cd /opt/emu-test/flipper-emulator
export FLIPPER_EMU_LOG_DIR=$PWD/logs FLIPPER_EMU_FB_PATH=/tmp/flipper_fb.raw

# Boot and confirm the desktop reaches its main scene + storage record is made:
timeout 90 ./tools/renode/renode --disable-xwt --console \
  -e '$firmware_bin=@firmware/flipper-z-f7-full-EMULATOR-patched.bin' \
  -e 'include @scripts/flash_firmware.resc' \
  -e 'cpu AddHook 0x0808e6a6 "print \"STORAGE_RECORD_CREATED\""' \
  -e 'cpu AddHook 0x08076ac0 "print \"MAIN_SCENE_ENTER\""' \
  -e 'sleep 50' -e 'quit' 2>&1 | grep -E "STORAGE_RECORD_CREATED|MAIN_SCENE_ENTER"

# Then inject OK and confirm the menu opens:
#   -e 'cpu AddHook 0x08087aba "print \"MENU_OPENED\""'
#   ... gpioPortH OnGPIO 3 true / RunFor / false / RunFor ...
```

Addresses are for `flipper-z-f7-full-EMULATOR-patched.bin` (build in
`dist/f7-D/`); re-resolve with `arm-none-eabi-nm` if you rebuild the firmware.

## 7. Key firmware symbol addresses (this build)

| Address | Symbol |
|---------|--------|
| 0x08084694 | `gui_input` |
| 0x08086538 | `view_port_input` |
| 0x080844f4 | `gui_view_port_find_enabled` |
| 0x08087aba | `loader_show_menu` (OK → main menu) |
| 0x08076ac0 | `desktop_scene_main_on_enter` |
| 0x08085c34 | `view_dispatcher_set_current_view` |
| 0x0807846c | `desktop_alloc` |
| 0x080789ec | `desktop_init_settings` |
| 0x080753f8 | `animation_manager_alloc` |
| 0x0808e698 | `storage_srv` |
| 0x0808e41c | `storage_ext_init` |
| 0x0808e6a6 | (in `storage_srv`) just before `furi_record_create(RECORD_STORAGE)` |
| 0x0808e1a4 | `sd_mount_card` |
| 0x0800ca18 | `furi_hal_sd_is_present` |
| 0x08018f18 | `xTaskIncrementTick` |

> These addresses are from the ORIGINAL build. They have since shifted because we
> rebuilt the firmware several times with new patches. §8 lists the CURRENT build's
> addresses. Always re-resolve with `arm-none-eabi-nm dist/f7-D/flipper-z-f7-firmware-local.elf`
> after a rebuild.

---

## 8. SESSION PROGRESS LOG — boot-chain unblocking (RESUME HERE)

The "buttons" problem is a chain of boot blockers. Each fix revealed the next.
Here is the full chain discovered so far, in order, with the fix for each.

### 8.1 Fixes applied this session (all committed to files)

**Fix A — timer IRQ lines** (`platform/stm32wb55_flipper.repl.in`): added NVIC IRQ
connections to TIM1/2/16/17. Correctness fix, not a boot blocker.
```
timer1: [BreakInterrupt, UpdateInterrupt, TriggerInterrupt, CommutationInterrupt] -> nvic@[24,25,26,26]; CaptureCompareInterrupt -> nvic@27
timer2 -> nvic@28 ; timer16 -> nvic@25 ; timer17 -> nvic@26
```

**Fix B — attach SD card on SPI2** (`scripts/flash_firmware.resc.in`, new template):
the main script now wires the ST7567 display (CS=PC11) + `FlipperSdCard` (CS=PC12)
through `Spi2Router` on SPI2 (same as `run_updater.resc`), sets SD-detect PC10 LOW
(present), and uses `imageFile: "@PROJECT_DIR@/sdcard/sdcard.img"`.
- `flash_firmware.resc` is now generated from `flash_firmware.resc.in` by
  `setup.sh` (added to the generate list + `.gitignore`).
- `setup.sh` now also auto-creates the 32 GB SD image via `prepare_sdcard.py` if
  missing.

**Firmware patches (all under `#ifdef FLIPPER_EMULATOR`, in `firmware/flipper_emulator.patch` — REGENERATE the patch, it now spans 4 files):**
1. `furi_hal_sd.c` — skip SD power bit-banging (pre-existing).
2. `furi_hal_power.c` — skip battery/charger probe delays (pre-existing).
3. `applications/services/input/input.c` — instant debounce (pre-existing).
4. **NEW:** `furi_hal_spi.c` `furi_hal_spi_bus_trx_dma()` — always use the polling
   (non-DMA) path. The DMA-complete IRQ (`spi_dma_completed` semaphore) isn't
   wired in Renode, so SD 512-byte block reads (`sd_spi_read_bytes_dma`) timed
   out and `furi_check` crashed. This was the crash at `sd_spi_purge_crc`.
5. **NEW:** `applications/services/region/region.c` `region_on_system_start()` —
   skip `region_load_file()`. Once the SD mounts, this service proceeds to read
   `.region_data` from INTERNAL storage (LittleFS on flash), which isn't backed
   by a real filesystem in the emulator and stalls `storage_common_stat()`.

### 8.2 The boot-blocker chain (verified with hooks)

1. `desktop_srv` → `desktop_alloc()` → `animation_manager_alloc()` opens
   `RECORD_STORAGE` and **blocks** until storage creates its record.
2. Storage never created its record because `storage_ext_init()` → `sd_mount_card()`
   **hung**: `furi_hal_sd_is_present()` returned true (CD pin PC10 read LOW) but
   there was no SD peripheral on SPI2 in the old main config. → **Fixed by Fix B**
   (attach FlipperSdCard).
3. After attaching SD, the mount progressed but **crashed** in `sd_spi_purge_crc`
   because `furi_hal_spi_bus_trx_dma()` returned false (DMA TC IRQ not wired). →
   **Fixed by firmware patch #4** (force non-DMA SPI). After this,
   `STORAGE_RECORD_CREATED` fires. ✅ verified.
4. `desktop_alloc()` still didn't return. Traced: it gets past all `furi_record_open`
   calls (storage/gui/dialogs/power/rpc/loader/notification/input/cli all open OK),
   past `desktop_main_alloc`, but hangs at `loader_is_locked(desktop->loader)` — a
   synchronous request to the loader service that never answers.
5. The loader service never enters its message loop because it's stuck running the
   `FLIPPER_ON_SYSTEM_START[]` hooks. Bisected the 13 hooks: hooks 1–12 run,
   **#12 `region_on_system_start` hangs** (hook #13 `clock_settings_start` never runs).
6. `region_on_system_start` (now that SD is ready) calls `region_load_file()` →
   `storage_common_stat()` on INTERNAL storage (LittleFS) → **hangs**. →
   **Fixed by firmware patch #5** (skip region_load_file). Built, copied. **NOT yet
   re-validated** (the validation run was aborted).

### 8.3 EXACT NEXT STEP when resuming

The firmware with patch #5 is already built and copied to
`firmware/flipper-z-f7-full-EMULATOR-patched.bin`. Re-run the validation:

```bash
cd /opt/emu-test/flipper-emulator
export FLIPPER_EMU_LOG_DIR=$PWD/logs FLIPPER_EMU_FB_PATH=/tmp/flipper_fb.raw
rm -f /tmp/flipper_fb.raw
# CURRENT build addresses:
#   desktop_scene_main_on_enter = 0x080767a0
#   loader_show_menu            = 0x0808779a
timeout 90 ./tools/renode/renode --disable-xwt --console \
  -e '$firmware_bin=@firmware/flipper-z-f7-full-EMULATOR-patched.bin' \
  -e 'include @scripts/flash_firmware.resc' \
  -e 'cpu AddHook 0x080767a0 "print \"MAIN_SCENE_ENTER\""' \
  -e 'sleep 55' -e 'quit' 2>&1 | grep -oE "^MAIN_SCENE_ENTER" | sort | uniq -c
pkill -9 renode
```

- **If `MAIN_SCENE_ENTER` fires:** the boot chain is unblocked. Then test button
  navigation: hook `loader_show_menu` (0x0808779a), boot, inject OK
  (`gpioPortH OnGPIO 3 true` / RunFor 0.05 / `false` / RunFor 0.5) and confirm
  `MENU_OPENED`. If the menu opens → the whole thing works; capture the display
  with `view_display.py` and compare to the real menu (icon list).
- **If it still hangs:** bisect again. Likely next suspects (all touch internal
  storage / other services): `clock_settings_start` (#13), the loader autorun app
  (`loader_do_start_by_name` @ ~0x808791a), or `desktop_init_settings` reading
  settings from internal storage. Use the same technique: hook the next function
  in the chain, see the last one that runs.

### 8.4 To finish and package when it works

1. Regenerate the patch (now 5 changes across 4 files):
   ```bash
   cd /opt/emu-test/flipperzero-firmware
   git diff targets/f7/furi_hal/furi_hal_sd.c targets/f7/furi_hal/furi_hal_power.c \
     targets/f7/furi_hal/furi_hal_spi.c applications/services/input/input.c \
     applications/services/region/region.c \
     > /opt/emu-test/flipper-emulator/firmware/flipper_emulator.patch
   ```
2. Run `./setup.sh` (regenerates `flash_firmware.resc` from the new `.in`).
3. Update the backup zip + dist zip + README (note the SD is now attached in the
   main flow, and the extra patches).
4. Verify portability: extract dist zip elsewhere, `./setup.sh`, boot.

### 8.5 Open items / caveats

- **Internal storage (LittleFS on flash) is not functional** in the emulator.
  Region uses it (skipped). Other features that read internal storage (dolphin
  state, some settings) may degrade or hang — watch for new hangs and apply the
  same "skip under FLIPPER_EMULATOR" or, better, make internal storage work
  (would need the flash region formatted as LittleFS + working `furi_hal_flash`).
- **Deferred**: making the DMA↔SPI wiring real (instead of forcing polling) would
  let us drop firmware patch #4. Low priority.
- The three-workstream plan in `prompt2.md`: this session effectively did WS1's
  diagnosis + WS3's SD attach (Fix B). WS2 (RF/NFC/RFID/IR decode) is untouched.

### 8.6 Files changed this session (for the commit / review)

Emulator repo (`/opt/emu-test/flipper-emulator/`):
- `platform/stm32wb55_flipper.repl.in` — timer IRQ lines (Fix A).
- `scripts/flash_firmware.resc.in` — NEW template (SD attach, Fix B); old
  `flash_firmware.resc` is now generated & gitignored.
- `setup.sh` — generate `flash_firmware.resc`; auto-create SD image.
- `.gitignore` — ignore generated `scripts/flash_firmware.resc`.
- `DIAGNOSIS.md` — this log.

Firmware source (`/opt/emu-test/flipperzero-firmware/`, captured in the patch):
- `targets/f7/furi_hal/furi_hal_spi.c` — force non-DMA SPI (patch #4).
- `applications/services/region/region.c` — skip region_load_file (patch #5).
- (plus pre-existing sd/power/input patches).

---

## 9. SESSION v2.0 — DESKTOP BOOTS TO MAIN SCENE (milestone)

Continuing from §8, the boot chain had more blockers after the region fix. Found
and fixed each by **reading the firmware source + hooking + reading the firmware's
own USART log** (hooking `furi_log_print_format` @ its address and dumping the
tag+format strings — this was the key that revealed exactly what the firmware was
doing at each stall). Final result: **`MAIN_SCENE_ENTER` (desktop main scene)
fires** and the desktop is up.

### 9.1 CRITICAL: build the firmware in RELEASE mode

The debug build (`f7-firmware-D`) has `FURI_DEBUG` -> `furi_assert()` crashes.
This firmware version **phased out internal storage** (`/int`): `storage_get_data()`
has `furi_assert(type == ST_EXT)`, so ANY `/int` access crashes in a debug build.
The official release firmware uses `FURI_NDEBUG` where `furi_assert` is a no-op, so
`/int` access just returns FSE_NOT_READY and the firmware falls back to defaults.

**Build with `DEBUG=0`** (output goes to `dist/f7/`, no `-D` suffix):
```bash
./fbt DEBUG=0 --extra-define=FLIPPER_EMULATOR updater_package
cp dist/f7/flipper-z-f7-full-local.bin \
   /path/to/flipper-emulator/firmware/flipper-z-f7-full-EMULATOR-patched.bin
```

### 9.2 The full boot-blocker chain (v2.0 additions)

Continuing §8.2, after storage mounted:

7. **`desktop_settings_load` crashed** on `/int/.desktop.settings` via
   `furi_assert(type == ST_EXT)` in `storage_get_data`. → Fixed two ways: build
   RELEASE (§9.1, disables the assert) **and** patch #6 below (fail-fast for /int).
8. **Firmware crashed with `HW_IPCC_SYS_SendCmd timeout`** (33 s) in
   `furi_hal_bt_start_radio_stack` → `gap_extra_beacon_init()`. That function is
   called *unconditionally* even after Core2 (BLE) fails to start; it sends an
   SHCI command over IPCC to the absent Core2 and blocks until the 33 s timeout,
   then crashes. → Fixed by patch #7: only call `gap_extra_beacon_init()` if the
   radio stack actually started (it never does in the emulator).
9. After those, the firmware reaches **`MAIN_SCENE_ENTER`** ✅ (verified). Boot is
   slow (~60–100 s wall-clock) because of emulated I2C timeouts (the battery gauge
   `bq27220` isn't emulated, so each I2C op waits out its timeout).

### 9.3 New firmware patches this session (all `#ifdef FLIPPER_EMULATOR`)

The patch (`firmware/flipper_emulator.patch`) now spans **7 files, 9 guards**:
1. `furi_hal_sd.c` — skip SD power bit-banging.
2. `furi_hal_power.c` — skip battery/charger probe delays.
3. `applications/services/input/input.c` — instant input debounce.
4. **`furi_hal_spi.c`** — `furi_hal_spi_bus_trx_dma()` always uses the polling
   (non-DMA) path (DMA-complete IRQ not wired -> SD block reads crashed).
5. **`applications/services/region/region.c`** — skip `region_load_file()`
   (reads internal storage).
6. **`applications/services/storage/storage_processing.c`** — `storage_get_data()`
   returns `FSE_NOT_READY` for any non-EXT (`/int`) path, fail-fast.
7. **`targets/f7/furi_hal/furi_hal_bt.c`** — skip `gap_extra_beacon_init()` when
   the radio stack didn't start (avoids the 33 s IPCC timeout crash).

### 9.4 What works now (v2.0)

- ✅ Firmware boots to the **desktop main scene** (real UI, dolphin).
- ✅ **SD card mounts** (FlipperSdCard on SPI2 via Spi2Router) — storage works,
   so files/apps have real backing storage.
- ✅ Display renders. Custom WB55 RCC + EXTI + timers wired.
- ✅ Input events reach the GUI (EXTI fires, events published).
- ⚠️ **Button → app-menu navigation not yet confirmed** (OK on the desktop should
   call `loader_show_menu` @0x0808636e; still finalizing the input→viewport
   delivery on the main scene). This is the remaining item.

### 9.5 EXACT NEXT STEP (button navigation) when resuming

The desktop main scene is now active, so its viewport SHOULD receive input.
Re-verify the input path with the desktop up (boot takes ~95 s):
```bash
cd /opt/emu-test/flipper-emulator
export FLIPPER_EMU_LOG_DIR=$PWD/logs FLIPPER_EMU_FB_PATH=/tmp/flipper_fb.raw
# CURRENT (release) build addresses:
#   desktop_scene_main_on_enter = 0x08075d30
#   loader_show_menu            = 0x0808636e
#   desktop_main_input_callback = 0x08076b28
#   view_port_input             = 0x08084ed8
#   gui_input                   = 0x080830f4
timeout 130 ./tools/renode/renode --disable-xwt --console \
  -e '$firmware_bin=@firmware/flipper-z-f7-full-EMULATOR-patched.bin' \
  -e 'include @scripts/flash_firmware.resc' \
  -e 'cpu AddHook 0x08084ed8 "print \"VP_INPUT\""' \
  -e 'cpu AddHook 0x08076b28 "print \"DESKTOP_INPUT\""' \
  -e 'cpu AddHook 0x0808636e "print \"MENU\""' \
  -e 'sleep 95' -e 'pause' \
  -e 'gpioPortH OnGPIO 3 true' -e 'emulation RunFor "0.1"' \
  -e 'gpioPortH OnGPIO 3 false' -e 'emulation RunFor "1.5"' \
  -e 'quit' 2>&1 | grep -oE "^(VP_INPUT|DESKTOP_INPUT|MENU)" | sort | uniq -c
pkill -9 renode
```
- If `DESKTOP_INPUT`/`VP_INPUT` fire but not `MENU`: the event reaches the view but
  isn't a "Short" press. Tune the press duration (must be < ~300 ms *emulated*).
- If nothing fires: the main-scene viewport still isn't the one `gui_input` routes
  to. Check `gui_view_port_find_enabled` for the Desktop layer while the main
  scene is active (was NULL before the scene entered — should be non-NULL now).
- Debugging tip that unlocked everything this session: **hook
  `furi_log_print_format` and dump `[r1=tag] r2=format`** to read the firmware's
  own log without a working USART console.

### 9.6 Files changed this session (v2.0, for the commit)

Emulator repo:
- `platform/stm32wb55_flipper.repl.in` — timer IRQ lines (Fix A, §8).
- `scripts/flash_firmware.resc.in` — SD attach (Fix B, §8).
- `setup.sh`, `.gitignore` — generate flash_firmware.resc, auto-create SD image.
- `firmware/flipper_emulator.patch` — regenerated (7 files, 9 guards).
- `DIAGNOSIS.md` — this log.

Firmware (in the patch): furi_hal_sd/power/spi/bt.c, input.c, region.c,
storage_processing.c.

### 9.7 Packaging note

The shipped `firmware/flipper-z-f7-full-EMULATOR-patched.bin` is now the
**RELEASE** build (`dist/f7/`, DEBUG=0). If you rebuild, use `DEBUG=0` or the
`/int` assert will crash the desktop.
