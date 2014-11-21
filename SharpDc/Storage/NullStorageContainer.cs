// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
// licensed under the LGPL
// -------------------------------------------------------------

using SharpDc.Interfaces;
using SharpDc.Structs;

namespace SharpDc.Storage
{
    /// <summary>
    /// Wastes all the data
    /// </summary>
    public class NullStorageContainer : IStorageContainer
    {
        public override bool WriteData(SegmentInfo segment, int offset, byte[] buffer, int bufferOffset, int length)
        {
            return true;
        }

        public override int Read(int segmentIndex, int segmentOffset, byte[] buffer, int bufferOffset, int count)
        {
            throw new System.NotSupportedException();
        }

        public override int FreeSegments
        {
            get { return int.MaxValue; }
        }

        public override bool CanReadSegment(int segmentIndex)
        {
            return false;
        }

        public override bool Available { get { return true; } }

        public override void Dispose()
        {

        }
    }
}