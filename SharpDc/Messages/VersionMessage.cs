// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

namespace SharpDc.Messages
{
    public struct VersionMessage : IStringMessage
    {
        public string Raw
        {
            get { return "$Version 1,0091"; }
        }
    }
}