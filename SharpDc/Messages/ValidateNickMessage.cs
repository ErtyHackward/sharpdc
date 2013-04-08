// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

namespace SharpDc.Messages
{
    public struct ValidateNickMessage : IStringMessage
    {
        public string Nick;

        public string Raw
        {
            get { return string.Format("$ValidateNick {0}", Nick); }
        }
    }
}