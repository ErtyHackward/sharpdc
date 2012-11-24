//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using SharpDc.Structs;

namespace SharpDc.Events
{
    public class DownloadEventArgs : EventArgs
    {
        public DownloadItem DownloadItem { get; set; }
    }

    public class DownloadCompletedEventArgs : EventArgs
    {
        public DownloadItem DownloadItem { get; set; }
        /// <summary>
        /// Contains exception information if any when file moving
        /// </summary>
        public Exception Exception { get; set; }
    }
}