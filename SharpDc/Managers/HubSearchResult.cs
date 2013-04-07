//  -------------------------------------------------------------
//  LiveDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
//  licensed under the LGPL
//  -------------------------------------------------------------
using System.Collections.Generic;
using System.Net;
using SharpDc.Interfaces;
using SharpDc.Structs;

namespace SharpDc.Managers
{
    public class HubSearchResult : ISearchResult
    {
        public string Name { get { return Magnet.FileName; } }
        public long Size { get { return Magnet.Size; } }
        public List<string> VirtualDirs { get; set; }
        public bool IsFolder { get { return string.IsNullOrEmpty(Magnet.TTH) || Magnet.Size == -1; } }
        public Magnet Magnet { get; set; }
        public List<IPEndPoint> UserAddress { get; set; }

        /// <summary>
        /// Source list
        /// </summary>
        public List<Source> Sources { get; set; }

        /// <summary>
        /// Creates new object
        /// </summary>
        /// <param name="magnet">Magnet for result</param>
        /// <param name="src"></param>
        /// <param name="virt"></param>
        public HubSearchResult(Magnet magnet, Source src, string virt)
        {
            Magnet = magnet;
            VirtualDirs = new List<string> { virt };
            Sources = new List<Source> { src };
        }

        #region ICloneable Members

        public object Clone()
        {
            var hsr = (HubSearchResult)MemberwiseClone();
            hsr.VirtualDirs = new List<string>(VirtualDirs);
            hsr.Sources = new List<Source>(Sources);
            return hsr;
        }

        #endregion
    }
}