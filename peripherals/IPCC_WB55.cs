//
// STM32WB55 IPCC (Inter-Processor Communication Controller) for the Flipper
// Zero emulator.
//
// The IPCC is the hardware mailbox between CPU1 (the Cortex-M4 running the
// Flipper application firmware, which we emulate) and CPU2 (the Cortex-M0+
// running the closed BLE/FUS wireless stack, which we do NOT emulate).
//
// Register map (base 0x58000C00, RM0434):
//     0x00 C1CR      0x04 C1MR      0x08 C1SCR     0x0C C1TOC2SR
//     0x10 C2CR      0x14 C2MR      0x18 C2SCR     0x1C C2TOC1SR
//
// This model does two things:
//
//  (1) It keeps the behaviour of the previous Python stub (peripherals/
//      ipcc_stub.py): C1TOC2SR and C2TOC1SR always read 0. Reading C1TOC2SR==0
//      makes LL_C1_IPCC_IsActiveFlag_CHx() return false immediately, so the
//      HW_IPCC_SYS_SendCmd() busy-wait (hw_ipcc.c:120) exits at once instead of
//      spinning for 33 s and hitting furi_check("HW_IPCC_SYS_SendCmd timeout").
//      C1MR resets to 0x3F3F (all channels masked), matching the stub.
//
//  (2) It fakes the CPU2 "wireless stack ready" boot handshake so that
//      ble_glue_wait_for_c2_start() (ble_glue.c:204) succeeds and the firmware
//      believes a radio stack of a chosen version is installed. See the long
//      comment on DeliverReadyEvent() for the exact mechanism, all verified
//      against the firmware sources (targets/f7/ble_glue + lib/stm32wb_copro).
//
// ─── The CPU2 "ready" handshake, verified against the firmware ───────────────
//
// Boot order in ble_glue_init() (ble_glue.c):
//     TL_Init();                 // populates the MB_RefTable in SRAM2A
//     ble_event_thread_start();
//     shci_init(...);            // registers the SYS user-event callback
//     TL_MM_Init(...);
//     TL_Enable();               // -> HW_IPCC_Enable() + channel unmasking
//
// TL_Init() (tl_mbox.c:87) fills the reference table pointed to by
// FLASH->IPCCBR (IPCCDBA). On this platform IPCCDBA=0, so the MB_RefTable lives
// at SRAM2A_BASE = 0x20030000. By the time the firmware unmasks the SYSTEM RX
// channel (during TL_Enable), TL_Init has already run, so the mailbox tables are
// populated and safe to read.
//
// HW_IPCC_SYS_Init() (called from TL_Enable via shci) does
//     LL_C1_IPCC_EnableReceiveChannel(IPCC, HW_IPCC_SYSTEM_EVENT_CHANNEL)
// which is CLEAR_BIT(C1MR, LL_IPCC_CHANNEL_2)  (stm32wbxx_ll_ipcc.h:360).
// HW_IPCC_SYSTEM_EVENT_CHANNEL == LL_IPCC_CHANNEL_2 == 0x02 (mbox_def.h:267),
// and the receive-occupied mask bits (CHxOM) live in C1MR[5:0]
// (stm32wb55xx.h: IPCC_C1MR_CH1OM_Pos = 0). So the robust trigger for "the
// firmware just enabled the SYSTEM receive channel" is: a C1MR write that
// CLEARS bit 1 (CH2OM). That is exactly when the firmware is ready to receive
// the ready event on the system channel. We arm the delivery once, on that
// edge.
//
// HW_IPCC_Rx_Handler() (the IPCC_C1_RX ISR) checks
//     HW_IPCC_RX_PENDING(chan) = LL_C2_IPCC_IsActiveFlag_CHx(chan)   // C2TOC1SR
//                              && LL_C1_IPCC_IsEnabledReceiveChannel(chan)
// then, for the system channel, calls HW_IPCC_SYS_EvtHandler() which drains the
// SystemEvtQueue (tl_mbox.c:238) and calls the SHCI callback for each node.
// So to deliver an event we must (a) place a packet in the SystemEvtQueue and
// (b) make C2TOC1SR read the channel-2 flag as active for the duration of the
// ISR, then raise the IPCC_C1_RX NVIC line (IRQn 44 on the WB55, verified in
// lib/stm32wb_cmsis/Include/stm32wb55xx.h:128).
//
// The event packet is a TL_EvtPacket_t (tl.h). Its first 8 bytes are the
// TL_PacketHeader_t {next, prev} that doubles as the intrusive list node; the
// event body follows:
//     +0x00 next        (u32)   list linkage (written by LST_insert_tail)
//     +0x04 prev        (u32)
//     +0x08 type        (u8) = TL_SYSEVT_PKT_TYPE (0x12)   (tl.h:45)
//     +0x09 evtcode     (u8) = SHCI_EVTCODE (0xFF)         (shci.h)
//     +0x0A plen        (u8) = 3  (subevtcode[2] + ready_rsp[1])
//     +0x0B subevtcode  (u16 LE) = SHCI_SUB_EVT_CODE_READY (0x9200) (shci.h:56)
//     +0x0D ready_rsp   (u8) = WIRELESS_FW_RUNNING (0x00)   (shci.h:36)
// ble_sys_user_event_callback (ble_glue.c:338) reads exactly these fields and,
// on subevtcode==READY with ready_rsp==WIRELESS_FW_RUNNING, sets
// c2_info.mode = Stack and status = C2Started.
//
// We locate the queue head like the firmware does:
//     p_sys_table  = [MB_RefTable + 0x0C]   (MB_RefTable.p_sys_table, mbox_def.h)
//     sys_queue    = [p_sys_table + 0x04]   (MB_SysTable.sys_queue, mbox_def.h)
// sys_queue is &SystemEvtQueue, a circular tListNode {next, prev} the firmware
// initialised empty (next=prev=&self) in TL_SYS_Init (tl_mbox.c:198). We insert
// our packet at the tail with LST_insert_tail's exact pointer arithmetic
// (stm_list.c:71).
//
// ─── Version reporting ───────────────────────────────────────────────────────
// After the ready event, the firmware calls SHCI_GetWirelessFwInfo()
// (ble_glue.c:149 -> shci.c:672), which is LOCAL: it reads the device info table
// from SRAM. It first dereferences the FUS validity keyword at
// [p_device_info_table + 0x00]; if it equals FUS_DEVICE_INFO_TABLE_VALIDITY_
// KEYWORD (0xA94656B9, shci.h:917) it treats CPU2 as FUS, otherwise as the
// wireless stack. We therefore force that word to a non-FUS value and fill:
//     [p_device_info_table + 0x10] = WirelessFwInfoTable.Version   (RadioStackVersion)
//     [p_device_info_table + 0x18] = WirelessFwInfoTable.InfoStack (RadioStackType, low byte)
// (MB_DeviceInfoTable_t layout: SafeBoot.Version@0x00, FusInfo@0x04..0x0F,
// WirelessFwInfoTable.Version@0x10, MemorySize@0x14, InfoStack@0x18 - mbox_def.h.)
//
// p_device_info_table = [MB_RefTable + 0x00] (mbox_def.h:151).
//
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Utilities;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class IPCC_WB55 : IDoubleWordPeripheral, IKnownSize, INumberedGPIOOutput
    {
        public IPCC_WB55(IMachine machine)
        {
            this.machine = machine;

            // IPCC_C1_RX_IRQn == 44 on the STM32WB55 (CMSIS
            // stm32wb55xx.h:128). Exposed as GPIO output 0 (property IRQ) so the
            // platform can wire it: `IRQ -> nvic@44`.
            IRQ = new GPIO();
            Connections = new ReadOnlyDictionary<int, IGPIO>(
                new Dictionary<int, IGPIO> { { 0, IRQ } });

            registers = new DoubleWordRegisterCollection(this);
            DefineRegisters();

            // Defaults: the CPU2 wireless stack is reported ABSENT on a normal
            // boot, exactly like the old Python stub -- there is no real Core2, so
            // BtSrv must take its graceful "no radio stack" path (a normal Flipper
            // desktop with BLE simply unavailable). If we faked a present stack
            // here, ble_app_init() would try to start the real BLE app against a
            // nonexistent coprocessor and furi_check-crash BtSrv into a boot loop.
            //
            // The OTA UPDATER is the only flow that needs a "present, matching"
            // stack (so update_task_manage_radiostack validates and skips the radio
            // flash). run_updater.resc's generated update_manifest_params.resc sets
            // RadioStackPresent=true + the manifest's version just for that run.
            RadioStackVersion = DefaultRadioStackVersion;
            RadioStackType = DefaultRadioStackType;
            RadioStackPresent = false;

            Reset();
        }

        public uint ReadDoubleWord(long offset)
        {
            switch(offset)
            {
                // C1TOC2SR and C2TOC1SR ALWAYS read 0. C1TOC2SR==0 => the SYS
                // send-cmd busy-wait exits instantly (no 33 s timeout). C2TOC1SR
                // is presented as 0 for normal polling; the only moment CPU2 has
                // a "flag active" is the brief window during our synthetic RX ISR
                // (see DeliverReadyEvent), handled via c2ToC1SrOverride.
                case RegC1ToC2Sr:
                    return 0;
                case RegC2ToC1Sr:
                    return c2ToC1SrOverride;
                default:
                    return registers.Read(offset);
            }
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            switch(offset)
            {
                case RegC1Scr:
                    // CHxS bits [16:21] set a CPU1->CPU2 transmit flag; we treat
                    // Core2 as servicing them instantly, so C1TOC2SR stays 0.
                    //
                    // CHxC bits [5:0] CLEAR a received CPU2->CPU1 flag (the ISR
                    // calls LL_C1_IPCC_ClearFlag_CHx after draining the queue).
                    // When the firmware clears the SYSTEM channel (bit 1), drop
                    // our C2TOC1SR override so subsequent RX_PENDING checks read 0.
                    if((value & SystemChannelFlag) != 0)
                    {
                        c2ToC1SrOverride &= ~SystemChannelFlag;
                        // No more pending SYSTEM RX flag -> deassert the line.
                        if(c2ToC1SrOverride == 0)
                        {
                            IRQ.Set(false);
                        }
                    }
                    return;
                case RegC1ToC2Sr:
                case RegC2ToC1Sr:
                    // Status registers are read-only from CPU1's perspective.
                    return;
                default:
                    registers.Write(offset, value);
                    return;
            }
        }

        public void Reset()
        {
            registers.Reset();
            c1mr = ResetC1Mr;
            c2ToC1SrOverride = 0;
            readyArmed = false;
            readyDelivered = false;
            IRQ.Unset();
        }

        public long Size => 0x400;

        public IReadOnlyDictionary<int, IGPIO> Connections { get; }

        // IPCC_C1_RX (IRQn 44). Wired in the platform as `IRQ -> nvic@44`.
        public GPIO IRQ { get; }

        // ─── Monitor-settable properties (auto-exposed by the Renode monitor) ──
        // Usage from a .resc / monitor:
        //     ipcc RadioStackVersion 0x01140000
        //     ipcc RadioStackType 0x01
        //     ipcc RadioStackPresent true
        // The packed Version word follows the CPU2 layout (mbox_def.h):
        //   [24:31]=Major [16:23]=Minor [8:15]=Sub [4:7]=Branch [0:3]=BuildType.
        public uint RadioStackVersion { get; set; }
        public byte RadioStackType { get; set; }
        public bool RadioStackPresent { get; set; }

        private void DefineRegisters()
        {
            // C1CR (0x00): RXOIE bit0, TXFIE bit16. Plain storage; the interrupt
            // enables do not need side effects here because we drive the RX line
            // ourselves and gate delivery on the C1MR channel unmask instead.
            registers.DefineRegister(RegC1Cr)
                .WithValueField(0, 32, name: "C1CR");

            // C1MR (0x04, reset 0x3F3F): CHxOM (RX occupied mask) in [5:0],
            // CHxFM (TX free mask) in [21:16]. The firmware unmasks the SYSTEM
            // receive channel (channel 2) by clearing bit 1 (CH2OM) via
            // LL_C1_IPCC_EnableReceiveChannel(SYSTEM_EVENT_CHANNEL). That is our
            // trigger to arm the ready-event delivery.
            registers.DefineRegister(RegC1Mr, ResetC1Mr)
                .WithValueField(0, 32,
                    valueProviderCallback: _ => c1mr,
                    writeCallback: (_, v) => OnC1MrWrite((uint)v),
                    name: "C1MR");

            // C1SCR (0x08): write-only set/clear. Handled in WriteDoubleWord.
            registers.DefineRegister(RegC1Scr)
                .WithValueField(0, 32, FieldMode.Write, name: "C1SCR");

            // C1TOC2SR (0x0C): always reads 0 (handled in ReadDoubleWord).
            registers.DefineRegister(RegC1ToC2Sr)
                .WithValueField(0, 32, FieldMode.Read, valueProviderCallback: _ => 0, name: "C1TOC2SR");

            // CPU2-side registers: accept and store (the firmware occasionally
            // touches C2CR/C2MR; C2SCR is CPU2's set/clear).
            registers.DefineRegister(RegC2Cr)
                .WithValueField(0, 32, name: "C2CR");
            registers.DefineRegister(RegC2Mr)
                .WithValueField(0, 32, name: "C2MR");
            registers.DefineRegister(RegC2Scr)
                .WithValueField(0, 32, FieldMode.Write, name: "C2SCR");

            // C2TOC1SR (0x1C): read via ReadDoubleWord (override during ISR).
            registers.DefineRegister(RegC2ToC1Sr)
                .WithValueField(0, 32, FieldMode.Read, valueProviderCallback: _ => c2ToC1SrOverride, name: "C2TOC1SR");
        }

        private void OnC1MrWrite(uint value)
        {
            var previous = c1mr;
            c1mr = value;

            // Detect the falling edge on CH2OM (bit 1): the firmware just enabled
            // the SYSTEM receive channel. Arm the (one-shot) ready delivery.
            var wasMasked = (previous & SystemRxMaskBit) != 0;
            var nowUnmasked = (value & SystemRxMaskBit) == 0;
            if(wasMasked && nowUnmasked && !readyDelivered && !readyArmed && ShouldFakeStackPresent())
            {
                readyArmed = true;
                this.Log(LogLevel.Info,
                    "IPCC: firmware unmasked SYSTEM RX channel (C1MR CH2OM cleared); scheduling CPU2 ready event");
                // Defer to a synced state. Even though TL_Init has already run by
                // the time C1MR is written, deferring keeps us off the guest's
                // in-flight translation block and lets the SHCI event thread be
                // scheduled to consume the event, mirroring real asynchronous
                // CPU2 delivery.
                machine.LocalTimeSource.ExecuteInNearestSyncedState(_ => DeliverReadyEvent());
            }
        }

        // Whether to fake a present, ready CPU2 wireless stack on this boot.
        //
        // We do it ONLY when RadioStackPresent has been explicitly enabled from
        // the monitor (the OTA updater flow, via update_manifest_params.resc) AND
        // the current RTC boot_mode is "Update" (3). The updater needs C2 to look
        // present+matching so update_task_manage_radiostack validates and skips
        // re-flashing the radio. On any other boot (Normal/PostUpdate) we leave
        // the stack absent so BtSrv takes its graceful no-radio path instead of
        // furi_check-crashing when ble_app_init talks to a nonexistent Core2.
        //
        // boot_mode lives in RTC BKP1R (0x40002854) bits [19:16]; Update == 3
        // (FuriHalRtcBootModeUpdate). Reading it makes the behavior correct across
        // the whole update -> reboot -> normal chain without needing to unset the
        // property between the in-process reboots.
        private bool ShouldFakeStackPresent()
        {
            if(!RadioStackPresent)
            {
                return false;
            }
            try
            {
                var systemReg = machine.SystemBus.ReadDoubleWord(RtcBkp1rAddress);
                var bootMode = (systemReg >> RtcBootModeShift) & 0xF;
                return bootMode == RtcBootModeUpdate;
            }
            catch(Exception)
            {
                // If the RTC can't be read for some reason, err on the safe side
                // (absent) so we never crash-loop a normal boot.
                return false;
            }
        }

        private void DeliverReadyEvent()
        {
            if(readyDelivered)
            {
                return;
            }

            var sysbus = machine.SystemBus;

            // Walk the mailbox reference table (built by TL_Init in SRAM2A).
            var pDeviceInfoTable = sysbus.ReadDoubleWord(MbRefTableBase + OffRefDeviceInfoTable);
            var pSysTable = sysbus.ReadDoubleWord(MbRefTableBase + OffRefSysTable);
            if(pSysTable == 0 || pDeviceInfoTable == 0)
            {
                // Tables not populated yet - re-arm and retry shortly. This should
                // not happen given the TL_Init/TL_Enable ordering, but guard for it.
                this.Log(LogLevel.Warning,
                    "IPCC: mailbox tables not ready (sys=0x{0:X8} devinfo=0x{1:X8}); retrying ready delivery",
                    pSysTable, pDeviceInfoTable);
                machine.LocalTimeSource.ExecuteInNearestSyncedState(_ => DeliverReadyEvent());
                return;
            }

            var sysQueueAddr = sysbus.ReadDoubleWord(pSysTable + OffSysTableSysQueue);
            if(sysQueueAddr == 0)
            {
                this.Log(LogLevel.Warning,
                    "IPCC: SystemEvtQueue pointer not ready (p_sys_table=0x{0:X8}); retrying", pSysTable);
                machine.LocalTimeSource.ExecuteInNearestSyncedState(_ => DeliverReadyEvent());
                return;
            }

            // Publish the wireless-stack version info the firmware will read via
            // SHCI_GetWirelessFwInfo() right after C2Started.
            WriteDeviceInfo(sysbus, pDeviceInfoTable);

            // Build the TL_EvtPacket_t in our reserved SRAM2A scratch area.
            BuildReadyPacket(sysbus);

            // Insert the packet at the tail of the circular SystemEvtQueue,
            // replicating LST_insert_tail(listHead=sysQueueAddr, node=EventPacketBase):
            //     node.next        = listHead
            //     node.prev        = listHead.prev            (old tail)
            //     listHead.prev    = node
            //     (node.prev).next = node                     (old_tail.next = node)
            var oldTail = sysbus.ReadDoubleWord(sysQueueAddr + OffNodePrev);
            sysbus.WriteDoubleWord(EventPacketBase + OffNodeNext, sysQueueAddr);
            sysbus.WriteDoubleWord(EventPacketBase + OffNodePrev, oldTail);
            sysbus.WriteDoubleWord(sysQueueAddr + OffNodePrev, EventPacketBase);
            sysbus.WriteDoubleWord(oldTail + OffNodeNext, EventPacketBase);

            this.Log(LogLevel.Info,
                "IPCC: enqueued CPU2 READY event (queue=0x{0:X8}, packet=0x{1:X8}); raising IPCC_C1_RX",
                sysQueueAddr, EventPacketBase);

            readyDelivered = true;
            readyArmed = false;

            // Present channel-2 as active in C2TOC1SR so
            // HW_IPCC_RX_PENDING(SYSTEM_EVENT_CHANNEL) reads true when the ISR
            // runs, then raise the IPCC_C1_RX NVIC line. We keep the override
            // ASSERTED (do NOT clear it here): the ISR reads C2TOC1SR
            // asynchronously after the CPU takes the interrupt, and the firmware
            // clears the flag itself by writing C1SCR CHxC (bit 1) once it has
            // drained the SystemEvtQueue -- that write drops the override (see
            // WriteDoubleWord/RegC1Scr). We hold the IRQ line high until then so
            // it behaves like a real level-sensitive mailbox interrupt.
            c2ToC1SrOverride = SystemChannelFlag;
            IRQ.Set(true);
        }

        private void WriteDeviceInfo(IBusController sysbus, uint pDeviceInfoTable)
        {
            // SafeBootInfoTable.Version (@+0x00) doubles as the FUS validity
            // keyword. Force it to a NON-FUS value so SHCI_GetWirelessFwInfo takes
            // the "wireless stack running" branch (shci.c:710).
            sysbus.WriteDoubleWord(pDeviceInfoTable + OffDevInfoSafeBootVersion, NonFusKeyword);

            // WirelessFwInfoTable.Version (@+0x10) and .InfoStack (@+0x18).
            sysbus.WriteDoubleWord(pDeviceInfoTable + OffDevInfoWirelessVersion, RadioStackVersion);
            sysbus.WriteDoubleWord(pDeviceInfoTable + OffDevInfoWirelessInfoStack, (uint)RadioStackType);

            this.Log(LogLevel.Info,
                "IPCC: published radio-stack info (Version=0x{0:X8}, StackType=0x{1:X2}) at device_info=0x{2:X8}",
                RadioStackVersion, RadioStackType, pDeviceInfoTable);
        }

        private void BuildReadyPacket(IBusController sysbus)
        {
            // next/prev are set by the queue insertion below; zero them for
            // cleanliness. The event body starts at +0x08 (see file header).
            sysbus.WriteDoubleWord(EventPacketBase + OffNodeNext, 0);
            sysbus.WriteDoubleWord(EventPacketBase + OffNodePrev, 0);

            var body = new byte[]
            {
                TlSysEvtPktType,                 // +0x08 type = 0x12
                ShciEvtCode,                     // +0x09 evtcode = 0xFF
                ReadyPayloadLen,                 // +0x0A plen = 0x03
                (byte)(ShciSubEvtCodeReady & 0xFF),        // +0x0B subevtcode LE lo (0x00)
                (byte)((ShciSubEvtCodeReady >> 8) & 0xFF), // +0x0C subevtcode LE hi (0x92)
                WirelessFwRunning,               // +0x0D ready_rsp = 0x00
            };
            sysbus.WriteBytes(body, EventPacketBase + OffEvtBody);
        }

        // ─── Register offsets ────────────────────────────────────────────────
        private const long RegC1Cr = 0x00;
        private const long RegC1Mr = 0x04;
        private const long RegC1Scr = 0x08;
        private const long RegC1ToC2Sr = 0x0C;
        private const long RegC2Cr = 0x10;
        private const long RegC2Mr = 0x14;
        private const long RegC2Scr = 0x18;
        private const long RegC2ToC1Sr = 0x1C;

        // C1MR reset: all channels masked (matches the previous Python stub).
        private const uint ResetC1Mr = 0x3F3F;
        // CH2OM (SYSTEM receive-channel occupied mask) is C1MR bit 1.
        private const uint SystemRxMaskBit = 1u << 1;
        // Channel-2 flag bit in C2TOC1SR (LL_IPCC_CHANNEL_2 == 0x02).
        private const uint SystemChannelFlag = 0x02;

        // ─── Mailbox reference table (SRAM2A, IPCCDBA=0 on this platform) ──────
        private const uint Sram2aBase = 0x20030000;
        private const uint MbRefTableBase = Sram2aBase; // (IPCCDBA<<2) + SRAM2A_BASE, IPCCDBA=0
        private const uint OffRefDeviceInfoTable = 0x00; // MB_RefTable.p_device_info_table
        private const uint OffRefSysTable = 0x0C;        // MB_RefTable.p_sys_table
        private const uint OffSysTableSysQueue = 0x04;   // MB_SysTable.sys_queue

        // MB_DeviceInfoTable_t field offsets.
        private const uint OffDevInfoSafeBootVersion = 0x00;    // SafeBootInfoTable.Version (FUS keyword)
        private const uint OffDevInfoWirelessVersion = 0x10;    // WirelessFwInfoTable.Version
        private const uint OffDevInfoWirelessInfoStack = 0x18;  // WirelessFwInfoTable.InfoStack

        // tListNode / TL_PacketHeader_t offsets within a packet.
        private const uint OffNodeNext = 0x00;
        private const uint OffNodePrev = 0x04;
        private const uint OffEvtBody = 0x08;

        // Scratch packet buffer, high in SRAM2A (0x20030000..0x20032800, 10 KB).
        // Placed at 0x20032780 (14-byte packet fits before 0x20032800) so it
        // never collides with the firmware's TL_RefTable/tables at the low end.
        private const uint EventPacketBase = 0x20032780;

        // RTC BKP1R (FuriHalRtcRegisterSystem): boot_mode in bits [19:16].
        private const long RtcBkp1rAddress = 0x40002854;
        private const int RtcBootModeShift = 16;
        private const uint RtcBootModeUpdate = 3; // FuriHalRtcBootModeUpdate

        // ─── SHCI ready-event constants (verified against firmware headers) ────
        private const byte TlSysEvtPktType = 0x12;      // tl.h:45 TL_SYSEVT_PKT_TYPE
        private const byte ShciEvtCode = 0xFF;          // shci.h SHCI_EVTCODE
        private const byte ReadyPayloadLen = 0x03;      // subevtcode(2) + ready_rsp(1)
        private const ushort ShciSubEvtCodeReady = 0x9200; // shci.h:56 SHCI_SUB_EVT_CODE_BASE
        private const byte WirelessFwRunning = 0x00;    // shci.h:36 WIRELESS_FW_RUNNING
        private const uint NonFusKeyword = 0x00000000;  // != FUS_DEVICE_INFO_TABLE_VALIDITY_KEYWORD (0xA94656B9)

        // Defaults: 1.20.0, BLE_FULL. Version packing (mbox_def.h):
        // [24:31]=Major=1, [16:23]=Minor=0x14(20), [8:15]=Sub=0, [4:7]=Branch=0,
        // [0:3]=BuildType=0.
        private const uint DefaultRadioStackVersion = 0x01140000;
        private const byte DefaultRadioStackType = 0x01; // INFO_STACK_TYPE_BLE_FULL

        private readonly IMachine machine;
        private readonly DoubleWordRegisterCollection registers;

        private uint c1mr;
        private uint c2ToC1SrOverride;
        private bool readyArmed;
        private bool readyDelivered;
    }
}
