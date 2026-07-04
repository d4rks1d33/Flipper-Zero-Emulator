//
// LP5562 LED driver I2C peripheral for Flipper Zero emulator.
// Minimal model: acknowledges all writes, stores registers, returns them on read.
// Controls RGB LED + White (display backlight). No visual effect modelled.
//
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.I2C;

namespace Antmicro.Renode.Peripherals.Sensors
{
    public class LP5562 : II2CPeripheral
    {
        public LP5562()
        {
            registers = new Dictionary<byte, byte>();
            Reset();
        }

        public void Reset()
        {
            registers.Clear();
            registerAddress = 0;
            firstByte = true;
        }

        public void Write(byte[] data)
        {
            if(data.Length == 0)
            {
                return;
            }
            // First byte after START is the register address
            registerAddress = data[0];
            for(var i = 1; i < data.Length; i++)
            {
                registers[registerAddress] = data[i];
                this.Log(LogLevel.Noisy, "LP5562 write reg 0x{0:X} = 0x{1:X}", registerAddress, data[i]);
                registerAddress++;
            }
        }

        public byte[] Read(int count = 1)
        {
            var result = new byte[count];
            for(var i = 0; i < count; i++)
            {
                registers.TryGetValue(registerAddress, out var v);
                result[i] = v;
                registerAddress++;
            }
            return result;
        }

        public void FinishTransmission()
        {
        }

        private byte registerAddress;
        private bool firstByte;
        private readonly Dictionary<byte, byte> registers;
    }
}
