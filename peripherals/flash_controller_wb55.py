# STM32WB55 Flash controller registers stub (Renode request-based API).
# FLASH_REG base = 0x5800_4000
#   ACR=0x00, KEYR=0x08, SR=0x10, CR=0x14, OPTR=0x20, IPCCBR=0x3C
# Handles the unlock key sequence and always reports "not busy".
KEY1 = 0x45670123
KEY2 = 0xCDEF89AB

if request.IsInit:
    regs = {
        0x00: 0x00000000,   # ACR
        0x10: 0,            # SR (not busy)
        0x14: (1 << 31),    # CR (LOCK=1)
        0x20: 0xFFEFF8AA,   # OPTR
        0x3C: 0,            # IPCCBR
        0x80: 0,            # SFR
        0x84: 0,            # SRRVR
    }
    unlock_state = 0

elif request.IsRead:
    off = request.Offset
    if off == 0x10:   # SR: always not busy
        request.Value = 0
    else:
        request.Value = regs.get(off, 0)

elif request.IsWrite:
    off = request.Offset
    val = request.Value
    if off == 0x08:  # KEYR
        if unlock_state == 0 and val == KEY1:
            unlock_state = 1
        elif unlock_state == 1 and val == KEY2:
            unlock_state = 2
            regs[0x14] = regs.get(0x14, 0) & ~(1 << 31)  # clear LOCK
        else:
            unlock_state = 0
    elif off == 0x14:  # CR
        if val & (1 << 31):
            unlock_state = 0
            regs[0x14] = val | (1 << 31)
        else:
            regs[0x14] = val
    else:
        regs[off] = val
