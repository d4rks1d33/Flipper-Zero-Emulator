//
// CC1101 Sub-1GHz transceiver SPI stub for Flipper Zero emulator.
//
// Logs every SPI transaction (register R/W, strobes, FIFO) to a JSONL file so
// you can inspect what the firmware would do on SubGHz. Returns plausible
// status/register values. Does NOT model RF.
//
// SPI header byte: R/W(bit7) BURST(bit6) ADDR(bits5:0).
//   0x00-0x2E config regs, 0x30-0x3D strobes/status, 0x3E/0x3F FIFO.
//
using System;
using System.IO;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.SPI;

namespace Antmicro.Renode.Peripherals.SPI
{
    public class CC1101 : ISPIPeripheral
    {
        public CC1101()
        {
            regs = new byte[0x2F];
            logPath = LogDir() + "/cc1101.jsonl";
            Reset();
        }

        public void Reset()
        {
            Array.Clear(regs, 0, regs.Length);
            regs[0x02] = 0x06;  // IOCFG0 (GDO0)
            regs[0x0D] = 0x10;  // FREQ2
            regs[0x0E] = 0xA7;  // FREQ1
            regs[0x0F] = 0x62;  // FREQ0
            byteIndex = 0;
            readMode = false;
            burst = false;
            addr = 0;
            state = 0x00;  // IDLE
            fifoBytes = 0;
        }

        public byte Transmit(byte data)
        {
            byteIndex++;
            if(byteIndex == 1)
            {
                readMode = (data & 0x80) != 0;
                burst = (data & 0x40) != 0;
                addr = (byte)(data & 0x3F);
                return StatusByte();
            }

            if(addr <= 0x2E)
            {
                if(readMode)
                {
                    var v = regs[addr];
                    Log("read_reg", addr, v);
                    if(burst && addr < 0x2E) { addr++; }
                    return v;
                }
                regs[addr] = data;
                Log("write_reg", addr, data);
                if(burst && addr < 0x2E) { addr++; }
                return StatusByte();
            }
            else if(addr >= 0x30 && addr <= 0x3D)
            {
                if(readMode)
                {
                    return ReadStatusReg(addr);
                }
                HandleStrobe(addr);
                return StatusByte();
            }
            else // FIFO 0x3E/0x3F
            {
                if(readMode) { return 0x00; }
                Log("fifo_write", addr, data);
                return StatusByte();
            }
        }

        public void FinishTransmission()
        {
            byteIndex = 0;
        }

        private byte StatusByte()
        {
            return (byte)((state & 0x70) | (fifoBytes & 0x0F));
        }

        private byte ReadStatusReg(byte a)
        {
            byte v;
            switch(a)
            {
                case 0x30: v = 0x00; break;  // PARTNUM
                case 0x31: v = 0x14; break;  // VERSION (CC1101)
                case 0x34: v = 0x80; break;  // RSSI
                case 0x35: v = 0x01; break;  // MARCSTATE IDLE
                default:   v = 0x00; break;
            }
            Log("read_status", a, v);
            return v;
        }

        private void HandleStrobe(byte a)
        {
            Log("strobe", a, 0);
            switch(a)
            {
                case 0x30: Reset(); break;          // SRES
                case 0x34: state = 0x10; break;     // SRX
                case 0x35: state = 0x20; break;     // STX
                case 0x36: state = 0x00; break;     // SIDLE
                case 0x3A: fifoBytes = 0; break;    // SFRX
                case 0x3B: fifoBytes = 0; break;    // SFTX
            }
        }

        private void Log(string action, int a, int v)
        {
            try
            {
                var line = string.Format(
                    "{{\"t\":{0},\"dev\":\"CC1101\",\"action\":\"{1}\",\"addr\":\"0x{2:X2}\",\"value\":\"0x{3:X2}\"}}\n",
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
        private bool readMode;
        private bool burst;
        private byte addr;
        private byte state;
        private int fifoBytes;
    }
}
