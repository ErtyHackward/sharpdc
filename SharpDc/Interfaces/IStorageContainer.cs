//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using SharpDc.Structs;

namespace SharpDc.Interfaces
{
    public interface IStorageContainer : IDisposable
    {
        bool WriteData(SegmentInfo segment, int offset, byte[] buffer, int length);
    }
}