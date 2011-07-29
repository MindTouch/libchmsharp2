/***************************************************************************
 *                        lzx.c - LZX decompression routines               *
 *                           -------------------                           *
 *                                                                         *
 *  maintainer: Bob Carroll <bob.carroll@alum.rit.edu>                     *
 *  source:     C# port of lzx.c from CHMLib                               *
 *  notes:      This file was taken from CHMLib v0.40a and before that     *
 *              cabextract v0.5, which was, itself, a modified version of  *
 *              the lzx decompression code from unlzx.                     *
 *                                                                         *
 *  platforms:  Microsoft CLR and Mono                                     *
 ***************************************************************************/

/***************************************************************************
 *                                                                         *
 *   This program is free software; you can redistribute it and/or modify  *
 *   it under the terms of the GNU General Public License as published by  *
 *   the Free Software Foundation; either version 2 of the License, or     *
 *   (at your option) any later version.  Note that an exemption to this   *
 *   license has been granted by Stuart Caie for the purposes of           *
 *   distribution with chmlib.  This does not, to the best of my           *
 *   knowledge, constitute a change in the license of this (the LZX) code  *
 *   in general.                                                           *
 *                                                                         *
 ***************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UBYTE = System.Byte;      /* 8 bits exactly    */
using UWORD = System.UInt16;    /* 16 bits (or more) */
using ULONG = System.UInt32;    /* 32 bits (or more) */
using LONG = System.Int32;      /* 32 bits (or more) */

namespace CHMsharp
{
    internal sealed class Lzx
    {
        /* return codes */
        public const int DECR_OK = 0;
        public const int DECR_DATAFORMAT = 1;
        public const int DECR_ILLEGALDATA = 2;
        public const int DECR_NOMEMORY = 3;

        /* some constants defined by the LZX specification */
        private const int LZX_MIN_MATCH = 2;
        private const int LZX_MAX_MATCH = 257;
        private const int LZX_NUM_CHARS = 256;
        private const int LZX_BLOCKTYPE_INVALID = 0;         /* also blocktypes 4-7 invalid */
        private const int LZX_BLOCKTYPE_VERBATIM = 1;
        private const int LZX_BLOCKTYPE_ALIGNED = 2;
        private const int LZX_BLOCKTYPE_UNCOMPRESSED = 3;
        private const int LZX_PRETREE_NUM_ELEMENTS = 20;
        private const int LZX_ALIGNED_NUM_ELEMENTS = 8;      /* aligned offset tree #elements */
        private const int LZX_NUM_PRIMARY_LENGTHS = 7;       /* this one missing from spec! */
        private const int LZX_NUM_SECONDARY_LENGTHS = 249;   /* length tree #elements */

        /* LZX huffman defines: tweak tablebits as desired */
        private const int LZX_PRETREE_MAXSYMBOLS = LZX_PRETREE_NUM_ELEMENTS;
        private const int LZX_PRETREE_TABLEBITS = 6;
        private const int LZX_MAINTREE_MAXSYMBOLS = (LZX_NUM_CHARS + 50 * 8);
        private const int LZX_MAINTREE_TABLEBITS = 12;
        private const int LZX_LENGTH_MAXSYMBOLS = (LZX_NUM_SECONDARY_LENGTHS + 1);
        private const int LZX_LENGTH_TABLEBITS = 12;
        private const int LZX_ALIGNED_MAXSYMBOLS = LZX_ALIGNED_NUM_ELEMENTS;
        private const int LZX_ALIGNED_TABLEBITS = 7;

        private const int LZX_LENTABLE_SAFETY = 64;          /* we allow length table decoding overruns */

        internal sealed class LZXstate
        {
            public LZXstate() { }

            public UBYTE[] window;        /* the actual decoding window              */
            public ULONG window_size;     /* window size (32Kb through 2Mb)          */
            public ULONG actual_size;     /* window size when it was first allocated */
            public ULONG window_posn;     /* current offset within the window        */
            public ULONG R0, R1, R2;      /* for the LRU offset system               */
            public UWORD main_elements;   /* number of main tree elements            */
            public int header_read;       /* have we started decoding at all yet?    */
            public ULONG block_type;      /* type of this block                      */
            public ULONG block_length;    /* uncompressed length of this block       */
            public ULONG block_remaining; /* uncompressed bytes still left to decode */
            public ULONG frames_read;     /* the number of CFDATA blocks processed   */
            public LONG intel_filesize;   /* magic header value used for transform   */
            public LONG intel_curpos;     /* current offset in transform space       */
            public int intel_started;     /* have we seen any translatable data yet? */

            /* LZX_DECLARE_TABLE(PRETREE); */
            public UWORD[] PRETREE_table = new UWORD[(1 << LZX_PRETREE_TABLEBITS) + (LZX_PRETREE_MAXSYMBOLS << 1)];
            public UBYTE[] PRETREE_len = new UBYTE[LZX_PRETREE_MAXSYMBOLS + LZX_LENTABLE_SAFETY];

