using System;
using System.IO;
using System.Reflection;

namespace SharpDc.Hash
{
    public static class HashHelper
    {
        static IThexThreaded _hashCalculator;

        static HashHelper()
        {
            if (!TigerNative.SelfTest())
            {
                // try to setup path for dlls
                string codeBase = Assembly.GetExecutingAssembly().CodeBase;
                string path = Uri.UnescapeDataString(codeBase);
                var dllDirectory = Path.Combine(Path.GetDirectoryName(path), Environment.Is64BitProcess ? "x64" : "x86");
                Environment.SetEnvironmentVariable("PATH", Environment.GetEnvironmentVariable("PATH") + ";" + dllDirectory);
            }
            
            if (TigerNative.SelfTest())
                _hashCalculator = new ThexThreaded<TigerNative>();
            else
            {
                // use managed tth calculation
                _hashCalculator = new ThexThreaded<softwareunion.Tiger>();
            }
        }

        public static string GetTTH(string filePath)
        {
            byte[][][] tree;
            return GetTTH(filePath, out tree);
        }

        public static string GetTTH(string filePath, out byte[][][] tigerTree)
        {
            lock (_hashCalculator)
            {
                tigerTree = _hashCalculator.GetTTHTree(filePath);
                return Base32Encoding.ToString(tigerTree[tigerTree.GetLength(0) - 1][0]);
            }
        }
    }
}
