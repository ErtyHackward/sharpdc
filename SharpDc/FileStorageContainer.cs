//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using SharpDc.Interfaces;
using SharpDc.Structs;

namespace SharpDc
{
    /// <summary>
    /// Allows to save the data into a file
    /// </summary>
    public class FileStorageContainer : IStorageContainer
    {
        private readonly DownloadItem _downloadItem;
        private readonly Dictionary<int, FileStream> _aliveStreams = new Dictionary<int, FileStream>();
        private readonly Stack<FileStream> _idleStreams = new Stack<FileStream>();
        private readonly object _syncRoot = new object();
        
        public string TempFilePath { get; set; }
        
        private bool _isDisposed;
        private bool _isDisposing;

        private long _maxPosition;

        /// <summary>
        /// Allows to store data into a file
        /// </summary>
        /// <param name="tempFilePath"></param>
        /// <param name="item"> </param>
        public FileStorageContainer(string tempFilePath, DownloadItem item)
        {
            _downloadItem = item;
            TempFilePath = tempFilePath;
            var folderPath = Path.GetDirectoryName(tempFilePath);
            if (folderPath != null && !Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);
        }

        public bool WriteData(SegmentInfo segment, int offset, byte[] buffer, int length)
        {
            if (_isDisposed || _isDisposing) return false;

            if (length + offset > segment.Length)
                length = (int)(segment.Length - offset);

            var setupStream = false;
            FileStream stream;
            lock (_syncRoot)
            {
                if (!_aliveStreams.TryGetValue(segment.Index, out stream))
                {
                    if (_idleStreams.Count > 0)
                    {
                        stream = _idleStreams.Pop();
                        setupStream = true;
                        _aliveStreams.Add(segment.Index, stream);
                    }
                }
            }

            if (stream == null)
            {
                try
                {
                    stream = new FileStream(TempFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                                            FileShare.ReadWrite, 1024 * 1024, true);
                    setupStream = true;
                }
                catch
                {
                    return false;
                }

                lock (_syncRoot)
                {
                    _aliveStreams.Add(segment.Index, stream);
                }
            }

            if (setupStream)
            {
                stream.Position = segment.StartPosition;
                _maxPosition = Math.Max(_maxPosition, segment.StartPosition);
            }


            stream.Write(buffer, 0, length);

            if (length + offset >= segment.Length)
            {
                stream.Flush();
                lock (_syncRoot)
                {
                    _idleStreams.Push(stream);
                    if (!_aliveStreams.Remove(segment.Index))
                        throw new InvalidDataException();
                }
            }

            return true;
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
                        if (!_downloadItem.DoneSegments[i])
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

        /// <summary>
        /// Tries to read data from the file
        /// </summary>
        /// <param name="position"></param>
        /// <param name="buffer"></param>
        /// <returns></returns>
        public bool Read(long position, byte[] buffer)
        {
            // ensure all segments downloaded
            throw new NotImplementedException();
        }

        private static int GetSegmentIndex(long filePosition)
        {
            return (int)(filePosition / DownloadItem.SegmentSize);
        }

        public void Dispose()
        {
            _isDisposing = true;
            lock (_syncRoot)
            {
                foreach (var aliveStream in _aliveStreams)
                {
                    aliveStream.Value.Flush();
                    aliveStream.Value.Close();
                }

                foreach (var fileStream in _idleStreams)
                {
                    fileStream.Flush();
                    fileStream.Close();
                }

                _aliveStreams.Clear();
                _idleStreams.Clear();

                _isDisposed = true;
            }
        }
    }
}