            /* LZX_DECLARE_TABLE(MAINTREE); */
            public UWORD[] MAINTREE_table = new UWORD[(1 << LZX_MAINTREE_TABLEBITS) + (LZX_MAINTREE_MAXSYMBOLS << 1)];
            public UBYTE[] MAINTREE_len = new UBYTE[LZX_MAINTREE_MAXSYMBOLS + LZX_LENTABLE_SAFETY];

            /* LZX_DECLARE_TABLE(LENGTH); */
            public UWORD[] LENGTH_table = new UWORD[(1 << LZX_LENGTH_TABLEBITS) + (LZX_LENGTH_MAXSYMBOLS << 1)];
            public UBYTE[] LENGTH_len = new UBYTE[LZX_LENGTH_MAXSYMBOLS + LZX_LENTABLE_SAFETY];

            /*LZX_DECLARE_TABLE(ALIGNED); */
            public UWORD[] ALIGNED_table = new UWORD[(1 << LZX_ALIGNED_TABLEBITS) + (LZX_ALIGNED_MAXSYMBOLS << 1)];
            public UBYTE[] ALIGNED_len = new UBYTE[LZX_ALIGNED_MAXSYMBOLS + LZX_LENTABLE_SAFETY];
        };

        /* LZX decruncher */

        /* Microsoft's LZX document and their implementation of the
         * com.ms.util.cab Java package do not concur.
         *
         * In the LZX document, there is a table showing the correlation between
         * window size and the number of position slots. It states that the 1MB
         * window = 40 slots and the 2MB window = 42 slots. In the implementation,
         * 1MB = 42 slots, 2MB = 50 slots. The actual calculation is 'find the
         * first slot whose position base is equal to or more than the required
         * window size'. This would explain why other tables in the document refer
         * to 50 slots rather than 42.
         *
         * The constant NUM_PRIMARY_LENGTHS used in the decompression pseudocode
         * is not defined in the specification.
         *
         * The LZX document does not state the uncompressed block has an
         * uncompressed length field. Where does this length field come from, so
         * we can know how large the block is? The implementation has it as the 24
         * bits following after the 3 blocktype bits, before the alignment
         * padding.
         *
         * The LZX document states that aligned offset blocks have their aligned
         * offset huffman tree AFTER the main and length trees. The implementation
         * suggests that the aligned offset tree is BEFORE the main and length
         * trees.
         *
         * The LZX document decoding algorithm states that, in an aligned offset
         * block, if an extra_bits value is 1, 2 or 3, then that number of bits
         * should be read and the result added to the match offset. This is
         * correct for 1 and 2, but not 3, where just a huffman symbol (using the
         * aligned tree) should be read.
         *
         * Regarding the E8 preprocessing, the LZX document states 'No translation
         * may be performed on the last 6 bytes of the input block'. This is
         * correct.  However, the pseudocode provided checks for the *E8 leader*
         * up to the last 6 bytes. If the leader appears between -10 and -7 bytes
         * from the end, this would cause the next four bytes to be modified, at
         * least one of which would be in the last 6 bytes, which is not allowed
         * according to the spec.
         *
         * The specification states that the huffman trees must always contain at
         * least one element. However, many CAB files contain blocks where the
         * length tree is completely empty (because there are no matches), and
         * this is expected to succeed.
         */


        /* LZX uses what it calls 'position slots' to represent match offsets.
         * What this means is that a small 'position slot' number and a small
         * offset from that slot are encoded instead of one large offset for
         * every match.
         * - position_base is an index to the position slot bases
         * - extra_bits states how many bits of offset-from-base data is needed.
         */
        private static UBYTE[] extra_bits = new UBYTE[] {
             0,  0,  0,  0,  1,  1,  2,  2,  3,  3,  4,  4,  5,  5,  6,  6,
             7,  7,  8,  8,  9,  9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14,
            15, 15, 16, 16, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17,
            17, 17, 17
        };

        private static ULONG[] position_base = new ULONG[] {
                  0,       1,       2,      3,      4,      6,      8,     12,     16,     24,     32,       48,      64,      96,     128,     192,
                256,     384,     512,    768,   1024,   1536,   2048,   3072,   4096,   6144,   8192,    12288,   16384,   24576,   32768,   49152,
              65536,   98304,  131072, 196608, 262144, 393216, 524288, 655360, 786432, 917504, 1048576, 1179648, 1310720, 1441792, 1572864, 1703936,
            1835008, 1966080, 2097152
        };

