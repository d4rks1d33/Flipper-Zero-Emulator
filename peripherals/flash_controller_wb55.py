# =============================================================================
# STM32WB55 FLASH controller model  (Renode request-based Python.PythonPeripheral)
# Base address: 0x5800_4000   (mapped by stm32wb55_flipper.repl.in)
#
# PURPOSE
# -------
# Make the STOCK firmware's updater (updater.bin) able to erase + program the
# internal flash at 0x0800_0000, and make option-byte / secure-flash reads
# return sane values. This models ONLY the FLASH controller registers; the
# actual 1 MB flash content lives in the "flash" Memory.MappedMemory at
# 0x0800_0000 (see the .repl).
#
# KEY HARDWARE FACT (STM32WB55)
# -----------------------------
# Flash is *programmed* by the CPU writing the data words DIRECTLY to the
# 0x0800_0000+ addresses while CR.PG (or CR.FSTPG) is set. There is NO data
# register in the FLASH controller. Those direct writes hit the MappedMemory
# and Renode stores them natively -- we do NOT have to do anything for program.
# The controller must only:
#   * accept the KEYR unlock sequence and clear CR.LOCK,
#   * report SR as "idle, no error, EOP after an op" so the driver's poll loops
#     and furi_check()s pass,
#   * ERASE a page (fill 4 KB with 0xFF in the 0x0800_0000 memory) on
#     CR.PER + CR.STRT, because a real erase clears the page and the firmware
#     relies on that before programming.
#
# CROSS-REGION MEMORY ACCESS (the tricky part)
# --------------------------------------------
# A request-based PythonPeripheral does NOT get `machine`, `sysbus`, or
# `self.Machine` in its scope (verified empirically on Renode 1.16.1 -- those
# raise NameError / AttributeError). What IS available on `self` is the C#
# peripheral object, which exposes `GetMachine()`. From there:
#       self.GetMachine().SystemBus.WriteBytes(byteArray, address)
# writes straight into the flash MappedMemory. Verified: the bytes are
# read-backable at 0x0800_0000 afterwards, and a 4 KB WriteBytes is fast.
#
# ----- furi_hal_flash.c register/sequence spec (verified against source) -----
#
# Register offsets from base 0x5800_4000:
#   ACR    = 0x00   OPTR   = 0x20
#   KEYR   = 0x08   ...
#   OPTKEYR= 0x0C   IPCCBR = 0x3C
#   SR     = 0x10   SFR    = 0x80  (secure flash start, SFSA[7:0])
#   CR     = 0x14   SRRVR  = 0x84
#   ECCR   = 0x18
#
# SR bits:  EOP=0 OPERR=1 PROGERR=3 WRPERR=4 PGAERR=5 SIZERR=6 PGSERR=7
#           MISERR=8 FASTERR=9 RDERR=14 OPTVERR=15 BSY=16 CFGBSY=18 PESD=19
# CR bits:  PG=0 PER=1 MER=2 PNB=[10:3] STRT=16 OPTSTRT=17 FSTPG=18
#           OBL_LAUNCH=27 OPTLOCK=30 LOCK=31
#
# UNLOCK (furi_hal_flash_unlock):
#   furi_check(CR.LOCK != 0)                 -> LOCK must start set (reset=locked)
#   write KEYR = 0x45670123
#   write KEYR = 0xCDEF89AB                   -> hardware clears CR.LOCK
#   furi_check(CR.LOCK == 0)
#
# WAIT_LAST_OPERATION (furi_hal_flash_wait_last_operation):
#   while(SR.BSY) ...                         -> must read 0 (we never stay busy)
#   error = SR; if(SR.EOP) clear EOP
#   error &= ALL_ERROR_BITS; furi_check(error == 0)   -> no error bits ever
#   clear error bits
#   while(SR.CFGBSY) ...                      -> must read 0
#
# ERASE (furi_hal_flash_erase):
#   furi_check(SR == 0)                       -> SR must be exactly 0 at start
#   wait_last_operation
#   MODIFY_REG(CR, PNB, (page<<PNB_Pos)|PER|STRT)   -> triggers the erase
#   wait_last_operation
#   CLEAR_BIT(CR, PER|PNB)
#   => On the write that sets PER+STRT we fill page*4096 .. +4096 with 0xFF.
#
# PROGRAM (furi_hal_flash_write_dword / _program_page):
#   furi_check(SR == 0)
#   SET_BIT(CR, PG)   [or FSTPG for 512-byte fast blocks]
#   *(u32*)addr = lo ; *(u32*)(addr+4) = hi   -> these land in MappedMemory
#   wait_last_operation
#   CLEAR_BIT(CR, PG)
#   => We do nothing to the data on program; the direct writes already stored it.
#
# =============================================================================
# WHY SR ALWAYS READS 0  (the fix)
# =============================================================================
# The previous model latched SR.EOP on any CR write that set PG/FSTPG/PER+STRT.
# That was fragile and WRONG because SET_BIT/CLEAR_BIT/MODIFY_REG are all
# read-modify-writes, so the driver issues *extra* CR writes at times that don't
# line up with a following wait_last_operation. Concretely, in
# furi_hal_flash_program_page the driver does SET_BIT(CR, PG) at line 425
# UNCONDITIONALLY, and when the payload is 512-byte aligned neither dword loop
# runs, so NO wait_last_operation follows before the next program_page's
# furi_hal_flash_erase() executes furi_check(FLASH->SR == 0) at line 300. A
# latched EOP therefore survived into the next op and blew up either the
# SR==0 check (line 300) or wait_last_operation (line 303).
#
# The driver NEVER *requires* EOP==1. Grep of furi_hal_flash.c shows EOP is only
# ever read to *defensively clear* it inside wait_last_operation
# (if(SR & EOP) CLEAR_BIT(SR, EOP)); there is no code path that waits for EOP to
# become 1 or that fails if EOP is 0. What the driver DOES require:
#     * SR.BSY    always reads 0   (poll loops exit immediately)
#     * SR.CFGBSY always reads 0   (poll loops exit immediately)
#     * error bits (FURI_HAL_FLASH_SR_ERRORS) always 0  (furi_check(error==0))
#     * FLASH->SR == 0 at every furi_check(FLASH->SR == 0) between ops
# All four are satisfied, robustly and unconditionally, by making SR always
# read 0. wait_last_operation then returns true immediately every time, and
# every furi_check(SR==0) passes. No latching, no ordering hazards.
#
# OPTION BYTES (furi_hal_flash_ob_*): OPTKEYR unlock clears CR.OPTLOCK; reads of
#   OPTR/etc. return the reset-ish values below. The updater does not rewrite OB.
#
# SFR / SFSA (furi_hal_flash_get_free_end_address):
#   sfsa = (SFR & 0xFF)
#   free_end = sfsa*4096 + 0x08000000
#   Radio stack (per update manifest) starts at 0x080D7000, so:
#       sfsa = (0x080D7000 - 0x08000000) / 0x1000 = 0xD7
#   => SFR = 0x000000D7  -> free_end = 0x080D7000. Correct.
# =============================================================================

