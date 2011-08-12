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
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace CHMsharp
{
    /* structure of ITSP headers */
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct chmItspHeader
    {
        public char[] signature;                 /*  0 (ITSP) */
        public Int32 version;                    /*  4 */
        public Int32 header_len;                 /*  8 */
        public Int32 unknown_000c;               /*  c */
        public UInt32 block_len;                 /* 10 */
        public Int32 blockidx_intvl;             /* 14 */
        public Int32 index_depth;                /* 18 */
        public Int32 index_root;                 /* 1c */
        public Int32 index_head;                 /* 20 */
        public Int32 unknown_0024;               /* 24 */
        public UInt32 num_blocks;                /* 28 */
        public Int32 unknown_002c;               /* 2c */
        public UInt32 lang_id;                   /* 30 */
        public byte[] system_uuid;               /* 34 */
        public byte[] unknown_0044;              /* 44 */
    }; /* __attribute__ ((aligned (1))); */

    internal sealed partial class Itsp
    {
        public const int CHM_ITSP_V1_LEN = 0x54;

        public static void InitialiseItspHeader(ref chmItspHeader h)
        {
            h.signature = new char[5];
            h.system_uuid = new byte[16];
            h.unknown_0044 = new byte[16];
        }

        public static bool UnmarshalItpfHeader(ref byte[] pData, ref uint pDataPos,
            ref uint pDataLen, ref chmItspHeader dest)
        {
            /* we only know how to deal with a 0x54 byte structures */
            if (pDataLen != CHM_ITSP_V1_LEN)
                return false;

            InitialiseItspHeader(ref dest);

            /* unmarshal fields */
            Unmarshal.ToCharArray(ref pData, ref pDataPos, ref pDataLen, ref dest.signature, 4);
            Unmarshal.ToInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.version);
            Unmarshal.ToInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.header_len);
            Unmarshal.ToInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.unknown_000c);
            Unmarshal.ToUInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.block_len);
            Unmarshal.ToInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.blockidx_intvl);
            Unmarshal.ToInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.index_depth);
            Unmarshal.ToInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.index_root);
            Unmarshal.ToInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.index_head);
            Unmarshal.ToInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.unknown_0024);
            Unmarshal.ToUInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.num_blocks);
            Unmarshal.ToInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.unknown_002c);
            Unmarshal.ToUInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.lang_id);
            Unmarshal.ToUuid(ref pData, ref pDataPos, ref pDataLen, ref dest.system_uuid);
            Unmarshal.ToByteArray(ref pData, ref pDataPos, ref pDataLen, ref dest.unknown_0044, 16);

            /* error check the data */
            if (new String(dest.signature).CompareTo("ITSP") != 0)
                return false;
            if (dest.version != 1)
                return false;
            if (dest.header_len != CHM_ITSP_V1_LEN)
                return false;

            return true;
        }
    }
}
