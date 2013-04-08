// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using SharpDc.Messages;

namespace SharpDc.Events
{
    public class IncomingConnectionRequestEventArgs : EventArgs
    {
        public RevConnectToMeMessage Message { get; set; }

        /// <summary>
        /// Set this field to our local address, if null or empty connection will not be established
        /// </summary>
        public string LocalAddress { get; set; }
    }
}