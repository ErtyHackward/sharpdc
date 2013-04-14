// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using SharpDc.Interfaces;

namespace SharpDc.Storage
{
    /// <summary>
    /// Provides memory buffering of write operations to the file
    /// This allows to reduce write delays in spatial areas of the file
    /// when the file is not yet allocated
    /// </summary>
    public class BufferedFileStorageContainer : IStorageContainer
    {
        public override bool WriteData(Structs.SegmentInfo segment, int offset, byte[] buffer, int length)
        {
            throw new NotImplementedException();
        }

        public override int Read(int segmentIndex, int segmentOffset, byte[] buffer, int bufferOffset, int count)
        {
            throw new NotImplementedException();
        }

        public override int FreeSegments { get { throw new NotImplementedException(); } }

        public override bool CanReadSegment(int segmentIndex)
        {
            throw new NotImplementedException();
        }

        public override bool Available { get {
            throw new NotImplementedException();
        } }

        public override void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}