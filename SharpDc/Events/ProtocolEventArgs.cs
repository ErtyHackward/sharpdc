// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;

namespace SharpDc.Events
{
    /// <summary>
    /// Event that can be handled
    /// </summary>
    public class BaseEventArgs : EventArgs
    {
        public bool Handled { get; set; }
    }

    public class ConnectionStatusEventArgs : EventArgs
    {
        public ConnectionStatus Status { get; set; }
        public ConnectionStatus Previous { get; set; }
        public Exception Exception { get; set; }
    }

    public enum ConnectionStatus
    {
        Connecting,
        Connected,
        Disconnecting,
        Disconnected
    }
}