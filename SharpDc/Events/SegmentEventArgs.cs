//  -------------------------------------------------------------
//  LiveDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using SharpDc.Structs;

namespace SharpDc.Events
{
    public class SegmentEventArgs : EventArgs
    {
        public DownloadItem DownloadItem { get; set; }
        public SegmentInfo SegmentInfo { get; set; }
        public Source Source { get; set; }
    }
}