        public static LZXstate LZXinit(int window)
        {
            LZXstate pState = null;
            ULONG wndsize = (ULONG)(1 << window);
            int i, posn_slots;

            /* LZX supports window sizes of 2^15 (32Kb) through 2^21 (2Mb) */
            /* if a previously allocated window is big enough, keep it     */
            if (window < 15 || window > 21) return null;

            /* allocate state and associated window */
            pState = new LZXstate();
            pState.window = new UBYTE[wndsize];
            pState.actual_size = wndsize;
            pState.window_size = wndsize;

            /* calculate required position slots */
            if (window == 20) posn_slots = 42;
            else if (window == 21) posn_slots = 50;
            else posn_slots = window << 1;

            /** alternatively **/
            /* posn_slots=i=0; while (i < wndsize) i += 1 << extra_bits[posn_slots++]; */

            /* initialize other state */
            pState.R0  =  pState.R1  = pState.R2 = 1;
            pState.main_elements   = (UWORD)(LZX_NUM_CHARS + (posn_slots << 3));
            pState.header_read     = 0;
            pState.frames_read     = 0;
            pState.block_remaining = 0;
            pState.block_type      = LZX_BLOCKTYPE_INVALID;
            pState.intel_curpos    = 0;
            pState.intel_started   = 0;
            pState.window_posn     = 0;

            /* initialise tables to 0 (because deltas will be applied to them) */
            for (i = 0; i < LZX_MAINTREE_MAXSYMBOLS; i++) pState.MAINTREE_len[i] = 0;
            for (i = 0; i < LZX_LENGTH_MAXSYMBOLS; i++)   pState.LENGTH_len[i]   = 0;

            return pState;
        }

        public static void LZXteardown(LZXstate pState)
        {

        }

        public static int LZXreset(LZXstate pState)
        {
            int i;

            pState.R0  =  pState.R1  = pState.R2 = 1;
            pState.header_read     = 0;
            pState.frames_read     = 0;
            pState.block_remaining = 0;
            pState.block_type      = LZX_BLOCKTYPE_INVALID;
            pState.intel_curpos    = 0;
            pState.intel_started   = 0;
            pState.window_posn     = 0;

            for (i = 0; i < LZX_MAINTREE_MAXSYMBOLS + LZX_LENTABLE_SAFETY; i++) pState.MAINTREE_len[i] = 0;
            for (i = 0; i < LZX_LENGTH_MAXSYMBOLS + LZX_LENTABLE_SAFETY; i++)   pState.LENGTH_len[i]   = 0;

            return DECR_OK;
        }

        /* Bitstream reading macros:
         *
         * INIT_BITSTREAM    should be used first to set up the system
         * READ_BITS(var,n)  takes N bits from the buffer and puts them in var
         *
         * ENSURE_BITS(n)    ensures there are at least N bits in the bit buffer
         * PEEK_BITS(n)      extracts (without removing) N bits from the bit buffer
         * REMOVE_BITS(n)    removes N bits from the bit buffer
         *
         * These bit access routines work by using the area beyond the MSB and the
         * LSB as a free source of zeroes. This avoids having to mask any bits.
         * So we have to know the bit width of the bitbuffer variable. This is
         * sizeof(ULONG) * 8, also defined as ULONG_BITS
         */

        /* number of bits in ULONG. Note: This must be at multiple of 16, and at
         * least 32 for the bitbuffer code to work (ie, it must be able to ensure
         * up to 17 bits - that's adding 16 bits when there's one bit left, or
         * adding 32 bits when there are no bits left. The code should work fine
         * for machines where ULONG >= 32 bits.
         */
        private static ULONG ULONG_BITS()
        {
            return sizeof(ULONG) << 3;
        }

        private static void INIT_BITSTREAM(ref int bitsleft, ref ULONG bitbuf)
        {
            bitsleft = 0;
            bitbuf = 0;
        }

        private static void ENSURE_BITS(ref int bitsleft, ref ULONG bitbuf, ref UBYTE[] ipbuf, 
            ref long inpos, int n)
        {
            while (bitsleft < n) {
                bitbuf |= (ULONG)((ipbuf[inpos + 1] << 8) | ipbuf[inpos + 0]) << (int)(ULONG_BITS() - 16 - bitsleft);
                bitsleft += 16; inpos += 2;
            }
        }

        private static ULONG PEEK_BITS(ref ULONG bitbuf, int n)
        {
            return bitbuf >> (int)(ULONG_BITS() - n);
        }

        private static void REMOVE_BITS(ref int bitsleft, ref ULONG bitbuf, int n)
        {
            bitbuf <<= n;
            bitsleft -= n;
        }

        private static void READ_BITS(ref ULONG v, int n, ref ULONG bitbuf, ref int bitsleft, 
            ref UBYTE[] ipbuf, ref long inpos)
        {
            ENSURE_BITS(ref bitsleft, ref bitbuf, ref ipbuf, ref inpos, n);
            v = PEEK_BITS(ref bitbuf, n);
            REMOVE_BITS(ref bitsleft, ref bitbuf, n);
        }

        /* Huffman macros */

        /* BUILD_TABLE(tablename) builds a huffman lookup table from code lengths.
         * In reality, it just calls make_decode_table() with the appropriate
         * values - they're all fixed by some #defines anyway, so there's no point
         * writing each call out in full by hand.
         */
        private static bool BUILD_TABLE(ref UWORD[] tbl, ref UBYTE[] lentbl, int tablebits, int maxsymbols)
        {
            return make_decode_table((uint)maxsymbols, (uint)tablebits, ref lentbl, ref tbl) == 0;
        }

