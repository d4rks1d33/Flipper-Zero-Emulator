# STM32WB55 RNG stub (Renode request-based API).
import random as _random
if request.IsInit:
    regs = {}
elif request.IsRead:
    off = request.Offset
    if off == 0x00:      # CR
        request.Value = regs.get(0x00, 0)
    elif off == 0x04:    # SR: DRDY=1 if RNGEN
        request.Value = 1 if (regs.get(0x00, 0) & (1 << 2)) else 0
    elif off == 0x08:    # DR
        request.Value = _random.getrandbits(32) & 0xFFFFFFFF
    else:
        request.Value = regs.get(off, 0)
elif request.IsWrite:
    regs[request.Offset] = request.Value
