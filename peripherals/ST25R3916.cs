//
// ST25R3916 NFC transceiver SPI stub for Flipper Zero emulator.
//
// Logs every SPI transaction (register R/W, FIFO, direct commands) to a JSONL
// file. Does NOT model the 13.56 MHz RF field.
//
// SPI command byte (st25r3916_reg.c):
//   00xxxxxx (0x00) = write reg,   01xxxxxx (0x40) = read reg,
//   11xxxxxx (0xC0) = direct cmd,  0x80 = FIFO load, 0x9F = FIFO read,
//   0xFB = Space-B access prefix (next byte is the real reg cmd in space B).
//
// CHIP-ID / BOOT-PATH DECISION (see furi_hal_nfc_init, furi_hal_nfc.c:71-253):
//   The firmware reads IC_IDENTITY (reg 0x3F) and requires
//   (chip_id & 0xF8) == 0x28 (ic_type_st25r3916 = 5<<3, mask = 0x1F<<3).
//   We return 0x28 (ic_type_st25r3916, rev v0) so the chip is recognised as
//   PRESENT and init runs the full, faithful register-configuration flow rather
//   than bailing out early with "Wrong chip id".
//
//   This is safe (NON-HANGING) because every wait on the present-chip path is
//   timeout-bounded:
//     * furi_hal_nfc_turn_on_osc(): sets OP_CONTROL.en, then
//       furi_hal_nfc_event_wait_for_specific_irq(OSC, 10 ms). That is a FreeRTOS
//       thread-flags wait with a real timeout; if our model never raises the NFC
//       IRQ (PA2), the flag is never set and the wait simply TIMES OUT. The
//       subsequent osc_ok check reads AUX_DISPLAY (0x31) bit4, which we keep 0,
//       so init records FuriHalNfcErrorOscillator and drops into low-power mode.
//     * The measure-VDD path (wait_for_specific_irq(DCT, 100 ms)) times out the
//       same way.
//   Because we NEVER assert PA2, furi_hal_nfc_get_irq()'s `while(gpio_read(PA2))`
//   loop is never entered (its enclosing thread-flag is never set), so there is
//   no possibility of a spin. The osc-timeout -> low-power outcome is exactly
//   what a real ST25R3916 with no antenna/field would settle into, and it is
//   strictly more faithful than the old "wrong chip id" stub.
//
//   To go further (make the oscillator actually "start"), the model would need
//   to pulse the IRQ output (PA2) and return the OSC bit from IRQ_MAIN (0x1A)
//   exactly once, clearing it on read. That is intentionally NOT done here: it
//   adds a read-to-clear IRQ state machine whose only benefit is skipping a
//   10 ms timeout, at the cost of a potential spin in the `while(gpio_read(PA2))`
//   loop if the clear-on-read timing is wrong. Priority 1 is "never hang", so we
//   keep PA2 quiescent. The IrqChanged hook is wired for completeness.
//
using System;
using System.IO;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.SPI;

namespace Antmicro.Renode.Peripherals.SPI
{
    public class ST25R3916 : ISPIPeripheral
    {
        // ST25R3916 IC_IDENTITY: ic_type = 5<<3 = 0x28 (rev v0). Passes the
        // firmware's (id & 0xF8) == 0x28 present-chip check.
        private const byte ChipId = 0x28;

        // Raised if the model ever asserts/deasserts its IRQ line (PA2). The
        // owner (Spi1Router) forwards this to the NFC IRQ GPIO output. Currently
        // never invoked (PA2 kept low) - see the boot-path decision above.
        public Action<bool> IrqChanged;

        public ST25R3916()
        {
            regs = new byte[0x40];
            logPath = LogDir() + "/st25r3916.jsonl";
            Reset();
        }

        public void Reset()
        {
            Array.Clear(regs, 0, regs.Length);
            regs[0x3F] = ChipId; // IC_IDENTITY: see CHIP-ID decision above
            // AUX_DISPLAY (0x31): osc_ok (bit4) left 0 so furi_hal_nfc_turn_on_osc
            // reports FuriHalNfcErrorOscillator after the IRQ wait times out and
            // init settles into low-power mode (faithful "no field" outcome).
            byteIndex = 0;
            mode = 0;
            addr = 0;
            cmdByte = 0;
            spaceB = false;
            if(IrqChanged != null)
            {
                IrqChanged(false);
            }
        }

        public byte Transmit(byte data)
        {
            byteIndex++;
            if(byteIndex == 1)
            {
                // Access-space prefixes: absorb the prefix and wait for the real
                // command byte. Do NOT count the prefix as the command so the
                // following byte is still treated as byte 1 (the actual reg/cmd).
                //   0xFB = Space-B access (st25r3916_reg.c ST25R3916_CMD_SPACE_B_ACCESS)
                //   0xFC = Test-register access (ST25R3916_CMD_TEST_ACCESS)
                if(data == 0xFB) // ST25R3916_CMD_SPACE_B_ACCESS
                {
                    spaceB = true;
                    byteIndex = 0;
                    Log("space_b", 0, data);
                    return 0x00;
                }
                if(data == 0xFC) // ST25R3916_CMD_TEST_ACCESS
                {
                    testAccess = true;
                    byteIndex = 0;
                    Log("test_access", 0, data);
                    return 0x00;
                }

                cmdByte = data;
                mode = (data >> 6) & 0x3;
                addr = (byte)(data & 0x3F);
                // A direct command (mode 3, 0xC0 prefix) carries no data byte; it
                // is fully described by this first byte. Log it here.
                if(mode == 3)
                {
                    Log("direct_cmd", addr, data);
                }
                return 0x00;
            }

            switch(mode)
            {
                case 0:  // write reg
                    if(addr < 0x40)
                    {
                        regs[addr] = data;
                        Log(RegAction("write_reg"), addr, data);
                        addr = (byte)((addr + 1) & 0x3F);
                    }
                    return 0x00;
                case 1:  // read reg
                    if(addr < 0x40)
                    {
                        var v = regs[addr];
                        Log(RegAction("read_reg"), addr, v);
                        addr = (byte)((addr + 1) & 0x3F);
                        return v;
                    }
                    return 0x00;
                case 2:  // FIFO (0x80 load / 0x9F read) - not exercised at init
                    if((cmdByte & 0x1F) == 0x1F) { return 0x00; }  // FIFO read
                    Log("fifo_write", 0, data);
                    return 0x00;
                default: // direct command: no data byte follows (handled at byte 1)
                    return 0x00;
            }
        }

        // Compose the JSONL action name with the active access-space suffix so a
        // reader can tell main-space, Space-B and test-register accesses apart.
        private string RegAction(string baseAction)
        {
            if(testAccess) { return baseAction + "_test"; }
            if(spaceB) { return baseAction + "_b"; }
            return baseAction;
        }

        public void FinishTransmission()
        {
            byteIndex = 0;
            spaceB = false;
            testAccess = false;
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
                    "\"dev\":\"ST25R3916\"," +
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
        private int mode;
        private byte addr;
        private byte cmdByte;
        private bool spaceB;
        private bool testAccess;
    }
}
