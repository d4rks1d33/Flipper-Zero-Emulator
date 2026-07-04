//
// ST25R3916 NFC transceiver SPI stub for Flipper Zero emulator.
//
// Logs every SPI transaction (register R/W, FIFO, direct commands) to a JSONL
// file. Returns the chip ID (0x05) so the driver recognizes the device.
// Does NOT model the 13.56 MHz RF field.
//
// SPI command byte: mode(bits7:6) addr/cmd(bits5:0).
//   00 = write reg, 01 = read reg, 10 = FIFO, 11 = direct command.
//
using System;
using System.IO;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.SPI;

namespace Antmicro.Renode.Peripherals.SPI
{
    public class ST25R3916 : ISPIPeripheral
    {
        public ST25R3916()
        {
            regs = new byte[0x40];
            logPath = LogDir() + "/st25r3916.jsonl";
            Reset();
        }

        public void Reset()
        {
            Array.Clear(regs, 0, regs.Length);
            regs[0x3F] = 0x05;  // IC Identity: ST25R3916
            regs[0x16] = 0x80;  // Regulator result: regulation OK
            byteIndex = 0;
            mode = 0;
            addr = 0;
            cmdByte = 0;
        }

        public byte Transmit(byte data)
        {
            byteIndex++;
            if(byteIndex == 1)
            {
                cmdByte = data;
                mode = (data >> 6) & 0x3;
                addr = (byte)(data & 0x3F);
                return 0x00;
            }

            switch(mode)
            {
                case 0:  // write reg
                    if(addr < 0x40)
                    {
                        regs[addr] = data;
                        Log("write_reg", addr, data);
                        addr = (byte)((addr + 1) & 0x3F);
                    }
                    return 0x00;
                case 1:  // read reg
                    if(addr < 0x40)
                    {
                        var v = regs[addr];
                        Log("read_reg", addr, v);
                        addr = (byte)((addr + 1) & 0x3F);
                        return v;
                    }
                    return 0x00;
                case 2:  // FIFO
                    if((cmdByte & 0x20) != 0) { return 0x00; }  // FIFO read
                    Log("fifo_write", 0, data);
                    return 0x00;
                default: // direct command
                    Log("direct_cmd", addr, data);
                    return 0x00;
            }
        }

        public void FinishTransmission()
        {
            byteIndex = 0;
        }

        private void Log(string action, int a, int v)
        {
            try
            {
                var line = string.Format(
                    "{{\"t\":{0},\"dev\":\"ST25R3916\",\"action\":\"{1}\",\"addr\":\"0x{2:X2}\",\"value\":\"0x{3:X2}\"}}\n",
                    DateTime.UtcNow.Ticks, action, a, v);
                File.AppendAllText(logPath, line);
            }
            catch { }
        }

        private static string LogDir()
        {
            var d = Environment.GetEnvironmentVariable("FLIPPER_EMU_LOG_DIR");
            return string.IsNullOrEmpty(d) ? "/tmp" : d;
        }

        private readonly byte[] regs;
        private readonly string logPath;
        private int byteIndex;
        private int mode;
        private byte addr;
        private byte cmdByte;
    }
}
