# STM32WB55 HSEM (Hardware Semaphore) model  (Renode request-based Python).
# Base 0x58001400.
#
# The HSEM is a 32-semaphore hardware mutex used to arbitrate shared resources
# between CPU1 (Cortex-M4, the app firmware we emulate) and CPU2 (Cortex-M0+,
# the closed BLE/FUS coprocessor we do NOT emulate). The firmware uses it e.g.
# to coordinate flash access with Core2 during an OTA update.
#
# Register map (RM0434, per-semaphore n = 0..31):
#   R[n]   @ 0x00 + n*4   : semaphore register. bit31=LOCK, bits[11:8]=COREID,
#                           bits[7:0]=PROCID. Read = current state; write with
#                           LOCK=0 releases it (2-step protocol).
#   RLR[n] @ 0x80 + n*4   : read-lock register. A READ attempts a 1-step lock and
#                           returns the resulting state (LOCK|COREID|PROCID).
#   C1IER  @ 0x100 ... etc : interrupt/clear/status/CR/KEYR (not needed here).
#
# LL driver semantics we must satisfy (stm32wbxx_ll_hsem.h), verified:
#   LL_HSEM_1StepLock(sem):        returns (RLR[sem] != (LOCK|COREID)) ? 1 : 0
#                                  -> 0 means "we got the lock".
#   LL_HSEM_IsSemaphoreLocked(sem):returns (R[sem] & LOCK) ? 1 : 0
#   LL_HSEM_ReleaseLock(sem,proc): writes R[sem] = (COREID | proc)  (LOCK=0)
#   LL_HSEM_GetCoreId(sem):        returns R[sem] & COREID_Msk
#
# COREID for CPU1 is 0x4 (HSEM_CR_COREID_CPU1), at bit position 8 -> 0x00000400.
# LOCK is bit 31 -> 0x80000000.
#
# WHY A FAITHFUL MODEL IS NEEDED (the OTA flash bug):
# The old stub returned LOCK|COREID for EVERY register read, i.e. it reported
# ALL semaphores as permanently locked. That broke furi_hal_flash_begin_with_core2()
# (furi_hal_flash.c:140-181): it polls
#     LL_HSEM_IsSemaphoreLocked(CFG_HW_BLOCK_FLASH_REQ_BY_CPU1_SEMID=6)
# expecting it to be FREE (Core2 not blocking flash). With the old stub it read
# "locked" forever, so the loop spun until furi_check() fired a furi_crash at the
# 3 s timeout -> reset -> the OTA updater re-ran from scratch (boot loop) and the
# firmware was never flashed.
#
# This model implements the real per-semaphore lock state:
#   * a 1-step lock (RLR[n] read) GRANTS the semaphore to CPU1 and records it,
#   * R[n] reflects the real state: semaphores CPU1 holds read LOCK|COREID_CPU1,
#     everything else (in particular the CPU2-owned block-flash semaphore 6,
#     which no Core2 ever takes here) reads 0 = FREE,
#   * writing R[n] with LOCK cleared releases it.
# So LL_HSEM_1StepLock succeeds, LL_HSEM_IsSemaphoreLocked(6) reports FREE, and
# the flash begin sequence completes -> the updater actually writes the firmware.

_LOCK = 0x80000000
_COREID_CPU1 = 0x00000400   # COREID field (bits[11:8]) = 4
_PROCID_MSK = 0x000000FF
_NUM_SEM = 32

if request.IsInit:
    # Per-semaphore lock state. 0 = free; otherwise (LOCK|COREID|PROCID).
    # scope-global so it survives Reset() within a process.
    try:
        _sem
    except NameError:
        _sem = [0] * _NUM_SEM
    regs = {}

elif request.IsRead:
    off = request.Offset

    if 0x00 <= off < 0x80:
        # R[n]: return the current lock state of semaphore n.
        n = off >> 2
        request.Value = _sem[n] if n < _NUM_SEM else 0

    elif 0x80 <= off < 0x100:
        # RLR[n]: 1-step lock attempt by CPU1. If free, grant it to CPU1
        # (PROCID 0). If already held by CPU1, it stays granted. If (hypo-
        # thetically) held by another core, return that state so 1StepLock
        # reports failure. Here nothing else ever holds a semaphore, so this
        # always grants.
        n = (off - 0x80) >> 2
        if n < _NUM_SEM:
            cur = _sem[n]
            if cur == 0 or (cur & _COREID_CPU1) == _COREID_CPU1:
                _sem[n] = _LOCK | _COREID_CPU1  # PROCID 0
            request.Value = _sem[n]
        else:
            request.Value = _LOCK | _COREID_CPU1

    else:
        # Interrupt/status/CR/KEYR: return stored (default 0).
        request.Value = regs.get(off, 0)

elif request.IsWrite:
    off = request.Offset
    val = request.Value & 0xFFFFFFFF

    if 0x00 <= off < 0x80:
        # R[n] write: 2-step lock or release.
        #   - LOCK set  -> lock request with COREID|PROCID (only valid if the
        #                  written COREID matches this core).
        #   - LOCK clear-> release (must match holder COREID to succeed; we
        #                  accept CPU1 releases).
        n = off >> 2
        if n < _NUM_SEM:
            if val & _LOCK:
                _sem[n] = _LOCK | (val & (0xF00 | _PROCID_MSK))
            else:
                # Release: clear if owned by CPU1 (or unconditionally, harmless).
                _sem[n] = 0
    else:
        regs[off] = val
