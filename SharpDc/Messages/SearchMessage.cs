// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;

namespace SharpDc.Messages
{
    //$Search 10.10.10.10:412 T?T?500000?1?Gentoo$2005
    //$Search Hub:Вася T?T?500000?1?Gentoo$2005
    //$Search 10.10.10.10:3746 F?T?0?9?TTH:TO32WPD6AQE7VA7654HEAM5GKFQGIL7F2BEKFNA
    //$Search Hub:Пётр F?T?0?9?TTH:TO32WPD6AQE7VA7654HEAM5GKFQGIL7F2BEKFNA
    //$Search 10.102.106.69:13440 F?T?0?1?порно|

    public struct SearchMessage : IStringMessage
    {
        public string SearchAddress;
        public bool SizeRestricted;
        public bool MaximumSize;
        public long Size;
        public SearchType SearchType;
        public string SearchRequest;

        public string Raw
        {
            get
            {
                string size = "F?T?0";
                if (SizeRestricted)
                {
                    if (MaximumSize)
                        size = "T?T?" + Size;
                    else size = "T?F?" + Size;
                }

                var search = (SearchType == SearchType.TTH ? "TTH:" : "") + SearchRequest;

                search = search.Replace(' ', '$');

                return string.Format("$Search {0} {1}?{2}?{3}", SearchAddress, size, (int)SearchType, search);
            }
        }

        public static SearchMessage Parse(string cmd)
        {
            //$Search 10.10.10.10:3746 F?T?0?9?TTH:TO32WPD6AQE7VA7654HEAM5GKFQGIL7F2BEKFNA

            SearchMessage msg;

            var parts = cmd.Split(' ');
            if (parts.Length != 3)
                throw new FormatException("Invalid Search message: " + cmd);

            msg.SearchAddress = parts[1];

            parts = parts[2].Split('?');
            if (parts.Length != 5)
                throw new FormatException("Invalid Search message: " + cmd);

            msg.SizeRestricted = parts[0] == "T";
            msg.MaximumSize = parts[1] == "T";

            msg.Size = long.Parse(parts[2]);

            msg.SearchType = (SearchType)int.Parse(parts[3]);

            msg.SearchRequest = parts[4].Replace('$', ' ');

            if (msg.SearchRequest.StartsWith("TTH:"))
                msg.SearchRequest = msg.SearchRequest.Remove(0, 4);

            return msg;
        }
    }

    public enum SearchType
    {
        Any = 1,
        Audio = 2,
        Archive = 3,
        Document = 4,
        Executable = 5,
        Picture = 6,
        Video = 7,
        Folder = 8,
        TTH = 9,
        Image = 10
    }

    public struct ConnectToMeMessage : IStringMessage
    {
        /// <summary>
        /// Recepient nickname
        /// </summary>
        public string RecipientNickname;

        /// <summary>
        /// Sender address
        /// </summary>
        public string SenderAddress;

        //$ConnectToMe [Ник_получателя] [IP_отправителя]:[Порт_отправителя]|

        public static ConnectToMeMessage Parse(string raw)
        {
            ConnectToMeMessage msg;

            var split = raw.Split(' ');

            msg.RecipientNickname = split[1];
            msg.SenderAddress = split[2];

            return msg;
        }

        public string Raw
        {
            get { return string.Format("$ConnectToMe {0} {1}", RecipientNickname, SenderAddress); }
        }
    }

    public struct RevConnectToMeMessage : IStringMessage
    {
        //$RevConnectToMe [SenderNick] [TargetNick]|

        public string SenderNickname;
        public string TargetNickname;

        public static RevConnectToMeMessage Parse(string raw)
        {
            RevConnectToMeMessage msg;

            var split = raw.Split(' ');

            msg.SenderNickname = split[1];
            msg.TargetNickname = split[2];

            return msg;
        }

        public string Raw
        {
            get { return string.Format("$RevConnectToMe {0} {1}", SenderNickname, TargetNickname); }
        }
    }

    public struct SRMessage : IStringMessage
    {
        //$SR [Ник_ответчика] [Результат][0x05][Свободные_слоты]/[Всего_слотов][0x05][Имя_хаба] ([IP_хаба:Порт]){[0x05][Целевой_ник]}|
        //$SR Вася Файл.txt[0x05]437 3/4[0x05]МойХаб (10.10.10.10:411)[0x05]Петя|
        //$SR Вася Файл.txt[0x05]437 3/4[0x05]МойХаб (10.10.10.10:411)|
        //$SR m-s-w Download\Surviving.Sid.2008.HDTV.mkv577849188 7/7TTH:SB2IRWPEAKDFZKZB3YXXEWJHBJK4EGRGJKRKYUQ (212.75.211.131:411)|

        public string Nickname;
        public string FileName;
        public long FileSize;
        public int FreeSlots;
        public int TotalSlots;
        public string HubName;
        public string HubAddress;
        public string TargetNickname;

        public static SRMessage Parse(string raw)
        {
            int x5Count = 0; // 1 - folder, 2 - file

            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == '\x05')
                    x5Count++;
            }

            bool file = x5Count == 2;

            var parts = raw.Split('\x05');

            var nickEnd = raw.IndexOf(' ', 4);

            SRMessage msg;

            msg.Nickname = parts[0].Substring(4, nickEnd - 4);

            msg.FileName = parts[0].Substring(nickEnd + 1);

            var partIndex = file ? 1 : 0;
            if (file)
            {
                var sizeEnd = parts[1].IndexOf(' ');
                var sizeString = parts[1].Substring(0, sizeEnd);
                long.TryParse(sizeString, out msg.FileSize);
            }
            else msg.FileSize = -1;

            var slotString = parts[partIndex].Substring(parts[partIndex].IndexOf(' ') + 1);

            var slots = slotString.Split('/');

            int.TryParse(slots[0], out msg.FreeSlots);
            int.TryParse(slots[1], out msg.TotalSlots);

            partIndex++;

            var hubString = parts[partIndex];

            msg.HubName = hubString.Substring(4, hubString.IndexOf(' ') - 4);

            var openIndex = hubString.IndexOf('(');
            var closeIndex = hubString.IndexOf(')');

            hubString = hubString.Substring(openIndex + 1, closeIndex - openIndex - 1);

            msg.HubAddress = hubString;
            msg.TargetNickname = null;

            return msg;
        }

        public string Raw
        {
            get
            {
                return string.Format("$SR {0} {1} {2}/{3}\x0005{4} ({5}){6}", Nickname,
                                     FileName + (FileSize > 0 ? "\x0005" + FileSize : ""), FreeSlots, TotalSlots,
                                     HubName, HubAddress,
                                     string.IsNullOrEmpty(TargetNickname) ? "" : "\x0005" + TargetNickname);
            }
        }
    }
}