# --- register offsets ---
ACR     = 0x00
KEYR    = 0x08
OPTKEYR = 0x0C
SR      = 0x10
CR      = 0x14
ECCR    = 0x18
OPTR    = 0x20
IPCCBR  = 0x3C
SFR     = 0x80
SRRVR   = 0x84

# --- keys ---
KEY1     = 0x45670123
KEY2     = 0xCDEF89AB
OPT_KEY1 = 0x08192A3B
OPT_KEY2 = 0x4C5D6E7F

# --- SR bits ---
SR_EOP     = 1 << 0
SR_BSY     = 1 << 16
SR_CFGBSY  = 1 << 18
SR_ERRORS  = ((1 << 1) | (1 << 3) | (1 << 4) | (1 << 5) | (1 << 6) |
              (1 << 7) | (1 << 8) | (1 << 9) | (1 << 14) | (1 << 15))

# --- CR bits ---
CR_PG         = 1 << 0
CR_PER        = 1 << 1
CR_MER        = 1 << 2
CR_PNB_POS    = 3
CR_PNB_MSK    = 0xFF << CR_PNB_POS     # PNB[10:3], 8 bits (page 0..255)
CR_STRT       = 1 << 16
CR_OPTSTRT    = 1 << 17
CR_FSTPG      = 1 << 18
CR_OBL_LAUNCH = 1 << 27
CR_OPTLOCK    = 1 << 30
CR_LOCK       = 1 << 31

# --- flash memory geometry ---
FLASH_BASE = 0x08000000
PAGE_SIZE  = 4096

# SFSA = 0xD7  -> free flash ends at 0x080D7000 (radio stack start)
SFR_RESET = 0x000000D7


def _fill_page_ff(page):
    # Erase a 4 KB page: fill it with 0xFF in the real flash MappedMemory.
    # A request-based PythonPeripheral has no `machine`/`sysbus`/`self.Machine`,
    # but `self.GetMachine().SystemBus` is reachable and can WriteBytes across
    # regions (verified on Renode 1.16.1).
    try:
        import System
        addr = FLASH_BASE + page * PAGE_SIZE
        blank = System.Array[System.Byte]([0xFF] * PAGE_SIZE)
        self.GetMachine().SystemBus.WriteBytes(blank, addr)
        self.NoisyLog("FLASH: erased page %d @ 0x%08X" % (page, addr))
    except Exception as e:
        # If cross-region access ever fails, don't hang the updater; just log.
        self.WarningLog("FLASH: page %d erase failed: %s" % (page, str(e)))


