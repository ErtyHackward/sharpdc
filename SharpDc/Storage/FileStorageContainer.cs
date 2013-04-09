// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SharpDc.Helpers;
using SharpDc.Interfaces;
using SharpDc.Logging;
using SharpDc.Structs;

namespace SharpDc.Storage
{
    /// <summary>
    /// Allows to save the data into a file
    /// </summary>
    public class FileStorageContainer : IStorageContainer
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly DownloadItem _downloadItem;
        private readonly Dictionary<int, FileStream> _aliveStreams = new Dictionary<int, FileStream>();
        private readonly Stack<FileStream> _idleStreams = new Stack<FileStream>();
        private readonly object _syncRoot = new object();

        public string TempFilePath { get; set; }

        private bool _isDisposed;
        private bool _isDisposing;
        private long _maxPosition;

        // allows to tell if the segment is written to the file
        private readonly BitArray _segmentsWritten;
        private int _segmentsWrittenCount;

        /// <summary>
        /// Uses sparse files if possible (only works in Windows)
        /// Usefull for video on demand services, allows to quickly write at the end of huge and empty file
        /// Read more at http://en.wikipedia.org/wiki/Sparse_file
        /// </summary>
        public bool UseSparseFiles { get; set; }

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

            _segmentsWritten = new BitArray(item.TotalSegmentsCount);
        }

        public bool WriteData(SegmentInfo segment, int offset, byte[] buffer, int length)
        {
            if (_isDisposed || _isDisposing)
                return false;

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

            try
            {
                if (stream == null)
                {
                    try
                    {
                        stream = new FileStream(TempFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                                                FileShare.ReadWrite, 1024 * 1024, true);
                        if (UseSparseFiles)
                        {
                            Windows.SetSparse(stream);
                        }

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
                        _segmentsWritten.Set(segment.Index, true);
                        _segmentsWrittenCount++;
                        _idleStreams.Push(stream);
                        if (!_aliveStreams.Remove(segment.Index))
                            throw new InvalidDataException();
                    }
                }
            }
            catch (Exception x)
            {
                Logger.Error("File write error: {0}", x.Message);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Reads data from the saved segment
        /// Returns amount of bytes read
        /// </summary>
        /// <param name="segmentIndex"></param>
        /// <param name="segmentOffset">segment offset to read from</param>
        /// <param name="buffer"></param>
        /// <param name="bufferOffset">buffer write offset</param>
        /// <param name="count"></param>
        /// <returns></returns>
        public int Read(int segmentIndex, int segmentOffset, byte[] buffer, int bufferOffset, int count)
        {
            try
            {
                using (var fs = new FileStream(TempFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, count)
                    )
                {
                    fs.Position = (long)DownloadItem.SegmentSize * segmentIndex + segmentOffset;
                    return fs.Read(buffer, bufferOffset, count);
                }
            }
            catch (Exception x)
            {
                Logger.Error("File read error: " + x.Message);
            }

            return 0;
        }

        public int FreeSegments
        {
            get { return _segmentsWritten.Count - _segmentsWrittenCount; }
        }

        public bool CanReadSegment(int index)
        {
            lock (_syncRoot)
                return _segmentsWritten[index];
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
            _isDisposing = false;
        }
    }
}