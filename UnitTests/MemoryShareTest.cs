using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SharpDc.Managers;
using SharpDc.Structs;

namespace UnitTests
{
    [TestClass]
    public class MemoryShareTest
    {
        [TestMethod]
        public void TestMethod1()
        {
            var share = new MemoryShare();

            var rand = new Random(1);

            var search = new List<string>();

            for (int i = 0; i < 20000; i++)
            {
                var fileName = "C:\\temp\\" + Path.GetRandomFileName();
                share.AddFile(new ContentItem { 
                    SystemPath = fileName, 
                    Magnet = new Magnet { TTH = GetRandomTTH(rand) } 
                });
                if (rand.NextDouble() < 0.01)
                    search.Add(fileName);
            }

            share.SearchByName(search[0]);

            var sw = Stopwatch.StartNew();

            foreach (var item in search)
            {
                Assert.IsTrue(share.SearchByName(item).Count == 1);
            }

            sw.Stop();

            Trace.WriteLine("Search time " + sw.Elapsed.TotalMilliseconds / search.Count + " ms Items: " + search.Count);

        }

        private string GetRandomTTH(Random r)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 39; i++)
            {
                sb.Append((char)('a' + r.Next(0, 26)));    
            }
            return sb.ToString().ToUpper();
        }
    }
}
