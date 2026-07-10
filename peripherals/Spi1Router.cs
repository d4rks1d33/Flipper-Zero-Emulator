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
// GPIO OUTPUTS (INumberedGPIOOutput), wired in the platform .repl:
//   output 0 = CC1101 GDO0  -> PA1  (gpio_cc1101_g0)
//   output 1 = ST25R3916 IRQ -> PA2 (gpio_nfc_irq_rfid_pull)
//
// GDO0/PA1 (CC1101 power-on self test)
// ────────────────────────────────────
// furi_hal_subghz_init writes IOCFG0=CC1101IocfgHW (GDO0 low), waits for PA1 to
// read low within 10 ms, then writes IOCFG0=CC1101IocfgHW|INV (GDO0 high) and
// waits for PA1 high. With no antenna the real chip toggles GDO0; we mirror the
// IOCFG0 INV bit onto PA1 here so the self test PASSES and subghz reports
// "Init OK" instead of "Init Fail". Driving PA1 also pokes EXTI line 1, but the
// SYSCFG EXTICR mux blocks it unless PA is selected for that line, so it is
// inert during init (gpio_cc1101_g0 is configured as a plain input, no ISR).
//
// NFC IRQ/PA2 (ST25R3916)
// ───────────────────────
// The router exposes an IRQ output for completeness/faithful wiring, but the
// ST25R3916 model deliberately never asserts it (see ST25R3916.cs): the NFC
// init's IRQ waits (osc-start, measure-VDD) are timeout-bounded, so leaving PA2
// low makes them time out cleanly into the low-power path with NO hang. PA2 is
// also the RFID pull pin (shared), driven as an OUTPUT by the firmware in the
// RFID paths; we only ever drive it from the NFC side, which the model keeps
// quiescent, so there is no conflict.
//
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.SPI;

namespace Antmicro.Renode.Peripherals.SPI
{
    public class Spi1Router : ISPIPeripheral, IGPIOReceiver, INumberedGPIOOutput
    {
        public Spi1Router()
        {
            cc1101 = new CC1101();
            nfc = new ST25R3916();
            ccSelected = false;
            nfcSelected = false;

            var connections = new Dictionary<int, IGPIO>
            {
                { 0, new GPIO() }, // GDO0 -> PA1
                { 1, new GPIO() }, // NFC IRQ -> PA2
            };
            Connections = new ReadOnlyDictionary<int, IGPIO>(connections);

            // Forward device signals to the GPIO outputs.
            cc1101.Gdo0Changed = level => Connections[0].Set(level);
            nfc.IrqChanged = level => Connections[1].Set(level);
        }

        public void Reset()
        {
            cc1101.Reset();
            nfc.Reset();
            ccSelected = false;
            nfcSelected = false;
            Connections[0].Set(false);
            Connections[1].Set(false);
        }

        // CS lines are active-low: value=false means selected.
        //
        // Each device frames its SPI protocol by byte position within a CS-low
        // window (byte 1 = header/command, byte 2+ = data). Renode's STM32SPI
        // does not reliably call FinishTransmission on every CS edge, so we reset
        // the just-deselected device's frame state HERE, on the CS rising edge.
        // Without this, a stray leftover byteIndex leaks into the next
        // transaction and header/data get swapped (e.g. IOCFG0 writes decode with
        // addr and value transposed). Resetting on deselect makes each CS window
        // start cleanly at byte 1.
        public void OnGPIO(int number, bool value)
        {
            if(number == 0)
            {
                var wasSelected = ccSelected;
                ccSelected = !value;
                if(wasSelected && value) // CC1101 CS rising edge (deselect)
                {
                    cc1101.FinishTransmission();
                }
            }
            else if(number == 1)
            {
                var wasSelected = nfcSelected;
                nfcSelected = !value;
                if(wasSelected && value) // NFC CS rising edge (deselect)
                {
                    nfc.FinishTransmission();
                }
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

        public IReadOnlyDictionary<int, IGPIO> Connections { get; }

        private readonly CC1101 cc1101;
        private readonly ST25R3916 nfc;
        private bool ccSelected;
        private bool nfcSelected;
    }
}
