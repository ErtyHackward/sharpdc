//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
namespace SharpDc.Messages
{
    public struct MyNickMessage : IStringMessage
    {
        public string Nickname;

        public static MyNickMessage Parse(string raw)
        {
            MyNickMessage mn;
            mn.Nickname = raw.Substring(8);
            return mn;
        }
        
        public string Raw
        {
            get { return string.Format("$MyNick {0}", Nickname); }
        }
    }
}