        /* READ_HUFFSYM(tablename, var) decodes one huffman symbol from the
         * bitstream using the stated table and puts it in var.
         */
        private static bool READ_HUFFSYM(ref UWORD[] tbl, ref UWORD[] hufftbl, ref LONG bitsleft, ref ULONG bitbuf,
            ref UBYTE[] ipbuf, ref long inpos, ref ULONG i, int tablebits, int maxsymbols, ref UBYTE[] tablelen, 
            ref ULONG j, ref int var)
        {
            ENSURE_BITS(ref bitsleft, ref bitbuf, ref ipbuf, ref inpos, 16);
            hufftbl = tbl;

            if ((i = hufftbl[PEEK_BITS(ref bitbuf, tablebits)]) >= maxsymbols) {
                j = (ULONG)(1 << (int)(ULONG_BITS() - tablebits));
                do {
                    j >>= 1; i <<= 1; i |= Convert.ToBoolean(bitbuf & j) ? (ULONG)1 : 0;
                    if (!Convert.ToBoolean(j)) { return false; /* DECR_ILLEGALDATA */ }
                } while ((i = hufftbl[i]) >= maxsymbols);
            }

            j = tablelen[var = (int)i];
            REMOVE_BITS(ref bitsleft, ref bitbuf, (int)j);

            return true;
        }

        /* READ_LENGTHS(tablename, first, last) reads in code lengths for symbols
         * first to last in the given table. The code lengths are stored in their
         * own special LZX way.
         */
        private static bool READ_LENGTHS(ULONG first, ULONG last, lzx_bits lb, ref ULONG bitbuf, 
            ref LONG bitsleft, ref UBYTE[] ip, ref long inpos, LZXstate pState, ref UBYTE[] tablelen)
        {
            lb.bb = bitbuf;
            lb.bl = bitsleft;
            lb.ip = ip;
            lb.ippos = inpos;

            if (lzx_read_lens(pState, ref tablelen, first, last, lb) != 0)
                return false;

            bitbuf = lb.bb;
            bitsleft = lb.bl;
            inpos = lb.ippos;

            return true;
        }

        /* make_decode_table(nsyms, nbits, length[], table[])
         *
         * This function was coded by David Tritscher. It builds a fast huffman
         * decoding table out of just a canonical huffman code lengths table.
         *
         * nsyms  = total number of symbols in this huffman tree.
         * nbits  = any symbols with a code length of nbits or less can be decoded
         *          in one lookup of the table.
         * length = A table to get code lengths from [0 to syms-1]
         * table  = The table to fill up with decoded symbols and pointers.
         *
         * Returns 0 for OK or 1 for error
         */
        private static int make_decode_table(ULONG nsyms, ULONG nbits, ref UBYTE[] length, ref UWORD[] table)
        {
            UWORD sym;
            ULONG leaf;
            UBYTE bit_num = 1;
            ULONG fill;
            ULONG pos         = 0; /* the current position in the decode table */
            ULONG table_mask  = (ULONG)(1 << (int)nbits);
            ULONG bit_mask    = table_mask >> 1; /* don't do 0 length codes */
            ULONG next_symbol = bit_mask; /* base of allocation for long codes */

            /* fill entries for codes short enough for a direct mapping */
            while (bit_num <= nbits) {
                for (sym = 0; sym < nsyms; sym++) {
                    if (length[sym] == bit_num) {
                        leaf = pos;

                        if((pos += bit_mask) > table_mask) return 1; /* table overrun */

                        /* fill all possible lookups of this symbol with the symbol itself */
                        fill = bit_mask;
                        while (fill-- > 0) table[leaf++] = sym;
                    }
                }

                bit_mask >>= 1;
                bit_num++;
            }

            /* if there are any codes longer than nbits */
            if (pos != table_mask) {
                /* clear the remainder of the table */
                for (sym = (UWORD)pos; sym < table_mask; sym++) table[sym] = 0;

                /* give ourselves room for codes to grow by up to 16 more bits */
                pos <<= 16;
                table_mask <<= 16;
                bit_mask = 1 << 15;

                while (bit_num <= 16) {
                    for (sym = 0; sym < nsyms; sym++) {
                        if (length[sym] == bit_num) {
                            leaf = pos >> 16;
                            for (fill = 0; fill < bit_num - nbits; fill++) {
                                /* if this path hasn't been taken yet, 'allocate' two entries */
                                if (table[leaf] == 0) {
                                    table[(next_symbol << 1)] = 0;
                                    table[(next_symbol << 1) + 1] = 0;
                                    table[leaf] = (UWORD)next_symbol++;
                                }
                                /* follow the path and select either left or right for next bit */
                                leaf = (ULONG)(table[leaf] << 1);
                                if (Convert.ToBoolean((pos >> (int)(15 - fill)) & 1)) leaf++;
                            }
                            table[leaf] = sym;

                            if ((pos += bit_mask) > table_mask) return 1; /* table overflow */
                        }
                    }
                    bit_mask >>= 1;
                    bit_num++;
                }
            }

            /* full table? */
            if (pos == table_mask) return 0;

            /* either erroneous table, or all elements are 0 - let's find out. */
            for (sym = 0; sym < nsyms; sym++) if (Convert.ToBoolean(length[sym])) return 1;
            return 0;
        }

