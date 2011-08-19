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

using LONGINT64 = System.Int64;     /* long long */
using LONGUINT64 = System.UInt64;   /* unsigned long long */

namespace CHMsharp
{
    public sealed class ChmFile
    {
        private const int CHM_PARAM_MAX_BLOCKS_CACHED = 0;
        private const int CHM_MAX_BLOCKS_CACHED = 5;

        private ChmFileInfo _h;
        private string _filename;

        private ChmFile(string filename)
        {
            _h = new ChmFileInfo();
            _filename = filename;
        }

        /* close an ITS archive */
        public void Close()
        { 
            if (_h.fd != null)
                _h.fd.Close();
            _h.fd = null;

            if (_h.lzx_state != null)
                Lzx.LZXteardown(_h.lzx_state);
            _h.lzx_state = null;

            _h.cache_blocks = null;
            _h.cache_block_indices = null;
        }

        /* enumerate the objects in the .chm archive */
        public bool Enumerate(EnumerateLevel what, ChmEnumerator e, object context)
        {
            Int32 curPage;

            /* buffer to hold whatever page we're looking at */
            /* RWE 6/12/2003 */
            byte[] page_buf = new byte[_h.block_len];
            chmPmglHeader header = new chmPmglHeader();
            uint end;
            uint cur;
            uint lenRemain;
            UInt64 ui_path_len;

            /* the current ui */
            ChmUnitInfo ui = new ChmUnitInfo();
            int type_bits = ((int)what & 0x7);
            int filter_bits = ((int)what & 0xF8);

            /* starting page */
            curPage = _h.index_head;

            /* until we have either returned or given up */
            while (curPage != -1) {
                /* try to fetch the index page */
                if (Storage.FetchBytes(ref _h,
                                     ref page_buf,
                                     (UInt64)_h.dir_offset + (UInt64)curPage * _h.block_len,
                                     _h.block_len) != _h.block_len)
                    return false;

                /* figure out start and end for this page */
                cur = 0;
                lenRemain = Pmgl.CHM_PMGL_LEN;
                if (!Pmgl.UnmarshalPmglHeader(ref page_buf, ref cur, ref lenRemain, ref header))
                    return false;
                end = _h.block_len - (header.free_space);

                /* loop over this page */
                while (cur < end) {
                    ui.flags = 0;

                    if (!Pmgl.ParsePgmlEntry(page_buf, ref cur,  ref ui))
                        return false;

                    /* get the length of the path */
                    ui_path_len = (ulong)ui.path.Length;

                    /* check for DIRS */
                    if (ui.path.EndsWith("/"))
                        ui.flags |= (int)EnumerateLevel.Directories;

                    /* check for FILES */
                    if (!ui.path.EndsWith("/"))
                        ui.flags |= (int)EnumerateLevel.Files;

                    /* check for NORMAL vs. META */
                    if (ui.path.StartsWith("/")) {
                        /* check for NORMAL vs. SPECIAL */
                        if (ui.path.Length > 1 && (ui.path[1] == '#' || ui.path[1] == '$'))
                            ui.flags |= (int)EnumerateLevel.Special;
                        else
                            ui.flags |= (int)EnumerateLevel.Normal;
                    }
                    else
                        ui.flags |= (int)EnumerateLevel.Meta;

                    if (!Convert.ToBoolean(type_bits & ui.flags))
                        continue;

                    if (filter_bits != 0 && (filter_bits & ui.flags) == 0)
                        continue;

                    /* call the enumerator */
                    EnumerateStatus status = e(this, ui, context);
                    switch (status) {
                        case EnumerateStatus.Failure:
                            return false;

                        case EnumerateStatus.Continue:
                            break;

                        case EnumerateStatus.Success:
                            return true;

                        default:
                            break;
                    }
                }

                /* advance to next page */
                curPage = header.block_next;
            }

            return true;
        }

