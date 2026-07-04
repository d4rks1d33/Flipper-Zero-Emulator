//
// SD card (SPI mode) model tailored to the Flipper Zero firmware.
//
// Renode's stock SD.SDCard SPI mode is fragile with the Flipper's driver and
// its GPIO bus-banging during init. This model implements exactly the SD-SPI
// command subset that furi_hal_sd.c uses, as an SDHC (high-capacity) card, and
// reads/writes 512-byte blocks against a backing image file.
//
// Wiring: connected on SPI2 behind Spi2Router (CS = PC12). The card only reacts
// while its CS is asserted (the router forwards bytes only then).
//
// Command frame (6 bytes): 0x40|CMD, arg[31:24..0], CRC|0x01.
// Responses: R1 (1 byte), R3/R7 (R1 + 4 bytes), data token 0xFE + 512 data + 2 CRC.
//
using System;
using System.IO;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.SPI;

namespace Antmicro.Renode.Peripherals.SPI
{
    public class FlipperSdCard : ISPIPeripheral, IDisposable
    {
        public FlipperSdCard(IMachine machine, string imageFile, long capacity)
        {
            this.capacity = capacity;
            this.imagePath = imageFile;
            // Open (or create) the backing image, read/write, shared.
            stream = new FileStream(imageFile, FileMode.OpenOrCreate,
                                    FileAccess.ReadWrite, FileShare.ReadWrite);
            if(stream.Length < capacity)
            {
                stream.SetLength(capacity);
            }
            Reset();
        }

        public void Reset()
        {
            state = St.Idle;
            cmdBuf = new byte[6];
            cmdLen = 0;
            respQueue = new System.Collections.Generic.Queue<byte>();
            idle = true;
            appCmd = false;
            readBlock = null;
            readBlockPos = 0;
            writeAddress = 0;
            writeBuf = null;
            writePos = 0;
            expectingWriteToken = false;
        }

        public void Dispose()
        {
            stream?.Flush();
            stream?.Dispose();
        }

        // The router calls this per byte only while CS(SD) is asserted.
        public byte Transmit(byte data)
        {
            // 1) If we owe response bytes, send them (R1/R3/R7, data tokens, etc.)
            if(respQueue.Count > 0)
            {
                // While draining a response we ignore the incoming MOSI byte.
                return respQueue.Dequeue();
            }

            // 2) If we are streaming a read block out.
            if(readBlock != null)
            {
                var b = readBlock[readBlockPos++];
                if(readBlockPos >= readBlock.Length)
                {
                    readBlock = null;
                    readBlockPos = 0;
                }
                return b;
            }

            // 3) If we are receiving a write block.
            if(writeBuf != null)
            {
                if(expectingWriteToken)
                {
                    if(data == 0xFE) // start-block token
                    {
                        expectingWriteToken = false;
                        writePos = 0;
                    }
                    return 0xFF;
                }
                if(writePos < 512)
                {
                    writeBuf[writePos++] = data;
                    return 0xFF;
                }
                // two CRC bytes then a data-response
                if(writePos < 514)
                {
                    writePos++;
                    if(writePos == 514)
                    {
                        FlushWrite();
                        // data response: 0x05 = data accepted
                        respQueue.Enqueue(0x05);
                        // then busy(0x00) once, then ready(0xFF)
                        respQueue.Enqueue(0x00);
                        respQueue.Enqueue(0xFF);
                        writeBuf = null;
                        writePos = 0;
                    }
                    return 0xFF;
                }
                return 0xFF;
            }

            // 4) Command assembly. A command byte has bit7=0,bit6=1 (0x40 mask).
            if(cmdLen == 0)
            {
                if((data & 0xC0) != 0x40)
                {
                    // dummy clock / padding: reply idle-ish
                    return 0xFF;
                }
                cmdBuf[0] = data;
                cmdLen = 1;
                return 0xFF;
            }
            else
            {
                cmdBuf[cmdLen++] = data;
                if(cmdLen == 6)
                {
                    cmdLen = 0;
                    HandleCommand();
                }
                return 0xFF;
            }
        }

        public void FinishTransmission()
        {
            // Keep partial state across CS toggles; only clear the command byte
            // accumulator so a fresh transaction starts clean.
            cmdLen = 0;
        }

