# STM32WB55 AES1/AES2 stub (Renode request-based API).
if request.IsInit:
    regs = {}
elif request.IsRead:
    off = request.Offset
    if off == 0x04:    # SR: CCF=1 (computation complete)
        request.Value = 1
    elif off == 0x0C:  # DOUTR
        request.Value = 0
    else:
        request.Value = regs.get(off, 0)
elif request.IsWrite:
    regs[request.Offset] = request.Value
