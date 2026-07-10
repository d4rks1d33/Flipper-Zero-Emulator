# STM32WB55 IPCC (Inter-Processor Communication Controller) model.
#
# The IPCC is the mailbox between CPU1 (the Cortex-M4 running the app firmware,
# which we emulate) and CPU2 (the Cortex-M0+ running the closed BLE/FUS stack,
# which we do NOT emulate). Registers (base 0x58000C00):
#   0x00 C1CR      0x04 C1MR      0x08 C1SCR     0x0C C1TOC2SR
#   0x10 C2CR      0x14 C2MR      0x18 C2SCR     0x1C C2TOC1SR
#
# HW_IPCC_SYS_SendCmd() (hw_ipcc.c) does:
#     LL_C1_IPCC_SetFlag_CHx(SYS_CMD_RSP=CH2)   -> WRITE C1SCR = (ch << 16)
#     while(LL_C1_IPCC_IsActiveFlag_CHx(CH2))   -> poll C1TOC2SR bit(ch)
#         furi_check(!timer_expired, "HW_IPCC_SYS_SendCmd timeout")
#     HW_IPCC_SYS_CmdEvtHandler()
#
# LL_C1_IPCC_SetFlag_CHx writes (Channel << IPCC_C1SCR_CH1S_Pos=16) to C1SCR,
# which sets the corresponding CHxF bit in C1TOC2SR. On real silicon CPU2 reads
# the command, processes it, and clears CHxF, which releases the poll loop.
#
# Since CPU2 is absent, we simulate an INSTANT Core2 ack: whenever CPU1 sets a
# transmit-channel flag via C1SCR, we DO NOT latch it into C1TOC2SR (or we clear
# it immediately). LL_C1_IPCC_IsActiveFlag_CHx then reads 0 right away and the
# firmware's send-command busy-wait exits without the 33s timeout + furi_crash.
# This lets stock firmware run gap_extra_beacon_init() and other SYS/MM commands
# without a real BLE coprocessor.
#
# C2TOC1SR stays 0 (CPU2 never posts an event to us), so CPU1's receive path is
# simply never triggered - the BLE stack times out gracefully elsewhere
# (ble_glue_wait_for_c2_start -> "C2 startup failed", non-fatal).

_C1CR = 0x00
_C1MR = 0x04
_C1SCR = 0x08
_C1TOC2SR = 0x0C
_C2TOC1SR = 0x1C
_CH1S_POS = 16  # CHxS (set) bits start at bit 16 in C1SCR
_CH1C_POS = 0   # CHxC (clear) bits start at bit 0 in C1SCR

if request.IsInit:
    regs = {
        _C1CR: 0,
        _C1MR: 0x3F3F,      # all channels masked
        _C1SCR: 0,
        _C1TOC2SR: 0,       # no CPU1->CPU2 flag pending
        _C2TOC1SR: 0,       # CPU2 never posts to CPU1
    }

elif request.IsRead:
    # C1TOC2SR always reads 0: any command CPU1 "sent" to CPU2 is considered
    # instantly acknowledged, so IsActiveFlag_CHx() returns false and send-cmd
    # busy-waits exit immediately.
    off = request.Offset
    if off == _C1TOC2SR:
        request.Value = 0
    elif off == _C2TOC1SR:
        request.Value = 0
    else:
        request.Value = regs.get(off, 0)

elif request.IsWrite:
    off = request.Offset
    if off == _C1SCR:
        # Writing CHxS/CHxC just pulses the mailbox; we treat the channel as
        # instantly serviced by the (absent) Core2, so C1TOC2SR stays 0.
        # Nothing to latch.
        pass
    else:
        regs[off] = request.Value
