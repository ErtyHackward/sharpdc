//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using SharpDc.Connections;

namespace SharpDc.Events
{
    public class MessageEventArgs : EventArgs
    {
        public TcpConnection Connection { get; set; }
        public string Message { get; set; }
    }
}