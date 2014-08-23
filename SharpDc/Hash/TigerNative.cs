// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2014
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using SharpDc.Helpers;

namespace SharpDc.Hash
{
    public class TigerNative : HashAlgorithm
    {
        static TigerNative()
        {
            DllLoadHelper.LoadUmnanagedLibrary("Tiger.dll");
        }

        [DllImport("Tiger.dll")]
        extern static private void tiger(byte[] b, long lenght, long[] sb);

        private byte[] ComputeHash(string sinput)
        {
            byte[] input = Encoding.ASCII.GetBytes(sinput);
            return ComputeHash(input);
        }

        private static byte[] ComputeTigerHash(byte[] input)
        {
            byte[] result = new byte[24];
            long[] res = new long[3];
            tiger(input, input.Length, res);
            byte[] b = BitConverter.GetBytes(res[0]);
            Array.Copy(b, result, 8);
            b = BitConverter.GetBytes(res[1]);
            Array.Copy(b, 0, result, 8, 8);
            b = BitConverter.GetBytes(res[2]);
            Array.Copy(b, 0, result, 16, 8);
            return result;
        }

        public static bool SelfTest()
        {
            try
            {
                var input = new byte[0];
                ComputeTigerHash(input);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private byte[] _buffer;

        /// <summary>
        /// When overridden in a derived class, routes data written to the object into the hash algorithm for computing the hash.
        /// </summary>
        /// <param name="array">The input to compute the hash code for. </param><param name="ibStart">The offset into the byte array from which to begin using data. </param><param name="cbSize">The number of bytes in the byte array to use as data. </param>
        protected override void HashCore(byte[] array, int ibStart, int cbSize)
        {
            if (_buffer == null)
            {
                if (ibStart == 0 && cbSize == array.Length)
                {
                    _buffer = array;
                    return;
                }

                _buffer = new byte[0];
            }
            Buffer.BlockCopy(array, ibStart, _buffer, _buffer.Length, cbSize);
        }

        protected override byte[] HashFinal()
        {
            return ComputeTigerHash(_buffer);
        }

        public override void Initialize()
        {
            _buffer = null;
        }
    }
}