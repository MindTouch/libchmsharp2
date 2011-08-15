/*
 * libchmsharp2 - a C# port of chmlib
 * Copyright (C) 2011 MindTouch, Inc.
 * www.mindtouch.com  oss@mindtouch.com
 *
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.
 *
 * This library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public
 * License along with this library; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace CHMsharp
{
    internal struct ChmFileInfo {
        public FileStream fd;

        public Mutex mutex;
        public Mutex lzx_mutex;
        public Mutex cache_mutex;

        public UInt64 dir_offset;
        public UInt64 dir_len;
        public UInt64 data_offset;
        public Int32 index_root;
        public Int32 index_head;
        public UInt32 block_len;

        public UInt64 span;
        public ChmUnitInfo rt_unit;
        public ChmUnitInfo cn_unit;
        public chmLzxcResetTable reset_table;

        /* LZX control data */
        public bool compression_enabled;
        public UInt32 window_size;
        public UInt32 reset_interval;
        public UInt32 reset_blkcount;

        /* decompressor state */
        public bool lzx_init;
        public Lzx.LZXstate lzx_state;
        public int lzx_last_block;

        /* cache for decompressed blocks */
        public byte[][] cache_blocks;
        public UInt64[] cache_block_indices;
        public Int32 cache_num_blocks;
    };
}
