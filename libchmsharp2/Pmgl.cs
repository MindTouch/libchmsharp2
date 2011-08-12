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
using System.Runtime.InteropServices;
using System.Text;

namespace CHMsharp
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct chmPmglHeader
    {
        public char[] signature;        /*  0 (PMGL) */
        public UInt32 free_space;       /*  4 */
        public UInt32 unknown_0008;     /*  8 */
        public Int32 block_prev;        /*  c */
        public Int32 block_next;        /* 10 */
    }; /* __attribute__ ((aligned (1))); */

    internal sealed class Pmgl
    {
        public const string CHM_PMGL_MARKER = "PMGL";
        public const int CHM_PMGL_LEN = 0x14;

        public static void InitialisePmglHeader(ref chmPmglHeader dest)
        {
            dest.signature = new char[4];
        }

        /* find an exact entry in PMGL; return NULL if we fail */
        public static long FindInPmgl(byte[] page_buf, UInt32 block_len, string objPath)
        {
            /* XXX: modify this to do a binary search using the nice index structure
             *      that is provided for us.
             */
            chmPmglHeader header = new chmPmglHeader();
            uint hremain;
            uint end;
            uint cur;
            uint temp;
            UInt64 strLen;
            char[] buffer = new char[Storage.CHM_MAX_PATHLEN];

            /* figure out where to start and end */
            cur = 0;
            hremain = CHM_PMGL_LEN;
            if (!UnmarshalPmglHeader(ref page_buf, ref cur, ref hremain, ref header))
                return -1;
            end = block_len - (header.free_space);

            /* now, scan progressively */
            while (cur < end) {
                /* grab the name */
                temp = cur;
                strLen = Storage.ParseCWord(page_buf, ref cur);
                if (strLen > Storage.CHM_MAX_PATHLEN)
                    return -1;
                if (!Storage.ParseUTF8(page_buf, ref cur, strLen, ref buffer))
                    return -1;

                /* check if it is the right name */
                if (String.Compare(new String(buffer), objPath, true) == 0)
                    return (long)temp;

                SkipPmglEntryData(page_buf, ref cur);
            }

            return -1;
        }

        /* parse a PMGL entry into a chmUnitInfo struct; return 1 on success. */
        public static bool ParsePgmlEntry(byte[] pEntry, ref uint os, ref ChmUnitInfo ui)
        {
            char[] buf = new char[Storage.CHM_MAX_PATHLEN];
            UInt64 strLen;

            /* parse str len */
            strLen = Storage.ParseCWord(pEntry, ref os);
            if (strLen > Storage.CHM_MAX_PATHLEN)
                return false;

            /* parse path */
            if (!Storage.ParseUTF8(pEntry, ref os, strLen, ref buf))
                return false;

            /* parse info */
            ui.space  = (int)Storage.ParseCWord(pEntry, ref os);
            ui.start = Storage.ParseCWord(pEntry, ref os);
            ui.length = Storage.ParseCWord(pEntry, ref os);
            ui.path = new String(buf);

            return true;
        }

        /* skip the data from a PMGL entry */
        public static void SkipPmglEntryData(byte[] pEntry, ref uint os)
        {
            Storage.SkipCWord(pEntry, ref os);
            Storage.SkipCWord(pEntry, ref os);
            Storage.SkipCWord(pEntry, ref os);
        }

        public static bool UnmarshalPmglHeader(ref byte[] pData, ref uint pDataPos, 
            ref uint pDataLen, ref chmPmglHeader dest)
        {
            /* we only know how to deal with a 0x14 byte structures */
            if (pDataLen != CHM_PMGL_LEN)
                return false;

            InitialisePmglHeader(ref dest);

            /* unmarshal fields */
            Unmarshal.ToCharArray(ref pData, ref pDataPos, ref pDataLen, ref dest.signature, 4);
            Unmarshal.ToUInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.free_space);
            Unmarshal.ToUInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.unknown_0008);
            Unmarshal.ToInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.block_prev);
            Unmarshal.ToInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.block_next);

            /* check structure */
            if (new String(dest.signature, 0, 4).CompareTo(CHM_PMGL_MARKER) != 0)
                return false;

            return true;
        }
    }
}
