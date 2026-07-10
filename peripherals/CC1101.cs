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
        // Delegate the CC1101 raised on IOCFG0 writes so the owner (Spi1Router)
        // can forward it to the GDO0 GPIO output wired to PA1. See the GDO0/PA1
        // self-test note in Spi1Router.cs. Level meaning is "GDO0 output high".
        public Action<bool> Gdo0Changed;

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
            // IOCFG0 reset default 0x06 has no INV bit => GDO0 low.
            UpdateGdo0(regs[0x02]);
        }

        // SELF-FRAMING NOTE
        // ─────────────────
        // Renode's STM32SPI never calls FinishTransmission per SPI transaction,
        // and the Flipper CC1101 driver holds CS low for the WHOLE acquire window
        // (many cc1101_write_reg/strobe calls back-to-back - CS is only toggled on
        // furi_hal_spi_acquire/release, not per transfer). So this model cannot
        // rely on a CS edge or FinishTransmission to delimit each command. Instead
        // it self-frames from the CC1101 header byte, whose command class implies a
        // fixed length:
        //   * strobe  (0x30-0x3D, non-burst): 1-byte frame (header only)
        //   * config/status single access (non-burst): header + 1 data = 2 bytes
        //   * burst access: header + N data, terminated only by CS deassert; we
        //     stay "in frame" auto-incrementing until FinishTransmission (which the
        //     router raises on the CS rising edge, once per acquire/release).
        // After a fixed-length frame completes we reset byteIndex to 0 so the next
        // byte is decoded as a fresh header. This keeps IOCFG0 writes (and the GDO0
        // self test) correctly framed even though CS stays asserted.
        public byte Transmit(byte data)
        {
            byteIndex++;
            if(byteIndex == 1)
            {
                readMode = (data & 0x80) != 0;
                burst = (data & 0x40) != 0;
                addr = (byte)(data & 0x3F);

                // A non-burst strobe (0x30-0x3D) is a complete 1-byte command.
                if(!burst && addr >= 0x30 && addr <= 0x3D)
                {
                    HandleStrobe(addr);
                    byteIndex = 0; // frame complete: next byte is a new header
                    return StatusByte();
                }
                return StatusByte();
            }

            byte ret;
            if(addr <= 0x2E)
            {
                if(readMode)
                {
                    ret = regs[addr];
                    Log("read_reg", addr, ret);
                }
                else
                {
                    regs[addr] = data;
                    Log("write_reg", addr, data);
                    if(addr == 0x02) { UpdateGdo0(data); }  // IOCFG0 => drive GDO0 (PA1)
                    ret = StatusByte();
                }
                if(burst) { if(addr < 0x2E) { addr++; } }
                else { byteIndex = 0; } // single access: 2-byte frame complete
            }
            else if(addr >= 0x30 && addr <= 0x3D)
            {
                // Status-register access. The Flipper firmware always reads these
                // as BURST but exactly one data byte per header (e.g.
                // cc1101_read_reg(CC1101_STATUS_RXBYTES | CC1101_BURST)). Treat it
                // as a fixed 2-byte frame and reset regardless of the burst bit so
                // back-to-back status reads within one CS window stay aligned.
                ret = readMode ? ReadStatusReg(addr) : StatusByte();
                byteIndex = 0;
            }
            else // FIFO 0x3E/0x3F (burst or single)
            {
                if(readMode) { ret = 0x00; }
                else { Log("fifo_write", addr, data); ret = StatusByte(); }
                if(!burst) { byteIndex = 0; }
            }
            return ret;
        }

        public void FinishTransmission()
        {
            byteIndex = 0;
            burst = false;
        }

        private byte StatusByte()
        {
            return (byte)((state & 0x70) | (fifoBytes & 0x0F));
        }

        // Drive the GDO0 output line (wired to PA1) from an IOCFG0 write so the
        // firmware's power-on self test (furi_hal_subghz_init) passes:
        //   IOCFG0 = CC1101IocfgHW (0x2F)             -> GDO0 low
        //   IOCFG0 = CC1101IocfgHW | CC1101_IOCFG_INV -> GDO0 high (0x6F)
        //   IOCFG0 = CC1101IocfgHighImpedance (0x2E)  -> low (floating)
        // The self test writes 0x2F, waits for PA1==low, then 0x6F, waits for
        // PA1==high. We model only the inversion bit (0x40): the base signal is
        // treated as logic-low, INV makes it high. This is enough for the test.
        private void UpdateGdo0(byte iocfg0)
        {
            var high = (iocfg0 & 0x40) != 0; // CC1101_IOCFG_INV
            if(Gdo0Changed != null)
            {
                Gdo0Changed(high);
            }
            Log("gdo0", 0x02, high ? 1 : 0);
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

        // JSONL logging. A single StreamWriter is opened lazily and reused so we
        // do not open/close the file on every SPI byte. AutoFlush keeps the file
        // consistent if the emulator is killed. All I/O is wrapped so a logging
        // failure can NEVER propagate back into the SPI transfer (no hang/crash).
        private void Log(string action, int a, int v)
        {
            try
            {
                if(writer == null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                    writer = new StreamWriter(logPath, append: true) { AutoFlush = true };
                }
                var dir = action.StartsWith("read") ? "in" : "out";
                writer.Write(
                    "{\"t\":\"" + DateTime.UtcNow.ToString("o") + "\"," +
                    "\"dev\":\"CC1101\"," +
                    "\"dir\":\"" + dir + "\"," +
                    "\"action\":\"" + action + "\"," +
                    "\"addr\":\"0x" + a.ToString("X2") + "\"," +
                    "\"value\":\"0x" + v.ToString("X2") + "\"}\n");
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
        private StreamWriter writer;
        private int byteIndex;
        private bool readMode;
        private bool burst;
        private byte addr;
        private byte state;
        private int fifoBytes;
    }
}
