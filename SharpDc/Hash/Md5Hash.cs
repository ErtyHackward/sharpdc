using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SharpDc.Hash
{
    /// <summary>
    /// Represents a 16 byte md5 hash
    /// </summary>
    public class Md5Hash : IEquatable<Md5Hash>, IComparable<Md5Hash>
    {
        private static readonly Md5Hash _empty = new Md5Hash(new byte[16]);

        private byte[] _bytes;

        /// <summary>
        /// Gets hash bytes
        /// </summary>
        public byte[] Bytes
        {
            get { return _bytes; }
            set { _bytes = value; }
        }

        /// <summary>
        /// Gets an empty hash
        /// </summary>
        public static Md5Hash Empty
        {
            get { return _empty; }
        }

        public Md5Hash()
        {
            _bytes = new byte[16];
        }

        /// <summary>
        /// Creates new instance of Md5Hash
        /// </summary>
        /// <param name="bytes"></param>
        public Md5Hash(byte[] bytes)
        {
            if(bytes == null || bytes.Length != 16)
                throw new ArgumentException("Md5 hash must be constructed from 16 bytes array");

            _bytes = bytes;
        }

        public override bool Equals(object obj)
        {
            if (obj == null || obj.GetType() != GetType()) return false;

            return Equals((Md5Hash)obj);
        }

        public bool Equals(Md5Hash other)
        {
            if (other == null) return false;
            for (int i = 0; i < 16; i++)
            {
                if (_bytes[i] != other._bytes[i]) return false;
            }
            return true;
        }

        public static bool operator ==(Md5Hash one, Md5Hash two)
        {
            if (ReferenceEquals(one, two)) return true;
            if (ReferenceEquals(one, null)) return false;
            if (ReferenceEquals(two, null)) return false;
            
            return one.Equals(two);
        }

        public static bool operator !=(Md5Hash one, Md5Hash two)
        {
            return !(one == two);
        }

        public static Md5Hash operator ^(Md5Hash one, Md5Hash two)
        {
            var hash = new Md5Hash();

            for (int i = 0; i < 16; i++)
            {
                hash._bytes[i] = (byte)( one._bytes[i] ^ two._bytes[i]);
            }

            return hash;
        }

        public static bool operator >(Md5Hash one, Md5Hash two)
        {
            for (int i = 0; i < 16; i++)
            {
                if (one._bytes[i] > two._bytes[i])
                    return true;
                if (one._bytes[i] < two._bytes[i])
                    return false;
            }
            return false;
        }

        public static bool operator <(Md5Hash one, Md5Hash two)
        {
            for (int i = 0; i < 16; i++)
            {
                if (one._bytes[i] > two._bytes[i])
                    return false;
                if (one._bytes[i] < two._bytes[i])
                    return true;
            }
            return false;
        }

        public override int GetHashCode()
        {
            int hash = 0;
            for (int i = 0; i < 16; i++)
            {
                hash += (_bytes[i] << i*2);
            }
            return hash;
        }

        public static Md5Hash Calculate(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);

            using (var provider = new MD5CryptoServiceProvider())
            {
                return new Md5Hash(provider.ComputeHash(bytes));
            }
        }

        public static Md5Hash Calculate(byte[] bytes)
        {
            using (var provider = new MD5CryptoServiceProvider())
            {
                return new Md5Hash(provider.ComputeHash(bytes));
            }
        }

        public static Md5Hash Calculate(Stream ms)
        {
            using (var provider = new MD5CryptoServiceProvider())
            {
                return new Md5Hash(provider.ComputeHash(ms));
            }
        }

        public int CompareTo(Md5Hash other)
        {
            return this > other ? 1 : this < other ? -1 : 0;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _bytes.Length; i++)
            {
                sb.Append(_bytes[i].ToString("x2"));
            }
            return sb.ToString();
        }
    }
}
