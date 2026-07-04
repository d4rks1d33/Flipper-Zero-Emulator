# STM32WB55 PKA stub (Renode request-based API).
if request.IsInit:
    regs = {}
elif request.IsRead:
    off = request.Offset
    if off == 0x04:   # SR
        sr = (1 << 17)  # PROCENDF
        if regs.get(0x00, 0) & 1:
            sr |= 1     # INITOK
        request.Value = sr
    else:
        request.Value = regs.get(off, 0)
elif request.IsWrite:
    regs[request.Offset] = request.Value
