// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using SharpDc.Interfaces;
using SharpDc.Structs;

namespace SharpDc.Storage
{
    /// <summary>
    /// Provides a container that stores all data in memory
    /// </summary>
    public class MemoryStorageConatiner : IStorageContainer
    {
        private readonly DownloadItem _downloadItem;
        private readonly Dictionary<int, byte[]> _memoryBuffer = new Dictionary<int, byte[]>();

        private readonly int _maxSegments;
        private readonly object _syncRoot = new object();
        private readonly List<int> _doneSegments;

        public MemoryStorageConatiner(DownloadItem item, int bufferSegments = 64)
        {
            _maxSegments = bufferSegments;
            _downloadItem = item;
            _doneSegments = new List<int>();
        }

        public override void Dispose()
        {
            _memoryBuffer.Clear();
            _doneSegments.Clear();
        }

        /// <summary>
        /// Reads data from the saved segment
        /// Returns amount of bytes read
        /// </summary>
        /// <param name="segmentIndex"></param>
        /// <param name="segmentOffset"></param>
        /// <param name="buffer"></param>
        /// <param name="bufferOffset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public override int Read(int segmentIndex, int segmentOffset, byte[] buffer, int bufferOffset, int count)
        {
            byte[] memorySegment;
            lock (_syncRoot)
            {
                if (!_memoryBuffer.TryGetValue(segmentIndex, out memorySegment))
                    return 0;

                if (!_doneSegments.Contains(segmentIndex))
                    return 0;
            }

            var startOffset = segmentOffset % _downloadItem.SegmentLength;
            Buffer.BlockCopy(memorySegment, startOffset, buffer, bufferOffset, count);

            return count;
        }

        /// <summary>
        /// Releases occupied segments allowing to accept other
        /// </summary>
        /// <param name="index"></param>
        public void ReleaseSegment(int index)
        {
            lock (_syncRoot)
            {
                _memoryBuffer.Remove(index);
                _doneSegments.Remove(index);
            }
        }

        /// <summary>
        /// Writes data to the storage 
        /// </summary>
        /// <param name="segment">segment information</param>
        /// <param name="offset">segment space offset</param>
        /// <param name="buffer">data buffer to write</param>
        /// <param name="bufferOffset"></param>
        /// <param name="length">amount of bytes to write</param>
        /// <returns></returns>
        public override bool WriteData(SegmentInfo segment, int offset, byte[] buffer, int bufferOffset, int length)
        {
            byte[] memorySegment;
            lock (_syncRoot)
            {
                if (!_memoryBuffer.TryGetValue(segment.Index, out memorySegment))
                {
                    if (_memoryBuffer.Count >= _maxSegments)
                        return false;

                    memorySegment = new byte[_downloadItem.SegmentLength];
                    _memoryBuffer.Add(segment.Index, memorySegment);
                }
            }

            Buffer.BlockCopy(buffer, bufferOffset, memorySegment, offset, length);

            if (offset + length == segment.Length)
                lock (_syncRoot)
                    _doneSegments.Add(segment.Index);

            return true;
        }

        /// <summary>
        /// Gets how much new segments the container can accept
        /// </summary>
        public override int FreeSegments
        {
            get { return _maxSegments - _memoryBuffer.Count; }
        }

        /// <summary>
        /// Tells if the segment is available for reading
        /// i.e. is completely downloaded
        /// </summary>
        /// <returns></returns>
        public override bool CanReadSegment(int segment)
        {
            lock (_syncRoot)
            {
                return _memoryBuffer.ContainsKey(segment) && _doneSegments.Contains(segment);
            }
        }

        public override bool Available { get { return true; } }
    }
}