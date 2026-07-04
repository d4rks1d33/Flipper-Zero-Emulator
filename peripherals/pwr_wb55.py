# STM32WB55 PWR stub (Renode request-based API).
if request.IsInit:
    regs = {}
elif request.IsRead:
    off = request.Offset
    if off == 0x10:      # SR1: no wakeup/standby flags
        request.Value = 0
    elif off == 0x14:    # SR2: VOSF=0, regulators ready, PVM ok
        request.Value = 0
    else:
        request.Value = regs.get(off, 0)
elif request.IsWrite:
    regs[request.Offset] = request.Value
