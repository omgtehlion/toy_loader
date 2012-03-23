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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace loader
{
    public enum HexRecordType
    {
        Data = 0x00,
        EndOfFile = 0x01,
        ExtendedSegmentAddress = 0x02,
        StartSegmentAddress = 0x03,
        ExtendedLinearAddress = 0x04,
        StartLinearAddress = 0x03,
    }

    [DebuggerDisplay("@{StartAddr.ToString(\"X4\"),nq} {Type}({Data.Count}): {DataAsHex(),nq}")]
    public sealed class HexLineContent
    {
        public uint LineStartAddr;
        public HexRecordType Type;
        public byte[] Data;

        public HexLineContent()
        {
        }

        public HexLineContent(string line)
        {
            var bcount = Convert.ToByte(line.Substring(1, 2), 16);
            LineStartAddr = Convert.ToUInt16(line.Substring(3, 4), 16);
            Type = (HexRecordType)Convert.ToByte(line.Substring(7, 2), 16);
            var data = new List<byte>();
            for (var i = 0; i < bcount; i++) {
                data.Add(Convert.ToByte(line.Substring(9 + i * 2, 2), 16));
            }
            Data = data.ToArray();
            var cksum = Convert.ToByte(line.Substring(9 + bcount * 2, 2), 16);
            if (cksum != CalcCheckSum()) {
                throw new Exception();
            }
        }

        public byte CalcCheckSum()
        {
            byte result = 0;
            result += (byte)Data.Length;
            result += (byte)(LineStartAddr >> 8);
            result += (byte)(LineStartAddr);
            result += (byte)(Type);
            result = Data.Aggregate(result, (current, i) => (byte)(current + i));
            return (byte)(0 - result);
        }

        public string DataAsHex()
        {
            var hex = new StringBuilder();
            foreach (var i in Data) {
                hex.Append(i.ToString("X2"));
            }
            return hex.ToString();
        }

        public string Serialize()
        {
            var hex = new StringBuilder();
            hex.AppendFormat(":{0:X2}{1:X4}{2:X2}", Data.Length, LineStartAddr, (byte)Type);
            hex.Append(DataAsHex());
            hex.AppendFormat("{0:X2}", CalcCheckSum());
            return hex.ToString();
        }
    }

    public sealed class HexRegion
    {
        public uint StartAddr;
        public byte[] Data;
        public uint NextAddr { get { return StartAddr + (uint)Data.Length; } }
        public int Length { get { return Data.Length; } }
    }

    public sealed class HexFile
    {
        List<HexLineContent> lines;
        List<HexRegion> regions;

        public HexFile(string fileName)
        {
            lines = File.ReadAllLines(fileName).Select(l => new HexLineContent(l)).ToList();
        }

        public IEnumerable<HexRegion> Regions
        {
            get
            {
                if (regions == null)
                    Consolidate();
                return regions;
            }
        }

        private sealed class HexBlock
        {
            public uint StartAddr;
            public List<byte[]> Data;
            public uint NextAddr;
            public HexBlock(uint startAddr, byte[] data)
            {
                NextAddr = StartAddr = startAddr;
                Data = new List<byte[]>();
                Append(data);
            }
            public void Append(byte[] data)
            {
                Data.Add(data);
                NextAddr += (uint)data.Length;
            }
        }

        private void Consolidate()
        {
            var blocks = new List<HexBlock>();
            var ela = 0u;
            foreach (var l in lines) {
                if (l.Type == HexRecordType.EndOfFile)
                    break;
                switch (l.Type) {
                    case HexRecordType.Data:
                        blocks.Add(new HexBlock(l.LineStartAddr + ela, l.Data));
                        break;
                    case HexRecordType.ExtendedLinearAddress:
                        ela = ((uint)l.Data[0] << 24) + ((uint)l.Data[1] << 16);
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }

            blocks.Sort((a, b) => (int)a.StartAddr - (int)b.StartAddr);
            HexBlock last = null;
            var consolidated = new List<HexBlock>();
            foreach (var b in blocks) {
                if (last != null && b.StartAddr == last.NextAddr) {
                    last.Append(b.Data[0]);
                } else {
                    last = b;
                    consolidated.Add(last);
                }
            }

            regions = new List<HexRegion>();
            foreach (var b in consolidated) {
                regions.Add(new HexRegion {
                    StartAddr = b.StartAddr,
                    Data = b.Data.SelectMany(a => a).ToArray(),
                });
            }
        }
    }
}
