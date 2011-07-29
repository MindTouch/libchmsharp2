using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using LONGINT64 = System.Int64;     /* long long */
using LONGUINT64 = System.UInt64;   /* unsigned long long */

namespace CHMsharp
{
    /* structure of LZXC control data block */
    internal struct chmLzxcControlData
    {
        public UInt32 size;                    /*  0        */
        public char[] signature;               /*  4 (LZXC) */
        public UInt32 version;                 /*  8        */
        public UInt32 resetInterval;           /*  c        */
        public UInt32 windowSize;              /* 10        */
        public UInt32 windowsPerReset;         /* 14        */
        public UInt32 unknown_18;              /* 18        */
    };

    /* structure of LZXC reset table */
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct chmLzxcResetTable
    {
        public UInt32 version;
        public UInt32 block_count;
        public UInt32 unknown;
        public UInt32 table_offset;
        public UInt64 uncompressed_len;
        public UInt64 compressed_len;
        public UInt64 block_len;
    }; /* __attribute__ ((aligned (1))); */

    internal sealed class Lzxc
    {
        public const int CHM_LZXC_MIN_LEN = 0x18;
        public const int CHM_LZXC_V2_LEN = 0x1c;

        public const int CHM_LZXC_RESETTABLE_V1_LEN = 0x28;

        public static void InitialiseLzxcControlData(ref chmLzxcControlData lcd)
        {
            lcd.signature = new char[4];
        }

        /* decompress the block.  must have lzx_mutex. */
        public static Int64 DecompressBlock(ref chmFileInfo h, UInt64 block, ref byte[] ubuffer)
        {
            byte[] cbuffer = new byte[h.reset_table.block_len + 6144];
            long cbufferpos = 0;
            UInt64 cmpStart = 0;                                /* compressed start  */
            Int64 cmpLen = 0;                                   /* compressed len    */
            int indexSlot;                                      /* cache index slot  */
            uint lbuffer;                                       /* local buffer ptr  */
            UInt32 blockAlign = (UInt32)(block % h.reset_blkcount); /* reset intvl. aln. */
            UInt32 i;                                           /* local loop index  */

            /* let the caching system pull its weight! */
            if ((int)(block - blockAlign) <= h.lzx_last_block  &&
                (int)block                >= h.lzx_last_block)
                blockAlign = (uint)((int)block - h.lzx_last_block);

            /* check if we need previous blocks */
            if (blockAlign != 0) {
                /* fetch all required previous blocks since last reset */
                for (i = blockAlign; i > 0; i--) {
                    UInt32 curBlockIdx = (UInt32)(block - (LONGUINT64)i);

                    /* check if we most recently decompressed the previous block */
                    if (h.lzx_last_block != curBlockIdx) {
                        if ((curBlockIdx % h.reset_blkcount) == 0)
                            Lzx.LZXreset(h.lzx_state);

                        indexSlot = (int)((curBlockIdx) % h.cache_num_blocks);
                        if (h.cache_blocks[indexSlot] == null)
                            h.cache_blocks[indexSlot] = new byte[h.reset_table.block_len];

                        h.cache_block_indices[indexSlot] = curBlockIdx;
                        lbuffer = (uint)indexSlot;

                        /* decompress the previous block */
                        if (!GetCmpBlockBounds(ref h, curBlockIdx, ref cmpStart, ref cmpLen)   ||
                            cmpLen < 0                                                         ||
                            cmpLen > (LONGINT64)h.reset_table.block_len + 6144                 ||
                            Storage.FetchBytes(ref h, ref cbuffer, cmpStart, cmpLen) != cmpLen ||
                            Lzx.LZXdecompress(h.lzx_state, ref cbuffer, cbufferpos, 
                                              ref h.cache_blocks[lbuffer], 0, (int)cmpLen,
                                              (int)h.reset_table.block_len) != Lzx.DECR_OK)
                        {
                            return (Int64)0;
                        }

                        h.lzx_last_block = (int)curBlockIdx;
                    }
                }
            } else {
                if ((block % h.reset_blkcount) == 0)
                    Lzx.LZXreset(h.lzx_state);
            }

            /* allocate slot in cache */
            indexSlot = (int)(block % (LONGUINT64)h.cache_num_blocks);
            if (h.cache_blocks[indexSlot] == null)
                h.cache_blocks[indexSlot] = new byte[h.reset_table.block_len];

            h.cache_block_indices[indexSlot] = block;
            lbuffer = (uint)indexSlot;
            ubuffer = h.cache_blocks[lbuffer];

            /* decompress the block we actually want */
            if (!GetCmpBlockBounds(ref h, block, ref cmpStart, ref cmpLen)         ||
                Storage.FetchBytes(ref h, ref cbuffer, cmpStart, cmpLen) != cmpLen ||
                Lzx.LZXdecompress(h.lzx_state, ref cbuffer, cbufferpos, 
                                  ref h.cache_blocks[lbuffer], 0, (int)cmpLen,
                              (int)h.reset_table.block_len) != Lzx.DECR_OK)
            {
                return (Int64)0;
            }
            h.lzx_last_block = (int)block;

            /* XXX: modify LZX routines to return the length of the data they
             * decompressed and return that instead, for an extra sanity check.
             */
            return (Int64)h.reset_table.block_len;
        }

