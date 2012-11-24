//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using SharpDc.Interfaces;
using SharpDc.Structs;

namespace SharpDc
{
    /// <summary>
    /// Wastes all the data
    /// </summary>
    public class NullStorageContainer : IStorageContainer
    {
        public bool WriteData(SegmentInfo segment, int offset, byte[] buffer, int length)
        {
            return true;
        }

        public void Dispose()
        {
            
        }
    }
}