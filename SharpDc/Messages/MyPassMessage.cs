//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
namespace SharpDc.Messages
{
    public struct MyPassMessage : IStringMessage
    {
        public string Password { get; set; }
        
        public string Raw
        {
            get { return string.Format("$MyPass {0}", Password); }
        }
    }
}
