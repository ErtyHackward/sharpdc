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

        public int Read(int segmentIndex, int segmentOffset, byte[] buffer, int bufferOffset, int count)
        {
            throw new System.NotSupportedException();
        }

        public int FreeSegments {
            get { return int.MaxValue; }
        }

        public bool CanReadSegment(int segmentIndex)
        {
            return false;
        }

        public void Dispose()
        {
            
        }
    }
}