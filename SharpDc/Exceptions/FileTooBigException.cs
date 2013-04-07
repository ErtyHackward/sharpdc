//  -------------------------------------------------------------
//  LiveDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;

namespace SharpDc.Exceptions
{
    public class FileTooBigException : Exception
    {
        public long MaxFileSize { get; set; }

        public long CurrentSize { get; set; }

        public FileTooBigException(long maxSize, long currentSize)
        {
            MaxFileSize = maxSize;
            CurrentSize = currentSize;
        }
    }
}