//  -------------------------------------------------------------
//  LiveDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;

namespace SharpDc.Events
{
    public class TransferSegmentCompletedEventArgs : EventArgs
    {
        /// <summary>
        /// Set this propert to true if you want to pause downloading temporary
        /// </summary>
        public bool Pause { get; set; }
    }
}