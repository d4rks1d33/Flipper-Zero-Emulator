//
// STM32WB55 SYSCFG for the Flipper Zero emulator.
//
// In addition to persisting SYSCFG / VREFBUF / COMP registers within the 0x400
// window, this model decodes the SYSCFG_EXTICR[1..4] registers (offsets
// 0x08,0x0C,0x10,0x14) and programs the EXTI line-source multiplexer. On real
// silicon EXTICR selects which GPIO port drives each EXTI line (0=PA,1=PB,2=PC,
// 3=PD,4=PE,7=PH); without it, the platform wires every port's pin N to EXTI
// line N and they fight over the line, which lost the button press edge.
// Forwarding EXTICR to the EXTI makes the mux behave like hardware so only the
// selected port (e.g. PH for the PH3 OK button) drives its line.
//
// Each EXTICRx holds four 3-bit fields (one per EXTI line): line = regIndex*4+i,
// field at bit 4*i.
//
// ── SYSCFG_MEMRMP (offset 0x00, MEM_MODE bits [2:0]) ────────────────────────
// The Flipper self-updater (targets/f7/src/update.c flipper_update_load_stage)
// does:  memmove(SRAM1_BASE, updater, size);      // copy updater.bin to SRAM1
//        LL_SYSCFG_SetRemapMemory(LL_SYSCFG_REMAP_SRAM);  // MEM_MODE = 0b011
//        furi_hal_switch(0x0);   // ldr msp,[0x0]; ldr r3,[0x4]; mov pc,r3
// On real STM32WB silicon MEM_MODE=0b011 aliases SRAM1 (0x20000000) at address
// 0x00000000 so the copied updater is reachable at 0x0, and furi_hal_switch then
// reads the updater's MSP/reset-vector from [0x0]/[0x4] and jumps into it.
//
// ── Why the naive "re-register memory in the write callback" approach FAILS ──
// You CANNOT simply Unregister flash / Register sram1 at 0x0 synchronously from
// inside the MEMRMP WriteDoubleWord callback and expect the guest's own
// `ldr r3,[0x0]; mov pc,r3` to then see SRAM. At that moment the CPU is in the
// middle of executing the translation block that contains furi_hal_switch; the
// bus-access path and the in-flight instruction stream still resolve 0x0 to the
// OLD (flash) mapping, so [0x0]/[0x4] read the MAIN firmware vectors and the CPU
// jumps straight back into the main firmware — looping forever (the symptom the
// user observed: furi_hal_switch entered ~36 times, PC never reaching SRAM).
//
// The correct, Renode-sanctioned pattern (identical to the upstream
// RenesasDA14_ClockGenerationController "remap eflash to 0x0 and restart" boot
// path — see renode-infrastructure .../Miscellaneous/RenesasDA14_ClockGeneration
// Controller.cs, SystemControl.SW_RESET write) is:
//   1. Grab the current CPU (machine.SystemBus.TryGetCurrentCPU).
//   2. Halt it (IsHalted = true) so it stops executing the doomed block.
//   3. Defer the actual bus remap to machine.LocalTimeSource
//      .ExecuteInNearestSyncedState(...) — bus registration must not mutate the
//      map from inside the running block; the synced callback runs when the CPU
//      is at a safe sync point.
//   4. In that callback: UnregisterFromAddress(0x0) (drop flash's 0x0 alias),
//      Register(sram1, new BusPointRegistration(0x0)) so SRAM1 now answers 0x0.
//   5. Read the updater vectors that furi_hal_switch(0x0) *would* have read —
//      SP = [0x0], PC = [0x4] — and set them on the CPU directly
//      (ICPUWithRegisters.PC / SetRegister(SP=13, ...)). This is the exact
//      "emulate a reset by loading MSP and jumping to the reset ISR" that
//      furi_hal_switch performs in asm; we perform it on the emulator side so
//      the guest picks up execution in SRAM1 regardless of what the (now
//      superseded) in-flight block would have done.
//   6. Unhalt (IsHalted = false).
// The updater's own Reset_Handler sets SCB->VTOR = SRAM1_BASE, so we only need
// to hand it a correct MSP+PC; VTOR is the updater's responsibility.
//
// We register sram1 at 0x0 as an ADDITIONAL BusPointRegistration; its existing
// 0x20000000 point is left untouched so the memmove'd updater content (the same
// backing store) stays visible at both addresses.
//
using System.Linq;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.CPU;
using Antmicro.Renode.Peripherals.Memory;
using Antmicro.Renode.Peripherals.IRQControllers;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class SYSCFG_WB55 : IDoubleWordPeripheral, IKnownSize
    {
        // flash and sram1 are the MappedMemory peripherals declared in the .repl;
        // they are wired in through named properties (see the platform:
        //   syscfg: Miscellaneous.SYSCFG_WB55 @ sysbus 0x40010000
        //       exti: exti
        //       flash: flash
        //       sram1: sram1
        // ) exactly like the exti reference. This mirrors how upstream's
        // RenesasDA14_ClockGenerationController receives `rom`/`eflashDataText`.
        public SYSCFG_WB55(IMachine machine, STM32WB55_EXTI exti, MappedMemory flash, MappedMemory sram1)
        {
            this.machine = machine;
            this.exti = exti;
            this.flash = flash;
            this.sram1 = sram1;
            registers = new DoubleWordRegisterCollection(this);
            DefineRegisters();
            Reset();
        }

        public uint ReadDoubleWord(long offset)
        {
            return registers.Read(offset);
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            registers.Write(offset, value);
        }

        public void Reset()
        {
            registers.Reset();
            // MEM_MODE reset default is 0 (flash at 0x0). We only track the value.
            memMode = MemModeMainFlash;

            // ── Restore flash at 0x0 on a *runtime* reset (the OTA reboot) ───────
            // The RAM updater aliases SRAM1 at 0x0 (MEM_MODE=3) before jumping to
            // itself. A firmware-triggered reset (furi_hal_power_reset ->
            // SYSRESETREQ) resets the whole machine and runs this Reset(), but the
            // SRAM1-at-0x0 alias is a bus-registration side effect that Renode does
            // NOT undo automatically. If left in place, the next boot's Cortex-M
            // vector fetch from [0x0]/[0x4] hits stale updater bytes in SRAM1
            // instead of the freshly-flashed firmware, faulting instantly and
            // bootlooping. So if SRAM1 is currently aliased at 0x0, drop it and
            // re-alias flash there (matching the MEM_MODE=0 reset default).
            //
            // This is a no-op at construction-time Reset(): SRAM1 is not yet mapped
            // at 0x0 then (the platform maps flash there), so the guard below skips
            // the bus manipulation and only flash-mapping that actually needs
            // undoing (post-updater) is touched.
            // NOTE: we no longer alias SRAM1 at 0x0 during the updater boot (see
            // RemapToSramAndBoot), so there is nothing to undo here — `flash` stays
            // mapped at both 0x0 and 0x08000000 for the whole run and its disk
            // persistence is never disturbed. This block is kept as a defensive
            // no-op in case some path ever leaves SRAM1 aliased at 0x0.
            if(machine != null && sram1 != null && flash != null)
            {
                var sysbus = machine.SystemBus;
                if(sysbus.WhatPeripheralIsAt(AliasBase) == sram1)
                {
                    this.Log(LogLevel.Warning,
                        "SYSCFG: SRAM1 unexpectedly aliased at 0x0 on reset; leaving bus untouched to avoid disturbing flash persistence.");
                }
            }

            // Reset default of EXTICR is all-zero => every line sourced from PA.
            for(var line = 0; line < 16; line++)
            {
                exti.SetExtiSource(line, 0);
            }
        }

        public long Size => 0x400;

        private void DefineRegisters()
        {
            // SYSCFG_MEMRMP (offset 0x00). MEM_MODE = bits [2:0]. Writing 0b011
            // (LL_SYSCFG_REMAP_SRAM) aliases SRAM1 at 0x0 and boots the updater;
            // 0b000 is the flash reset default. Bit 8 (FB_MODE) is stored only.
            registers.DefineRegister(0x00)
                .WithValueField(0, 3,
                    valueProviderCallback: _ => memMode,
                    writeCallback: (_, v) => SetMemMode((uint)v),
                    name: "MEM_MODE")
                .WithReservedBits(3, 5)
                .WithValueField(8, 1, name: "FB_MODE")
                .WithReservedBits(9, 23);

            // EXTICR1..4 at 0x08,0x0C,0x10,0x14. Each holds 4 lines, one 4-bit
            // port field per line (RM0434: EXTI0[3:0], EXTI1[7:4], EXTI2[11:8],
            // EXTI3[15:12]). Port codes: PA=0,PB=1,PC=2,PD=3,PE=4,PH=7. Use a
            // 4-bit mask (0xF) -- a 3-bit (0x7) mask happens to work for PA..PH
            // since PH=7 fits in 3 bits, but 0xF is the correct field width.
            for(var reg = 0; reg < 4; reg++)
            {
                var regIndex = reg;
                registers.DefineRegister(0x08 + 0x04 * reg)
                    .WithValueField(0, 32,
                        valueProviderCallback: _ => exticr[regIndex],
                        writeCallback: (_, v) =>
                        {
                            exticr[regIndex] = (uint)v;
                            for(var i = 0; i < 4; i++)
                            {
                                var line = regIndex * 4 + i;
                                var port = (int)(((uint)v >> (4 * i)) & 0xF);
                                exti.SetExtiSource(line, port);
                            }
                        }, name: "EXTICR" + (reg + 1));
            }

            // ── SYSCFG_CFGR1 (0x04) ────────────────────────────────────────────
            // Touched by clock/boot init (e.g. BOOSTEN, I2C fast-mode+ bits).
            // Plain storage; no side effects needed. Defined only to avoid the
            // "Unhandled read/write to 0x4" warning spam.
            registers.DefineRegister(0x04)
                .WithValueField(0, 32, name: "CFGR1");

            // ── COMP1 / COMP2 (0x200 / 0x204) ──────────────────────────────────
            // On real STM32WB the analog comparators live inside the SYSCFG/COMP
            // APB2 window (COMP1_BASE = APB2 + 0x200). furi_hal_rfid_init() calls
            // LL_COMP_Init(COMP1) (writes CSR) and, during 125 kHz reads,
            // LL_COMP_ReadOutputLevel(COMP1) (reads CSR bit30 = VALUE). There is
            // no RF field in this emulator, so the comparator output is always
            // LOW: we persist the configuration bits the firmware writes but force
            // the read-only VALUE bit (bit30) to 0. This is enough for init (no
            // polling) and for the read path (comp_start only spins a fixed
            // counter, never a CSR flag), so RFID never hangs. Registers are
            // defined here (rather than falling through to "unhandled") so the
            // config reads back consistently and the log stays quiet.
            registers.DefineRegister(0x200) // COMP1_CSR
                .WithValueField(0, 30,
                    valueProviderCallback: _ => comp1Csr & 0x3FFFFFFF,
                    writeCallback: (_, v) => comp1Csr = (comp1Csr & 0xC0000000) | ((uint)v & 0x3FFFFFFF),
                    name: "COMP1_CSR")
                .WithFlag(30, FieldMode.Read, valueProviderCallback: _ => false, name: "COMP1_VALUE") // no field -> LOW
                .WithReservedBits(31, 1);
            registers.DefineRegister(0x204) // COMP2_CSR
                .WithValueField(0, 30,
                    valueProviderCallback: _ => comp2Csr & 0x3FFFFFFF,
                    writeCallback: (_, v) => comp2Csr = (comp2Csr & 0xC0000000) | ((uint)v & 0x3FFFFFFF),
                    name: "COMP2_CSR")
                .WithFlag(30, FieldMode.Read, valueProviderCallback: _ => false, name: "COMP2_VALUE")
                .WithReservedBits(31, 1);
        }

        private void SetMemMode(uint mode)
        {
            if(mode == memMode)
            {
                return;
            }
            var previous = memMode;
            memMode = mode;
            switch(mode)
            {
                case MemModeSram: // 0b011 = LL_SYSCFG_REMAP_SRAM
                    this.Log(LogLevel.Info, "SYSCFG MEMRMP: MEM_MODE=SRAM (0b011) - aliasing SRAM1 at 0x00000000 and booting the image there");
                    RemapToSramAndBoot();
                    break;
                case MemModeMainFlash: // 0b000
                    this.Log(LogLevel.Info, "SYSCFG MEMRMP: MEM_MODE=MainFlash (0b000) - restoring FLASH at 0x00000000");
                    RemapToFlash();
                    break;
                default:
                    // MEM_MODE=1 (system-flash/bootloader at 0x0) and other modes
                    // are not needed for the Flipper and are written frequently by
                    // stock firmware (e.g. low-power paths). Don't remap and don't
                    // pretend we did; keep it at Debug so it doesn't flood the log.
                    this.Log(LogLevel.Debug,
                        "SYSCFG MEMRMP: MEM_MODE=0x{0:X} not modeled (only 0=flash, 3=SRAM); leaving 0x0 as-is (was mode {1})",
                        mode, previous);
                    memMode = previous; // don't pretend we applied an unsupported remap
                    break;
            }
        }

        // Alias SRAM1 at 0x0 and boot the image copied there, mirroring what
        // furi_hal_switch(0x0) does after the memmove. See the file header for
        // why this must be deferred + why we set PC/SP ourselves.
        private void RemapToSramAndBoot()
        {
            var sysbus = machine.SystemBus;

            // Idempotency guard: if SRAM1 already answers 0x0, nothing to do.
            if(sysbus.WhatPeripheralIsAt(AliasBase) == sram1)
            {
                return;
            }

            // Normally the MEMRMP write comes from the guest CPU executing
            // LL_SYSCFG_SetRemapMemory, so TryGetCurrentCPU succeeds. Fall back to
            // the sole CPU when there is no transaction initiator (e.g. a write
            // issued from the monitor), so the remap still boots correctly.
            if(!sysbus.TryGetCurrentCPU(out var cpu))
            {
                var cpus = sysbus.GetCPUs().ToList();
                if(cpus.Count != 1)
                {
                    this.Log(LogLevel.Error,
                        "SYSCFG MEMRMP: no current CPU and {0} CPUs present; cannot boot updater", cpus.Count);
                    return;
                }
                cpu = cpus[0];
            }
            var cpuWithRegisters = cpu as ICPUWithRegisters;
            if(cpuWithRegisters == null)
            {
                this.Log(LogLevel.Error, "SYSCFG MEMRMP: current CPU is not ICPUWithRegisters; cannot set MSP/PC for updater");
                return;
            }

            // Stop the doomed translation block (the one running furi_hal_switch)
            // before it can jump using the stale flash mapping.
            cpuWithRegisters.IsHalted = true;

            machine.LocalTimeSource.ExecuteInNearestSyncedState(_ =>
            {
                var bus = machine.SystemBus;

                // Boot the RAM updater WITHOUT touching any bus registration.
                //
                // WHY WE DO NOT ALIAS SRAM1 AT 0x0 ANYMORE:
                // On real silicon MEM_MODE=SRAM aliases SRAM1 at 0x00000000 so that
                // furi_hal_switch(0x0) can read the updater's vector table from
                // [0x0]/[0x4]. We used to reproduce that by Unregister(flash) +
                // Register(sram1 @ 0x0). But Unregister(flash) leaves the `flash`
                // peripheral with ZERO registrations for an instant, which triggers
                // Renode's peripheral garbage collector -> flash.Dispose(). That
                // Dispose closes the flash's disk backing-file stream and marks it
                // `disposed`, so every subsequent erase/program the updater does is
                // written to RAM but NEVER mirrored to firmware/flash.img -- the
                // update "succeeded" in RAM yet the on-disk image stayed stock, and
                // the next boot ran the OLD firmware. (Confirmed by instrumentation.)
                //
                // The alias at 0x0 is unnecessary: the updater is linked to run from
                // SRAM1 (0x20000000, stm32wb55xx_ram_fw.ld), not from 0x0, and its
                // own Reset_Handler sets SCB->VTOR = SRAM1_BASE. All furi_hal_switch
                // actually needs is the CPU to start at the updater's reset vector
                // with its initial stack. We read those straight from SRAM1's real
                // base and drive the CPU there — no bus remap, so `flash` is never
                // unregistered and its persistence stream stays alive.
                var sp = bus.ReadDoubleWord(Sram1Base + 0x0);
                var pc = bus.ReadDoubleWord(Sram1Base + 0x4);
                cpuWithRegisters.SetRegister(StackPointerRegister, sp);
                cpuWithRegisters.PC = pc;

                this.Log(LogLevel.Info,
                    "SYSCFG MEMRMP: booting RAM updater from SRAM1 (no bus alias) with MSP=0x{0:X8}, reset vector=0x{1:X8}",
                    sp, pc);

                cpuWithRegisters.IsHalted = false;
            });
        }

        // MEM_MODE=0 (flash at 0x0). Since we never alias SRAM1 at 0x0 (see
        // RemapToSramAndBoot), flash already answers 0x0 the whole time and there
        // is nothing to restore. Kept as a no-op for completeness; we must NOT
        // Unregister `flash` here (that would Dispose its disk backing file).
        private void RemapToFlash()
        {
            // Intentionally empty: flash stays mapped at 0x0 and 0x08000000 for the
            // entire run; no bus manipulation needed and none that could disturb
            // the flash persistence stream.
        }

        private const uint MemModeMainFlash = 0x0;
        private const uint MemModeSram = 0x3;
        private const ulong AliasBase = 0x00000000;
        private const ulong FlashBase = 0x08000000;   // flash execution/program address
        private const ulong Sram1Base = 0x20000000;   // SRAM1 normal address
        // ARM Cortex-M: SP is core register index 13 (ICPUWithRegisters uses the
        // architectural register index; ArmRegisters enum is in another assembly).
        private const int StackPointerRegister = 13;

        private uint memMode;
        private readonly IMachine machine;
        private readonly STM32WB55_EXTI exti;
        private readonly MappedMemory flash;
        private readonly MappedMemory sram1;
        private readonly DoubleWordRegisterCollection registers;
        private readonly uint[] exticr = new uint[4];
        private uint comp1Csr; // COMP1_CSR config bits (VALUE bit forced 0 on read)
        private uint comp2Csr; // COMP2_CSR config bits
    }
}
