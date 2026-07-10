# Diagnosis & Fix Log — Flipper Zero Emulator (Renode)

This document records how the emulated STM32WB55 hardware was made **faithful
enough to run STOCK, UNPATCHED Flipper firmware** — the same `firmware.bin` you
would flash to a real device, with **no `FLIPPER_EMULATOR` define and no source
patches**. Every fix below is on the **Renode side** (peripheral models,
platform description, SD image), verified with CPU hooks and
`arm-none-eabi-addr2line` against the firmware ELF.

> **STATUS v4.0 — STOCK FIRMWARE RUNS.**
> A plain release `firmware.bin` (no patches, no special defines) now:
> - boots through every `furi_hal` init to `[Flipper] Startup complete`,
> - mounts the SD card over SPI + DMA and **reads/writes files reliably**,
> - loads `/int` settings, renders the **dolphin animation**, reaches the desktop,
> - delivers **button presses** to the GUI (OK opens the main menu),
> - brings up SubGHz (`Init OK`) and NFC (chip id `0x28`) as **present chips**
>   (no RF over the air; logged to JSONL), and treats the battery gauge and BLE
>   Core2 as absent (logged, non-fatal),
> - installs a firmware **`.tgz`** through the stock updater (flash relocates to
>   SRAM via MEMRMP, flashes 194 pages, installs resources, reboots).
>
> The old approach (patching the firmware with `#ifdef FLIPPER_EMULATOR` and
> building with `--extra-define=FLIPPER_EMULATOR`) is **no longer required**. The
> patch file is kept only for reference. See the sections below for each fix.

---

## Why the pivot: patch-the-firmware → faithful-hardware

Earlier versions booted a *patched* firmware that skipped the battery gauge,
forced non-DMA SPI, snapped the input debounce, etc. That worked but meant you
could only run a firmware you rebuilt yourself. The goal now is to run **any**
firmware image — including third-party `.tgz` builds installed by the stock
updater — exactly as real hardware would. That required making the emulated
peripherals behave like the real silicon in the specific ways the firmware
depends on. The rest of this document is the list of those behaviors.

---

## 1. DWT cycle counter — long `furi_delay_us` spins (`peripherals/dwt_fast.py`)

**Problem.** `furi_hal_cortex_delay_us(us)` busy-waits on `DWT->CYCCNT`
(0xE0001004) until `us * 64` cycles elapse. The battery-gauge probe calls
`furi_delay_us(4000000)` several times (4 s each). Renode's real `Miscellaneous.DWT`
ties CYCCNT to virtual time, so a 4 s spin executes ~256 M tight-loop iterations
and takes ~100 s of wall-clock. Boot never finishes in reasonable time.

**Why not `PerformanceInMips` / `AdvanceImmediately`.** Renode's virtual time =
`instructions / (PerformanceInMips·1e6)`. Raising MIPS makes a busy-wait *slower*
in wall-clock; lowering it distorts all timing and breaks the UART. Global
`SetGlobalAdvanceImmediately` still only reached ~0.6 virtual-seconds per host
second here.

**Fix.** Replace the DWT with a Python peripheral whose `CYCCNT` (offset 0x04)
**auto-advances by a large fixed step on every read**. The firmware only ever
compares CYCCNT *deltas*, so any `while((CYCCNT - start) < ticks)` loop exits after
a handful of reads regardless of `ticks`. All `furi_delay_us` / cortex timers
become logically instantaneous (they are only guard delays for real silicon that
isn't here), while UART/timer virtual-time pacing is untouched (unlike a global
MIPS change). Boot drops from ~100 s to ~4 s.

---

## 2. I2C battery gauge / charger — absent, fast NAK

**Problem.** Stock `furi_hal_power_init` probes the `bq27220` fuel gauge and
`bq25896` charger over I2C1. They are not emulated. On real hardware a missing
gauge makes the driver retry with `furi_delay_us(4000000)` between tries.

**Fix.** With the fast DWT (above), the retry delays are instant, so the stock
probe simply fails fast: `[Gauge] ID: Device is not responding`, and boot
continues with the gauge/charger marked absent (the firmware falls back to safe
defaults). No firmware change; the I2C bus just NAKs the unimplemented device.

---

## 3. SD card over SPI2 + DMA (the big one)

The SD card is a custom `ISPIPeripheral` (`peripherals/FlipperSdCard.cs`) behind
`Spi2Router.cs` (SPI2 is shared by the display ST7567 and the microSD, muxed by
CS). Three separate fixes were needed to make stock reads **and writes** work.

### 3.1 DMA-driven block reads — `DMARecieve -> dma2@6`

Stock `furi_hal_spi_bus_trx_dma` reads SD blocks via DMA2 channel 6 (RX) / 7 (TX)
and waits on a semaphore released by the DMA Transfer-Complete IRQ. Renode's
`STM32SPI` raises its RX DMA request (the historically misspelled `DMARecieve`
GPIO) once per byte written to `DR`. Wiring `spi2.DMARecieve -> dma2@6` lets the
RX channel reach TC6. The TX channel (memory-to-peripheral) fires on channel
enable automatically, so TC7 also fires. Without this, SD block reads timed out
and `StorageSrv` crashed.

### 3.2 SD-SPI protocol state machine (`FlipperSdCard.cs`)

