using System;
using QuickDc.Connections;

namespace QuickDc
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