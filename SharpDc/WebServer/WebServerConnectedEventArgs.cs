// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Net.Sockets;

namespace SharpDc.WebServer
{
    public class WebServerConnectedEventArgs : EventArgs
    {
        public WebServerConnectedEventArgs(TcpClient client)
        {
            Client = client;
        }

        public bool Handled { get; set; }
        public TcpClient Client { get; set; }
    }
}