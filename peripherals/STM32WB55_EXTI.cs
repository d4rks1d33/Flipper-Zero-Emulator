//
// STM32WB55 EXTI (Extended Interrupts) controller for the Flipper Zero emulator.
//
// Renode's stock STM32F4_EXTI / STM32WBA_EXTI have DIFFERENT register layouts
// than the STM32WB55. The Flipper firmware programs the WB55 EXTI at these
// offsets (RM0434 / stm32wb55xx.h):
//     RTSR1 = 0x00   rising trigger selection
//     FTSR1 = 0x04   falling trigger selection
//     SWIER1= 0x08   software interrupt event
//     PR1   = 0x0C   pending (write-1-to-clear)
//     IMR1  = 0x80   CPU1 interrupt mask   <-- key: NOT at 0x00 like F4
//     EMR1  = 0x84   CPU1 event mask
//     C2IMR1= 0xC0   CPU2 interrupt mask (ignored, no Core2)
//
// Using the F4 model made the firmware write the mask to 0x80 while the model
// read it from 0x00, so button EXTI interrupts never fired. This model uses the
// correct offsets and drives the NVIC lines for EXTI 0..15.
//
// ─── EXTI line source multiplexing (SYSCFG_EXTICR) ──────────────────────────
//
// On real hardware each EXTI line 0..15 is driven by EXACTLY ONE GPIO port,
// selected by the SYSCFG_EXTICR[] registers (4-bit field per line:
// 0=PA,1=PB,2=PC,3=PD,4=PE,7=PH). The Flipper firmware routes:
//     EXTI3  -> PH (OK button, PH3)
//     EXTI13 -> PC (Back button, PC13)
//     EXTI6  -> PC (Down, PC6)   EXTI10 -> PB (Up, PB10)
//     EXTI11 -> PB (Left, PB11)  EXTI12 -> PB (Right, PB12)
//
// PROBLEM this model solves: in the platform ALL six GPIO ports (A,B,C,D,E,H)
// connect their pin N to this EXTI's line N. Renode's STM32_GPIOPort.OnGPIO
// calls Connections[N].Set(level) -- a LEVEL, not a pulse -- so six different
// ports fight over the same EXTI input line. When PH3 goes high for the OK
// press, another port's pin-3 endpoint (idle low) had also driven line 3, and
// the shared pending/level bookkeeping made the press edge get lost while the
// release edge survived: input_isr fired on release only.
//
// FIX: this model is port-aware. Each GPIO port connects on a DISTINCT input
// range (port P, pin p -> input P*16 + p). SYSCFG writes EXTICR into this model
// (SetExtiSource). An incoming edge is only accepted for EXTI line p if it comes
// from the port currently selected by EXTICR for that line. Edges from
// non-selected ports are ignored, exactly like the hardware mux. Until the
// firmware programs EXTICR, the reset default (all lines -> PA) applies.
//
        // GPIO inputs: number = portIndex*16 + pin, where portIndex is the
//              STM32 port ordinal PA=0,PB=1,PC=2,PD=3,PE=4,PH=7 (matching the
//              SYSCFG_EXTICR port codes). So portA pins -> 0..15, portB ->
//              16..31, portC -> 32..47, portD -> 48..63, portE -> 64..79,
//              portH -> 112..127. The .repl wires gpioPortH [0-15] -> exti
//              [112-127]; OnGPIO recovers port via number/16 and pin via
//              number%16, and EXTICR writes 7 for PH, so line 3 (OK/PH3) is
//              driven only by input 115 (=7*16+3). NOTE: NumberOfPorts=8 below
//              bounds the accepted range at 0..127.
// Outputs 0..15 map to the NVIC as on real hardware:
//     EXTI0..4  -> NVIC 6..10
//     EXTI5..9  -> NVIC 23 (EXTI9_5)  via a combiner
//     EXTI10..15-> NVIC 40 (EXTI15_10) via a combiner
//
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Utilities;

