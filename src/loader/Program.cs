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

#define LOOPBACK

using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;

namespace loader
{
    class Program
    {
        const int MAX_MEM = 0x7C00;
        const int MAX_WRITE = 256 - 32;
        const int ERASE_PAGE = 64;
        const int MAX_ERASE_PAGES = 255;
        const int WRITE_PAGE = 32;

        const byte STX = 0x0F;
        const byte ETX = 0x04;
        const byte DLE = 0x05;

        static SerialPort port;

        sealed class EraseBlock
        {
            public uint StartAddr;
            public uint Pages;
        }

        static int Main(string[] args)
        {
            if (args.Length < 1) {
                PrintUsage();
                return 0;
            }

            string fw = null, portName = null, baudStr = null;
            foreach (var a in args) {
                if (a.StartsWith("-")) {
                    if (a.StartsWith("-p=")) {
                        portName = a.Substring(3);
                    } else if (a.StartsWith("-b=")) {
                        baudStr = a.Substring(3);
                    } else {
                        Console.Error.WriteLine("Unknown option: '{0}'", a);
                    }
                } else {
                    if (fw != null) {
                        Console.Error.WriteLine("Bad argument: '{0}'", a);
                        return 1;
                    }
                    fw = a;
                }
            }
            if (fw == null) {
                Console.Error.WriteLine("No firmware file spicified");
                return 1;
            }
            if (portName == null) {
                Console.Error.WriteLine("No port spicified");
                return 1;
            }
            var baud = baudStr != null ? int.Parse(baudStr) : 115200;

            var hex = new HexFile(fw);
            var pgmRegions = hex.Regions.Where(r => r.StartAddr < MAX_MEM).ToList();

            var erase = new List<EraseBlock>();
            foreach (var r in pgmRegions) {
                var eraseStart = (r.StartAddr / ERASE_PAGE) * ERASE_PAGE;
                var eraseEnd = r.NextAddr;
                while (eraseStart < eraseEnd) {
                    var pages = Math.Min((eraseEnd - eraseStart - 1) / ERASE_PAGE + 1, MAX_ERASE_PAGES);
                    erase.Add(new EraseBlock { StartAddr = eraseStart, Pages = pages });
                    eraseStart += pages * ERASE_PAGE;
                }
            }

            var wrPager = new Pager<byte>(32, 0xFF);
            foreach (var r in pgmRegions) {
                wrPager.Write((int)r.StartAddr, r.Data);
            }
            var write = wrPager.GetContiguousPages(MAX_WRITE / WRITE_PAGE).ToList();

            port = new SerialPort(portName, baud, Parity.None, 8, StopBits.One) {
                ReadTimeout = 1000,
                NewLine = "\r\n",
                DiscardNull = false,
            };

#if !LOOPBACK
            port.DataReceived += port_DataReceived;
#endif

            try {
                port.Open();
            } catch (Exception ex) {
                Console.Error.WriteLine(ex.Message + "Can’t open port");
                return 1;
            }
            Console.WriteLine("Port {0}:{1} open, press any key when ready", port.PortName, port.BaudRate);
            while (!Console.KeyAvailable) {
                port.Write(new string((char)0x55, 100));
            }
            port.Write("\0");
            Console.WriteLine("Started");
#if LOOPBACK
            while (!port.ReadExisting().EndsWith("\0")) {
                Thread.Sleep(1);
            }
#else
            Thread.Sleep(1000);
#endif
            port.DiscardInBuffer();

            Console.WriteLine("Erasing...");
            SendErase(erase);

            foreach (var w in write) {
                Console.WriteLine("Writing 0x{0:X}...", w.Offset);
                SendWrite(w);
            }
            Console.WriteLine("Finishing...");
            SendFinish();

            return 0;
        }

        private static void PrintUsage()
        {
            Console.WriteLine(@"loader.exe firmware_hex -p=port [-b=baudrate]
    -p=port       Sets COM port used to flash firmware
    -b=baudrate   Sets baud rate (default is 115200)");

        }

        private static void SendErase(List<EraseBlock> blocks)
        {
            var wait = 1;
            var data = new byte[blocks.Count * 3];
            var i = 0;
            foreach (var b in blocks) {
                data[i++] = (byte)(b.StartAddr >> 8);
                data[i++] = (byte)(b.StartAddr & 0xFF);
                data[i++] = (byte)b.Pages;
                wait += (int)b.Pages * 3 + 1;
            }
            WritePacket(new byte[] { 0x11 }, data);
            Thread.Sleep(wait);
        }

        private static void SendWrite(FilledPage<byte> w)
        {
            if ((w.Offset & 0x1F) != 0) {
                throw new Exception("Data must be 32-byte aligned");
            }
            var cmd = new byte[] { 0x12, (byte)(w.Offset >> 8), (byte)(w.Offset & 0xFF) };
            WritePacket(cmd, w.Data);
            Thread.Sleep(3 + (w.Data.Length / WRITE_PAGE) * 4);
        }

        private static void SendFinish()
        {
            //default: 0x00, 0x05, 0x1F, 0x1F, 0x00, 0x83, 0x85, 0x00,
            // boot requires: ----, intrc 0x08, ----, wdt 0x1E, ----, ----, xinst 0xC1
            //WritePacket(0x13, new byte[] { 0x00, 0x08, 0x1F, 0x1E, 0xFF, 0x81, 0xC1 });
            //WritePacket(0x13, new byte[] { 0x12, 0x34, 0x56, 0x78, 0x12, 0x34, 0x56, 0x78, });
            WritePacket(new byte[] { 0x13 });
            Thread.Sleep(10);
        }

        private static void WritePacket(params byte[][] data)
        {
            var buff = new byte[1024];
            var len = 0;
            var buffUsed = 0;

            buff[len++] = STX;
            buff[len++] = STX;

            Action<byte> put = b => {
                if (b == STX || b == ETX || b == DLE)
                    buff[len++] = DLE;
                buff[len++] = b;
                buffUsed++;
            };

            var crc = new Crc16Ccitt();
            foreach (var b in data.SelectMany(arr => arr)) {
                put(b);
                crc.Update(b);
            }
            put((byte)(crc.Value >> 8));
            put((byte)crc.Value);

            if (buffUsed > 255) {
                throw new Exception();
            }

            buff[len++] = ETX;

#if LOOPBACK
            port.DiscardInBuffer();
            port.Write(buff, 0, len);
            while (len > port.BytesToRead) {
                Thread.Sleep(1);
            }
            port.DiscardInBuffer();
#else
            port.Write(buff, 0, len);
            Thread.Sleep(300);
#endif
        }

#if !LOOPBACK
        static void port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            Console.Write(port.ReadExisting());
        }
#endif
    }
}
