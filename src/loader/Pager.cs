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

namespace loader
{
    public sealed class FilledPage<T>
    {
        public T[] Data;
        public int Offset;
    }

    public sealed class Pager<T>
    {
        private sealed class PageSegment
        {
            public ArraySegment<T> Data;
            public int Offset;
            public PageSegment(T[] data, int offset, int count, int offsetOnPage)
            {
                Data = new ArraySegment<T>(data, offset, count);
                Offset = offsetOnPage;
            }
        }

        readonly int pageSize;
        readonly T emptyFill;
        SortedDictionary<int, List<PageSegment>> pages = new SortedDictionary<int, List<PageSegment>>();

        public Pager(int pageSize, T emptyFill)
        {
            this.pageSize = pageSize;
            this.emptyFill = emptyFill;
        }

        public void Write(int startAddr, T[] data)
        {
            var idx = 0;
            while (idx < data.Length) {
                var pageId = startAddr / pageSize;
                var offsetOnPage = startAddr - pageId * pageSize;
                var countOnPage = Math.Min(pageSize - offsetOnPage, data.Length - idx);

                List<PageSegment> page;
                if (!pages.TryGetValue(pageId, out page))
                    pages.Add(pageId, page = new List<PageSegment>());

                page.Add(new PageSegment(data, idx, countOnPage, offsetOnPage));
                idx += countOnPage;
                startAddr += countOnPage;
            }
        }

        public IEnumerable<FilledPage<T>> GetContiguousPages(int maxPages)
        {
            var current = new List<List<PageSegment>>();
            var lastPageId = -2;
            foreach (var kvp in pages) {
                if (current.Count > 0 && (kvp.Key != lastPageId + 1 || current.Count >= maxPages)) {
                    yield return Fill(current, lastPageId);
                    current.Clear();
                }

                current.Add(kvp.Value);
                lastPageId = kvp.Key;
            }
            if (current.Count > 0) {
                yield return Fill(current, lastPageId);
            }
        }

        private FilledPage<T> Fill(List<List<PageSegment>> current, int lastPageId)
        {
            var data = new T[current.Count * pageSize];
            for (var i = 0; i < data.Length; i++)
                data[i] = emptyFill;
            for (var i = 0; i < current.Count; i++) {
                var offset = i * pageSize;
                foreach (var seg in current[i]) {
                    Array.Copy(seg.Data.Array, seg.Data.Offset, data, seg.Offset + offset, seg.Data.Count);
                }
            }

            var firstPageId = lastPageId - current.Count + 1;
            return new FilledPage<T> {
                Data = data,
                Offset = firstPageId * pageSize,
            };
        }
    }
}
