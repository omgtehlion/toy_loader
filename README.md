Toy Loader (bootloader for PIC18 µC)
====================================

Toy Loader is a simple UART bootloader for PIC18Fxx microcontrollers, specifically pic18f2550.
Unlike other popular bootloaders it doesn’t require additional wires to work: you need power supply wires only.

Sample Schematics
-----------------
![fig1](/omgtehlion/toy_loader/raw/f60cfae8c3397814f5be82e7f5c63eed6465afe4/schematic.png)

This bootloader requires TTL UART port (or USB-UART converter) plus simple schematics to provide power via the same wire.
You can use any n-MOSFET or npn-BJT for Q1, for it acts as inverter. For Q2 choose p-MOSFET that can handle reasonable current. I use FDS8958A, which contains p- and n-MOSFETs.
D1 is used for power bypass and is not required in low-power applications because pic-controller contains some protection diodes within. If you need it, use any schottky diode of your choice (for me it’s BAT54C).
Values of R1, R2, C1, C2 depend on parameters of transistors, power drained by microcontroller and wire length.

Bootloader Protocol
-------------------

At the lowest level this bootloader uses UART. Baud rate is calculated automatically, PC software needs to send a stream of 0x55 bytes. When baud rate is locked, firmware will notify user (currently with logical 1 set on RB0). Baud rates from 9600 to 153846 are supported with internal RC oscillator.

Next level represents packets similar to those in AN1310 but with subtle differences.
Each packet consists of:
`STX, STX, [DATA]*, CRC1, CRC2, ETX`
Where, `STX` is Start marker, `ETX` is End marker, `[DATA]*` is 0 or more payload data bytes, `CRCx` are bytes of payload checksum.
Each byte of `DATA` or `CRC` which can be interpreted as control character must be preceded by `DLE` byte.
`STX = 0x0F
ETX = 0x04
DLE = 0x05`
Checksum used is CRC-16-CCITT with 0x1021 polynomial and 0xFFFF initial value.

First byte of `DATA` should be command identifier.
This bootloader is not dialog-style as other popular bootloaders; hence only write commands are supported.

**Erase command**
`0x11, [ ADDRH, ADDRL, PAGECOUNT ]*`
`ADDR` is start address of erase operation. `PAGECOUNT` — count of 64-byte pages to erase.
This command can consist of any number of erase ranges.

**Write command**
`0x12, ADDRH, ADDRL, [ DATA ]*`
`ADDR` is start address of write operation, must be aligned on 32-byte boundary.
`DATA` — content to write.

**Finish command**
`0x13, [ CONFIG ]*`
`CONFIG` is optional configuration fuses value. *CAUTION*: writing incorrect config can brick your device.

Firmware
--------

This bootloader is stored in the last 1024 bytes of Flash program memory space.
This allows user application to handle interrupts at the normal hardware interrupt vector addresses and work without additional modifications.
User firmware must meet only following requirements:
* it must start with `GOTO xxx` instruction (most of pic18 compilers do this by default);
* it must not use last 1024 bytes of program memory;
* its configuration value must be compatible with bootloader’s, or you have to recompile bootloader with your config.

Bootloader automatically changes `GOTO` instruction at address 0x0000 to jump to itself, and preserves original reset vector in the last page of program memory.

Current version of bootloader communicates with user via LEDs attached to PORTB, but you can change this behavior in file boot.h.
For pic18f2550 bootloader code starts at address 0x7C00 and continues till 0x7FBF, the last page is used to store configuration, namely original reset vector.

PC software
-----------

PC software is written in C# and works on Windows machines. It should work in Linux or OSX via Mono, but I have not checked it yet. Source code is inside src/loader directory.
Usage:

```
loader.exe firmware_hex -p=port [-b=baudrate]
    -p=port       Sets COM port used to flash firmware
    -b=baudrate   Sets baud rate (default is 115200)
```
Loader requires HEX file as input. After you start it, it will be sending stream of 0x55 bytes to initiate auto-baud calculation and entry into bootloader mode. When bootloader starts you should press any key and PC loader will start sending instructions to microcontroller.