        private sealed class lzx_bits
        {
            public ULONG bb;
            public int bl;
            public UBYTE[] ip;
            public long ippos;
        };

        private static int lzx_read_lens(LZXstate pState, ref UBYTE[] lens, ULONG first, ULONG last, lzx_bits lb)
        {
            ULONG i = 0, j = 0, x = 0, y = 0;
            int z = 0;

            ULONG bitbuf = lb.bb;
            int bitsleft = lb.bl;
            long inpos = lb.ippos;
            UWORD[] hufftbl = null;
            bool hr;

            for (x = 0; x < 20; x++) {
                READ_BITS(ref y, 4, ref bitbuf, ref bitsleft, ref lb.ip, ref inpos);
                pState.PRETREE_len[x] = (byte)y;
            }
            if (!BUILD_TABLE(ref pState.PRETREE_table, ref pState.PRETREE_len, 
                    LZX_PRETREE_TABLEBITS, LZX_PRETREE_MAXSYMBOLS))
                return DECR_ILLEGALDATA;

            for (x = first; x < last; ) {
                hr = READ_HUFFSYM(ref pState.PRETREE_table, ref hufftbl, ref bitsleft, 
                    ref bitbuf, ref lb.ip, ref inpos, ref i, LZX_PRETREE_TABLEBITS, LZX_PRETREE_MAXSYMBOLS, 
                    ref pState.PRETREE_len, ref j, ref z);
                if (!hr)
                    return DECR_ILLEGALDATA;

                if (z == 17) {
                    READ_BITS(ref y, 4, ref bitbuf, ref bitsleft, ref lb.ip, ref inpos); y += 4;
                    while (Convert.ToBoolean(y--)) lens[x++] = 0;
                }
                else if (z == 18) {
                    READ_BITS(ref y, 5, ref bitbuf, ref bitsleft, ref lb.ip, ref inpos); y += 20;
                    while (Convert.ToBoolean(y--)) lens[x++] = 0;
                }
                else if (z == 19) {
                    READ_BITS(ref y, 1, ref bitbuf, ref bitsleft, ref lb.ip, ref inpos); y += 4;
                    hr = READ_HUFFSYM(ref pState.PRETREE_table, ref hufftbl, ref bitsleft,
                        ref bitbuf, ref lb.ip, ref inpos, ref i, LZX_PRETREE_TABLEBITS, LZX_PRETREE_MAXSYMBOLS,
                        ref pState.PRETREE_len, ref j, ref z);
                    if (!hr)
                        return DECR_ILLEGALDATA;

                    z = lens[x] - z; if (z < 0) z += 17;
                    while (Convert.ToBoolean(y--)) lens[x++] = (byte)z;
                }
                else {
                    z = lens[x] - z; if (z < 0) z += 17;
                    lens[x++] = (byte)z;
                }
            }

            lb.bb = bitbuf;
            lb.bl = bitsleft;
            lb.ippos = inpos;
            return 0;
        }

