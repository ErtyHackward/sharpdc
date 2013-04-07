//  -------------------------------------------------------------
//  LiveDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
//  licensed under the LGPL
//  -------------------------------------------------------------
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


        public static MyINFOMessage Parse(string raw)
        {
            //$MyINFO $ALL [Ник] [Описание][Тэг]$ $[Соедиенние][Флаг]$[E-Mail]$[Шара]$|
            //$MyINFO $ALL pas143 <OGODC 2.4.83.701,M:A,H:2/0/0,S:7,C:Ленинск-Кузнецкий>$ $$$4098067221$
            MyINFOMessage myinfo;

            var nickEnd = raw.IndexOf(' ', 13);
            var tagStart = raw.IndexOf('<', nickEnd);
            var tagEnd = raw.IndexOf('>', tagStart);
            var conStart = tagEnd + 4;
            var conEnd = raw.IndexOf('$', conStart);
            var emailStart = conEnd +1;
            var emailEnd = raw.IndexOf('$', emailStart);
            var shareStart = emailEnd+1;
            var shareEnd = raw.IndexOf('$', shareStart);

            myinfo.Nickname = raw.Substring(13, nickEnd - 13);
            myinfo.Description = raw.Substring(nickEnd + 1, tagStart - nickEnd -1);
            myinfo.Tag = raw.Substring(tagStart, tagEnd - tagStart + 1);
            myinfo.Connection = raw.Substring(conStart, conEnd-1- conStart);
            if(string.IsNullOrEmpty(myinfo.Connection))
                myinfo.Flag = 0;
            else 
                myinfo.Flag = (byte)raw.Substring(conEnd - 1, 1)[0];
            myinfo.Email = raw.Substring(emailStart, emailEnd-emailStart);
            var share = raw.Substring(shareStart, shareEnd - shareStart);
            long.TryParse(share, out myinfo.Share);
            
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
            get { return string.Format("$MyINFO $ALL {0} {1}{2}$ ${3}{4}${5}${6}$", Nickname, Description, Tag, Connection, (char)Flag, Email, Share ); }
        }
    }
}
