// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using SharpDc.Structs;

namespace SharpDc.Events
{
    public class SourceEventArgs : EventArgs
    {
        public Source Source { get; set; }
        public DownloadItem DownloadItem { get; set; }
    }
}