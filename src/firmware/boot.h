#ifndef __BOOT_H
#define __BOOT_H

#define USE_OR_MASKS
#include "stdint.h"
#include <p18cxxx.h>

#define BOOTLDR_BASE 0x7C00
#define REBASED_RESET_VEC (0x7FFF - 64 + 1)

// Edit defines here to provide some diagnostics
#define NOTIFY_CONF() TRISB = 0x00; ADCON1 = 0x0F

#define NOTIFY_RESET() LATB = 0x00
#define NOTIFY_ENTRY() NOTIFY_CONF(); LATB = 0x01 // first led
#define NOTIFY_UNK_COMMAND() LATB = 0x08 // 1 led
#define NOTIFY_UNEXPECTED_STX() LATB = 0x18 // 2 leds
#define NOTIFY_OVERRUN() LATB = 0x38 // 3 leds
#define NOTIFY_BAD_CKSUM() LATB = 0x78 // 4 leds
#define NOTIFY_NO_FIRMWARE() NOTIFY_CONF(); LATB = 0xF8 // 5 leds

#define NOTIFY_BEGIN_FLASH() LATBbits.LATB1 = 1 // 2nd led
#define NOTIFY_END_FLASH() LATBbits.LATB1 = 0

#endif // __BOOT_H
