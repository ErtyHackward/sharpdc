using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

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

        public static bool VerifyLeaves(byte[] correctHash, byte[][] hashBlock) //compresses hash blocks to hash.
        {
            if (hashBlock.Length == 0)
                return false;

            return ThexHelper.HashesEquals(correctHash, ThexHelper.CompressHashBlock(_hashCalculator.Hasher, hashBlock));
        }

        public static bool VerifySegment(byte[] correctHash, string filePath, long start, int length)
        {
            return ThexHelper.VerifySegment(_hashCalculator.Hasher, correctHash, filePath, start, length);
        }

        public static long GetBytePerHash(long hashesCount, long fileSize)
        {
            long chunks = (fileSize / 1024) + (fileSize % 1024 > 0 ? 1 : 0);
            long bph = 1024;

            while (chunks > hashesCount)
            {
                chunks = chunks / 2 + (chunks % 2 > 0 ? 1 : 0);
                bph *= 2;
            }
            return bph;
        }
    }
}
