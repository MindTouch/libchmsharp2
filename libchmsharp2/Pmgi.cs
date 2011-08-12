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
    internal struct chmPmgiHeader
    {
        public char[] signature;        /*  0 (PMGI) */
        public UInt32 free_space;       /*  4 */
    }; /* __attribute__ ((aligned (1))); */

    internal sealed class Pmgi
    {
        public const string CHM_PMGI_MARKER = "PMGI";
        public const int CHM_PMGI_LEN = 0x08;

        public static void InitialisePmgiHeader(ref chmPmgiHeader dest)
        {
            dest.signature = new char[4];
        }

        /* find which block should be searched next for the entry; -1 if no block */
        public static Int32 FindInPmgi(byte[] page_buf, UInt32 block_len, string objPath)
        {
            /* XXX: modify this to do a binary search using the nice index structure
             *      that is provided for us
             */
            chmPmgiHeader header = new chmPmgiHeader();
            uint hremain;
            int page = -1;
            uint end;
            uint cur;
            UInt64 strLen;
            char[] buffer = new char[Storage.CHM_MAX_PATHLEN];

            /* figure out where to start and end */
            cur = 0;
            hremain = CHM_PMGI_LEN;
            if (!UnmarshalPmgiHeader(ref page_buf, ref cur, ref hremain, ref header))
                return -1;
            end = block_len - (header.free_space);

            /* now, scan progressively */
            while (cur < end) {
                /* grab the name */
                strLen = Storage.ParseCWord(page_buf, ref cur);
                if (strLen > Storage.CHM_MAX_PATHLEN)
                    return -1;
                if (!Storage.ParseUTF8(page_buf, ref cur, strLen, ref buffer))
                    return -1;

                /* check if it is the right name */
                if (String.Compare(new String(buffer), objPath, true) == 0)
                    return page;

                /* load next value for path */
                page = (int)Storage.ParseCWord(page_buf, ref cur);
            }

            return page;
        }

        public static bool UnmarshalPmgiHeader(ref byte[] pData, ref uint pDataPos,
            ref uint pDataLen, ref chmPmgiHeader dest)
        {
            /* we only know how to deal with a 0x8 byte structures */
            if (pDataLen != CHM_PMGI_LEN)
                return false;

            InitialisePmgiHeader(ref dest);

            /* unmarshal fields */
            Unmarshal.ToCharArray(ref pData, ref pDataPos, ref pDataLen, ref dest.signature, 4);
            Unmarshal.ToUInt32(ref pData, ref pDataPos, ref pDataLen, ref dest.free_space);

            /* check structure */
            if (new String(dest.signature, 0, 4).CompareTo(CHM_PMGI_MARKER) != 0)
                return false;

            return true;
        }
    }
}