namespace Antmicro.Renode.Peripherals.IRQControllers
{
    public class STM32WB55_EXTI : IDoubleWordPeripheral, IKnownSize, IIRQController, INumberedGPIOOutput
    {
        public STM32WB55_EXTI(IMachine machine)
        {
            this.machine = machine;
            var connections = new Dictionary<int, IGPIO>();
            for(var i = 0; i < NumberOfLines; i++)
            {
                connections[i] = new GPIO();
            }
            Connections = new ReadOnlyDictionary<int, IGPIO>(connections);
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
            rtsr1 = 0xFFFFFFFF;
            ftsr1 = 0xFFFFFFFF;
            imr1 = 0xFFFFFFFF;
            pr1 = 0;
            lineState = 0;
            for(var i = 0; i < NumberOfLines; i++)
            {
                // Reset default: every EXTI line is sourced from port A (0).
                lineSource[i] = 0;
            }
            // Every (port,pin) tracked level starts unknown/low.
            for(var p = 0; p < NumberOfPorts; p++)
            {
                for(var i = 0; i < NumberOfLines; i++)
                {
                    portPinLevel[p, i] = false;
                }
            }
            registers.Reset();
            foreach(var c in Connections.Values)
            {
                c.Unset();
            }
        }

        // Set which GPIO port (0=PA..4=PE,7=PH) drives EXTI line `line`.
        // Called by the SYSCFG peripheral when the firmware writes EXTICR.
        //
        // IMPORTANT: we do NOT synthesize an edge on a source switch. The mux
        // just changes which port's tracked level this line will edge-detect
        // against from now on. This is what lets idle levels that were injected
        // BEFORE the firmware programmed EXTICR (all sources default to PA then)
        // still be correct once the real source is selected: the level of each
        // (port,pin) is always recorded (see OnGPIO), so switching the source
        // simply points the line at the already-correct level.
        public void SetExtiSource(int line, int port)
        {
            if(line < 0 || line >= NumberOfLines)
            {
                return;
            }
            lineSource[line] = port;
            BitHelper.SetBit(ref lineState, (byte)line, GetSelectedLevel(line));
            this.Log(LogLevel.Debug, "EXTI: line {0} source set to port {1}", line, port);
        }

        // GPIO input. The connection number encodes BOTH the source port and the
        // pin: number = port*16 + pin. `value` is the new pin level.
        public void OnGPIO(int number, bool value)
        {
            if(number < 0 || number >= NumberOfLines * NumberOfPorts)
            {
                return;
            }

            var port = number / NumberOfLines;
            var pin = number % NumberOfLines;

            // ALWAYS record the level of THIS (port,pin), even if this port is
            // not the one currently selected by SYSCFG_EXTICR for the line. This
            // is critical: idle levels are injected before the firmware programs
            // EXTICR (when every line still defaults to PA), so if we dropped
            // edges from unselected ports without recording their level, the
            // line's baseline would be wrong once the real port is selected and
            // the first real press would land on "no edge" (previous==value),
            // while its release produced a spurious edge on a shared IRQ -- the
            // exact "buttons do nothing / Up acts like Back" symptom.
            var previous = portPinLevel[port, pin];
            portPinLevel[port, pin] = value;

            // The EXTI line only edge-detects against the port currently selected
            // by the mux (SYSCFG_EXTICR). Edges from non-selected ports are
            // recorded (above) but do not (yet) drive the interrupt logic.
            if(lineSource[pin] != port)
            {
                return;
            }

            BitHelper.SetBit(ref lineState, (byte)pin, value);

            var rising = !previous && value;
            var falling = previous && !value;

            // Honour the firmware's edge selection. Buttons are configured
            // GpioModeInterruptRiseFall so both edges fire; but respecting
            // RTSR1/FTSR1 keeps this faithful and avoids spurious double events.
            var risingEnabled = BitHelper.IsBitSet(rtsr1, (byte)pin);
            var fallingEnabled = BitHelper.IsBitSet(ftsr1, (byte)pin);

            if(!((rising && risingEnabled) || (falling && fallingEnabled)))
            {
                // No enabled edge matched. Nothing to latch.
                return;
            }

            // Only latch the pending bit for lines whose interrupt is UNMASKED
            // (IMR1 set). On real silicon PR1 latches regardless of IMR1, but here
            // that faithfulness backfires: some modeled peripherals (notably the
            // ST25R3916 NFC IRQ on PA2 / EXTI line 2) produce a spurious rising
            // edge during init while their EXTI line is still masked. On hardware
            // that stray pending bit is harmless, but the Flipper's tickless-idle
            // path (furi_hal_os_is_pending_irq -> LL_EXTI_ReadFlag_0_31 reads the
            // WHOLE PR1) treats ANY pending bit — even a masked one that no ISR
            // will ever clear — as "an interrupt is pending", so it aborts every
            // sleep and live-locks the FreeRTOS idle task, starving the input/GUI
            // threads. Result: the very first button is processed, then the UI
            // freezes. Gating the latch on IMR1 keeps a masked line from poisoning
            // PR1 while still latching every real, unmasked interrupt (all six
            // buttons enable their IMR1 bit, so they are unaffected).
            var unmasked = BitHelper.IsBitSet(imr1, (byte)pin);
            if(!unmasked)
            {
                return;
            }

            BitHelper.SetBit(ref pr1, (byte)pin, true);
            this.Log(LogLevel.Info, "EXTI line {0} triggered (port={1} rising={2} falling={3})",
                pin, port, rising, falling);

            // IMR1 unmasked (checked above): the NVIC line reaches the CPU.
            Connections[pin].Blink();
        }