The card model implements exactly the SD-SPI command subset the driver uses
(CMD0/8/9/10/16/17/24/55/58/13/12, ACMD41), as an SDHC card backed by the image
file. Writes use a **positional** `WritePhase` state machine
(`WaitToken → Data → Crc → Response → Busy`) so the data-response `0x05` and the
trailing `0xFF` busy/ready bytes are produced by stream position, never queued —
this survives the deselect/reselect the driver performs inside
`sd_spi_get_data_response`.

### 3.3 The write bug: SPI RX buffer depth (`bufferCapacity: 1`) — ROOT CAUSE

**Symptom.** Every `/int` file was created 0-byte; `f_open` intermittently
returned `FR_DISK_ERR`; the driver re-initialised the card in a loop, so the
dolphin/desktop never fully loaded.

**Diagnosis.** After a 512-byte TX-only DMA block write, the firmware's
`furi_hal_spi_bus_end_txrx` drains the SPI RX FIFO by polling **FRLVL** (SR bits
10:9). But Renode v1.16.1's `STM32SPI` **does not implement FRLVL** (those bits
read 0), and its RX buffer is a **fixed 4-deep ring that silently overwrites**.
So after the DMA the ring holds 4 stale `0xFF` responses, the FRLVL-based drain
is a no-op, and the next *polled* read (the SD data-response) returns a stale
`0xFF` instead of the card's `0x05`. `0xFF & 0x1F = 0x1F ≠ 0x05` → the driver
thinks the write failed → re-init loop → `FR_DISK_ERR`.

**Fix.** Instantiate SPI2 with **`bufferCapacity: 1`**. With a 1-deep RX ring the
buffer only ever holds the most recent response, so a polled read after a DMA
write returns the matching fresh byte and the data-response `0x05` is read
correctly. **Result: all `f_open` return `FR_OK`, `/int` settings persist, the
dolphin animation renders.** (Verified: 252/252 `f_open` calls returned `0x0`.)

### 3.4 `/int` is on the SD card, not flash

Modern firmware has **no LittleFS-on-flash**; it redirects every `/int/...` path
to `/ext/.int/...` on the SD. `prepare_sdcard.py` now pre-creates the `/.int`
directory on the image so `/int` settings (`.dolphin.state`, notification,
desktop, region, expansion) can be created and written on a stock firmware. This
removed the need for the old `region.c` / `storage_processing.c` patches.

---

## 4. BLE Core2 / IPCC — `gap_extra_beacon_init` no longer hangs (`ipcc_stub.py`)

**Problem.** The Cortex-M0+ BLE coprocessor (Core2) is a closed ST blob and is not
emulated, so the radio stack never starts (`[Core2] C2 startup failed` — expected,
non-fatal). But stock `furi_hal_bt_start_radio_stack` still calls
`gap_extra_beacon_init()`, which sends a SYS command to Core2 over IPCC via
`HW_IPCC_SYS_SendCmd` and **busy-waits ~33 s then `furi_crash`es**.

**Mechanism.** `HW_IPCC_SYS_SendCmd` sets the SYS_CMD_RSP channel flag
(`C1SCR = ch << 16`, which latches into `C1TOC2SR`) and spins
`while(LL_C1_IPCC_IsActiveFlag_CHx(...))` until Core2 clears it.

**Fix.** The IPCC model makes **`C1TOC2SR` always read 0** (and never latches the
transmit-channel flags). This simulates an instant Core2 acknowledgement, so the
send-command busy-wait exits immediately and stock BT init completes gracefully
without a real coprocessor. `C2TOC1SR` stays 0 (Core2 never posts events), so the
firmware's receive path simply never fires.

---

## 5. Button input — EXTI port routing via SYSCFG_EXTICR

**Problem.** Pressing a button (injecting a GPIO edge) did not reach the GUI.
`input_isr` fired on the *release* but not the *press*.

**Root cause.** Real STM32 EXTI has a per-line **port-selection mux**
(`SYSCFG_EXTICR`): EXTI line 3 can be driven by PA3 **or** PB3 **or** … **or**
PH3, but only the one selected by `SYSCFG_EXTICR`. The platform wired *all six*
GPIO ports' pin 3 to the same `exti@3` input, so ports that idle low on pin 3
kept overwriting the shared line state and masked the PH3 (OK) press edge.

