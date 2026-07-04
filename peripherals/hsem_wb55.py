# STM32WB55 HSEM stub (Renode request-based API).
# Always grants locks to CPU1 (COREID=4). R[n] and RLR[n] reads return
# LOCK=1, COREID=4 so LL_HSEM_1StepLock succeeds immediately.
if request.IsInit:
    regs = {}
elif request.IsRead:
    off = request.Offset
    if off < 0x100:   # R[0..31] (0x00-0x7C) and RLR[0..31] (0x80-0xFC)
        request.Value = 0x80000400  # LOCK | (COREID=4 << 8)
    else:
        request.Value = regs.get(off, 0)
elif request.IsWrite:
    regs[request.Offset] = request.Value
