using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpDc.Hash
{
    public static class HashHelper
    {
        static IThexThreaded _hashCalculator;

        static HashHelper()
        {
            if (TigerNative.SelfTest())
                _hashCalculator = new ThexThreaded<TigerNative>();
            else
                _hashCalculator = new ThexThreaded<softwareunion.Tiger>();
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
