// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

namespace SharpDc.Messages
{
    public struct HelloMessage : IStringMessage
    {
        public string Nickname;

        public static HelloMessage Parse(string raw)
        {
            //$Hello Erty_Hackward
            HelloMessage hm;

            hm.Nickname = raw.Substring(7);

            return hm;
        }

        public string Raw
        {
            get { return string.Format("$Hello {0}", Nickname); }
        }
    }
}