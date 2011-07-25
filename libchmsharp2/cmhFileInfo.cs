using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace CHMsharp
{
    internal struct chmFileInfo {
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
        public chmUnitInfo rt_unit;
        public chmUnitInfo cn_unit;
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
