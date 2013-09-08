// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;

namespace SharpDc.Messages
{
    public struct MyINFOMessage : IStringMessage
    {
        public string Nickname;
        public string Description;
        public string Tag;
        public string Connection;
        public byte Flag;
        public string Email;
        public long Share;

        public bool Equals(MyINFOMessage other)
        {
            return string.Equals(Nickname, other.Nickname) && string.Equals(Description, other.Description) && string.Equals(Tag, other.Tag) && string.Equals(Connection, other.Connection) && Flag == other.Flag && string.Equals(Email, other.Email) && Share == other.Share;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = ( Nickname != null ? Nickname.GetHashCode() : 0 );
                hashCode = ( hashCode * 397 ) ^ ( Description != null ? Description.GetHashCode() : 0 );
                hashCode = ( hashCode * 397 ) ^ ( Tag != null ? Tag.GetHashCode() : 0 );
                hashCode = ( hashCode * 397 ) ^ ( Connection != null ? Connection.GetHashCode() : 0 );
                hashCode = ( hashCode * 397 ) ^ Flag.GetHashCode();
                hashCode = ( hashCode * 397 ) ^ ( Email != null ? Email.GetHashCode() : 0 );
                hashCode = ( hashCode * 397 ) ^ Share.GetHashCode();
                return hashCode;
            }
        }
        
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is MyINFOMessage && Equals((MyINFOMessage)obj);
        }

        public static MyINFOMessage Parse(string raw)
        {
            //$MyINFO $ALL [Ник] [Описание][Тэг]$ $[Соедиенние][Флаг]$[E-Mail]$[Шара]$|
            //$MyINFO $ALL pas143 <OGODC 2.4.83.701,M:A,H:2/0/0,S:7,C:Ленинск-Кузнецкий>$ $$$4098067221$
            //$MyINFO $ALL мшефдшш $ $$$17583300011$
            MyINFOMessage myinfo;

            var spl = raw.Remove(0, 13).Split('$');

            var spaceIndex = spl[0].IndexOf(' ');
            if (spaceIndex == -1)
                throw new FormatException("Invalid MyINFO message format");
            myinfo.Nickname = spl[0].Substring(0, spaceIndex);

            if (spaceIndex + 1 < spl[0].Length)
            {
                // parse tag
                myinfo.Description = spl[0].Substring(spaceIndex + 1);

                var tagStart = spl[0].IndexOf('<', spaceIndex);

                if (tagStart != -1)
                {
                    myinfo.Tag = spl[0].Substring(tagStart);
                    myinfo.Description = spl[0].Substring(spaceIndex + 1, tagStart - (spaceIndex + 1));
                }
                else
                {
                    myinfo.Tag = null;
                }
            }
            else
            {
                myinfo.Description = null;
                myinfo.Tag = null;
            }

            myinfo.Connection = spl[2];
            if (string.IsNullOrEmpty(myinfo.Connection))
                myinfo.Flag = 0;
            else
                myinfo.Flag = (byte)myinfo.Connection.Substring(myinfo.Connection.Length - 1, 1)[0];

            myinfo.Email = spl[3];
            
            long.TryParse(spl[4], out myinfo.Share);

            return myinfo;
        }

        [Flags]
        public enum UserStatusFlag
        {
            Unknown = 0,
            Normal = 1,
            Away = 2,
            Server = 4,
            Fireball = 8,
            TLS = 16
        }

        public static UserStatusFlag ConvertByteToStatusFlag(byte value)
        {
            UserStatusFlag flag = UserStatusFlag.Unknown;
            if ((1 & value) == 1)
                flag |= UserStatusFlag.Normal;
            if ((2 & value) == 2)
                flag |= UserStatusFlag.Away;
            if ((4 & value) == 4)
                flag |= UserStatusFlag.Server;
            if ((8 & value) == 8)
                flag |= UserStatusFlag.Fireball;
            if ((16 & value) == 16)
                flag |= UserStatusFlag.TLS;
            return flag;
        }

        public static byte ConvertStatusFlagToByte(UserStatusFlag statusFlag)
        {
            byte statusFlagRawByte = 0;
            if ((UserStatusFlag.Normal & statusFlag) == UserStatusFlag.Normal)
                statusFlagRawByte |= 1;
            if ((UserStatusFlag.Away & statusFlag) == UserStatusFlag.Away)
                statusFlagRawByte |= 2;
            if ((UserStatusFlag.Server & statusFlag) == UserStatusFlag.Server)
                statusFlagRawByte |= 4;
            if ((UserStatusFlag.Fireball & statusFlag) == UserStatusFlag.Fireball)
                statusFlagRawByte |= 8;
            if ((UserStatusFlag.TLS & statusFlag) == UserStatusFlag.TLS)
                statusFlagRawByte |= 16;
            return statusFlagRawByte;
        }

        public string Raw
        {
            get
            {
                return string.Format("$MyINFO $ALL {0} {1}{2}$ ${3}{4}${5}${6}$", Nickname, Description, Tag, Connection,
                                     (char)Flag, Email, Share);
            }
        }
    }
}