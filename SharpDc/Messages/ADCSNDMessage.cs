// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.IO;

namespace SharpDc.Messages
{
    public struct ADCSNDMessage : IStringMessage
    {
        public ADCGETType Type;

        /// <summary>
        /// File name or TTH starting with TTH/
        /// </summary>
        public string Request;

        public long Start;

        public long Length;

        public static ADCSNDMessage Parse(string raw)
        {
            var parts = raw.Split(' ');

            if (parts.Length < 4) throw new FormatException();

            ADCSNDMessage msg;

            msg.Type = ADCGETType.Unknown;
            if (parts[1] == "tthl") msg.Type = ADCGETType.Tthl;
            if (parts[1] == "file") msg.Type = ADCGETType.File;

            msg.Request = parts[2];

            long.TryParse(parts[3], out msg.Start);

            long.TryParse(parts[4], out msg.Length);

            return msg;            
        }

        //$ADCGET file TTH/CUHKZ6J3D2AGAJ6FAAS7YIPRYXNDJMZ7G3WC6II 0 1048576|
        //$ADCSND file files.xml.bz2 0 8775 ZL1|[Содержимое_файла]
        public string Raw
        {
            get
            {
                string typeStr;

                switch (Type)
                {
                    case ADCGETType.Unknown:
                        typeStr = "";
                        break;
                    case ADCGETType.File:
                        typeStr = "file";
                        break;
                    case ADCGETType.Tthl:
                        typeStr = "tthl";
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                return string.Format("$ADCSND {0} {1} {2} {3}", typeStr, Request, Start.ToString(), Length.ToString());
            }
        }
    }
}