**Fix.**
- `peripherals/SYSCFG_WB55.cs` (new C# model, replaces the passive `syscfg_wb55.py`)
  decodes `SYSCFG_EXTICR[1..4]` and calls `exti.SetExtiSource(line, port)`.
- `peripherals/STM32WB55_EXTI.cs` is now **port-aware**: each port connects on a
  distinct input range (`input = port*16 + pin`), and an edge is accepted for a
  line only if it comes from the port currently selected by EXTICR. It also tracks
  RTSR1/FTSR1 edges and gates the NVIC line with IMR1.
- The platform connects the ports to disjoint EXTI input ranges
  (PA→0-15, PB→16-31, PC→32-47, PD→48-63, PE→64-79, PH→112-127).

**Result.** After boot `SYSCFG_EXTICR1 = 0x00007000` (EXTI3→PH, set by firmware);
injecting `gpioPortH OnGPIO 3 true` latches EXTI PR1 bit 3, fires `input_isr` on
the **press**, the debounce completes, `desktop_main_input_callback` runs and
`loader_show_menu` is called — the OK button opens the main menu.

---

## 6. RF / NFC / RFID — log-only stubs

These are intentionally **not** signal-accurate; they only need to let stock init
finish and to log SPI activity.

> **Note:** the presence-check behavior described below was later upgraded so the
> chips come up as **present** (SubGHz `Init OK`, NFC chip id `0x28`). See **§10**
> for the current faithful-init behavior; this section is kept for context.

- **CC1101 (SubGHz, SPI1, CS=PD0)** — `peripherals/CC1101.cs`. Correct status-byte
  format (CHIP_RDYn=0), register read/write framing, strobes. `furi_hal_subghz_init`
  runs a GDO0 (PA1) self-test with a 10 ms timeout; we don't drive PA1, so it logs
  `[E][FuriHalSubGhz] Init Fail` and continues (non-fatal). Every SPI transaction
  is appended to a JSONL log.
- **ST25R3916 (NFC, SPI1, CS=PE4)** — `peripherals/ST25R3916.cs`. Returns an
  IC_IDENTITY (reg 0x3F) that fails `(id & 0xF8) == 0x28`, so `furi_hal_nfc_init`
  takes the clean `[E][FuriHalNfc] Wrong chip id` → low-power path (non-fatal, no
  hang). SPI transactions logged to JSONL.
- Both correctly implement `ISPIPeripheral.Transmit` (one byte out per byte in) so
  the firmware's untimed `furi_hal_spi_bus_trx` busy-wait always progresses.

---

## 7. FLASH controller — for the updater (`flash_controller_wb55.py`)

For running the stock **updater** (`updater.bin`, which writes a firmware image to
internal flash), the FLASH controller at 0x58004000 is modelled to:
- accept the `KEYR` unlock sequence and clear `CR.LOCK`,
- report `SR` idle (BSY/CFGBSY/errors forced 0, EOP latched after each op) so
  `furi_hal_flash_wait_last_operation` and `furi_check(SR==0)` pass,
- handle `CR` PG/PER/FSTPG/STRT, filling an erased page with `0xFF` in the
  0x08000000 flash memory via `self.GetMachine().SystemBus.WriteBytes`,
- return `SFR = 0xD7` (SFSA) so `furi_hal_flash_get_free_end_address` = 0x080D7000,
  and a sane `OPTR`.

Program data is written **directly** to the 0x08000000 memory by the firmware
(the MappedMemory stores it natively); the controller only needs to not-hang and
to handle erase/status.

---

## 8. SYSCFG MEMRMP memory-remap — updater jumps to SRAM (`peripherals/SYSCFG_WB55.cs`)

**Problem.** The stock updater relocates itself to run from RAM: it
`memmove`s `updater.bin` into SRAM1 (0x20000000), then calls
`LL_SYSCFG_SetRemapMemory(LL_SYSCFG_REMAP_SRAM)` which sets `SYSCFG_MEMRMP`
`MEM_MODE = 0b011`. On real STM32WB silicon that **aliases SRAM1 at address
0x00000000** so the copied updater is reachable at 0x0, and `furi_hal_switch`
then jumps there. Renode does not remap the bus on a MEMRMP write, so the CPU
kept executing the old flash image and the updater never started.

**Fix.** `SYSCFG_WB55.cs` decodes `MEMRMP.MEM_MODE` and, on `0b011` (SRAM),
performs a bus remap using the **halt / defer / set-PC** pattern (the same
technique Renode's own `RenesasDA14592`/DA14 clock-generation controller uses for
its "remap eFlash to 0x0 and restart" boot):
1. Halt the current CPU inside the MEMRMP write callback (`IsHalted = true`).
2. Defer the actual bus change to `machine.LocalTimeSource` (you can't safely
   re-map the bus from inside the guest's own store instruction).
3. Register SRAM1 at a **new `BusPointRegistration(0x0)`** — the same backing
   store as its existing 0x20000000 point — so 0x0 now answers with SRAM1
   (the updater image). The flash's separate 0x08000000 registration is left
   untouched, and SRAM1's own 0x20000000 point is preserved so the `memmove`d
   content is intact.
4. Read the updater's vector table from SRAM1 and set the CPU **MSP** (initial
   stack) and **PC** (reset vector) so execution resumes in SRAM1 regardless of
   where the halted CPU was.
5. Unhalt (`IsHalted = false`).

`MEM_MODE = 0b000` (main flash) reverses the alias (removes the 0x0→SRAM1 point,
restores flash at 0x0). Other modes are logged and left as-is. Idempotency and
"no current CPU" (monitor-issued) fallbacks are handled. Result: the updater
relocates to SRAM and runs, exactly as on real hardware.

---

## 9. Updater `.tgz` flow — no Core2/FUS, no real option bytes

Running the stock over-the-air updater end-to-end required side-stepping the two
things the emulator can't reproduce (the closed BLE Core2/FUS coprocessor and
real option-byte hardware) **without patching the updater**:

