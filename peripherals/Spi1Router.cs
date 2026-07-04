//
// SPI1 chip-select router for Flipper Zero emulator.
//
// SPI1 is shared by the CC1101 (SubGHz, CS=PD0) and ST25R3916 (NFC, CS=PE4).
// Renode's STM32SPI supports a single registered slave, so this router sits on
// SPI1 and forwards each byte to whichever device currently has CS asserted.
//
// CS lines are delivered as GPIO inputs:
//   input 0 = CC1101 CS (PD0), active low
//   input 1 = NFC CS   (PE4), active low
//
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.SPI;

namespace Antmicro.Renode.Peripherals.SPI
{
    public class Spi1Router : ISPIPeripheral, IGPIOReceiver
    {
        public Spi1Router()
        {
            cc1101 = new CC1101();
            nfc = new ST25R3916();
            ccSelected = false;
            nfcSelected = false;
        }

        public void Reset()
        {
            cc1101.Reset();
            nfc.Reset();
            ccSelected = false;
            nfcSelected = false;
        }

        // CS lines are active-low: value=false means selected.
        public void OnGPIO(int number, bool value)
        {
            if(number == 0)
            {
                ccSelected = !value;
            }
            else if(number == 1)
            {
                nfcSelected = !value;
            }
        }

        public byte Transmit(byte data)
        {
            if(ccSelected)
            {
                return cc1101.Transmit(data);
            }
            if(nfcSelected)
            {
                return nfc.Transmit(data);
            }
            return 0x00;
        }

        public void FinishTransmission()
        {
            cc1101.FinishTransmission();
            nfc.FinishTransmission();
        }

        private readonly CC1101 cc1101;
        private readonly ST25R3916 nfc;
        private bool ccSelected;
        private bool nfcSelected;
    }
}
