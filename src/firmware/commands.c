//
//  This file is distributed under simplified BSD-like license.
//
//  Copyright (c) 2011, anton@drachev.com
//  All rights reserved.
//
//  Redistribution and use in source and binary forms, with or without
//  modification, are permitted provided that the following conditions are met:
//
//  1. Redistributions of source code must retain the above copyright notice, this
//     list of conditions and the following disclaimer.
//  2. Redistributions in binary form must reproduce the above copyright notice,
//     this list of conditions and the following disclaimer in the documentation
//     and/or other materials provided with the distribution.
//
//  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
//  ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
//  WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
//  DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
//  ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
//  (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
//  LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
//  ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
//  (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
//  SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
///////////////////////////////////////////////////////////////////////////////////

#include "boot.h"

uint8_t origReset[4];
volatile struct {
    unsigned unlockBoot:1;
} flags = { 0 };

typedef struct {
    uint8_t startH;
    uint8_t startL;
    uint8_t count;
} eraseBlock_t;

#define tblwt_preinc(val) TABLAT = val; _asm TBLWTPREINC _endasm

void ExecWrite(void)
{
    if (!flags.unlockBoot && TBLPTR >= BOOTLDR_BASE) {
        EECON1bits.WREN = 0;
    }
    NOTIFY_BEGIN_FLASH();
    EECON2 = 0x55; // Required Sequence
    EECON2 = 0xAA;
    EECON1bits.WR = 1; // start write/erase (CPU stall) ??ms
    NOTIFY_END_FLASH();
}

// Format: 0x11, [ ADDRH, ADDRL, PAGECOUNT ]*
void EraseCmd(void)
{
    static uint8_t i;
    static eraseBlock_t* blocks;

    blocks = (eraseBlock_t*)&buff[1];
    for (i = dataLen - 1; i; i -= 3) {
        TBLPTRL = blocks->startL & 0xC0; // 64-byte aligned
        TBLPTRH = blocks->startH;
        while (blocks->count--) {
            EECON1 = (1 << 7) | (1 << 4) | (1 << 2); // EEPGD=1, CFGS=0, WREN=1, FREE=1
            ExecWrite();
            TBLPTR += 64;
        }
        blocks++;
    }
}

void WritePages(const uint8_t* data, uint8_t len)
{
    static uint8_t i;
    i = 32;
    _asm TBLRDPOSTDEC _endasm
    while (len--) {
        tblwt_preinc(*data++);
        if (!--i) {
            i = 32;
            ExecWrite();
        }
    }
    if (i != 32) {
        ExecWrite();
    }
}

#define BOOTLDR_BASE_WORD (BOOTLDR_BASE / 2)
// Format: 0x12, ADDRH, ADDRL, [ DATA ]*
void WriteCmd(void)
{
    TBLPTRL = buff[2] & 0xE0; // 32-byte aligned
    TBLPTRH = buff[1];

    if (TBLPTR == 0) {
        // fix reset vector
        origReset[0] = buff[3 + 0];
        origReset[1] = buff[3 + 1];
        origReset[2] = buff[3 + 2];
        origReset[3] = buff[3 + 3];
        buff[3 + 0] = BOOTLDR_BASE_WORD & 0xFF;
        buff[3 + 1] = 0xEF;
        buff[3 + 2] = (BOOTLDR_BASE_WORD >> 8) & 0xFF;
        buff[3 + 3] = 0xF0 | ((BOOTLDR_BASE_WORD >> 16) & 0x0F);
    }
    EECON1 = (1 << 7) | (1 << 2); // EEPGD=1, CFGS=0, WREN=1, FREE=0
    WritePages(&buff[3], dataLen - 3);
}

#define CONFIG_REG 0x300000
// Format: 0x13, [ CONFIG ]*
void FinishCmd(void)
{
    static uint8_t i;

    flags.unlockBoot = 1;

    TBLPTRL = REBASED_RESET_VEC & 0xFF;
    TBLPTRH = REBASED_RESET_VEC >> 8;
    EECON1 = (1 << 7) | (1 << 4) | (1 << 2); // EEPGD=1, CFGS=0, WREN=1, FREE=1
    ExecWrite();
    _asm TBLRDPOSTDEC _endasm
    tblwt_preinc(origReset[0]);
    tblwt_preinc(origReset[1]);
    tblwt_preinc(origReset[2]);
    tblwt_preinc(origReset[3]);
    EECON1 = (1 << 7) | (1 << 2); // EEPGD=1, CFGS=0, WREN=1, FREE=0
    ExecWrite();

    TBLPTR = CONFIG_REG;
    EECON1 = (1 << 7) | (1 << 6) | (1 << 2); // EEPGD=1, CFGS=1, WREN=1, FREE=0
    for (i = 1; i < dataLen; i++) {
        TABLAT = buff[i];
        _asm TBLWT _endasm
        ExecWrite();
        _asm TBLRDPOSTINC _endasm
    }

    flags.unlockBoot = 0;
    _asm reset _endasm
}
