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
// GPIO inputs 0..15 = EXTI lines 0..15 (connected from the GPIO ports in the
// .repl). Outputs 0..15 map to the NVIC as on real hardware:
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
            rtsr1 = 0;
            ftsr1 = 0;
            imr1 = 0;
            pr1 = 0;
            lineState = 0;
            registers.Reset();
            foreach(var c in Connections.Values)
            {
                c.Unset();
            }
        }

        // GPIO input: line = pin number (0..15). value = new pin level.
        public void OnGPIO(int number, bool value)
        {
            if(number < 0 || number >= NumberOfLines)
            {
                return;
            }
            var previous = BitHelper.IsBitSet(lineState, (byte)number);
            BitHelper.SetBit(ref lineState, (byte)number, value);

            var rising = !previous && value;
            var falling = previous && !value;

            var risingEnabled = BitHelper.IsBitSet(rtsr1, (byte)number);
            var fallingEnabled = BitHelper.IsBitSet(ftsr1, (byte)number);

            if((rising && risingEnabled) || (falling && fallingEnabled))
            {
                // Latch pending
                BitHelper.SetBit(ref pr1, (byte)number, true);
                // Fire the NVIC line only if the interrupt is unmasked
                if(BitHelper.IsBitSet(imr1, (byte)number))
                {
                    this.Log(LogLevel.Noisy, "EXTI line {0} triggered (rising={1})", number, rising);
                    Connections[number].Blink();
                }
            }
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

        private const int NumberOfLines = 16;
    }
}
