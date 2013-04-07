//  -------------------------------------------------------------
//  LiveDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
//  licensed under the LGPL
//  -------------------------------------------------------------
namespace SharpDc.Messages
{
    public struct DirectionMessage : IStringMessage
    {
        //$Direction Upload 3875

        public bool Download;
        public int Number;

        public static DirectionMessage Parse(string raw)
        {
            DirectionMessage msg;

            var lastSpace = raw.LastIndexOf(' ');
            var number = raw.Substring(lastSpace + 1);

            msg.Download = raw.Contains("Do");
            int.TryParse(number, out msg.Number);

            return msg;
        }


        public string Raw
        {
            get { return string.Format("$Direction {0} {1}", Download ? "Download" : "Upload", Number); }
        }
    }
}