- **`prepare_sdcard.py` strips `Radio*` and `OB*` keys from `update.fuf`.**
  `_strip_radio_and_ob_from_fuf()` rewrites the update manifest to drop
  `Radio:`, `Radio address/version/CRC:` and `OB reference/mask/write mask:`.
  With no `Radio*` keys, `update_task` skips `update_task_manage_radiostack`
  (RadioImageValidate/Erase/Write/Install/Busy) — there is no Core2/FUS to flash
  a radio stack into. With no `OB*` keys, the option-byte validation stage is
  skipped — there is no real option-byte hardware to validate against. The now
  unreferenced `radio.bin` is not copied. This leaves the firmware-flash and
  resource-install stages, which the emulator does faithfully.
- **RTC backup registers persist across reset** (`rtc_wb55.py`). The updater's
  reboot state machine communicates through the RTC `BKP` registers (boot_mode /
  update state). Because the model keeps `BKP` contents across a machine reset,
  each reboot stage reads the state the previous stage left — the OTA reboot
  chain advances instead of restarting from scratch.
- **`run_updater.resc`'s `reset` macro does not clobber the flashed firmware.**
  A firmware-triggered reset (NVIC `SYSRESETREQ` → `furi_hal_power_reset`) makes
  Renode reset the whole machine and re-run the `reset` macro. `ForceUpdateBoot`
  / `LoadFirmware` run **only once** at script load; on subsequent
  firmware-triggered resets the macro re-applies only volatile host-side state
  (OTP, button levels) and must **not** reload/erase the image. The flash
  `MappedMemory` persists across reset (Renode never wipes it) and the RTC `BKP`
  boot_mode persists, so the freshly flashed firmware survives the reboot instead
  of the updater re-running forever in a boot loop.

The updater flashes the firmware to internal flash (194 pages, reaching
"Stage 17 Completed") and then reboots into it.

---

## 10. RF / NFC faithful init — chips come up "present" (updates §6)

The RF stubs were upgraded from "fail the presence check cleanly" to
**pass the presence check like real chips** (still no RF over the air; SPI/GPIO
is logged to JSONL). This supersedes the failure paths described in §6.

- **CC1101 (SubGHz)** — `furi_hal_subghz_init` runs a **GDO0 self-test**: it
  writes `IOCFG0` to drive GDO0 low, waits for the GDO0 line to read low, then
  writes the inverted value to drive it high and waits for high. The model
  drives the GDO0 output (wired to **PA1**) from each `IOCFG0` write (only the
  invert bit `0x40` is modeled: base low, inverted high). PA1 therefore tracks
  the expected level and the self-test passes:
  `[I][FuriHalSubGhz] Init OK`.
- **ST25R3916 (NFC)** — the firmware reads `IC_IDENTITY` (reg `0x3F`) and requires
  `(chip_id & 0xF8) == 0x28` (`ic_type_st25r3916 = 5<<3`). The model returns
  **`0x28`** (rev v0), so the chip is **recognized** and `furi_hal_nfc_init`
  takes the present-chip path into clean low-power instead of bailing out with
  "Wrong chip id".

---

## Summary — what makes stock firmware boot

| Area | Faithful behavior added | File |
|------|-------------------------|------|
| Delays | fast-advancing DWT CYCCNT | `peripherals/dwt_fast.py` |
| Gauge/charger | absent, instant NAK (via fast DWT) | (I2C default) |
| SD reads | SPI2 RX DMA → `dma2@6` | `platform/…repl.in` |
| SD writes | SPI2 `bufferCapacity: 1` (FRLVL/RX-ring fix) | `platform/…repl.in` |
| SD protocol | positional write state machine | `peripherals/FlipperSdCard.cs` |
| `/int` | `/ext/.int` dir pre-created on SD | `scripts/prepare_sdcard.py` |
| BLE/IPCC | `C1TOC2SR` reads 0 (instant Core2 ack) | `peripherals/ipcc_stub.py` |
| Buttons | SYSCFG_EXTICR port routing | `peripherals/SYSCFG_WB55.cs`, `STM32WB55_EXTI.cs` |
| RF/NFC | present-chip init (SubGHz `Init OK`, NFC id `0x28`) | `peripherals/CC1101.cs`, `ST25R3916.cs` |
| Updater flash | faithful FLASH controller (SR reads 0, SFR=0xD7) | `peripherals/flash_controller_wb55.py` |
| Updater relocate | SYSCFG MEMRMP → SRAM1 aliased at 0x0 | `peripherals/SYSCFG_WB55.cs` |
| Updater flow | strip Radio*/OB* from update.fuf, persist RTC BKP | `scripts/prepare_sdcard.py`, `peripherals/rtc_wb55.py` |

Everything else is the real, unmodified firmware.

---

## Appendix — expected boot log (stock firmware, non-fatal errors are normal)

```
[I][FuriHalRtc] Init OK ... [I][FuriHalI2c] Init OK
[E][Gauge] ID: Device is not responding      ← gauge absent (expected)
[I][FuriHalPower] Init OK
[E][FuriHalMemory] No SRAM2 available         ← Core2 SRAM (expected)
[I][FuriHalSubGhz] Init OK                    ← CC1101 GDO0 self-test passes
[I][Flipper] Startup complete
[I][StorageExt] card mounted                  ← SD works
[E][Core2] C2 startup failed                  ← BLE coprocessor absent (expected)
[I][BleExtraBeacon] Init                       ← would hang without the IPCC fix
[I][AnimationManager] Select 'L1_...' animation  ← dolphin renders
[I][ClockSettingsAlarm] spawning alarm thread ← desktop up
```

