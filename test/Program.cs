using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using CHMsharp;

namespace test
{
    class Program
    {
        static string _outdir;

        static void Main(string[] args)
        {
            if (args.Length != 2) {
                Console.WriteLine("USAGE: test.exe <input file> <output directory>");
                return;
            }

            _outdir = args[1];
            Object o = new Object();

            chmFile chmf = chmFile.Open(args[0]);
            chmf.Enumerate(
                EnumerateLevel.All,
                new chmEnumerator(EnumeratorCallback),
                ref o);
            chmf.Close();
        }

        static EnumerateStatus EnumeratorCallback(chmFile file, ref chmUnitInfo ui, ref Object context)
        {
            if (!ui.path.EndsWith("/"))
                Console.WriteLine(file.FileName + ": " + ui.path);

            if (ui.length > 0) {
                byte[] buf = new byte[ui.length];
                long ret = file.RetrieveObject(ui, ref buf, 0, buf.Length);

                if (ret > 0) {
                    try {
                        FileInfo fi =
                            new FileInfo(Path.Combine(_outdir, ui.path.Trim('/')));
                            
                        List<DirectoryInfo> created;

                        fi.Directory.CreateDirectory(out created);
                        File.WriteAllBytes(fi.FullName, buf);
                    } catch (ArgumentException ex) {
                        Console.WriteLine(ex.Message);
                    }
                }
            }

            return EnumerateStatus.Continue;
        }
    }
}
