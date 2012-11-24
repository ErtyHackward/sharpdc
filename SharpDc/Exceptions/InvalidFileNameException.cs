//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;

namespace SharpDc.Exceptions
{
    public class InvalidFileNameException : Exception
    {
        public InvalidFileNameException(string message)
            : base(message)
        {

        }

        public string FileName { get; set; }
    }
}