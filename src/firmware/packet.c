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

#define STX 0x0F
#define ETX 0x04
#define DLE 0x05

uint8_t calcCksum(void)
{
    static uint8_t crcH, crcL, i;
    crcH = 0xFF;
    crcL = 0xFF;
    for (i = 0; i < dataLen + 2; i++) {
        //crc = (uint8_t)(crc >> 8) | (crc << 8);
        crcL ^= crcH; crcH ^= crcL; crcL ^= crcH;
        //crc ^= buff[i];
        crcL ^= buff[i];
        //crc ^= (uint8_t)(crc & 0xFF) >> 4;
        crcL ^= crcL >> 4;
        //crc ^= crc << 12;
        crcH ^= crcL << 4;
        //crc ^= (crc & 0xFF) << 5;
        crcH ^= crcL >> 3; crcL ^= crcL << 5;
    }
    return crcH == 0 && crcL == 0;
}

void readPacket(void)
{
    static uint8_t overrun;
    static char c;

    overrun = 0;
l_wait:
    while (_getcUSART() != STX);
    if (_getcUSART() != STX)
        goto l_wait;

    dataLen = 0;
    while (1) {
        switch (c = _getcUSART()) {
            case DLE:
                c = _getcUSART();
                break;
            case STX:
                NOTIFY_UNEXPECTED_STX();
                while(1);
            case ETX:
                if (overrun || dataLen != 0)
                    dataLen -= 2;
                if (dataLen && !calcCksum()) {
                    NOTIFY_BAD_CKSUM();
                    while(1);
                }
                return;
        }
        if (overrun) {
            NOTIFY_OVERRUN();
            while(1);
        }
        buff[dataLen++] = c;
        if (dataLen == 0)
            overrun = 1;
    }
}
