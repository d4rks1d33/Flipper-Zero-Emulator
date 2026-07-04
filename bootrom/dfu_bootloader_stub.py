#!/usr/bin/env python3
"""
Minimal DFU / boot-mode helper for the Flipper Zero emulator.

On real hardware the STM32WB55 system ROM (0x1FFF0000) can enter USB DFU for
recovery. The Flipper firmware also implements an "alt boot" path
(flipper_boot_dfu_exec / flipper_boot_update_exec) selected at power-on by:
  - holding LEFT (DFU) / UP (recovery), or
  - the boot mode stored in the RTC backup registers.

Boot modes (furi_hal_rtc.h FuriHalRtcBootMode) live in the RTC "System"
backup register (FuriHalRtcRegisterSystem = BKP1R), in the SystemReg bitfield:
    log_level[3:0] log_reserved[7:4] flags[15:8] boot_mode[19:16] ...
So boot_mode occupies bits [19:16].

    0 = Normal
    1 = Dfu
    2 = Update (OTA self-flash from SD)

The RTC "Header" backup register (BKP0R) must contain a valid magic/version or
furi_hal_rtc_init_early() wipes all backup registers:
    magic[15:0]   = 0x10F1
    version[23:16]= 0

Backup register addresses: RTC_BASE(0x40002800) + 0x50 + index*4
    BKP0R (Header) = 0x40002850
    BKP1R (System) = 0x40002854
"""

RTC_BKP0R_ADDR = 0x40002850  # Header
RTC_BKP1R_ADDR = 0x40002854  # System (boot mode)

HEADER_MAGIC = 0x10F1
HEADER_VERSION = 0

BOOT_MODE_NORMAL = 0
BOOT_MODE_DFU = 1
BOOT_MODE_UPDATE = 2


def header_register_value():
    return (HEADER_VERSION << 16) | HEADER_MAGIC


def system_register_value(boot_mode, existing=0):
    existing &= ~(0xF << 16)
    return existing | ((boot_mode & 0xF) << 16)


def renode_commands(boot_mode):
    """Return Renode monitor commands to force a boot mode with a valid header."""
    return [
        f"sysbus WriteDoubleWord {hex(RTC_BKP0R_ADDR)} {hex(header_register_value())}",
        f"sysbus WriteDoubleWord {hex(RTC_BKP1R_ADDR)} {hex(system_register_value(boot_mode))}",
    ]


if __name__ == "__main__":
    import sys
    mode = {"normal": 0, "dfu": 1, "update": 2}.get(
        sys.argv[1] if len(sys.argv) > 1 else "update", 2)
    for cmd in renode_commands(mode):
        print(cmd)
