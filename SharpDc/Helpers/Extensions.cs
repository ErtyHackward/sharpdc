// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2014
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace SharpDc.Helpers
{
    public static class Extensions
    {
        public static int FirstFalse(this BitArray array, int startIndex = 0, int length = -1)
        {
            if (length == -1)
                length = array.Count - startIndex;

            for (var i = startIndex; i < startIndex + length; i++)
            {
                if (!array[i])
                    return i;
            }
            return -1;
        }

        public static int FirstTrue(this BitArray array, int startIndex = 0, int length = -1)
        {
            if (length == -1)
                length = array.Count - startIndex;

            for (var i = startIndex; i < startIndex + length; i++)
            {
                if (array[i])
                    return i;
            }
            return -1;
        }

        public static int TrueCount(this BitArray array)
        {
            return array.Cast<bool>().Count(v => v);
        }

        public static int FalseCount(this BitArray array)
        {
            return array.Count - array.TrueCount();
        }

        public static byte[] ToBytes(this BitArray array)
        {
            var bytes = new byte[array.Length / 8 + (array.Length % 8 == 0 ? 0 : 1)];
            array.CopyTo(bytes,0);
            return bytes;
        }
    }
}
