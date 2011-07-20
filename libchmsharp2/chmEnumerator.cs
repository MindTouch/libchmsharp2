using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CHMsharp
{
    public delegate EnumerateStatus chmEnumerator(
        chmFile f,
        ref chmUnitInfo ui,
        ref object context);
}
