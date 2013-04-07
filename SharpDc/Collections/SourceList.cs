//  -------------------------------------------------------------
//  LiveDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using SharpDc.Structs;

namespace SharpDc.Collections
{
    /// <summary>
    /// List of sources
    /// </summary>
    [Serializable]
    public class SourceList : ObservableList<Source>
    {
        public SourceList()
        {

        }

        public SourceList(IEnumerable<Source> copyFrom)
            : base(copyFrom)
        {

        }

        [XmlIgnore]
        public DownloadItem DownloadItem { get; set; }

        /// <summary>
        /// Adds source to list if not already have
        /// </summary>
        /// <param name="p"></param>
        public override void Add(Source p)
        {
            if (!Contains(p))
                base.Add(p);
        }
    }
}