        private void HandleCommand()
        {
            int cmd = cmdBuf[0] & 0x3F;
            uint arg = (uint)((cmdBuf[1] << 24) | (cmdBuf[2] << 16) |
                              (cmdBuf[3] << 8) | cmdBuf[4]);

            // R1 byte: bit0 = in-idle-state.
            byte r1 = (byte)(idle ? 0x01 : 0x00);

            if(appCmd)
            {
                appCmd = false;
                switch(cmd)
                {
                    case 41: // ACMD41 SD_APP_OP_COND -> leaves idle
                        idle = false;
                        respQueue.Enqueue(0x00);
                        return;
                    default:
                        respQueue.Enqueue(r1);
                        return;
                }
            }

            switch(cmd)
            {
                case 0:  // GO_IDLE_STATE
                    idle = true;
                    respQueue.Enqueue(0x01);
                    break;
                case 8:  // SEND_IF_COND -> R7: R1 + 4 bytes (echo voltage 0x1AA)
                    respQueue.Enqueue(0x01);
                    respQueue.Enqueue(0x00);
                    respQueue.Enqueue(0x00);
                    respQueue.Enqueue(0x01);
                    respQueue.Enqueue(0xAA);
                    break;
                case 9:  // SEND_CSD -> R1 then data token + 16 CSD bytes + 2 CRC
                    respQueue.Enqueue(0x00);
                    EnqueueDataBlock(BuildCsd(), 16);
                    break;
                case 10: // SEND_CID -> R1 + 16-byte CID
                    respQueue.Enqueue(0x00);
                    EnqueueDataBlock(BuildCid(), 16);
                    break;
                case 16: // SET_BLOCKLEN
                    respQueue.Enqueue(0x00);
                    break;
                case 17: // READ_SINGLE_BLOCK
                case 18: // READ_MULT_BLOCK (treated as single here; driver loops)
                    respQueue.Enqueue(0x00);
                    QueueReadBlock(arg);
                    break;
                case 24: // WRITE_SINGLE_BLOCK
                case 25:
                    respQueue.Enqueue(0x00);
                    writeAddress = arg;
                    writeBuf = new byte[512];
                    expectingWriteToken = true;
                    break;
                case 55: // APP_CMD
                    appCmd = true;
                    respQueue.Enqueue(r1);
                    break;
                case 58: // READ_OCR -> R3: R1 + 4 bytes OCR (CCS=1 => SDHC)
                    respQueue.Enqueue(0x00);
                    respQueue.Enqueue(0xC0); // bit31 power-up done, bit30 CCS=1
                    respQueue.Enqueue(0xFF);
                    respQueue.Enqueue(0x80);
                    respQueue.Enqueue(0x00);
                    break;
                case 12: // STOP_TRANSMISSION -> R1b
                    respQueue.Enqueue(0x00);
                    respQueue.Enqueue(0x00); // busy
                    respQueue.Enqueue(0xFF); // ready
                    break;
                case 13: // SEND_STATUS -> R2 (2 bytes)
                    respQueue.Enqueue(0x00);
                    respQueue.Enqueue(0x00);
                    break;
                case 59: // CRC_ON_OFF
                    respQueue.Enqueue(0x00);
                    break;
                default:
                    respQueue.Enqueue(0x00);
                    break;
            }
        }

        private void EnqueueDataBlock(byte[] data, int len)
        {
            respQueue.Enqueue(0xFE); // start data token
            for(var i = 0; i < len; i++)
            {
                respQueue.Enqueue(data[i]);
            }
            respQueue.Enqueue(0x00); // CRC hi
            respQueue.Enqueue(0x00); // CRC lo
        }

        private void QueueReadBlock(uint address)
        {
            // SDHC addresses by block; convert to byte offset.
            long offset = (long)address * 512L;
            var block = new byte[512 + 3];
            block[0] = 0xFE; // data start token
            try
            {
                if(offset + 512 <= stream.Length)
                {
                    stream.Seek(offset, SeekOrigin.Begin);
                    var tmp = new byte[512];
                    int read = 0;
                    while(read < 512)
                    {
                        int n = stream.Read(tmp, read, 512 - read);
                        if(n <= 0) break;
                        read += n;
                    }
                    Array.Copy(tmp, 0, block, 1, 512);
                }
            }
            catch(Exception e)
            {
                this.Log(LogLevel.Warning, "SD read @0x{0:X} failed: {1}", offset, e.Message);
            }
            block[513] = 0x00; // CRC
            block[514] = 0x00;
            readBlock = block;
            readBlockPos = 0;
        }

        private void FlushWrite()
        {
            long offset = (long)writeAddress * 512L;
            try
            {
                if(offset + 512 <= stream.Length)
                {
                    stream.Seek(offset, SeekOrigin.Begin);
                    stream.Write(writeBuf, 0, 512);
                    stream.Flush();
                }
            }
            catch(Exception e)
            {
                this.Log(LogLevel.Warning, "SD write @0x{0:X} failed: {1}", offset, e.Message);
            }
        }

        private byte[] BuildCsd()
        {
            // CSD version 2.0 (SDHC/SDXC). C_SIZE encodes capacity:
            //   capacity = (C_SIZE + 1) * 512 KB
            var csd = new byte[16];
            csd[0] = 0x40; // CSD_STRUCTURE = 1 (v2.0)
            csd[1] = 0x0E;
            csd[2] = 0x00;
            csd[3] = 0x32;
            csd[4] = 0x5B;
            csd[5] = 0x59;
            csd[6] = 0x00;
            long cSize = (capacity / (512L * 1024L)) - 1;
            csd[7]  = (byte)((cSize >> 16) & 0x3F);
            csd[8]  = (byte)((cSize >> 8) & 0xFF);
            csd[9]  = (byte)(cSize & 0xFF);
            csd[10] = 0x7F;
            csd[11] = 0x80;
            csd[12] = 0x0A;
            csd[13] = 0x40;
            csd[14] = 0x00;
            csd[15] = 0x01;
            return csd;
        }

        private byte[] BuildCid()
        {
            var cid = new byte[16];
            cid[0] = 0x03;           // Manufacturer ID
            cid[1] = (byte)'S';      // OEM
            cid[2] = (byte)'D';
            cid[3] = (byte)'F';      // product name "FLIPR"
            cid[4] = (byte)'L';
            cid[5] = (byte)'I';
            cid[6] = (byte)'P';
            cid[7] = (byte)'R';
            cid[8] = 0x10;           // revision
            cid[9] = 0x00; cid[10] = 0x00; cid[11] = 0x00; cid[12] = 0x01; // serial
            cid[13] = 0x01;          // mfg date
            cid[14] = 0x40;
            cid[15] = 0x01;          // CRC | 1
            return cid;
        }

        private enum St { Idle }

        private readonly long capacity;
        private readonly string imagePath;
        private readonly FileStream stream;

        private St state;
        private byte[] cmdBuf;
        private int cmdLen;
        private System.Collections.Generic.Queue<byte> respQueue;
        private bool idle;
        private bool appCmd;

        private byte[] readBlock;
        private int readBlockPos;

        private uint writeAddress;
        private byte[] writeBuf;
        private int writePos;
        private bool expectingWriteToken;
    }
}
