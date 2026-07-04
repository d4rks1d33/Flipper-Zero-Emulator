# STM32WB55 USB FS device stub (Renode request-based API).
if request.IsInit:
    regs = {0x40: 0x0003}  # CNTR: FRES|PDWN
elif request.IsRead:
    off = request.Offset
    if off == 0x44:   # ISTR: no interrupts
        request.Value = 0
    else:
        request.Value = regs.get(off, 0)
elif request.IsWrite:
    regs[request.Offset] = request.Value
