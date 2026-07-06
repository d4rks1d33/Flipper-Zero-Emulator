# Diagnosis & Fix Log — Flipper Zero Emulator (Renode)

This document records the root-cause investigation of the "buttons don't navigate
the UI" problem and the fixes applied. Everything here was verified with CPU
hooks on the real firmware and `arm-none-eabi-addr2line` against the ELF.

> **STATUS v3.2 — BUTTON NAVIGATION NOW WORKS.**
> The two critical bugs that blocked button input are fixed:
> - GUI thread: `FuriStatusErrorTimeout` (`-2 = 0xFFFFFFFE`) was misinterpreted as
>   a valid `GUI_THREAD_FLAG_INPUT` on every 50 ms timeout, causing the thread to
>   enter the input block with an empty queue and skip all redraws.
> - Input service: the emulator debounce snap made `is_changing` never true, so the
>   thread blocked forever on `furi_thread_flags_wait(INPUT_THREAD_FLAG_ISR, ...)`
>   after the very first poll and never polled again unless an EXTI interrupt
>   arrived.
>
> See §12 for details.

---

## 12. SESSION v3.2 — BUTTON INPUT FIXED

### 12.1 Symptoms

The firmware booted to the desktop, the display rendered, the event loop ticked
every 10 ms, but no button press produced any UI response. The firmware log
showed:

```
[E][GuiSrv] FLAG_INPUT flags=0xfffffffe    ← every 50 ms
[E][EventLoop] TIMEOUT count=N
[E][ViewDispatcher] TICK_EVENT cv=20015970
```

The `FLAG_INPUT` log appeared on every 50 ms GUI-thread timeout — **even when no
button was pressed** — while `[InputSrv] STATE_CHANGE`, `[GuiSrv] INPUT_CB`, and
`[GuiSrv] INPUT_DISPATCH` never appeared.

### 12.2 Root cause #1 — `FuriStatusErrorTimeout` misinterpreted (GUI thread)

In `gui.c` (`gui_srv`), the emulator patch changed the GUI thread's blocking wait
from `FuriWaitForever` to a 50 ms timeout:

```c
uint32_t flags = furi_thread_flags_wait(GUI_THREAD_FLAG_ALL, FuriFlagWaitAny, 50);
```

When no flags are set and the timeout expires, `furi_thread_flags_wait` returns
`(uint32_t)FuriStatusErrorTimeout` = `-2` = `0xFFFFFFFE`.

The flags are:
- `GUI_THREAD_FLAG_DRAW  = (1 << 0)` = `0x1`
- `GUI_THREAD_FLAG_INPUT = (1 << 1)` = `0x2`

The original code checked:
```c
if(flags & GUI_THREAD_FLAG_INPUT) { ... }   // 0xFFFFFFFE & 0x2 = 0x2 → TRUE!
```

`-2` happens to have bit 1 set (`0xFFFFFFFE & 0x2 ≠ 0`), so the code **entered the
input block on every timeout** with an empty queue (no-op), **skipped the draw
block** (`0xFFFFFFFE & 0x1 = 0`), and **skipped the forced redraw** (`0xFFFFFFFE≠0`).

**Fix:** Check for `FuriStatusErrorTimeout` before checking any flag bits:

```c
if(flags == (uint32_t)FuriStatusErrorTimeout) {
    gui_redraw(gui);
    continue;
}
```

### 12.3 Root cause #2 — input service thread blocks forever (no EXTI wakeup)

In `input.c` (`input_srv`), the emulator patch snaps the debounce to its terminal
value in one poll:

```c
pin_states[i].debounce = state ? INPUT_DEBOUNCE_TICKS : 0;
```

With this snap, `debounce` is always either `0` or `INPUT_DEBOUNCE_TICKS`, **never**
in between. The logic:

```c
if(pin_states[i].debounce > 0 && pin_states[i].debounce < INPUT_DEBOUNCE_TICKS) {
    is_changing = true;     // ← NEVER true with snap
} else if(pin_states[i].state != state) {
    // state change detected & published
}
```

Because `is_changing` is never true, the thread always falls through to:

```c
furi_thread_flags_wait(INPUT_THREAD_FLAG_ISR, FuriFlagWaitAny, FuriWaitForever);
```

It processes ONE poll, detects any state change, publishes the event, then
**blocks forever** waiting for an EXTI interrupt to wake it. If the EXTI→NVIC→
firmware interrupt chain doesn't complete (e.g., NVIC not yet enabled for the
EXTI line), the thread never wakes up again.

**Fix:** Use a timeout for the blocking wait under `#ifdef FLIPPER_EMULATOR`:

```c
furi_thread_flags_wait(INPUT_THREAD_FLAG_ISR, FuriFlagWaitAny, INPUT_DEBOUNCE_TICKS / 2);
```

This polls every 2 ticks (~2 ms) even without EXTI interrupts. EXTI interrupts
still work and wake the thread immediately (return before the timeout), so the
timeout is just a safety net.

### 12.4 Result

- `[GuiSrv] FLAG_INPUT` now only appears when a button is **actually pressed**
  (not every 50 ms).
- The GUI thread redraws correctly on timeout (dolphin animates).
- The input service polls GPIO every ~2 ms and detects state changes without
  relying on EXTI interrupts.
- Button → menu navigation works: OK on the desktop opens the main menu.

### 12.5 Files changed

Firmware source (captured in `firmware/flipper_emulator.patch`):
- `applications/services/gui/gui.c` — check `FuriStatusErrorTimeout` before
  interpreting flags.
- `applications/services/input/input.c` — replace `FuriWaitForever` with
  `INPUT_DEBOUNCE_TICKS / 2` timeout under `#ifdef FLIPPER_EMULATOR`.

Emulator repo (rebuilt binary):
- `firmware/flipper-z-f7-full-EMULATOR-patched.bin` — rebuilt RELEASE with the
  two fixes.

---

*(Previous sections §1–§11 record earlier sessions: boot-chain unblocking, SD
mount, resources fix, I2C busy-wait fix, and the old "remaining item" status.)*
