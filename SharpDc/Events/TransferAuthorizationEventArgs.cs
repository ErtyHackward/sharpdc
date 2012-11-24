//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;

namespace SharpDc.Events
{
    public class TransferAuthorizationEventArgs : EventArgs
    {
        public string OwnNickname { get; set; }

        public string HubAddress { get; set; }

        public string UserNickname { get; set; }
        /// <summary>
        /// Set this property to true if connection is allowed
        /// </summary>
        public bool Allowed { get; set; }
    }
}