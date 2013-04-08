// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System.Collections.Generic;

namespace SharpDc.Messages
{
    public struct SupportsMessage : IStringMessage
    {
        //$Supports BZList XmlBZList ADCGet TTHF TTHL|

        public const string sBZList = "BZList";
        public const string sXmlBZList = "XmlBZList";
        public const string sADCGet = "ADCGet";
        public const string sTTHF = "TTHF";
        public const string sTTHL = "TTHL";
        public const string sNoGetINFO = "NoGetINFO";
        public const string sNoHello = "NoHello";
        public const string sUserIP2 = "UserIP2";

        public bool BZList;
        public bool XmlBZList;
        public bool ADCGet;
        public bool TTHF;
        public bool TTHL;
        public bool NoGetINFO;
        public bool NoHello;
        public bool UserIP2;

        public static SupportsMessage Parse(string raw)
        {
            SupportsMessage sm;

            sm.BZList = raw.Contains(sBZList);
            sm.XmlBZList = raw.Contains(sXmlBZList);
            sm.ADCGet = raw.Contains(sADCGet);
            sm.TTHF = raw.Contains(sTTHF);
            sm.TTHL = raw.Contains(sTTHL);
            sm.NoGetINFO = raw.Contains(sNoGetINFO);
            sm.NoHello = raw.Contains(sNoHello);
            sm.UserIP2 = raw.Contains(sUserIP2);

            return sm;
        }

        public string Raw
        {
            get
            {
                var list = new List<string>();

                if (BZList) list.Add(sBZList);
                if (XmlBZList) list.Add(sXmlBZList);
                if (ADCGet) list.Add(sADCGet);
                if (TTHF) list.Add(sTTHF);
                if (TTHL) list.Add(sTTHL);
                if (NoGetINFO) list.Add(sNoGetINFO);
                if (NoHello) list.Add(sNoHello);
                if (UserIP2) list.Add(sUserIP2);

                return string.Format("$Supports {0}", string.Join(" ", list.ToArray()));
            }
        }
    }
}