        /* grab a region from a compressed block */
        public static Int64 DecompressRegion(ref chmFileInfo h, ref byte[] buf, ulong bufpos, UInt64 start, Int64 len)
        {
            UInt64 nBlock, nOffset;
            UInt64 nLen;
            UInt64 gotLen;
            byte[] ubuffer = null;

            if (len <= 0)
                return (Int64)0;

            /* figure out what we need to read */
            nBlock = start / h.reset_table.block_len;
            nOffset = start % h.reset_table.block_len;
            nLen = (LONGUINT64)len;
            if (nLen > (h.reset_table.block_len - nOffset))
                nLen = h.reset_table.block_len - nOffset;

            /* if block is cached, return data from it. */
            h.lzx_mutex.WaitOne();
            h.cache_mutex.WaitOne();
            if (h.cache_block_indices[nBlock % (LONGUINT64)h.cache_num_blocks] == nBlock &&
                h.cache_blocks[nBlock % (LONGUINT64)h.cache_num_blocks] != null)
            {
                Array.Copy(
                    h.cache_blocks[nBlock % (LONGUINT64)h.cache_num_blocks], 
                    (int)nOffset,
                    buf,
                    (int)bufpos,
                    (long)nLen);
                h.cache_mutex.ReleaseMutex();
                h.lzx_mutex.ReleaseMutex();

                return (Int64)nLen;
            }
            h.cache_mutex.ReleaseMutex();

            /* data request not satisfied, so... start up the decompressor machine */
            if (!h.lzx_init) {
                int window_size = ffs(h.window_size) - 1;
                h.lzx_last_block = -1;
                h.lzx_state = Lzx.LZXinit(window_size);
                h.lzx_init = true;
            }

            /* decompress some data */
            gotLen = (UInt64)DecompressBlock(ref h, nBlock, ref ubuffer);
            if (gotLen < nLen)
                nLen = gotLen;
            Array.Copy(ubuffer, (int)nOffset, buf, (int)bufpos, (int)nLen);
            h.lzx_mutex.ReleaseMutex();

            return (Int64)nLen;
        }

        private static int ffs(uint val)
        {
            int bit = 1, idx = 1;

            while (bit != 0  &&  (val & bit) == 0) {
                bit <<= 1;
                ++idx;
            }

            if (bit == 0)
                return 0;
            else
                return idx;
        }

