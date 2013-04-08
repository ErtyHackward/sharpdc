// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using SharpDc.Messages;

namespace SharpDc.Structs
{
    public struct UserInfo
    {
        private string _nickname;
        private string _email;
        private long _share;
        private string _ip;

        public string Nickname
        {
            get { return _nickname; }
            set { _nickname = value; }
        }

        public string Email
        {
            get { return _email; }
            set { _email = value; }
        }

        public long Share
        {
            get { return _share; }
            set { _share = value; }
        }

        public string IP
        {
            get { return _ip; }
            set { _ip = value; }
        }

        public UserInfo(MyINFOMessage msg)
        {
            _nickname = msg.Nickname;
            _email = msg.Email;
            _share = msg.Share;
            _ip = "";
        }
    }
}