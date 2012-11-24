//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using SharpDc.Messages;

namespace SharpDc.Events
{
    public class SearchRequestEventArgs : EventArgs
    {
        public SearchMessage Message { get; set; }
    }
}