        public static ChmFile Open(string filename)
        {
            byte[] sbuffer = new byte[256];
            uint sremain;
            uint sbufpos;
            chmItsfHeader itsfHeader = new chmItsfHeader();
            chmItspHeader itspHeader = new chmItspHeader();
            ChmUnitInfo uiLzxc = new ChmUnitInfo();
            chmLzxcControlData ctlData = new chmLzxcControlData();
            ChmFile chmf = new ChmFile(filename);

            /* allocate handle */
            chmf._h.fd = null;
            chmf._h.lzx_state = null;
            chmf._h.cache_blocks = null;
            chmf._h.cache_block_indices = null;
            chmf._h.cache_num_blocks = 0;

            /* open file */
            chmf._h.fd = File.Open(filename, FileMode.Open, FileAccess.Read);
            
            /* initialize mutexes, if needed */
            chmf._h.mutex = new Mutex();
            chmf._h.lzx_mutex = new Mutex();
            chmf._h.cache_mutex = new Mutex();

            /* read and verify header */
            sremain = Itsf.CHM_ITSF_V3_LEN;
            sbufpos = 0;
            if (Storage.FetchBytes(ref chmf._h, ref sbuffer, 0, sremain) != sremain ||
                !Itsf.UnmarshalItsfHeader(ref sbuffer, ref sbufpos, ref sremain, ref itsfHeader))
            {
                chmf.Close();
                throw new InvalidDataException();
            }

            /* stash important values from header */
            chmf._h.dir_offset = itsfHeader.dir_offset;
            chmf._h.dir_len = itsfHeader.dir_len;
            chmf._h.data_offset = itsfHeader.data_offset;

            /* now, read and verify the directory header chunk */
            sremain = Itsp.CHM_ITSP_V1_LEN;
            sbufpos = 0;
            if (Storage.FetchBytes(ref chmf._h, ref sbuffer,
                           (UInt64)itsfHeader.dir_offset, sremain) != sremain ||
                !Itsp.UnmarshalItpfHeader(ref sbuffer, ref sbufpos, ref sremain, ref itspHeader))
            {
                chmf.Close();
                throw new InvalidDataException();
            }

            /* grab essential information from ITSP header */
            chmf._h.dir_offset += (ulong)itspHeader.header_len;
            chmf._h.dir_len -= (ulong)itspHeader.header_len;
            chmf._h.index_root = itspHeader.index_root;
            chmf._h.index_head = itspHeader.index_head;
            chmf._h.block_len = itspHeader.block_len;

            /* if the index root is -1, this means we don't have any PMGI blocks.
             * as a result, we must use the sole PMGL block as the index root
             */
            if (chmf._h.index_root <= -1)
                chmf._h.index_root = chmf._h.index_head;

            /* By default, compression is enabled. */
            chmf._h.compression_enabled = true;

            /* prefetch most commonly needed unit infos */
            if (!chmf.ResolveObject(Storage.CHMU_RESET_TABLE, ref chmf._h.rt_unit) ||
                chmf._h.rt_unit.space == Storage.CHM_COMPRESSED ||
                !chmf.ResolveObject(Storage.CHMU_CONTENT, ref chmf._h.cn_unit) ||
                chmf._h.cn_unit.space == Storage.CHM_COMPRESSED ||
                !chmf.ResolveObject(Storage.CHMU_LZXC_CONTROLDATA, ref uiLzxc) ||
                uiLzxc.space == Storage.CHM_COMPRESSED)
            {
                chmf._h.compression_enabled = false;
            }

            /* read reset table info */
            if (chmf._h.compression_enabled) {
                sremain = Lzxc.CHM_LZXC_RESETTABLE_V1_LEN;
                sbufpos = 0;  /* TODO bobc: is sbuffer actually at index 0?? */
                if (chmf.RetrieveObject(chmf._h.rt_unit, ref sbuffer,
                                        0, sremain) != sremain              ||
                    !Lzxc.UnmarshalLzxcResetTable(ref sbuffer, ref sbufpos, ref sremain,
                                                 ref chmf._h.reset_table))
                {
                    chmf._h.compression_enabled = false;
                }
            }

            /* read control data */
            if (chmf._h.compression_enabled) {
                sremain = (uint)uiLzxc.length;
                if (uiLzxc.length > (ulong)sbuffer.Length) {
                    chmf.Close();
                    throw new InvalidDataException();
                }

                sbufpos = 0;  /* TODO bobc: is sbuffer actually at index 0?? */
                if (chmf.RetrieveObject(uiLzxc, ref sbuffer,
                                        0, sremain) != sremain              ||
                    !Lzxc.UnmarshalLzxcControlData(ref sbuffer, ref sbufpos, ref sremain,
                                                  ref ctlData))
                {
                    chmf._h.compression_enabled = false;
                }

                chmf._h.window_size = ctlData.windowSize;
                chmf._h.reset_interval = ctlData.resetInterval;

                /* Jed, Mon Jun 28: Experimentally, it appears that the reset block count */
                /*       must be multiplied by this formerly unknown ctrl data field in   */
                /*       order to decompress some files.                                  */
                chmf._h.reset_blkcount = chmf._h.reset_interval    /
                                            (chmf._h.window_size / 2) *
                                            ctlData.windowsPerReset;
            }

            /* initialize cache */
            chmf.SetParam(CHM_PARAM_MAX_BLOCKS_CACHED, CHM_MAX_BLOCKS_CACHED);

            return chmf;
        }