*(The `flipper_emulator.patch` file and older patched-firmware notes are retained
in the repo for historical reference only; they are not needed to run stock
firmware.)*

---
---

# PART II — Making OTA `.tgz` updates install AND boot the new firmware

> **STATUS v5.0 — OTA UPDATE WORKS END TO END.**
> Installing a real third-party update package (tested with
> **Flipper-ARF `dev-492cee43`**) through the stock updater now flashes the new
> firmware to the emulated internal flash, and the device **reboots into it on
> its own**, exactly like a physical Flipper. Verified over USART on a plain
> `./run.sh` after the update:
> ```
> Firmware version:  Flipper-ARF
> Build date:        02-07-2026
> Git Commit:        492cee43 (0)
> Boot mode 0, starting services
> Startup complete
> [I][AnimationManager] ...
> ```
> The emulated internal flash is now **non-volatile** (a disk-backed image), so
> whatever the updater writes survives the process exiting and the next boot runs
> it. This part documents everything that had to change to get there, in the
> order it was discovered — including the dead ends, because they explain *why*
> the final design looks the way it does.

This part supersedes the older claim in Part I §9 that the updater "flashes 194
pages and reboots". That was only ever true *within a single Renode process* with
a volatile flash that got reloaded from a bundled `.bin` on every launch — so a
custom firmware never actually persisted and the device kept booting stock. The
goal here is **full fidelity to a physical device**: the updater does the real
work and the result sticks.

## 12. Non-volatile internal flash (the foundation) — patched Renode

**Problem.** Renode's `Memory.MappedMemory` is volatile: its contents live only
in host RAM for the lifetime of the process. So even when the updater correctly
wrote a new firmware to `0x08000000`, it vanished when Renode exited, and the
launch script reloaded the bundled stock `.bin` — the custom firmware could never
persist or boot on its own.

**Fix (Renode source patch).** We build Renode from source with a small patch to
`MappedMemory.cs` (`tools/renode-patches/0001-mappedmemory-persistence.patch`,
shipped source in `third_party/renode-src`). It adds an optional
`persistenceFile:` constructor parameter that makes the memory **disk-backed and
write-through**:
- on construction, the backing file (if present) is loaded into memory;
- **every guest write** (`WriteBytes`/`WriteByte/Word/DoubleWord/QuadWord`) is
  mirrored to the file at the matching offset through an always-open
  `FileStream` — so the image on disk tracks the flash live and survives even an
  abrupt `kill -9` (verified);
- a full flush also runs on `Reset()`/`Dispose()`.

The platform declares the flash with
`persistenceFile: "@PROJECT_DIR@/firmware/flash.img"`. `setup.sh` seeds
`flash.img` (1 MB) from the bundled stock build; `run.sh` boots **whatever is in
`flash.img`** (no more `LoadBinary` at boot), and `./run.sh foo.bin` /
`--reset-flash` reflash it host-side. This is the emulator equivalent of the
physical chip's NVM.

## 13. The faithful updater path (no manifest stripping)

Earlier the emulator made the updater *skip* the radio-stack and option-byte
stages by deleting the `Radio*`/`OB*` keys from `update.fuf`. That is not what a
real device does. Now `prepare_sdcard.py` keeps the manifest intact and instead
makes the emulated hardware answer the updater's real checks like a device that
**already has the target radio stack and matching option bytes**:

- **Option Bytes.** `_emit_update_params_resc()` parses the manifest's
  `OB reference` and pre-loads those bytes at `0x1FFF8000`. The updater's
  `OBValidation` stage reads them, finds every value already matches, marks
  nothing dirty, and does **not** call `furi_hal_flash_ob_apply()` (no
  `OBL_LAUNCH`, no reset, no bootloop). Clean no-op.
- **Radio stack.** The manifest's `Radio version` is handed to the IPCC model
  (below) so `SHCI_GetWirelessFwInfo` reports an installed stack whose
  version+type match. `update_task_manage_radiostack` takes the
  `total_stack_match` branch, logs `Stack version is up2date`, and returns
  success **without any FUS operation or reset**.

## 14. IPCC/SHCI Core2 model — faking "radio stack already installed"
(`peripherals/IPCC_WB55.cs`, replaces `ipcc_stub.py`)

The updater refuses to proceed unless Core2 (the closed BLE coprocessor) starts:
`ble_glue_wait_for_c2_start()` must succeed or the update aborts with
`[7-10] Failed to start C2`. The C# IPCC model:
- keeps the old stub behavior (`C1TOC2SR` reads 0 → instant SYS-command ack, no
  33 s `HW_IPCC_SYS_SendCmd` timeout);
- when the firmware unmasks the SYSTEM receive channel (clears C1MR CH2OM), it
  synthesizes the real **"C2 ready"** handshake: it builds a `TL_EvtPacket`
  (`subevtcode = SHCI_SUB_EVT_CODE_READY 0x9200`, `WIRELESS_FW_RUNNING`),
  links it into the firmware's `SystemEvtQueue` (located via
  `MB_RefTable → p_sys_table → sys_queue` in SRAM2A), and raises
  `IPCC_C1_RX` (NVIC 44). `ble_sys_user_event_callback` then sets
  `status = C2Started`, `mode = Stack`;
- writes the `WirelessFwInfoTable` (`Version`/`InfoStack`) in the firmware's
  device-info table so `SHCI_GetWirelessFwInfo` reports the manifest's version.

