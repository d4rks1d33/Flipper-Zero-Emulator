# STM32WB55 RTC stub (Renode request-based API).
# Provides calendar (from host time), ICSR ready/init flags, WPR unlock,
# and 20 backup registers (BKP0R..BKP19R at 0x50..0x9C).
# Boot mode lives in BKP2R (FuriHalRtcRegisterSystem) bits 4:1 = 0 (Normal).
import time as _time

if request.IsInit:
    regs = {}
    _wpr = 0
    t = _time.localtime()
    regs[0x00] = ((t.tm_hour // 10) << 20) | ((t.tm_hour % 10) << 16) | \
                 ((t.tm_min // 10) << 12) | ((t.tm_min % 10) << 8) | \
                 ((t.tm_sec // 10) << 4) | (t.tm_sec % 10)          # TR
    yr = t.tm_year % 100
    regs[0x04] = ((yr // 10) << 20) | ((yr % 10) << 16) | \
                 (max(t.tm_wday, 1) << 13) | \
                 ((t.tm_mon // 10) << 12) | ((t.tm_mon % 10) << 8) | \
                 ((t.tm_mday // 10) << 4) | (t.tm_mday % 10)         # DR
    regs[0x0C] = (1 << 4) | (1 << 5)   # ICSR: INITS=1, RSF=1
    regs[0x10] = 0x007F00FF             # PRER
    for i in range(20):
        regs[0x50 + i * 4] = 0          # backup registers

elif request.IsRead:
    off = request.Offset
    if off == 0x0C:  # ICSR
        icsr = regs.get(0x0C, 0)
        if icsr & (1 << 7):   # INIT requested
            icsr |= (1 << 6)  # INITF
        icsr |= (1 << 5) | (1 << 4)  # RSF, INITS
        request.Value = icsr
    else:
        request.Value = regs.get(off, 0)

elif request.IsWrite:
    off = request.Offset
    if off == 0x24:  # WPR
        key = request.Value & 0xFF
        if key == 0xCA:
            _wpr = 1
        elif key == 0x53 and _wpr == 1:
            _wpr = 2
        else:
            _wpr = 0
    else:
        regs[off] = request.Value
