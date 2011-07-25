using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CHMsharp
{
    public enum EnumerateLevel
    {
        Normal = 1,
        Meta = 2,
        Special = 4,
        Files = 8,
        Directories = 16,
        All = 31
    }
}
