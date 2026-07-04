# STM32WB55 ADC1 stub (Renode request-based API).
if request.IsInit:
    regs = {}
elif request.IsRead:
    off = request.Offset
    if off == 0x00:   # ISR
        cr = regs.get(0x08, 0)
        isr = regs.get(0x00, 0)
        if cr & (1 << 0):    isr |= (1 << 0)   # ADRDY
        if cr & (1 << 31):                     # ADCAL done instantly
            regs[0x08] = cr & ~(1 << 31)
            isr |= (1 << 11)
        request.Value = isr
    elif off == 0x40:  # DR
        request.Value = 2048
    else:
        request.Value = regs.get(off, 0)
elif request.IsWrite:
    regs[request.Offset] = request.Value
