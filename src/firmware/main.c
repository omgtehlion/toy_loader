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

#pragma config WDT = OFF // WDT disabled
#pragma config FOSC = INTOSCIO_EC // IntOsc, EC Osc for USB, Digital I/O on RA6
#pragma config LVP = OFF // enable RB5
#pragma config DEBUG = OFF // Background debugger disabled
#pragma config CCP2MX = ON // CCP2 is not multiplexed with RB3
#pragma config XINST = ON

#pragma udata rx_buff
uint8_t buff[256];
#pragma udata
uint8_t dataLen;

char _getcUSART(void)
{
    if (!RCSTAbits.CREN) {
        RCSTAbits.CREN = 1;
    }
    if (RCSTAbits.OERR) {
        static char tmp;
        RCSTAbits.CREN = 0;
        tmp = RCREG;
        tmp = RCREG;
        RCSTAbits.CREN = 1;
    }

    while (!PIR1bits.RCIF); // wait until there is a byte to read
    return RCREG;
}

uint8_t confUsart(void)
{
    // TODO: try unchecking CREN
    static uint16_t cnt;

    RCSTAbits.SPEN = 1;
    TRISC |= (1 << 7); // RX is in EUSART mode
    //TXSTA = (1 << 5) | (1 << 2); // TX9=0, TXEN=1, SYNC=0, BRGH=1
    TXSTA = (1 << 2); // TX9=0, TXEN=0, SYNC=0, BRGH=1
    RCSTA = (1 << 7) | (1 << 4); // SPEN=1, RX9=0, CREN=1
    BAUDCON = (1 << 3) | (1); // RXDTP=0, TXCKP=0, BRG16=1, WUE=0, ABDEN=1

    cnt = 0x2000;
    while (BAUDCONbits.ABDEN) {
        if (--cnt == 0) {
            return 0;
        }
    }
    _getcUSART();

    if (SPBRG == 0x11 && SPBRGH == 0x00) {
        // dirty fix for 115200bps
        SPBRG = 0x10;
    }
    _getcUSART(); // throw away last char
    return 1;
}

#include "packet.c"
#include "commands.c"

void main(void)
{
    OSCCON = (7 << 4) | 2; // 8MHz int osc
    INTCONbits.GIEH = 0; // disable all interrupts
    WDTCONbits.SWDTEN = 0; // disable WDT in SW

    NOTIFY_RESET();
    if (!confUsart())
        return;
    NOTIFY_ENTRY();
    while (_getcUSART() == 0x55);

    while (1) {
        readPacket();
        switch (buff[0]) {
            case 0x11:
                EraseCmd();
                break;
            case 0x12:
                WriteCmd();
                break;
            case 0x13:
                FinishCmd();
                break;
            default:
                NOTIFY_UNK_COMMAND();
                while(1);
        }
    }
}

/////////////////////////////////////////
#pragma code _my_startup=BOOTLDR_BASE
void _my_startup(void)
{
    _asm
        // Initialize the stack pointer
        lfsr 1, _stack
        lfsr 2, _stack
        clrf TBLPTRU, 0 // 1st silicon doesn't do this on POR
    _endasm

    main();

    if (*((const rom uint32_t*)REBASED_RESET_VEC) == 0xFFFFFFFF) {
        NOTIFY_NO_FIRMWARE();
        while(1);
    } else {
        _asm goto REBASED_RESET_VEC _endasm
    }
}

#pragma code _my_entry=0x0
void _my_entry(void)
{
    _asm goto _my_startup _endasm
}
