//  -------------------------------------------------------------
//  LiveDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;

namespace SharpDc.Events
{
    public class PasswordRequiredEventArgs : EventArgs
    {
        public string Password { get; set; }
    }
}
