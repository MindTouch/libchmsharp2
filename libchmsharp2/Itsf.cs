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
    /* structure of ITSF headers */
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct chmItsfHeader
    {
        public char[] signature;                  /*  0 (ITSF) */
        public Int32 version;                     /*  4 */
        public Int32 header_len;                  /*  8 */
        public Int32 unknown_000c;                /*  c */
        public UInt32 last_modified;              /* 10 */
        public UInt32 lang_id;                    /* 14 */
        public byte[] dir_uuid;                   /* 18 */
        public byte[] stream_uuid;                /* 28 */
        public UInt64 unknown_offset;             /* 38 */
        public UInt64 unknown_len;                /* 40 */
        public UInt64 dir_offset;                 /* 48 */
        public UInt64 dir_len;                    /* 50 */
        public UInt64 data_offset;                /* 58 (Not present before V3) */
    }; /* __attribute__ ((aligned (1))); */

    internal sealed class Itsf
    {
        public const int CHM_ITSF_V2_LEN = 0x58;
        public const int CHM_ITSF_V3_LEN = 0x60;

        public static void InitialiseItsfHeader(ref chmItsfHeader h)
        {
            h.signature = new char[4];
            h.dir_uuid = new byte[16];
            h.stream_uuid = new byte[16];
        }

        public static bool UnmarshalItsfHeader(ref byte[] pData, ref uint pDataPos, 
            ref uint pDataLen, ref chmItsfHeader dest)
        {
            /* we only know how to deal with the 0x58 and 0x60 byte structures */
            if (pDataLen != CHM_ITSF_V2_LEN && pDataLen != CHM_ITSF_V3_LEN)
                return false;

            InitialiseItsfHeader(ref dest);

            /* unmarshal common fields */
            Unmarshal.ToCharArray(ref pData, ref pDataPos, ref pDataLen, ref dest.signature, 4);
            Unmarshal.ToInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.version);
            Unmarshal.ToInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.header_len);
            Unmarshal.ToInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.unknown_000c);
            Unmarshal.ToUInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.last_modified);
            Unmarshal.ToUInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.lang_id);
            Unmarshal.ToUuid(ref pData, ref pDataPos, ref pDataLen, ref dest.dir_uuid);
            Unmarshal.ToUuid(ref pData, ref pDataPos, ref pDataLen, ref dest.stream_uuid);
            Unmarshal.ToUInt64(ref pData, ref pDataPos, ref pDataLen, ref dest.unknown_offset);
            Unmarshal.ToUInt64(ref pData, ref pDataPos, ref pDataLen, ref dest.unknown_len);
            Unmarshal.ToUInt64(ref pData, ref pDataPos, ref pDataLen, ref dest.dir_offset);
            Unmarshal.ToUInt64(ref pData, ref pDataPos, ref pDataLen, ref dest.dir_len);

            /* error check the data */
            /* XXX: should also check UUIDs, probably, though with a version 3 file,
             * current MS tools do not seem to use them.
             */
            if (new String(dest.signature).CompareTo("ITSF") != 0)
                return false;
            if (dest.version == 2) {
                if (dest.header_len < CHM_ITSF_V2_LEN)
                    return false;
            } else if (dest.version == 3) {
                if (dest.header_len < CHM_ITSF_V3_LEN)
                    return false;
            } else
                return false;

            /* now, if we have a V3 structure, unmarshal the rest.
             * otherwise, compute it
             */
            if (dest.version == 3) {
                if (pDataLen != 0)
                    Unmarshal.ToUInt64(ref pData, ref pDataPos, ref pDataLen, ref dest.data_offset);
                else
                    return false;
            } else
                dest.data_offset = dest.dir_offset + dest.dir_len;

            return true;
        }
    }
}
