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

namespace CHMsharp
{
    internal sealed class Unmarshal
    {
        public static bool ToByteArray(ref byte[] pData, ref uint pDataPos,
            ref uint pLenRemain, ref byte[] dest, int count)
        {
            if (count <= 0 || (uint)count > pLenRemain)
                return false;

            Array.Copy(pData, pDataPos, dest, 0, count);
            pDataPos += (uint)count;
            pLenRemain -= (uint)count;

            return true;
        }

        public static bool ToCharArray(ref byte[] pData, ref uint pDataPos,
            ref uint pLenRemain, ref char[] dest, int count)
        {
            if (count <= 0 || (uint)count > pLenRemain)
                return false;

            Array.Copy(pData, pDataPos, dest, 0, count);
            pDataPos += (uint)count;
            pLenRemain -= (uint)count;

            return true;
        }

        public static bool ToInt16(ref byte[] pData, ref uint pDataPos,
            ref uint pLenRemain, ref Int16 dest)
        {
            if (2 > pLenRemain)
                return false;

            dest = (Int16)(pData[pDataPos + 0] |
                pData[pDataPos + 1] << 8);
            pDataPos += 2;
            pLenRemain -= 2;

            return true;
        }

        public static bool ToInt32(ref byte[] pData, ref uint pDataPos,
            ref uint pLenRemain, ref Int32 dest)
        {
            if (4 > pLenRemain)
                return false;

            dest = pData[pDataPos + 0] | 
                pData[pDataPos + 1] << 8 | 
                pData[pDataPos + 2] << 16 | 
                pData[pDataPos + 3] << 24;
            pDataPos += 4;
            pLenRemain -= 4;

            return true;
        }

        public static bool ToInt64(ref byte[] pData, ref uint pDataPos,
            ref uint pLenRemain, ref Int64 dest)
        {
            Int64 temp = 0;

            if (8 > pLenRemain)
                return false;

            for(int i = 8; i > 0; i--) {
                temp <<= 8;
                temp |= pData[pDataPos + (i - 1)];
            }

            dest = temp;
            pDataPos += 8;
            pLenRemain -= 8;

            return true;
        }

        public static bool ToUInt16(ref byte[] pData, ref uint pDataPos,
            ref uint pLenRemain, ref UInt16 dest)
        {
            if (2 > pLenRemain)
                return false;

            dest = (ushort)(pData[pDataPos + 0] |
                pData[pDataPos + 1] << 8);
            pDataPos += 2;
            pLenRemain -= 2;

            return true;
        }

        public static bool ToUInt32(ref byte[] pData, ref uint pDataPos,
            ref uint pLenRemain, ref UInt32 dest)
        {
            if (4 > pLenRemain)
                return false;

            dest = (uint)(pData[pDataPos + 0] |
                pData[pDataPos + 1] << 8 |
                pData[pDataPos + 2] << 16 |
                pData[pDataPos + 3] << 24);
            pDataPos += 4;
            pLenRemain -= 4;

            return true;
        }

        public static bool ToUInt64(ref byte[] pData, ref uint pDataPos,
            ref uint pLenRemain, ref UInt64 dest)
        {
            UInt64 temp = 0;

            if (8 > pLenRemain)
                return false;

            for (int i = 8; i > 0; i--) {
                temp <<= 8;
                temp |= pData[pDataPos + (i - 1)];
            }

            dest = temp;
            pDataPos += 8;
            pLenRemain -= 8;

            return true;
        }

        public static bool ToUuid(ref byte[] pData, ref uint pDataPos,
            ref uint pLenRemain, ref byte[] dest)
        {
            return ToByteArray(ref pData, ref pDataPos, ref pLenRemain, ref dest, 16);
        }
    }
}
