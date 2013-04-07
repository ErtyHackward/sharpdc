//  -------------------------------------------------------------
//  LiveDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
//  licensed under the LGPL
//  -------------------------------------------------------------
namespace SharpDc.Messages
{
    public struct HubNameMessage : IStringMessage
    {
        public string HubName;
        
        public string Raw
        {
            get { return string.Format("$HubName {0}",HubName); }
        }

        public static HubNameMessage Parse(string raw)
        {
            //$HubName MegaHub
            HubNameMessage hnm;
            
            hnm.HubName = raw.Substring(8);

            return hnm;
        }
    }
}
