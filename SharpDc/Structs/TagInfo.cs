// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System.Reflection;

namespace SharpDc.Structs
{
    /// <summary>
    /// Represents public information of this client
    /// </summary>
    public class TagInfo
    {
        /// <summary>
        /// Optional city name of the client
        /// </summary>
        public string City { get; set; }

        /// <summary>
        /// Client version
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Speed in megabits: 0.5, 10, 20, 100
        /// </summary>
        public string Connection { get; set; }

        public byte Flag { get; set; }

        public TagInfo()
        {
            Version = "SharpDC " + Assembly.GetExecutingAssembly().GetName().Version;
            Connection = "0";
            Flag = 0x01;
        }
    }
}