//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.Collections.Generic;
using SharpDc.Interfaces;
using SharpDc.Structs;

namespace SharpDc
{
    public class MemoryStorageConatiner : IStorageContainer
    {
        private readonly DownloadItem _downloadItem;
        private readonly Dictionary<int, byte[]> _memoryBuffer = new Dictionary<int, byte[]>();

        private const long BufferLimit = 30 * 1024 * 1024; // 30 Mb
        private readonly object _syncRoot = new object();
        private int _maxChunks;

        public MemoryStorageConatiner(DownloadItem item)
        {
            _downloadItem = item;
            _maxChunks = (int)(BufferLimit / DownloadItem.SegmentSize);
        }

        public void Dispose()
        {
            _memoryBuffer.Clear();
            GC.Collect(2);
        }

        public bool TryRead(byte[] buffer, long start, int count)
        {
            if (_downloadItem.DoneSegments == null) return false;
            if (CanRead(start, count))
            {
                var startIndex = GetSegmentIndex(start);
                var endIndex = GetSegmentIndex(start + count);
                lock (_downloadItem.SyncRoot)
                {
                    lock (_syncRoot)
                    {
                        if (startIndex == endIndex)
                        {
                            var segment = _memoryBuffer[startIndex];
                            var startOffset = (int)(start % DownloadItem.SegmentSize);
                            Buffer.BlockCopy(segment, startOffset, buffer, 0, count);
                        }
                        else
                        {
                            int position = 0;
                            for (var i = startIndex; i <= endIndex; i++)
                            {
                                var segment = _memoryBuffer[i];

                                if (i == startIndex)
                                {
                                    var startOffset = (int)(start % DownloadItem.SegmentSize);
                                    var length = DownloadItem.SegmentSize - startOffset;
                                    Buffer.BlockCopy(segment, startOffset, buffer, 0, length);
                                    position += length;
                                }
                                else if (i == endIndex)
                                {
                                    var length = (int)((start + count) % DownloadItem.SegmentSize);
                                    Buffer.BlockCopy(segment, 0, buffer, position, length);
                                }
                                else
                                {
                                    // copy whole segment
                                    Buffer.BlockCopy(segment, 0, buffer, position, DownloadItem.SegmentSize);
                                    position += DownloadItem.SegmentSize;
                                }
                            }
                        }
                    }

                }
                return true;
            }
            return false;
        }

        public void ReleaseSegment(int index)
        {

        }

        /// <summary>
        /// Determines was all the segments from range been downloaded
        /// </summary>
        /// <param name="startPos">File position from wich bytes should be read</param>
        /// <param name="count"></param>
        /// <param name="makeRequest">If true the not-finished segments will be marked as High-Priority segments</param>
        /// <returns></returns>
        protected bool CanRead(long startPos, int count, bool makeRequest = true)
        {
            var startIndex = GetSegmentIndex(startPos);
            var endIndex = GetSegmentIndex(startPos + count);
            var result = true;

            lock (_downloadItem.SyncRoot)
            {
                for (int i = startIndex; i <= endIndex; i++)
                {

                    lock (_syncRoot)
                    {
                        if (!_memoryBuffer.ContainsKey(i))
                        {
                            result = false;

                            if (makeRequest)
                            {
                                if (!_downloadItem.HighPrioritySegments.Contains(i))
                                {
                                    _downloadItem.HighPrioritySegments.Add(i);
                                }
                            }
                            else return false;

                        }
                    }
                }
                return result;
            }
        }

        private static int GetSegmentIndex(long filePosition)
        {
            return (int)(filePosition / DownloadItem.SegmentSize);
        }

        public bool WriteData(SegmentInfo segment, int offset, byte[] buffer, int length)
        {
            throw new NotImplementedException();
        }
    }
}
