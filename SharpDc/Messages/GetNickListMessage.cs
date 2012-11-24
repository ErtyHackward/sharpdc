//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
namespace SharpDc.Messages
{
    public struct GetNickListMessage : IStringMessage
    {
        public string Raw
        {
            get { return "$GetNickList"; }
        }
    }
}
