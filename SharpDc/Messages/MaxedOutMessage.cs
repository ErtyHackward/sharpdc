//  -------------------------------------------------------------
//  LiveDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
//  licensed under the LGPL
//  -------------------------------------------------------------
namespace SharpDc.Messages
{
    public struct MaxedOutMessage : IStringMessage
    {
        public string Raw
        {
            get { return "$MaxedOut"; }
        }
    }
}
