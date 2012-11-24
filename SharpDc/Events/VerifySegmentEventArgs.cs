//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.Collections.Generic;
using SharpDc.Structs;

namespace SharpDc.Events
{
    public class VerifySegmentEventArgs : EventArgs
    {
        public DownloadItem DownloadItem { get; set; }
        /// <summary>
        /// Sources from we have content received 
        /// </summary>
        public List<Source> Sources { get; set; }

        public bool IsCorrect { get; set; }
    }
}