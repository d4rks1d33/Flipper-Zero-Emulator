//
// SPI2 chip-select router for Flipper Zero emulator.
//
// SPI2 is shared by the ST7567 display (CS=PC11) and the microSD card
// (CS=PC12). This router forwards each SPI byte to whichever device has its
// CS asserted (active low). The display also needs the D/C line (PB1).
//
// GPIO inputs:
//   0 = display CS (PC11), active low
//   1 = SD CS      (PC12), active low
//   2 = display D/C (PB1): high=data, low=command
//
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.SPI;
using Antmicro.Renode.Peripherals.Video;

namespace Antmicro.Renode.Peripherals.SPI
{
    public class Spi2Router : ISPIPeripheral, IGPIOReceiver
    {
        public Spi2Router(IMachine machine, ST7567 display, ISPIPeripheral sdcard = null)
        {
            this.display = display;
            this.machine = machine;
            this.sdcard = sdcard;
            displaySelected = false;
            sdSelected = false;
        }

        public void SetSDCard(ISPIPeripheral card)
        {
            sdcard = card;
        }

        public void Reset()
        {
            displaySelected = false;
            sdSelected = false;
        }

        public void OnGPIO(int number, bool value)
        {
            switch(number)
            {
                case 0: displaySelected = !value; break;
                case 1:
                    // SD CS (active low). On a fresh assert (deselected->selected)
                    // tell the card a new transaction is starting so any leftover
                    // response/read-block bytes from an aborted transfer are
                    // dropped instead of corrupting the next command. Without this
                    // an intermittent FR_DISK_ERR appears during f_open.
                    {
                        bool nowSelected = !value;
                        if(nowSelected && !sdSelected)
                        {
                            (sdcard as ITransactionResettable)?.BeginTransaction();
                        }
                        sdSelected = nowSelected;
                    }
                    break;
                case 2: display.OnGPIO(0, value); break;  // D/C forwarded to display
            }
        }

        public byte Transmit(byte data)
        {
            // SD card has priority when its CS is asserted (updater flow).
            if(sdSelected && sdcard != null)
            {
                return sdcard.Transmit(data);
            }
            if(displaySelected)
            {
                return display.Transmit(data);
            }
            // No CS asserted: do not disturb either device's state machine.
            return 0xFF;
        }

        public void FinishTransmission()
        {
            // Only finish the transaction for the currently selected device.
            if(sdSelected && sdcard != null)
            {
                sdcard.FinishTransmission();
            }
            else if(displaySelected)
            {
                display.FinishTransmission();
            }
        }

        private readonly ST7567 display;
        private readonly IMachine machine;
        private ISPIPeripheral sdcard;
        private bool displaySelected;
        private bool sdSelected;
    }
}
