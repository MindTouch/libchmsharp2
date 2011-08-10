using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace CHMsharp
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SystemFileEntry
    {
        public UInt16 code;      /* 0x00 WORD code */
        public UInt16 length;    /* 0x02 WORD length */
        public byte[] data;      /* 0x04 BYTE data */
    }

    public enum Compatibility
    {
        Version_Unknown = 0,
        Version_1_0 = 2,
        Version_1_1 = 3
    }

    public sealed class SystemFile
    {
        public const UInt16 CODE_CONTENTS_FILE = 0;
        public const UInt16 CODE_INDEX_FILE = 1;
        public const UInt16 CODE_DEFAULT_TOPIC = 2;
        public const UInt16 CODE_TITLE = 3;
        public const UInt16 CODE_OPTIONS_BITFIELD = 4;
        public const UInt16 CODE_DEFAULT_WINDOW = 5;
        public const UInt16 CODE_COMPILED_FILE = 6;
        public const UInt16 CODE_UKNOWN_BINARY_INDEX = 7;
        public const UInt16 CODE_UKNOWN_STRINGS_PTR = 8;
        public const UInt16 CODE_GENERATOR_VERSION = 9;
        public const UInt16 CODE_TIMESTAMP = 10;
        public const UInt16 CODE_UNKNOWN_BINARY_TOC = 11;
        public const UInt16 CODE_NUMBER_OF_INFO_TYPES = 12;
        public const UInt16 CODE_UKNOWN_IDX_HEADER = 13;
        public const UInt16 CODE_UKNOWN_MSO_EXTENSIONS = 14;
        public const UInt16 CODE_INFO_TYPE_CHECKSUM = 15;
        public const UInt16 CODE_DEFAULT_FONT = 16;

        private Compatibility _compat;
        private SystemFileEntry[] _entries;

        private SystemFile(int compat, SystemFileEntry[] entries)
        {
            _compat = Enum.IsDefined(typeof(Compatibility), compat) ?
                (Compatibility)compat :
                Compatibility.Version_Unknown;
            _entries = entries;
        }

        public static SystemFile Read(chmFile f)
        {
            List<SystemFileEntry> sfelst = new List<SystemFileEntry>();
            chmUnitInfo ui = new chmUnitInfo();
            byte[] buf;
            uint pos = 0, remaining = 0;

            if (!f.ResolveObject("/#SYSTEM", ref ui))
                throw new InvalidOperationException("Could not find SYSTEM file in CHM!");

            buf = new byte[ui.length];
            remaining = (uint)buf.Length;

            if (f.RetrieveObject(ui, ref buf, 0, (long)ui.length) == 0)
                throw new InvalidOperationException("Could not read SYSTEM file in CHM!");

            Int32 version = 0;
            Unmarshal.ToInt32(ref buf, ref pos, ref remaining, ref version);

            for ( ; pos < buf.Length; ) {
                SystemFileEntry sfe = new SystemFileEntry();

                Unmarshal.ToUInt16(ref buf, ref pos, ref remaining, ref sfe.code);
                Unmarshal.ToUInt16(ref buf, ref pos, ref remaining, ref sfe.length);

                sfe.data = new byte[sfe.length];
                Array.Copy(buf, pos, sfe.data, 0, sfe.length);

                pos += sfe.length;
                remaining -= sfe.length;

                sfelst.Add(sfe);
            }

            return new SystemFile(version, sfelst.ToArray());
        }

        public SystemFileEntry[] Entries
        {
            get { return _entries; }
        }

        public Compatibility Version
        {
            get { return _compat; }
        }
    }
}