        /* get the bounds of a compressed block.  return 0 on failure */
        public static bool GetCmpBlockBounds(ref chmFileInfo h, UInt64 block, 
            ref UInt64 start, ref Int64 len)
        {
            byte[] buffer = new byte[8];
            uint remain;
            uint pos = 0;

            /* for all but the last block, use the reset table */
            if (block < h.reset_table.block_count - 1) {
                /* unpack the start address */
                pos = 0;
                remain = 8;
                if (Storage.FetchBytes(ref h, ref buffer,
                                     (UInt64)h.data_offset
                                        + (UInt64)h.rt_unit.start
                                        + (UInt64)h.reset_table.table_offset
                                        + (UInt64)block * 8,
                                     remain) != remain                            ||
                    !Unmarshal.ToUInt64(ref buffer, ref pos, ref remain, ref start))
                    return false;

                /* unpack the end address */
                pos = 0;
                remain = 8;
                if (Storage.FetchBytes(ref h, ref buffer,
                                 (UInt64)h.data_offset
                                        + (UInt64)h.rt_unit.start
                                        + (UInt64)h.reset_table.table_offset
                                        + (UInt64)block * 8 + 8,
                                 remain) != remain                                ||
                    !Unmarshal.ToInt64(ref buffer, ref pos, ref remain, ref len))
                    return false;
            }

            /* for the last block, use the span in addition to the reset table */
            else {
                /* unpack the start address */
                pos = 0;
                remain = 8;
                if (Storage.FetchBytes(ref h, ref buffer,
                                     (UInt64)h.data_offset
                                        + (UInt64)h.rt_unit.start
                                        + (UInt64)h.reset_table.table_offset
                                        + (UInt64)block * 8,
                                     remain) != remain                            ||
                    !Unmarshal.ToUInt64(ref buffer, ref pos, ref remain, ref start))
                    return false;

                len = (Int64)h.reset_table.compressed_len;
            }

            /* compute the length and absolute start address */
            len -= (Int64)start;
            start += h.data_offset + h.cn_unit.start;

            return true;
        }

        public static bool UnmarshalLzxcControlData(ref byte[] pData, ref uint pDataPos,
            ref uint pDataLen, ref chmLzxcControlData dest)
        {
            /* we want at least 0x18 bytes */
            if (pDataLen < CHM_LZXC_MIN_LEN)
                return false;

            InitialiseLzxcControlData(ref dest);

            /* unmarshal fields */
            Unmarshal.ToUInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.size);
            Unmarshal.ToCharArray(ref pData, ref pDataPos, ref pDataLen, ref dest.signature, 4);
            Unmarshal.ToUInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.version);
            Unmarshal.ToUInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.resetInterval);
            Unmarshal.ToUInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.windowSize);
            Unmarshal.ToUInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.windowsPerReset);

            if (pDataLen >= CHM_LZXC_V2_LEN)
                Unmarshal.ToUInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.unknown_18);
            else
                dest.unknown_18 = 0;

            if (dest.version == 2) {
                dest.resetInterval *= 0x8000;
                dest.windowSize *= 0x8000;
            }
            if (dest.windowSize == 0  ||  dest.resetInterval == 0)
                return false;

            /* for now, only support resetInterval a multiple of windowSize/2 */
            if (dest.windowSize == 1)
                return false;
            if ((dest.resetInterval % (dest.windowSize / 2)) != 0)
                return false;

            /* check structure */
            if (new String(dest.signature).CompareTo("LZXC") != 0)
                return false;

            return true;
        }

        public static bool UnmarshalLzxcResetTable(ref byte[] pData, ref uint pDataPos, 
            ref uint pDataLen, ref chmLzxcResetTable dest)
        {
            /* we only know how to deal with a 0x28 byte structures */
            if (pDataLen != CHM_LZXC_RESETTABLE_V1_LEN)
                return false;

            /* unmarshal fields */
            Unmarshal.ToUInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.version);
            Unmarshal.ToUInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.block_count);
            Unmarshal.ToUInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.unknown);
            Unmarshal.ToUInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.table_offset);
            Unmarshal.ToUInt64(ref pData, ref pDataPos, ref pDataLen, ref dest.uncompressed_len);
            Unmarshal.ToUInt64(ref pData, ref pDataPos, ref pDataLen, ref dest.compressed_len);
            Unmarshal.ToUInt64(ref pData, ref pDataPos, ref pDataLen, ref dest.block_len);

            /* check structure */
            if (dest.version != 2)
                return false;

            return true;
        }
    }
}
