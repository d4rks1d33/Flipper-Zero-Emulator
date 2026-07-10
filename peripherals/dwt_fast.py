# STM32/Cortex-M DWT (Data Watchpoint & Trace) model for the Flipper emulator.
#
# The firmware uses DWT->CYCCNT (offset 0x04) as its microsecond time base for
# ALL busy-wait delays and cortex timeouts:
#
#   furi_hal_cortex_delay_us(us):   while((CYCCNT - start) < us*64) {}
#   furi_hal_cortex_timer_is_expired: (CYCCNT - start) >= timeout_us*64
#
# It only ever compares CYCCNT *deltas*, never the absolute value, so the model
# just needs CYCCNT to advance monotonically. Renode's real Miscellaneous.DWT
# ties CYCCNT to virtual time, which means a furi_delay_us(4000000) (probing the
# absent battery gauge) spins for 4 virtual seconds = 256 million cycles. That
# tight loop is executed instruction-by-instruction and takes ~100 s wall-clock,
# stalling boot.
#
# This model instead advances CYCCNT by a large fixed STEP on every read. Any
# delay/timeout loop then exits after at most (time_ticks / STEP) reads:
#   - the longest delay, 4 s = 256e6 ticks, exits in ~256e6/STEP reads
#   - short I2C/SPI timeouts still exit promptly
# so every delay collapses to a handful of loop iterations while remaining
# monotonic and self-consistent. Delays become logically instantaneous, which is
# exactly what we want: the guard delays exist only to give real silicon time to
# settle, and there is no real silicon here.
#
# Registers (Cortex-M DWT, base 0xE0001000):
#   0x00 CTRL     - control (CYCCNTENA etc.); stored/returned verbatim
#   0x04 CYCCNT   - cycle counter; auto-advancing (the important one)
#   0x08 CPICNT .. 0x1C FOLDCNT - profiling counters (return stored/0)
#   0x20 COMP0 .. 0x5C - watchpoint comparators used by furi_hal_cortex_comp_*;
#                        stored/returned verbatim so writes round-trip.

# Advance-per-read. 0x100000 (~1M) makes the worst-case 256e6-tick delay finish
# in ~256 reads, while keeping enough resolution that short timeouts don't
# collapse to zero before a (fast) peripheral responds in the same iteration.
_STEP = 0x100000

if request.IsInit:
    regs = {}
    cyccnt = 0

elif request.IsRead:
    off = request.Offset
    if off == 0x04:  # CYCCNT: auto-advance on every read
        cyccnt = (cyccnt + _STEP) & 0xFFFFFFFF
        request.Value = cyccnt
    else:
        request.Value = regs.get(off, 0)

elif request.IsWrite:
    off = request.Offset
    if off == 0x04:
        cyccnt = request.Value & 0xFFFFFFFF  # firmware zeroes CYCCNT at init
    else:
        regs[off] = request.Value
