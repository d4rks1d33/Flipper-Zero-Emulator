# STM32WB55 RTC stub (Renode request-based API).
# Provides calendar (from host time), ICSR ready/init flags, WPR unlock,
# and 20 backup registers (BKP0R..BKP19R at 0x50..0x9C).
#
# Register map used by the firmware (furi_hal_rtc.c):
#   BKP0R (0x50) = FuriHalRtcRegisterHeader  (magic 0x10F1, version 0)
#   BKP1R (0x54) = FuriHalRtcRegisterSystem  (boot_mode in bits 19:16)
#   ...
# Boot modes: 0=Normal 1=Dfu 2=PreUpdate 3=Update 4=PostUpdate
#
# BACKUP-DOMAIN PERSISTENCE (important for the OTA update flow):
# On real hardware the RTC backup registers live in the VBAT-backed backup
# domain and SURVIVE a firmware-triggered reset (NVIC SYSRESETREQ). This is how
# boot_mode is handed off between boot stages:
#   Update -> (RAM updater flashes fw, sets PostUpdate) -> reset -> PostUpdate
#          -> (post-update resource unpack, disarm sets Normal) -> reset -> Normal
#
# In Renode a SYSRESETREQ resets the whole machine, which re-runs this script's
# IsInit branch. To emulate the backup domain we keep the backup registers
# (0x50..0x9C) in a scope-global (`_bkp`) that is created ONCE and preserved
# across resets; only the calendar/control registers are refreshed on init.
# The IronPython scope persists between PythonPeripheral.Reset() calls, so
# `_bkp` retains boot_mode written by the updater across the reboot.
import time as _time

if request.IsInit:
    # Backup registers persist across resets (VBAT-backed backup domain).
    # Create them only on the very first init; keep existing values otherwise.
    try:
        _bkp
    except NameError:
        _bkp = {}
        for i in range(20):
            _bkp[0x50 + i * 4] = 0          # backup registers, zeroed once

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

elif request.IsRead:
    off = request.Offset
    if off == 0x0C:  # ICSR
        icsr = regs.get(0x0C, 0)
        if icsr & (1 << 7):   # INIT requested
            icsr |= (1 << 6)  # INITF
        icsr |= (1 << 5) | (1 << 4)  # RSF, INITS
        request.Value = icsr
    elif 0x50 <= off <= 0x9C:  # backup registers (backup domain)
        request.Value = _bkp.get(off, 0)
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
    elif 0x50 <= off <= 0x9C:  # backup registers (backup domain, persistent)
        # Log boot_mode transitions (BKP1R @ 0x54, bits 19:16) at Info -- handy for
        # following the OTA reboot chain (Normal->Update->PostUpdate->Normal).
        if off == 0x54:
            _old = (_bkp.get(0x54, 0) >> 16) & 0xF
            _new = (request.Value >> 16) & 0xF
            if _old != _new:
                _names = {0: "Normal", 1: "Dfu", 2: "PreUpdate", 3: "Update", 4: "PostUpdate"}
                self.Log(LogLevel.Info, "RTC boot_mode %s(%d) -> %s(%d)" % (
                    _names.get(_old, "?"), _old, _names.get(_new, "?"), _new))
        _bkp[off] = request.Value
    else:
        regs[off] = request.Value
