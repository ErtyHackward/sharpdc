//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System.ComponentModel;
using SharpDc.Structs;

namespace SharpDc.Events
{
    public class CancelDownloadEventArgs : CancelEventArgs
    {
        public DownloadItem DownloadItem { get; set; }
    }
}