        /* resolve a particular object from the archive */
        public bool ResolveObject(string objPath, ref ChmUnitInfo ui)
        {
            /*
             * XXX: implement caching scheme for dir pages
             */

            Int32 curPage;

            /* buffer to hold whatever page we're looking at */
            /* RWE 6/12/2003 */
            byte[] page_buf = new byte[_h.block_len];

            /* starting page */
            curPage = _h.index_root;

            /* until we have either returned or given up */
            while (curPage != -1) {
                /* try to fetch the index page */
                if (Storage.FetchBytes(ref _h, ref page_buf,
                               (UInt64)_h.dir_offset + (UInt64)curPage * _h.block_len,
                               _h.block_len) != _h.block_len)
                    return false;

                /* now, if it is a leaf node: */
                if (ASCIIEncoding.UTF8.GetString(page_buf, 0, 4).CompareTo(Pmgl.CHM_PMGL_MARKER) == 0) {
                    /* scan block */
                    long pEntry = Pmgl.FindInPmgl(page_buf, _h.block_len, objPath);
                    if (pEntry < 0)
                        return false;

                    /* parse entry and return */
                    uint os = (uint)pEntry;
                    Pmgl.ParsePgmlEntry(page_buf, ref os, ref ui);
                    return true;
                }

                /* else, if it is a branch node: */
                else if (ASCIIEncoding.UTF8.GetString(page_buf, 0, 4).CompareTo(Pmgi.CHM_PMGI_MARKER) == 0)
                    curPage = Pmgi.FindInPmgi(page_buf, _h.block_len, objPath);

                /* else, we are confused.  give up. */
                else
                    return false;
            }

            /* didn't find anything.  fail. */
            return false;
        }

        /* retrieve (part of) an object */
        public LONGINT64 RetrieveObject(ChmUnitInfo ui, ref byte[] buf, LONGUINT64 addr, LONGINT64 len)
        {
            /* must be valid file handle */
            if (_h.fd == null)
                return (Int64)0;

            /* starting address must be in correct range */
            if (addr < 0  ||  addr >= ui.length)
                return (Int64)0;

            /* clip length */
            if (addr + (ulong)len > ui.length)
                len = (LONGINT64)(ui.length - addr);

            /* if the file is uncompressed, it's simple */
            if (ui.space == Storage.CHM_UNCOMPRESSED) {
                /* read data */
                return Storage.FetchBytes(ref _h, ref buf,
                                  (UInt64)_h.data_offset + (UInt64)ui.start + (UInt64)addr,
                                  len);
            }

            /* else if the file is compressed, it's a little trickier */
            else /* ui->space == CHM_COMPRESSED */
            {
                Int64 swath = 0, total = 0;
                UInt64 bufpos = 0;

                /* if compression is not enabled for this file... */
                if (!_h.compression_enabled)
                    return total;

                do {
                    /* swill another mouthful */
                    swath = Lzxc.DecompressRegion(ref _h, ref buf, bufpos, ui.start + addr, len);

                    /* if we didn't get any... */
                    if (swath == 0)
                        return total;

                    /* update stats */
                    total += swath;
                    len -= swath;
                    addr += (LONGUINT64)swath;
                    bufpos += (LONGUINT64)swath;

                } while (len != 0);

                return total;
            }
        }

        /*
         * set a parameter on the file handle.
         * valid parameter types:
         *          CHM_PARAM_MAX_BLOCKS_CACHED:
         *                 how many decompressed blocks should be cached?  A simple
         *                 caching scheme is used, wherein the index of the block is
         *                 used as a hash value, and hash collision results in the
         *                 invalidation of the previously cached block.
         */
        public void SetParam(int paramType, int paramVal)
        {
            switch (paramType) {
                case CHM_PARAM_MAX_BLOCKS_CACHED:
                    _h.cache_mutex.WaitOne();
                    if (paramVal != _h.cache_num_blocks) {
                        byte[][] newBlocks;
                        UInt64[] newIndices;
                        int i;

                        /* allocate new cached blocks */
                        newBlocks = new byte[paramVal][];
                        newIndices = new UInt64[paramVal];
                        for (i = 0; i < paramVal; i++) {
                            newBlocks[i] = null;
                            newIndices[i] = 0;
                        }

                        /* re-distribute old cached blocks */
                        if (_h.cache_blocks != null) {
                            for (i = 0; i < _h.cache_num_blocks; i++) {
                                int newSlot = (int)(_h.cache_block_indices[i] % (UInt64)paramVal);

                                if (_h.cache_blocks[i] != null) {
                                    /* in case of collision, destroy newcomer */
                                    if (newBlocks[newSlot] != null)
                                        _h.cache_blocks[i] = null;
                                    else {
                                        newBlocks[newSlot] = _h.cache_blocks[i];
                                        newIndices[newSlot] =
                                                    _h.cache_block_indices[i];
                                    }
                                }
                            }
                        }

                        /* now, set new values */
                        _h.cache_blocks = newBlocks;
                        _h.cache_block_indices = newIndices;
                        _h.cache_num_blocks = paramVal;
                    }
                    _h.cache_mutex.ReleaseMutex();
                    break;

                default:
                    break;
            }
        }

        public string FileName
        {
            get { return _filename; }
        }
    }
}
