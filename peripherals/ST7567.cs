//
// ST7567 128x64 monochrome LCD controller for Flipper Zero emulator.
//
// Connected on SPI2. The D/C (data/command) line is GPIO PB1, routed to this
// peripheral as GPIO input 0. When D/C is low => command byte, high => data byte.
//
// The controller has a 132x65 GDRAM organized in 8 pages (rows) x 132 columns,
// 1 bit per pixel (LSB = top row of the page). Flipper uses the leftmost 128 cols.
//
// On every data write we update an internal framebuffer and dump it to a PGM/raw
// file so the external SDL frontend can render it. The dump is throttled.
//
using System;
using System.IO;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.SPI;

namespace Antmicro.Renode.Peripherals.Video
{
    public class ST7567 : ISPIPeripheral, IGPIOReceiver
    {
        public ST7567(IMachine machine)
        {
            this.machine = machine;
            framebuffer = new byte[ColumnsTotal * Pages];  // full GDRAM (132 cols x 8 pages)
            dumpPath = Environment.GetEnvironmentVariable("FLIPPER_EMU_FB_PATH");
            if(string.IsNullOrEmpty(dumpPath))
            {
                dumpPath = "/tmp/flipper_fb.raw";
            }
            Reset();
        }

        public void Reset()
        {
            Array.Clear(framebuffer, 0, framebuffer.Length);
            column = 0;
            page = 0;
            isData = false;
            displayOn = false;
            startLine = 0;
            columnReverse = false;
            comReverse = false;
            inverse = false;
            dumpCounter = 0;
        }

        // GPIO input: pin 0 = D/C line (from PB1). true = data, false = command.
        public void OnGPIO(int number, bool value)
        {
            if(number == 0)
            {
                isData = value;
            }
        }

        public byte Transmit(byte data)
        {
            if(isData)
            {
                WriteData(data);
            }
            else
            {
                WriteCommand(data);
            }
            return 0x00;  // ST7567 does not return data on this interface
        }

        public void FinishTransmission()
        {
        }

        private void WriteData(byte value)
        {
            // Store into the full 132-column GDRAM at the current (page, column).
            if(page < Pages && column < ColumnsTotal)
            {
                framebuffer[page * ColumnsTotal + column] = value;
            }
            column++;
            if(column >= ColumnsTotal)
            {
                column = 0;
            }
            // Dump periodically. A full frame is 8 pages x 128 cols = 1024 data
            // bytes; dump every ~1024 writes so we capture complete frames rather
            // than mid-refresh partial ones.
            dumpCounter++;
            if(dumpCounter >= (Width * Pages))
            {
                dumpCounter = 0;
                DumpFramebuffer();
            }
        }

        private void WriteCommand(byte cmd)
        {
            if((cmd & 0xF0) == 0xB0)
            {
                // Set page address (0xB0-0xB7)
                page = (byte)(cmd & 0x0F);
            }
            else if((cmd & 0xF0) == 0x10)
            {
                // Set column address MSB
                column = (byte)((column & 0x0F) | ((cmd & 0x0F) << 4));
            }
            else if((cmd & 0xF0) == 0x00)
            {
                // Set column address LSB
                column = (byte)((column & 0xF0) | (cmd & 0x0F));
            }
            else if((cmd & 0xC0) == 0x40)
            {
                // Set display start line (0x40-0x7F)
                startLine = (byte)(cmd & 0x3F);
            }
            else if(cmd == 0xAE)
            {
                displayOn = false;
            }
            else if(cmd == 0xAF)
            {
                displayOn = true;
                DumpFramebuffer();
            }
            else if(cmd == 0xA6)
            {
                inverse = false;
            }
            else if(cmd == 0xA7)
            {
                inverse = true;
            }
            else if(cmd == 0xA0)
            {
                columnReverse = false;
            }
            else if(cmd == 0xA1)
            {
                columnReverse = true;
            }
            else if(cmd == 0xC0)
            {
                comReverse = false;
            }
            else if(cmd == 0xC8)
            {
                comReverse = true;
            }
            // Other commands (contrast, bias, power control, etc.) are accepted silently.
            this.Log(LogLevel.Noisy, "ST7567 cmd 0x{0:X2} (page={1} col={2})", cmd, page, column);
        }

        private void DumpFramebuffer()
        {
            try
            {
                // Write a simple binary: 128*64 bytes, 1 byte per pixel (0/1).
                // The Flipper display is used in flip mode with an x_offset of 4:
                // the visible 128 columns start at GDRAM column 4.
                var img = new byte[Width * Height];
                for(var p = 0; p < Pages; p++)
                {
                    for(var x = 0; x < Width; x++)
                    {
                        var srcCol = x + VisibleColumnOffset;
                        if(srcCol >= ColumnsTotal)
                        {
                            continue;
                        }
                        var b = framebuffer[p * ColumnsTotal + srcCol];
                        for(var bit = 0; bit < 8; bit++)
                        {
                            var y = p * 8 + bit;
                            if(y >= Height)
                            {
                                continue;
                            }
                            var on = (b & (1 << bit)) != 0;
                            if(inverse)
                            {
                                on = !on;
                            }
                            // Flipper drives the panel flipped: segment remap
                            // (0xA0) mirrors X and COM reverse (0xC8) mirrors Y.
                            var dx = columnReverse ? (Width - 1 - x) : x;
                            var dy = comReverse ? (Height - 1 - y) : y;
                            img[dy * Width + dx] = (byte)(on ? 1 : 0);
                        }
                    }
                }
                File.WriteAllBytes(dumpPath, img);
            }
            catch(Exception e)
            {
                this.Log(LogLevel.Warning, "ST7567 framebuffer dump failed: {0}", e.Message);
            }
        }

        private readonly IMachine machine;
        private readonly byte[] framebuffer;
        private readonly string dumpPath;

        private byte column;
        private byte page;
        private byte startLine;
        private bool isData;
        private bool displayOn;
        private bool columnReverse;
        private bool comReverse;
        private bool inverse;
        private int dumpCounter;

        private const int Width = 128;
        private const int Height = 64;
        private const int Pages = 8;
        private const int ColumnsTotal = 132;
        private const int VisibleColumnOffset = 4; // flip-mode x_offset (u8g2_glue.c)
    }
}
