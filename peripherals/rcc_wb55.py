# STM32WB55 RCC stub for Renode PythonPeripheral (request-based API).
# Returns "ready" flags for all oscillators and persists ENR/RSTR registers
# so furi_hal_bus's furi_check() readbacks succeed.
#
# Renode PythonPeripheral protocol: this script runs on every access.
#   request.IsInit  - True during initialization
#   request.IsRead  - True on read (set request.Value to return)
#   request.IsWrite - True on write (read request.Value)
#   request.Offset  - register offset
#   request.Value   - value (read: set it; write: contains written value)
# A persistent global dict `regs` survives across calls.

if request.IsInit:
    regs = {
        0x00: (1 << 0) | (1 << 1),  # CR: MSION + MSIRDY
        0x08: 0,                    # CFGR
        0x0C: 0x00001000,           # PLLCFGR
        0x10: 0x00001000,           # PLLSAI1CFGR
        0x90: 0,                    # BDCR
        0x94: 0,                    # CSR
        0x98: 0,                    # CRRCR
        0x9C: 0,                    # HSECR
        0x1C0: (1 << 8),            # SMPSCR (SMPSSRDY)
        0x108: 0,                   # EXTCFGR
    }

elif request.IsRead:
    off = request.Offset
    val = regs.get(off, 0)

    if off == 0x00:  # CR
        # Each "ready" flag must MIRROR its "on" bit: set when the oscillator is
        # enabled AND cleared when it is disabled. A previous version only set
        # the flag (sticky), which left MSIRDY=1 after LL_RCC_MSI_Disable() and
        # hung furi_hal_clock_init in `while(MSIRDY != 0)`.
        cr = regs.get(0x00, 0)
        cr = (cr | (1 << 1))  if (cr & (1 << 0))  else (cr & ~(1 << 1))   # MSIRDY
        cr = (cr | (1 << 10)) if (cr & (1 << 8))  else (cr & ~(1 << 10))  # HSIRDY
        cr = (cr | (1 << 17)) if (cr & (1 << 16)) else (cr & ~(1 << 17))  # HSERDY
        cr = (cr | (1 << 25)) if (cr & (1 << 24)) else (cr & ~(1 << 25))  # PLLRDY
        cr = (cr | (1 << 27)) if (cr & (1 << 26)) else (cr & ~(1 << 27))  # PLLSAI1RDY
        regs[0x00] = cr
        val = cr

    elif off == 0x08:  # CFGR: SWS mirrors SW
        cfgr = regs.get(0x08, 0)
        sw = cfgr & 0x3
        cfgr = (cfgr & ~0xC) | (sw << 2)
        regs[0x08] = cfgr
        val = cfgr

    elif off == 0x90:  # BDCR
        bdcr = regs.get(0x90, 0)
        if bdcr & (1 << 0): bdcr |= (1 << 1)  # LSERDY
        regs[0x90] = bdcr
        val = bdcr

    elif off == 0x94:  # CSR
        csr = regs.get(0x94, 0)
        if csr & (1 << 0): csr |= (1 << 1)  # LSI1RDY
        if csr & (1 << 2): csr |= (1 << 3)  # LSI2RDY
        regs[0x94] = csr
        val = csr

    elif off == 0x1C0:  # SMPSCR
        smpscr = regs.get(0x1C0, 0)
        sel = smpscr & 0x3
        smpscr = (smpscr & ~0x30) | (sel << 4) | (1 << 8)
        val = smpscr

    elif off == 0x98:  # CRRCR (HSI48)
        crrcr = regs.get(0x98, 0)
        if crrcr & (1 << 0): crrcr |= (1 << 1)  # HSI48RDY
        val = crrcr

    request.Value = val

elif request.IsWrite:
    off = request.Offset
    regs[off] = request.Value

    # --- APB1RSTR1 (0x38): peripheral reset register ---
    # The Flipper tickless-idle path stops LPTIM1 with furi_hal_bus_reset(LPTIM1)
    # = a pulse on RCC APB1RSTR1 bit 31 (LPTIM1RST). On real silicon that reset
    # clears the LPTIM peripheral, dropping its (level) interrupt line. Our RCC is
    # a stub that just stores registers, so the reset never reached the LPTIM1
    # model: its CMPM/ARRM flag stayed set and its NVIC line stayed ASSERTED. The
    # firmware sleeps with IRQs masked (PRIMASK=1) and has NO ISR registered for
    # LPTIM1 (it clears the flag inline); when it re-enables IRQs the still-
    # asserted LPTIM1 line fires LPTIM1_IRQHandler -> furi_hal_interrupt_call with
    # no handler -> furi_check crash + reboot. (This only surfaced once the EXTI
    # PR1 fix let the system actually reach tickless-idle.)
    #
    # Faithfully emulate the peripheral reset (FIX B): when LPTIM1RST is
    # asserted, fully STOP the LPTIM1 model, not just clear its flags.
    #
    # Why clearing ICR alone was not enough: the Renode STM32L0_LpTimer model
    # runs an internal `compareTimer` (one-shot LimitTimer). Writing ICR clears
    # the latched CMPM/ARRM status flags and calls UpdateInterrupts() (lowering
    # IRQ), but the compareTimer is still ENABLED. It keeps counting and, on the
    # next LimitReached, re-sets compareMatchInterruptStatus and re-asserts the
    # IRQ line -> the [CRASH][ISR LPTIM1] reappears on the next idle cycle.
    #
    # In the model, writing CR (0x10) with ENABLE=0 executes the "Disabling
    # timer" branch: this.Enabled=false AND compareTimer.Enabled=false, which
    # halts both timers so they can no longer re-assert the interrupt. That CR
    # write does NOT itself call UpdateInterrupts(), so we then write ICR=0x1F
    # to clear all latched status flags -- and ICR has a WithWriteCallback that
    # calls UpdateInterrupts(), which drops the IRQ line to false. Finally we
    # write IER=0 to disable all interrupt-enable bits (also keeps UpdateInterrupts
    # producing false even if a flag were somehow set again).
    #
    # Order matters: CR=0 (stop timers) -> ICR=0x1F (clear flags + lower IRQ)
    # -> IER=0 (mask interrupts).
    if off == 0x38 and (request.Value & (1 << 31)):
        try:
            import System
            sysbus = self.GetMachine().SystemBus
            # 1. CR (0x40007C10) = 0: ENABLE=0 -> stop LPTIM + its compareTimer.
            sysbus.WriteDoubleWord(
                System.UInt64(0x40007C10), System.UInt32(0x00000000))
            # 2. ICR (0x40007C04) = 0x1F: clear CMPMCF|ARRMCF|EXTTRIGCF|CMPOKCF|
            #    ARROKCF; its write callback runs UpdateInterrupts() -> IRQ low.
            sysbus.WriteDoubleWord(
                System.UInt64(0x40007C04), System.UInt32(0x0000001F))
            # 3. IER (0x40007C08) = 0: disable all interrupt-enable bits.
            sysbus.WriteDoubleWord(
                System.UInt64(0x40007C08), System.UInt32(0x00000000))
        except Exception as e:
            self.WarningLog("RCC: LPTIM1 reset stop failed: %s" % str(e))
