// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
// licensed under the LGPL
// -------------------------------------------------------------

namespace SharpDc.Messages
{
    public struct QuitMessage : IStringMessage
    {
        public string Nickname;

        public string Raw
        {
            get { return string.Format("$Quit {0}", Nickname); }
        }

        public static QuitMessage Parse(string command)
        {
            QuitMessage msg;

            msg.Nickname = command.Substring(6);

            return msg;
        }
    }
}