if request.IsInit:
    # Persistent register file. LOCK/OPTLOCK set at reset (flash starts locked).
    regs = {
        ACR:     0x00000000,
        SR:      0x00000000,               # idle, no errors, no EOP
        CR:      CR_LOCK | CR_OPTLOCK,      # locked (reset state)
        ECCR:    0x00000000,
        OPTR:    0xFFEFF8AA,               # typical WB55 user option bytes
        IPCCBR:  0x00000000,
        SFR:     SFR_RESET,                # SFSA = 0xD7
        SRRVR:   0x00000000,
    }
    unlock_state = 0        # KEYR sequence progress: 0 -> saw KEY1 -> unlocked
    opt_unlock_state = 0    # OPTKEYR sequence progress

elif request.IsRead:
    off = request.Offset

    if off == SR:
        # SR ALWAYS reads 0. See the "WHY SR ALWAYS READS 0" note above.
        #   BSY=0, CFGBSY=0  -> every wait_last_operation() poll loop exits at once
        #   errors=0         -> furi_check(error == 0) always passes
        #   EOP=0, whole SR=0 -> every furi_check(FLASH->SR == 0) passes, and the
        #                        driver's defensive `if(SR & EOP)` is simply a no-op
        # This is unconditionally correct because the driver never requires EOP==1
        # and never spins waiting for any SR bit to become set.
        request.Value = 0

    else:
        request.Value = regs.get(off, 0)

elif request.IsWrite:
    off = request.Offset
    val = request.Value & 0xFFFFFFFF

    if off == KEYR:
        # Flash unlock key sequence: KEY1 then KEY2 clears CR.LOCK.
        if unlock_state == 0 and val == KEY1:
            unlock_state = 1
        elif unlock_state == 1 and val == KEY2:
            unlock_state = 0
            regs[CR] = regs[CR] & ~CR_LOCK      # unlock: clear LOCK
        else:
            unlock_state = 0                     # wrong sequence -> restart

    elif off == OPTKEYR:
        # Option-byte unlock key sequence: clears CR.OPTLOCK.
        if opt_unlock_state == 0 and val == OPT_KEY1:
            opt_unlock_state = 1
        elif opt_unlock_state == 1 and val == OPT_KEY2:
            opt_unlock_state = 0
            regs[CR] = regs[CR] & ~CR_OPTLOCK
        else:
            opt_unlock_state = 0

    elif off == SR:
        # SR is stateless in this model (it always reads 0). The driver only ever
        # writes SR to clear EOP/error bits (WRITE_REG in init, CLEAR_BIT in
        # wait_last_operation); since there is nothing to clear, we ignore writes
        # and keep SR pinned to 0.
        regs[SR] = 0

    elif off == CR:
        # Handle LOCK re-lock explicitly.
        if val & CR_LOCK:
            unlock_state = 0
            regs[CR] = val | CR_LOCK
        else:
            regs[CR] = val

        # Erase: PER + STRT triggers a page erase of page = PNB.
        # This is the ONLY side effect we perform on a CR write: physically blank
        # the page in the 0x0800_0000 MappedMemory. We do NOT touch SR here --
        # SR always reads 0, so the following wait_last_operation() succeeds
        # immediately and every furi_check(SR==0) between ops passes. STRT is a
        # self-clearing "start" bit on real hardware, so we clear it here too;
        # this keeps CR readbacks (MODIFY_REG/CLEAR_BIT are read-modify-writes)
        # from re-triggering the erase on a later CR write.
        if (val & CR_PER) and (val & CR_STRT):
            page = (val & CR_PNB_MSK) >> CR_PNB_POS
            _fill_page_ff(page)
            regs[CR] = regs[CR] & ~CR_STRT

        # Program (PG/FSTPG): the data is written directly to 0x0800_0000 by the
        # CPU and Renode stores it natively -- nothing for us to do. We must NOT
        # latch any SR bit: SR stays 0 so the next furi_check(SR==0) passes.
        # OPTSTRT/OBL_LAUNCH (option bytes): likewise nothing to do; SR stays 0.
        # STRT/OPTSTRT are self-clearing.
        if val & (CR_STRT | CR_OPTSTRT):
            regs[CR] = regs[CR] & ~(CR_STRT | CR_OPTSTRT)

    else:
        regs[off] = val