        public static int LZXdecompress(LZXstate pState, ref UBYTE[] ip, long inpos, ref UBYTE[] op, 
            ulong outpos, int inlen, int outlen)
        {
            long endinp = inpos + inlen;
            UBYTE[] window = pState.window;
            long runsrc = 0;
            long rundest = 0;
            UWORD[] hufftbl = null; /* used in READ_HUFFSYM macro as chosen decoding table */

            ULONG window_posn = pState.window_posn;
            ULONG window_size = pState.window_size;
            ULONG R0 = pState.R0;
            ULONG R1 = pState.R1;
            ULONG R2 = pState.R2;

            ULONG bitbuf = 0;
            int bitsleft = 0;
            ULONG match_offset, i = 0, j = 0, k = 0; /* ijk used in READ_HUFFSYM macro */
            lzx_bits lb = new lzx_bits(); /* used in READ_LENGTHS macro */
            bool hr;

            int togo = outlen, this_run, main_element = 0, aligned_bits = 0;
            int match_length, length_footer = 0, extra;
            uint verbatim_bits = 0;

            INIT_BITSTREAM(ref bitsleft, ref bitbuf);

            /* read header if necessary */
            if (!Convert.ToBoolean(pState.header_read)) {
                i = j = 0;
                READ_BITS(ref k, 1, ref bitbuf, ref bitsleft, ref ip, ref inpos);
                if (Convert.ToBoolean(k)) { 
                    READ_BITS(ref i, 16, ref bitbuf, ref bitsleft, ref ip, ref inpos);
                    READ_BITS(ref j, 16, ref bitbuf, ref bitsleft, ref ip, ref inpos);
                }
                pState.intel_filesize = (LONG)((i << 16) | j); /* or 0 if not encoded */
                pState.header_read = 1;
            }

            /* main decoding loop */
            while (togo > 0) {
                /* last block finished, new block expected */
                if (pState.block_remaining == 0) {
                    if (pState.block_type == LZX_BLOCKTYPE_UNCOMPRESSED) {
                        if (Convert.ToBoolean(pState.block_length & 1)) inpos++; /* realign bitstream to word */
                        INIT_BITSTREAM(ref bitsleft, ref bitbuf);
                    }

                    READ_BITS(ref pState.block_type, 3, ref bitbuf, ref bitsleft, ref ip, ref inpos);
                    READ_BITS(ref i, 16, ref bitbuf, ref bitsleft, ref ip, ref inpos);
                    READ_BITS(ref j, 8, ref bitbuf, ref bitsleft, ref ip, ref inpos);
                    pState.block_remaining = pState.block_length = (i << 8) | j;

                    switch (pState.block_type) {
                        case LZX_BLOCKTYPE_ALIGNED:
                            for (i = 0; i < 8; i++) {
                                READ_BITS(ref j, 3, ref bitbuf, ref bitsleft, ref ip, ref inpos);
                                pState.ALIGNED_len[i] = (byte)j;
                            }
                            if (!BUILD_TABLE(ref pState.ALIGNED_table, ref pState.ALIGNED_len, 
                                    LZX_ALIGNED_TABLEBITS, LZX_ALIGNED_MAXSYMBOLS))
                                return DECR_ILLEGALDATA;
                            /* rest of aligned header is same as verbatim */
                            goto case LZX_BLOCKTYPE_VERBATIM;

                        case LZX_BLOCKTYPE_VERBATIM:
                            if (!READ_LENGTHS(0, 256, lb, ref bitbuf, ref bitsleft, ref ip, ref inpos, 
                                    pState, ref pState.MAINTREE_len))
                                return DECR_ILLEGALDATA;
                            if (!READ_LENGTHS(256, pState.main_elements, lb, ref bitbuf, ref bitsleft, 
                                    ref ip, ref inpos, pState, ref pState.MAINTREE_len))
                                return DECR_ILLEGALDATA;
                            if (!BUILD_TABLE(ref pState.MAINTREE_table, ref pState.MAINTREE_len, 
                                    LZX_MAINTREE_TABLEBITS, LZX_MAINTREE_MAXSYMBOLS))
                                return DECR_ILLEGALDATA;
                            if (pState.MAINTREE_len[0xE8] != 0) pState.intel_started = 1;

                            if (!READ_LENGTHS(0, LZX_NUM_SECONDARY_LENGTHS, lb, ref bitbuf, ref bitsleft, 
                                    ref ip, ref inpos, pState, ref pState.LENGTH_len))
                                return DECR_ILLEGALDATA;
                            if (!BUILD_TABLE(ref pState.LENGTH_table, ref pState.LENGTH_len, 
                                    LZX_LENGTH_TABLEBITS, LZX_LENGTH_MAXSYMBOLS))
                                return DECR_ILLEGALDATA;
                            break;

                        case LZX_BLOCKTYPE_UNCOMPRESSED:
                            pState.intel_started = 1; /* because we can't assume otherwise */
                            ENSURE_BITS(ref bitsleft, ref bitbuf, ref ip, ref inpos, 16); /* get up to 16 pad bits into the buffer */
                            if (bitsleft > 16) inpos -= 2; /* and align the bitstream! */
                            R0 = (ULONG)(ip[inpos + 0]|(ip[inpos + 1]<<8)|(ip[inpos + 2]<<16)|(ip[inpos + 3]<<24));inpos+=4;
                            R1 = (ULONG)(ip[inpos + 0]|(ip[inpos + 1]<<8)|(ip[inpos + 2]<<16)|(ip[inpos + 3]<<24));inpos+=4;
                            R2 = (ULONG)(ip[inpos + 0]|(ip[inpos + 1]<<8)|(ip[inpos + 2]<<16)|(ip[inpos + 3]<<24));inpos+=4;
                            break;

                        default:
                            return DECR_ILLEGALDATA;
                    }
                }

                /* buffer exhaustion check */
                if (inpos > endinp) {
                    /* it's possible to have a file where the next run is less than
                     * 16 bits in size. In this case, the READ_HUFFSYM() macro used
                     * in building the tables will exhaust the buffer, so we should
                     * allow for this, but not allow those accidentally read bits to
                     * be used (so we check that there are at least 16 bits
                     * remaining - in this boundary case they aren't really part of
                     * the compressed data)
                     */
                    if (inpos > (endinp+2) || bitsleft < 16) return DECR_ILLEGALDATA;
                }

                while ((this_run = (LONG)pState.block_remaining) > 0 && togo > 0) {
                    if (this_run > togo) this_run = togo;
                    togo -= this_run;
                    pState.block_remaining -= (ULONG)this_run;

                    /* apply 2^x-1 mask */
                    window_posn &= window_size - 1;
                    /* runs can't straddle the window wraparound */
                    if ((window_posn + this_run) > window_size)
                        return DECR_DATAFORMAT;

                    switch (pState.block_type) {

                        case LZX_BLOCKTYPE_VERBATIM:
                            while (this_run > 0) {
                                hr = READ_HUFFSYM(ref pState.MAINTREE_table, ref hufftbl, ref bitsleft, 
                                    ref bitbuf, ref ip, ref inpos, ref i, LZX_MAINTREE_TABLEBITS, 
                                    LZX_MAINTREE_MAXSYMBOLS, ref pState.MAINTREE_len, ref j, ref main_element);
                                if (!hr)
                                    return DECR_ILLEGALDATA;

                                if (main_element < LZX_NUM_CHARS) {
                                    /* literal: 0 to LZX_NUM_CHARS-1 */
                                    window[window_posn++] = (UBYTE)main_element;
                                    this_run--;
                                }
                                else {
                                    /* match: LZX_NUM_CHARS + ((slot<<3) | length_header (3 bits)) */
                                    main_element -= LZX_NUM_CHARS;

                                    match_length = main_element & LZX_NUM_PRIMARY_LENGTHS;
                                    if (match_length == LZX_NUM_PRIMARY_LENGTHS) {
                                        hr = READ_HUFFSYM(ref pState.LENGTH_table, ref hufftbl, ref bitsleft, 
                                            ref bitbuf, ref ip, ref inpos, ref i, LZX_LENGTH_TABLEBITS, 
                                            LZX_LENGTH_MAXSYMBOLS, ref pState.LENGTH_len, ref j, ref length_footer);
                                        if (!hr)
                                            return DECR_ILLEGALDATA;
                                        match_length += length_footer;
                                    }
                                    match_length += LZX_MIN_MATCH;

                                    match_offset = (ULONG)main_element >> 3;

                                    if (match_offset > 2) {
                                        /* not repeated offset */
                                        if (match_offset != 3) {
                                            extra = extra_bits[match_offset];
                                            READ_BITS(ref verbatim_bits, extra, ref bitbuf, ref bitsleft, 
                                                ref ip, ref inpos);
                                            match_offset = position_base[match_offset] - 2 + verbatim_bits;
                                        }
                                        else {
                                            match_offset = 1;
                                        }

                                        /* update repeated offset LRU queue */
                                        R2 = R1; R1 = R0; R0 = match_offset;
                                    }
                                    else if (match_offset == 0) {
                                        match_offset = R0;
                                    }
                                    else if (match_offset == 1) {
                                        match_offset = R1;
                                        R1 = R0; R0 = match_offset;
                                    }
                                    else /* match_offset == 2 */ {
                                        match_offset = R2;
                                        R2 = R0; R0 = match_offset;
                                    }

                                    rundest = window_posn;
                                    runsrc  = rundest - match_offset;
                                    window_posn += (ULONG)match_length;
                                    if (window_posn > window_size) return DECR_ILLEGALDATA;
                                    this_run -= match_length;

                                    /* copy any wrapped around source data */
                                    while ((runsrc < 0) && (match_length-- > 0)) {
                                        window[rundest++] = window[runsrc + window_size]; runsrc++;
                                    }

                                    /* copy match data - no worries about destination wraps */
                                    while (match_length-- > 0) window[rundest++] = window[runsrc++];
                                }
                            }
                            break;

                        case LZX_BLOCKTYPE_ALIGNED:
                            while (this_run > 0) {
                                hr = READ_HUFFSYM(ref pState.MAINTREE_table, ref hufftbl, ref bitsleft, 
                                    ref bitbuf, ref ip, ref inpos, ref i, LZX_MAINTREE_TABLEBITS, 
                                    LZX_MAINTREE_MAXSYMBOLS, ref pState.MAINTREE_len, ref j, ref main_element);
                                if (!hr)
                                    return DECR_ILLEGALDATA;

                                if (main_element < LZX_NUM_CHARS) {
                                    /* literal: 0 to LZX_NUM_CHARS-1 */
                                    window[window_posn++] = (UBYTE)main_element;
                                    this_run--;
                                }
                                else {
                                    /* match: LZX_NUM_CHARS + ((slot<<3) | length_header (3 bits)) */
                                    main_element -= LZX_NUM_CHARS;

                                    match_length = main_element & LZX_NUM_PRIMARY_LENGTHS;
                                    if (match_length == LZX_NUM_PRIMARY_LENGTHS) {
                                        hr = READ_HUFFSYM(ref pState.LENGTH_table, ref hufftbl, ref bitsleft, 
                                            ref bitbuf, ref ip, ref inpos, ref i, LZX_LENGTH_TABLEBITS, 
                                            LZX_LENGTH_MAXSYMBOLS, ref pState.LENGTH_len, ref j, ref length_footer);
                                        if (!hr)
                                            return DECR_ILLEGALDATA;
                                        match_length += length_footer;
                                    }
                                    match_length += LZX_MIN_MATCH;

                                    match_offset = (ULONG)main_element >> 3;

                                    if (match_offset > 2) {
                                        /* not repeated offset */
                                        extra = extra_bits[match_offset];
                                        match_offset = position_base[match_offset] - 2;
                                        if (extra > 3) {
                                            /* verbatim and aligned bits */
                                            extra -= 3;
                                            READ_BITS(ref verbatim_bits, extra, ref bitbuf, ref bitsleft, 
                                                ref ip, ref inpos);
                                            match_offset += (verbatim_bits << 3);
                                            hr = READ_HUFFSYM(ref pState.ALIGNED_table, ref hufftbl, ref bitsleft, 
                                                ref bitbuf, ref ip, ref inpos, ref i, LZX_ALIGNED_TABLEBITS, 
                                                LZX_ALIGNED_MAXSYMBOLS, ref pState.ALIGNED_len, ref j, ref aligned_bits);
                                            if (!hr)
                                                return DECR_ILLEGALDATA;
                                            match_offset += (ULONG)aligned_bits;
                                        }
                                        else if (extra == 3) {
                                            /* aligned bits only */
                                            hr = READ_HUFFSYM(ref pState.ALIGNED_table, ref hufftbl, ref bitsleft, 
                                                ref bitbuf, ref ip, ref inpos, ref i, LZX_ALIGNED_TABLEBITS, 
                                                LZX_ALIGNED_MAXSYMBOLS, ref pState.ALIGNED_len, ref j, ref aligned_bits);
                                            if (!hr)
                                                return DECR_ILLEGALDATA;
                                            match_offset += (ULONG)aligned_bits;
                                        }
                                        else if (extra > 0) { /* extra==1, extra==2 */
                                            /* verbatim bits only */
                                            READ_BITS(ref verbatim_bits, extra, ref bitbuf, ref bitsleft, 
                                                ref ip, ref inpos);
                                            match_offset += verbatim_bits;
                                        }
                                        else /* extra == 0 */ {
                                            /* ??? */
                                            match_offset = 1;
                                        }

                                        /* update repeated offset LRU queue */
                                        R2 = R1; R1 = R0; R0 = match_offset;
                                    }
                                    else if (match_offset == 0) {
                                        match_offset = R0;
                                    }
                                    else if (match_offset == 1) {
                                        match_offset = R1;
                                        R1 = R0; R0 = match_offset;
                                    }
                                    else /* match_offset == 2 */ {
                                        match_offset = R2;
                                        R2 = R0; R0 = match_offset;
                                    }

                                    rundest = window_posn;
                                    runsrc  = rundest - match_offset;
                                    window_posn += (ULONG)match_length;
                                    if (window_posn > window_size) return DECR_ILLEGALDATA;
                                    this_run -= match_length;

                                    /* copy any wrapped around source data */
                                    while ((runsrc < 0) && (match_length-- > 0)) {
                                        window[rundest++] = window[runsrc + window_size]; runsrc++;
                                    }
                                    /* copy match data - no worries about destination wraps */
                                    while (match_length-- > 0) window[rundest++] = window[runsrc++];

                                }
                            }
                            break;

                        case LZX_BLOCKTYPE_UNCOMPRESSED:
                            if ((inpos + this_run) > endinp) return DECR_ILLEGALDATA;
                            Array.Copy(ip, inpos, window, window_posn, this_run);
                            inpos += this_run; window_posn += (ULONG)this_run;
                            break;

                        default:
                            return DECR_ILLEGALDATA; /* might as well */
                    }

                }
            }

            if (togo != 0) return DECR_ILLEGALDATA;
            Array.Copy(
                window, 
                (!Convert.ToBoolean(window_posn) ? window_size : window_posn) - outlen,
                op,
                (long)outpos,
                outlen);

            pState.window_posn = window_posn;
            pState.R0 = R0;
            pState.R1 = R1;
            pState.R2 = R2;

            /* intel E8 decoding */
            if ((pState.frames_read++ < 32768) && pState.intel_filesize != 0) {
                if (outlen <= 6 || !Convert.ToBoolean(pState.intel_started)) {
                    pState.intel_curpos += outlen;
                }
                else {
                    ulong data    = outpos;
                    ulong dataend = outpos + (ulong)outlen - 10;
                    LONG curpos    = pState.intel_curpos;
                    LONG filesize  = pState.intel_filesize;
                    LONG abs_off, rel_off;

                    pState.intel_curpos = curpos + outlen;

                    while (data < dataend) {
                        if (op[data++] != 0xE8) { curpos++; continue; }
                        abs_off = op[data + 0] | (op[data + 1]<<8) | (op[data + 2]<<16) | (op[data + 3]<<24);
                        if ((abs_off >= -curpos) && (abs_off < filesize)) {
                            rel_off = (abs_off >= 0) ? abs_off - curpos : abs_off + filesize;
                            op[data + 0] = (UBYTE) rel_off;
                            op[data + 1] = (UBYTE)(rel_off >> 8);
                            op[data + 2] = (UBYTE)(rel_off >> 16);
                            op[data + 3] = (UBYTE)(rel_off >> 24);
                        }
                        data += 4;
                        curpos += 5;
                    }
                }
            }
            return DECR_OK;
        }
    }
}
