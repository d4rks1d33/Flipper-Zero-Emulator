# STM32WB55 SYSCFG stub (Renode request-based API).
# Covers SYSCFG, VREFBUF (0x30), COMP (0x200) within the 0x400 window.
# MEMRMP MEM_MODE bits control memory remap (updater sets SRAM remap).
if request.IsInit:
    regs = {0x00: 0}
elif request.IsRead:
    request.Value = regs.get(request.Offset, 0)
elif request.IsWrite:
    regs[request.Offset] = request.Value
