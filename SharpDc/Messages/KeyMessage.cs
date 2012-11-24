//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
namespace SharpDc.Messages
{
    public struct KeyMessage : IStringMessage
    {
        public string Raw
        {
            get { return string.Format("$Key {0}", Key); }
        }

        public string Key;

        public static KeyMessage Parse(string command)
        {
            KeyMessage msg;
            msg.Key = command.Substring(5);
            return msg;
        }
    }
}