**Gating (critical).** This "present stack" illusion is enabled **only when RTC
`boot_mode == Update`**. On a normal boot the stack is reported **absent** — if
we faked it present on a normal boot, `BtSrv` would try to start the real BLE app
against a nonexistent coprocessor and `furi_check`-crash into a boot loop
(observed). So `ShouldFakeStackPresent()` reads the RTC boot_mode and only fakes
during the actual update.

## 15. HSEM (hardware semaphores) — flash/Core2 arbitration
(`peripherals/hsem_wb55.py`)

**Problem.** With the radio stack reported present, `furi_hal_bt_is_alive()`
becomes true, so during flashing the firmware takes the
`furi_hal_flash_begin_with_core2()` path, which polls the HSEM semaphores that
coordinate flash access between CPU1 and CPU2. The old stub reported **every**
semaphore as permanently locked, so the loop spun on
`CFG_HW_BLOCK_FLASH_REQ_BY_CPU1_SEMID (6)` until the 3 s timeout, then
`furi_check` fired → `furi_crash` → reset → the updater restarted from scratch
forever (boot loop that never reached `PostUpdate`).

**Fix.** A faithful per-semaphore model: a 1-step lock (read of `RLR[n]`) grants
the semaphore to CPU1; `R[n]` returns the real per-semaphore state; the
CPU2-owned block-flash semaphore (6) reads **free** (no Core2 ever holds it).
`furi_hal_flash_begin_with_core2` now completes and the DFU flash proceeds.

## 16. The MEMRMP bug that silently discarded the flash writes (the big one)
(`peripherals/SYSCFG_WB55.cs`)

This is the subtle one that made the update *look* successful yet leave the flash
unchanged — the exact symptom "it finishes but boots the old firmware".

**Background.** The stock updater relocates itself into SRAM1 and runs from there
(`stm32wb55xx_ram_fw.ld`, link base `0x20000000`). It does
`LL_SYSCFG_SetRemapMemory(REMAP_SRAM)` (MEM_MODE=0b011) so that on real silicon
SRAM1 is aliased at `0x00000000`, then `furi_hal_switch(0x0)` reads the updater's
MSP/reset-vector from `[0x0]/[0x4]` and jumps in. Our `SYSCFG_WB55` reproduces the
jump with a halt / defer-to-synced-state / set-PC+MSP pattern.

**The bug.** To alias SRAM1 at `0x0`, the model used to call
`bus.Unregister(flash)` (because `flash` is one `MappedMemory` mapped at both
`0x0` and `0x08000000`, and there was no obvious way to drop just the `0x0`
point). But in Renode, unregistering a peripheral that ends up with **zero**
registrations triggers the peripheral **garbage collector**, which calls
`flash.Dispose()`. Our persistence patch **closes the backing-file stream and
marks the object `disposed` in `Dispose()`**. The immediately-following
`Register(flash, 0x08000000)` re-registered the *same, now-dead* object.

Consequence, confirmed by instrumentation (chronological log):
```
Machine paused                                   (start of Unregister(flash))
FlushPersistence wrote ... resetvec=0x08011BBD   Dispose flushed STOCK to disk
flash re-registered at 0x08000000 sameObj=True   same dead object, stream closed
WriteBytes offset=0 first4=FF FF FF FF persistStream=<null>   erase reached RAM, NOT disk
MappedMemory.Reset persistenceStream=<null> disposed=True     final flush can't save
(no PersistWriteThrough lines at all during the update)
```
So the updater erased/programmed all 200 pages **in RAM** (it reported success,
boot_mode advanced Normal→Update→PostUpdate→Normal correctly), but write-through
never mirrored anything to `flash.img` → the on-disk image stayed stock → the
next boot ran the OLD firmware. Not the SD, not the boot_mode, not the flash
controller — the bus remap was silently killing persistence.

**Fix.** Don't touch bus registrations at all. The updater is linked to run from
SRAM1 and sets its own `SCB->VTOR = SRAM1_BASE`; the `0x0` alias was never
actually needed. `RemapToSramAndBoot()` now just reads the updater's MSP/reset
vector straight from SRAM1's real base (`0x20000000`/`0x20000004`) and sets the
CPU PC/MSP. `flash` is never unregistered → never `Dispose()`d → its
write-through persistence stream stays open → the flashed firmware lands on
`flash.img`. `RemapToFlash()` and the reset-restore path are now no-ops (we never
alias, so there is nothing to undo, and we must never `Unregister(flash)`).

## 17. Supporting fixes discovered along the way

- **`/.fupdate` pointer reliability (`prepare_sdcard.py`).** The updater won't run
  at all if the SD's root `/.fupdate` pointer is missing — `main()` can't find the
  manifest and abandons to a Normal boot (looks like "the update did nothing").
  In this sandbox loop-mount is unavailable so the SD is written with `mtools`,
  which could silently fail for the leading-dot root file. `copy_verified()` now
  writes **and verifies** `/.fupdate` (retry, then hard-fail). Also note the
  firmware itself deletes `/.fupdate` on `update_operation_disarm()`, and the SD
  image is persistent, so `run.sh --with-update` always re-preps the SD.
