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

namespace CHMsharp
{
    internal sealed class Storage
    {
        public const int CHM_MAX_PATHLEN = 512;

        /* the two available spaces in a CHM file                      */
        /* N.B.: The format supports arbitrarily many spaces, but only */
        /*       two appear to be used at present.                     */
        public static int CHM_UNCOMPRESSED = 0;
        public static int CHM_COMPRESSED = 1;

        /* names of sections essential to decompression */
        public const string CHMU_RESET_TABLE = "::DataSpace/Storage/MSCompressed/Transform/" +
            "{7FC28940-9D31-11D0-9B27-00A0C91E9C7C}/InstanceData/ResetTable";
        public const string CHMU_LZXC_CONTROLDATA = "::DataSpace/Storage/MSCompressed/ControlData";
        public const string CHMU_CONTENT = "::DataSpace/Storage/MSCompressed/Content";
        public const string CHMU_SPANINFO = "::DataSpace/Storage/MSCompressed/SpanInfo";

        public static Int64 FetchBytes(ref ChmFileInfo h, ref byte[] buf, UInt64 os, Int64 len)
        {
            Int64 readLen = 0;
            long cur = 0;

            if (h.fd == null)
                return readLen;

            h.mutex.WaitOne();
            cur = h.fd.Seek((long)os, SeekOrigin.Begin);
            readLen = h.fd.Read(buf, 0, (int)len);
            h.mutex.ReleaseMutex();

            return readLen;
        }

        /* parse a compressed dword */
        public static UInt64 ParseCWord(byte[] pEntry, ref uint os)
        {
            UInt64 accum = 0;
            byte temp;

            while ((temp = pEntry[os++]) >= 0x80) {
                accum <<= 7;
                accum += (UInt64)(temp & 0x7f);
            }

            return (accum << 7) + temp;
        }

        /* parse a utf-8 string into an ASCII char buffer */
        public static bool ParseUTF8(byte[] pEntry, ref uint os, UInt64 count, ref char[] path)
        {
            byte[] res = ASCIIEncoding.Convert(
                Encoding.UTF8, 
                Encoding.ASCII, 
                pEntry, 
                (int)os, 
                (int)count);
            path = ASCIIEncoding.ASCII.GetChars(res);
            os += (uint)count;

            return true;
        }

        /* skip a compressed dword */
        public static void SkipCWord(byte[] pEntry, ref uint os)
        {
            while (pEntry[os++] >= 0x80)
                ;
        }
    }
}
