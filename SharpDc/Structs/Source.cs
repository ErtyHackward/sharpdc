// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace SharpDc.Structs
{
    public struct Source : IComparer<Source>, IComparable<Source>
    {
        public string UserNickname;
        public string HubAddress;

        public int Compare(Source x, Source y)
        {
            var cmp1 = string.Compare(x.UserNickname, y.UserNickname);
            return cmp1 == 0 ? string.Compare(x.HubAddress, y.HubAddress) : cmp1;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ( ( UserNickname != null ? UserNickname.GetHashCode() : 0 ) * 397 ) ^ ( HubAddress != null ? HubAddress.GetHashCode() : 0 );
            }
        }

        public int CompareTo(Source other)
        {
            return Compare(this, other);
        }

        public override string ToString()
        {
            return string.Format("Source [{0},{1}]", UserNickname, HubAddress);
        }

        public static bool operator ==(Source one, Source two)
        {
            var cmp1 = string.Compare(one.UserNickname, two.UserNickname);
            return (cmp1 == 0 ? string.Compare(one.HubAddress, two.HubAddress) : cmp1) == 0;
        }

        public static bool operator !=(Source one, Source two)
        {
            return !(one == two);
        }

        public bool Equals(Source other)
        {
            return string.Equals(UserNickname, other.UserNickname) && string.Equals(HubAddress, other.HubAddress);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is Source && Equals((Source)obj);
        }
    }
}