- **`FlipperSdCard.Reset()` torn-write logging.** A board reset (the updater
  reboots several times) used to silently discard a partially-received 512-byte
  SD block. Completed blocks are flushed immediately so they are safe; a genuinely
  torn block is faithful to a real card power-cycle, but the drop is now logged as
  a WARNING for diagnosability. (Investigated as a suspect; it was **not** the
  cause of the update failure — zero torn writes were observed in the failing
  run.)
- **RTC boot_mode transition logging (`rtc_wb55.py`).** Logs
  `Normal→Update→PostUpdate→Normal` at Info level, which is what let us confirm
  the OTA reboot chain was advancing correctly while the flash was (separately)
  not persisting.
- **Buttons.** Direct-injection testing proved the EXTI path and `SYSCFG_EXTICR`
  routing were already correct (OK opens the menu). The "buttons don't work"
  report was an SDL frontend/environment issue: the window needs keyboard focus
  and a real display. `sdl_frontend.py` now connects to the monitor before
  creating the renderer, warns when there is no `DISPLAY`/`WAYLAND_DISPLAY`,
  raises/focuses the window, and renders via a streaming texture (~60 fps) instead
  of ~8k per-pixel fills that throttled the event loop.

## 18. Validated OTA sequence (radio stack reported already-installed)

```
Normal  --(user arms update; boot_mode=Update)--> RESET
Update  boot: RAM updater flashes firmware.dfu (200 pages, write-through to
              flash.img), radio no-op ("up2date"), OB no-op, boot_mode=PostUpdate
        --> RESET
PostUpdate boot: restore /int (empty backup.tar stub), unpack resources, install
              splash, update_operation_disarm -> boot_mode=Normal, delete /.fupdate
        --> RESET
Normal  boot: runs the newly-installed Flipper-ARF firmware to the desktop.
```
Stage/error-code map (`[XX-YY]`) per the official OTA docs: 1 manifest, 2 backup,
3-7 radio, 8 option bytes, 9 check DFU, 10 write flash, 11 validate flash,
12 restore config, 13-15 resources.

## Summary — Part II changes

| Area | Faithful behavior added | File |
|------|-------------------------|------|
| NVM flash | disk-backed, write-through MappedMemory | Renode patch `0001-mappedmemory-persistence.patch` |
| Boot model | boot from persistent `flash.img`, reflash host-side | `setup.sh`, `run.sh`, `*.resc.in` |
| Manifest | keep Radio*/OB* intact, satisfy checks faithfully | `scripts/prepare_sdcard.py` |
| Core2/BLE | IPCC "C2 ready" + installed-stack version (gated to Update) | `peripherals/IPCC_WB55.cs` |
| HSEM | faithful per-semaphore model (flash/Core2 arbitration) | `peripherals/hsem_wb55.py` |
| MEMRMP | boot RAM updater WITHOUT unregistering flash (persistence-safe) | `peripherals/SYSCFG_WB55.cs` |
| SD `/.fupdate` | write + verify the update pointer | `scripts/prepare_sdcard.py` |
| Diagnostics | boot_mode + SD torn-write logging | `rtc_wb55.py`, `FlipperSdCard.cs` |
| Buttons | SDL focus/warn/fast-render fixes | `frontend/sdl_frontend.py` |

The single most important root cause was **§16**: `Unregister(flash)` during the
SRAM remap was Disposing the flash and silently killing write-through
persistence, so the update wrote only to RAM and never to disk. Removing that one
bus manipulation is what made custom firmware finally stick and boot.

---
---

# PART III — Buttons in the SDL GUI (input freeze after the first press)

> **STATUS v6.0 — BUTTONS WORK.**
> All six buttons (Up/Down/Left/Right/OK/Back), including short vs. long press,
> now reach the firmware from the SDL GUI. The device navigates the menu and
> responds to every button with no freeze and no crash, and the tickless-idle
> path is stable over long idle periods. Verified live: OK opens the menu,
> Up/Down/Left/Right navigate, Back exits, each producing a distinct display
> change; 0 crashes across a 40 s idle-stability run plus button injection.

## Symptom

In the SDL frontend the key presses were logged and the correct GPIO commands
were sent (`gpioPortB OnGPIO 10 false`, etc.), but on screen almost nothing
reacted: only the very FIRST button (OK) did anything, then the UI froze — even a
second OK did nothing. An early clue was that "up" seemed to act like "back".

## Root cause #1 — EXTI level desync + a phantom pending bit

The button mapping is fixed and correct (`furi_hal_resources.c`): Up=PB10,
Down=PC6, Left=PB11, Right=PB12, OK=PH3 (active-high), Back=PC13 (rest
active-low). The GPIO->EXTI wiring and `SYSCFG_EXTICR` routing were also correct.
Two defects in `peripherals/STM32WB55_EXTI.cs` broke input:

1. **Per-line level was only tracked for the EXTICR-selected port.** Idle button
   levels are injected (`SetButtonsIdle`, `init_buttons`) *before* the firmware
   programs `SYSCFG_EXTICR` (when every line still defaults to PA). The old model
   discarded edges from non-selected ports *before* recording their level, so the
   line's baseline stayed wrong once the real port was selected: the first real
   press landed on "no edge" (previous==value) and only the release produced a
   (wrong-polarity) edge on a shared IRQ — the "buttons do nothing / Up acts like
   Back" symptom. **Fix:** always record the level of *every* (port,pin) in
   `OnGPIO`; edge-detect the EXTI line against the currently EXTICR-selected
   port's tracked level. (Also fixed a latent 3-bit vs. 4-bit `EXTICR` field mask
   in `SYSCFG_WB55.cs` — harmless for PA..PH but incorrect.)

