//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using SharpDc.Logging;
using SharpDc.Managers;

namespace SharpDc.Structs
{
    /// <summary>
    /// Allows to read data from local file system
    /// </summary>
    public class UploadItem : IDisposable
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        
        private static int _fileStreamsCount;

        /// <summary>
        /// Gets total amount of FileSteam objects exists
        /// </summary>
        public static int TotalFileStreamsCount
        {
            get { return _fileStreamsCount; }
        }


        private readonly object _syncRoot = new object();
        private FileStream _fileStream;
        
        public ContentItem Content { get; set; }

        public int FileStreamReadBufferSize { get; private set; }

        public event EventHandler<UploadItemErrorEventArgs> Error;

        private void OnError(UploadItemErrorEventArgs e)
        {
            e.UploadItem = this;
            var handler = Error;
            if (handler != null) handler(this, e);
        }

        public event EventHandler Disposed;

        private void OnDisposed()
        {
            var handler = Disposed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public UploadItem(ContentItem item, int bufferSize = 1024 * 100)
        {
            Content = item;
            FileStreamReadBufferSize = bufferSize;
        }

        protected virtual int InternalRead(byte[] array, long start, int count)
        {
            using (new PerfLimit("Slow open " + Content.SystemPath, 1000))
            {
                if (_fileStream == null)
                {
                    _fileStream = new FileStream(Content.SystemPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                                                 FileStreamReadBufferSize, false);
                    Interlocked.Increment(ref _fileStreamsCount);
                }
            }

            using (new PerfLimit("Slow read " + Content.SystemPath))
            {
                _fileStream.Position = start;
                return _fileStream.Read(array, 0, count);
            }
        }

        public int Read(byte[] array, long start, int count)
        {
            try
            {
                lock (_syncRoot)
                    return InternalRead(array, start, count);
            }
            catch (Exception x)
            {
                OnError(new UploadItemErrorEventArgs { Exception = x });
                Logger.Error("Unable to read the data for upload: " + x.Message);
                return 0;
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_fileStream != null)
                {
                    _fileStream.Dispose();
                    _fileStream = null;
                    Interlocked.Decrement(ref _fileStreamsCount);
                }
            }
            OnDisposed();
        }
    }

    public class UploadItemErrorEventArgs : EventArgs
    {
        public UploadItem UploadItem { get; set; }
        public Exception Exception { get; set; }
    }
}