        // Current level of the EXTI line = level of the pin on the port the mux
        // currently selects for that line.
        private bool GetSelectedLevel(int line)
        {
            var port = lineSource[line];
            if(port < 0 || port >= NumberOfPorts)
            {
                return false;
            }
            return portPinLevel[port, line];
        }

        public long Size => 0x400;

        public IReadOnlyDictionary<int, IGPIO> Connections { get; }

        private void DefineRegisters()
        {
            registers.DefineRegister(0x00) // RTSR1
                .WithValueField(0, 32, valueProviderCallback: _ => rtsr1,
                    writeCallback: (_, v) => rtsr1 = (uint)v, name: "RTSR1");
            registers.DefineRegister(0x04) // FTSR1
                .WithValueField(0, 32, valueProviderCallback: _ => ftsr1,
                    writeCallback: (_, v) => ftsr1 = (uint)v, name: "FTSR1");
            registers.DefineRegister(0x08) // SWIER1
                .WithValueField(0, 32, valueProviderCallback: _ => 0,
                    writeCallback: (_, v) =>
                    {
                        // Software-trigger the selected lines (if unmasked).
                        var val = (uint)v;
                        for(var i = 0; i < NumberOfLines; i++)
                        {
                            if(BitHelper.IsBitSet(val, (byte)i))
                            {
                                BitHelper.SetBit(ref pr1, (byte)i, true);
                                if(BitHelper.IsBitSet(imr1, (byte)i))
                                {
                                    Connections[i].Blink();
                                }
                            }
                        }
                    }, name: "SWIER1");
            registers.DefineRegister(0x0C) // PR1 (write 1 to clear)
                .WithValueField(0, 32, valueProviderCallback: _ => pr1,
                    writeCallback: (_, v) => pr1 &= ~(uint)v, name: "PR1");
            registers.DefineRegister(0x80) // IMR1
                .WithValueField(0, 32, valueProviderCallback: _ => imr1,
                    writeCallback: (_, v) => imr1 = (uint)v, name: "IMR1");
            registers.DefineRegister(0x84) // EMR1 (event mask - stored, unused)
                .WithValueField(0, 32, name: "EMR1");
            // CPU2 masks and part-2 registers: accept and ignore.
            registers.DefineRegister(0x90).WithValueField(0, 32, name: "IMR2");
            registers.DefineRegister(0x94).WithValueField(0, 32, name: "EMR2");
            registers.DefineRegister(0xC0).WithValueField(0, 32, name: "C2IMR1");
            registers.DefineRegister(0xC4).WithValueField(0, 32, name: "C2EMR1");
            registers.DefineRegister(0xD0).WithValueField(0, 32, name: "C2IMR2");
            registers.DefineRegister(0xD4).WithValueField(0, 32, name: "C2EMR2");
            // Part-2 trigger/pending (lines 32..48, not used by buttons)
            registers.DefineRegister(0x20).WithValueField(0, 32, name: "RTSR2");
            registers.DefineRegister(0x24).WithValueField(0, 32, name: "FTSR2");
            registers.DefineRegister(0x28).WithValueField(0, 32, name: "SWIER2");
            registers.DefineRegister(0x2C).WithValueField(0, 32, name: "PR2");
        }

        private readonly IMachine machine;
        private readonly DoubleWordRegisterCollection registers;

        private uint rtsr1;
        private uint ftsr1;
        private uint imr1;
        private uint pr1;
        private uint lineState;

        // Per-line selected source port (0=PA..4=PE,7=PH), set via EXTICR.
        private readonly int[] lineSource = new int[NumberOfLines];
        // Level of EVERY (port,pin), always recorded from OnGPIO regardless of the
        // mux, so edge detection against the EXTICR-selected port is correct even
        // for levels injected before the firmware programmed EXTICR.
        private readonly bool[,] portPinLevel = new bool[NumberOfPorts, NumberOfLines];

        private const int NumberOfLines = 16;
        private const int NumberOfPorts = 8; // PA..PH (PF/PG unused, PH=7)
    }
}