2. **Phantom pending bit live-locked the idle task.** During init the NFC model
   (ST25R3916 IRQ on PA2 / EXTI line 2) produces a spurious rising edge while its
   EXTI line is still masked. On real silicon PR1 latches regardless of IMR1 and
   that stray bit is harmless, but the Flipper's tickless-idle path
   (`furi_hal_os_is_pending_irq` -> `LL_EXTI_ReadFlag_0_31` reads the *whole*
   PR1) treats ANY pending bit — even a masked one no ISR will ever clear — as
   "an interrupt is pending", aborts every sleep, and live-locks the FreeRTOS
   idle task, starving the input/GUI threads. The first OK squeezed through;
   after that the idle-spin ate the CPU. **Fix:** in `STM32WB55_EXTI.cs`, only
   latch `PR1` for lines whose interrupt is UNMASKED (IMR1 set). Every real
   button enables its IMR1 bit, so buttons are unaffected; a masked line can no
   longer poison PR1.

## Root cause #2 — LPTIM1 tickless-idle crash (unmasked by fixing #1)

Fixing the live-lock let the system actually enter tickless-idle for the first
time, which exposed a second, previously-hidden bug: repeated
`[CRASH][ISR LPTIM1] furi_check failed` + reboots.

Mechanism (confirmed against firmware + Renode core): the stock idle path
(`furi_hal_os.c vPortSuppressTicksAndSleep`) runs LPTIM1 as the wakeup timer with
IRQs masked (`__disable_irq`, PRIMASK=1), `__WFI`, then handles the compare flag
INLINE and stops LPTIM1 with `furi_hal_idle_timer_reset()` =
`furi_hal_bus_reset(LPTIM1)` (a pulse on `RCC_APB1RSTR1` bit 31) before
`__enable_irq()`. No ISR is ever registered for LPTIM1 —
`furi_hal_interrupt_call` `furi_check`s and crashes if its IRQ is ever taken.

On real silicon PRIMASK=1 lets `WFI` wake on the pending LPTIM1 IRQ without
running the handler, and the APB1 reset clears the peripheral and drops its
(level) IRQ line. The Renode Cortex-M core is faithful here (WFI wakes with
PRIMASK=1, handler does NOT run under PRIMASK). The break was that our **RCC is a
Python stub** (`rcc_wb55.py`) that only stored registers, so
`furi_hal_bus_reset(LPTIM1)` never reached the `STM32L0_LpTimer` model: its
`compareTimer` kept running and its level IRQ line stayed asserted, so the moment
the firmware ran `__enable_irq()` the still-asserted line fired the handler ->
crash. (Clearing only the ICR flags was insufficient — the compareTimer re-armed
and re-asserted.)

**Fix (`peripherals/rcc_wb55.py`).** Faithfully emulate the APB1 peripheral
reset: when the firmware pulses `APB1RSTR1` bit 31 (LPTIM1RST), fully STOP the
LPTIM1 model via the sysbus — write `CR=0` (ENABLE=0, which halts LPTIM1 and its
compareTimer in the model), then `ICR=0x1F` (clear all latched status flags; the
ICR write callback runs `UpdateInterrupts()` and drops the IRQ line), then
`IER=0` (mask). This reproduces exactly what the hardware reset does, so the
tickless-idle sleep completes cleanly with no spurious ISR.

## Root cause #3 (not a bug) — buttons read the LEVEL, not just the edge

Confirmed for completeness: the input service reads `furi_hal_gpio_read()` (the
GPIO IDR level) repeatedly, using the EXTI edge only to wake its thread; short vs.
long press is driven by a periodic `FuriTimer` that runs while the pressed LEVEL
is held. Renode's `STM32_GPIOPort` already reflects `OnGPIO(pin,value)` in the
IDR faithfully (independent of MODER/pull), so the level path was always correct.
The frontend must therefore inject a real level transition per press/release and
HOLD the pressed level for the duration (which it does). No change needed here.

## Summary — Part III changes

| Area | Fix | File |
|------|-----|------|
| EXTI level | track level per (port,pin) always; edge-detect vs. selected port | `peripherals/STM32WB55_EXTI.cs` |
| EXTI pending | only latch PR1 for UNMASKED lines (kills phantom-bit idle live-lock) | `peripherals/STM32WB55_EXTI.cs` |
| EXTICR | 4-bit port field mask (was 3-bit; latent) | `peripherals/SYSCFG_WB55.cs` |
| LPTIM1 | APB1 bus-reset fully stops LPTIM1 (CR=0/ICR/IER) -> no idle-ISR crash | `peripherals/rcc_wb55.py` |
| SD reset | log torn writes instead of silent drop (diagnosability) | `peripherals/FlipperSdCard.cs` |
| Frontend | connect monitor first, warn on no DISPLAY, focus window, fast render | `frontend/sdl_frontend.py` |

The two behavioral fixes (EXTI PR1 masking + LPTIM1 bus-reset stop) are what took
the GUI from "only the first button works, then frozen" to full, stable button
navigation.
