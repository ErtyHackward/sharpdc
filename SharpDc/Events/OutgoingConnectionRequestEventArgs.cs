//  -------------------------------------------------------------
//  LiveDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using SharpDc.Messages;

namespace SharpDc.Events
{
    public class OutgoingConnectionRequestEventArgs : EventArgs
    {
        public ConnectToMeMessage Message { get; set; }
    }
}