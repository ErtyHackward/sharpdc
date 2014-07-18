using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpDc;
using SharpDc.Hash;
using Tiger = softwareunion.Tiger;

namespace Tth
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args == null || args.Length < 1)
            {
                Console.WriteLine("Specify the file to hash");
                return;
            }
            
            var hasher = new ThexThreaded<Tiger>();

            var sw = Stopwatch.StartNew();
            var hash = Base32Encoding.ToString(hasher.GetTTHRoot(args[0]));
            sw.Stop();

            FileInfo fi = new FileInfo(args[0]);

            Console.WriteLine("{0} {2}ms {1}/s ", hash, Utils.FormatBytes(fi.Length/sw.Elapsed.TotalSeconds), sw.ElapsedMilliseconds);
            if (Debugger.IsAttached)
                Console.ReadLine();
        }
    }
}
