// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Text;

namespace SharpDc.Messages
{
    public struct LockMessage : IStringMessage
    {
        public bool ExtendedProtocol;

        private string _raw;

        public string Raw
        {
            get { return "$Lock EXTENDEDPROTOCOLABCABCABCABCABCABC Pk=SHARPDC"; }
        }

        public static LockMessage Parse(string raw)
        {
            LockMessage lm;
            lm._raw = raw;
            lm.ExtendedProtocol = raw.Contains("EXTENDEDPROTOCOL");
            return lm;
        }

        public KeyMessage CreateKey()
        {
            KeyMessage km;

            Encoding encoding;

            try
            {
                encoding = Encoding.GetEncoding(1251);
            }
            catch (Exception)
            {
                encoding = Encoding.ASCII;
            }

            var lck = _raw.Replace("$Lock ", "");
            var iPos = lck.IndexOf(" Pk=", 1);
            if (iPos > 0) lck = lck.Substring(0, iPos);

            var arrChar = new char[lck.Length];
            var arrRet = new int[lck.Length];
            arrChar[0] = lck[0];
            for (var i = 1; i < lck.Length; i++)
            {
                //arrChar[i] = lck[i];
                byte[] test = encoding.GetBytes(new[] { lck[i] });
                arrChar[i] = (char)test[0];
                arrRet[i] = arrChar[i] ^ arrChar[i - 1];
            }
            arrRet[0] = arrChar[0] ^ arrChar[lck.Length - 1] ^ arrChar[lck.Length - 2] ^ 5;
            var sKey = "";
            for (var n = 0; n < lck.Length; n++)
            {
                arrRet[n] = ((arrRet[n] * 16 & 240)) | ((arrRet[n] / 16) & 15);
                int j = arrRet[n];
                switch (j)
                {
                    case 0:
                    case 5:
                    case 36:
                    case 96:
                    case 124:
                    case 126:
                        sKey += string.Format("/%DCN{0:000}%/", j);
                        break;
                    default:
                        sKey += encoding.GetChars(new[] { Convert.ToByte((char)j) })[0];
                        break;
                }
            }

            km.Key = sKey;

            return km;
        }
    }
}