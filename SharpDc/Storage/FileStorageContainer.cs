// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SharpDc.Helpers;
using SharpDc.Interfaces;
using SharpDc.Logging;
using SharpDc.Structs;

namespace SharpDc.Storage
{
    /// <summary>
    /// Allows to save the data into a file
    /// </summary>
    [Serializable]
    public class FileStorageContainer : IStorageContainer
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly Dictionary<int, FileStream> _aliveStreams = new Dictionary<int, FileStream>();
        private readonly Stack<FileStream> _idleStreams = new Stack<FileStream>();
        private readonly object _syncRoot = new object();
        
        private bool _isDisposed;
        private bool _isDisposing;
        private long _maxPosition;
        private int _readThreads;
        private string _tempFilePath;

        /// <summary>
        /// Uses sparse files if possible (only works in Windows)
        /// Usefull for video on demand services, allows to quickly write at the end of huge and empty file
        /// Read more at http://en.wikipedia.org/wiki/Sparse_file
        /// </summary>
        public bool UseSparseFiles { get; set; }

        /// <summary>
        /// Indicates if this storage is available for read and write operations
        /// </summary>
        public override bool Available { get { return !(_isDisposed || _isDisposing); } }

        /// <summary>
        /// Gets or sets current 
        /// </summary>
        public string TempFilePath
        {
            get { return _tempFilePath; }
            set
            {
                if (_tempFilePath != null)
                    throw new InvalidOperationException("Path is already set");

                _tempFilePath = value;

                var folderPath = Path.GetDirectoryName(_tempFilePath);
                if (folderPath != null && !Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);
            }
        }

        public override int FreeSegments
        {
            get { return int.MaxValue; }
        }

        public FileStorageContainer()
        {
        }

        /// <summary>
        /// Allows to store data into a file
        /// </summary>
        /// <param name="tempFilePath"></param>
        public FileStorageContainer(string tempFilePath)
        {
            TempFilePath = tempFilePath;
        }
        
        public override bool WriteData(SegmentInfo segment, int offset, byte[] buffer, int srcOffset, int length)
        {
            if (_isDisposed || _isDisposing)
                throw new ObjectDisposedException("FileStorageContainer");

            if (length + offset > segment.Length)
                length = (int)(segment.Length - offset);

            var setupStream = offset == 0;
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

                stream.Write(buffer, srcOffset, length);

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
        public override int Read(int segmentIndex, int segmentOffset, byte[] buffer, int bufferOffset, int count)
        {
            if (_isDisposed || _isDisposing)
                throw new ObjectDisposedException("FileStorageContainer");

            if (count == 0)
                return 0;

            try
            {
                Interlocked.Increment(ref _readThreads);
                using (var fs = new FileStream(TempFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, count))
                {
                    fs.Position = (long)DownloadItem.SegmentLength * segmentIndex + segmentOffset;
                    return fs.Read(buffer, bufferOffset, count);
                }
            }
            catch (Exception x)
            {
                Logger.Error("File read error: " + x.Message);
            }
            finally
            {
                Interlocked.Decrement(ref _readThreads);
            }
            
            return 0;
        }

        public override bool CanReadSegment(int index)
        {
            return true;
        }

        public override void Dispose()
        {
            if (_isDisposed)
                return;
            
            _isDisposing = true;

            while (_readThreads != 0)
                Thread.Sleep(10);

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