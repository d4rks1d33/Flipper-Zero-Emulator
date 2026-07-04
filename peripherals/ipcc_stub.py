# STM32WB55 IPCC stub (Renode request-based API).
# Mailbox between CPU1(M4) and CPU2(M0+ BLE). CPU2 not emulated.
if request.IsInit:
    regs = {
        0x00: 0,       # C1CR
        0x04: 0x3F3F,  # C1MR - all masked
        0x08: 0,       # C1SCR
        0x0C: 0,       # C1TOC2SR
        0x10: 0,       # C2CR
        0x14: 0x3F3F,  # C2MR
        0x18: 0,       # C2SCR
        0x1C: 0,       # C2TOC1SR - never any data from (absent) CPU2
    }
elif request.IsRead:
    request.Value = regs.get(request.Offset, 0)
elif request.IsWrite:
    off = request.Offset
    regs[off] = request.Value
    if off == 0x08:  # C1SCR
        for ch in range(6):
            if request.Value & (1 << ch):
                regs[0x0C] = regs.get(0x0C, 0) & ~(1 << ch)
            if request.Value & (1 << (ch + 16)):
                regs[0x0C] = regs.get(0x0C, 0) | (1 << ch)
