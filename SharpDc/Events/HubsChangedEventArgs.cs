//  -------------------------------------------------------------
//  LiveDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using SharpDc.Connections;

namespace SharpDc.Events
{
    public class HubsChangedEventArgs : EventArgs
    {
        public HubConnection Hub { get; set; }

        public HubsChangedEventArgs(HubConnection hub)
        {
            Hub = hub;
        }
    }
}