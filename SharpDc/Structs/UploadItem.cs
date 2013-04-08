// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.IO;
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
        #region Static

        private static readonly ILogger Logger = LogManager.GetLogger();

        private static int _fileStreamsCount;

        /// <summary>
        /// Gets total amount of FileSteam objects exists
        /// </summary>
        public static int TotalFileStreamsCount
        {
            get { return _fileStreamsCount; }
        }

        #endregion

        private bool _isDisposed;
        private readonly object _syncRoot = new object();
        private FileStream _fileStream;
        private string _systemPath;

        /// <summary>
        /// Gets or sets system path to use
        /// </summary>
        public string SystemPath
        {
            get { return _systemPath ?? Content.SystemPath; }
            set { _systemPath = value; }
        }

        public ContentItem Content { get; set; }

        public int FileStreamReadBufferSize { get; private set; }

        public event EventHandler<UploadItemErrorEventArgs> Error;

        protected void OnError(UploadItemErrorEventArgs e)
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
            if (_fileStream == null)
            {
                FileStream fs;
                using (new PerfLimit("Slow open " + Content.SystemPath, 4000))
                {
                    fs = new FileStream(SystemPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                                        FileStreamReadBufferSize, false);
                }

                lock (_syncRoot)
                {
                    if (_isDisposed)
                    {
                        fs.Dispose();
                        return 0;
                    }
                    Interlocked.Increment(ref _fileStreamsCount);
                    _fileStream = fs;
                }
            }

            lock (_syncRoot)
            {
                using (new PerfLimit("Slow read " + Content.SystemPath, 4000))
                {
                    _fileStream.Position = start;
                    return _fileStream.Read(array, 0, count);
                }
            }
        }

        public int Read(byte[] array, long start, int count)
        {
            try
            {
                return InternalRead(array, start, count);
            }
            catch (Exception x)
            {
                OnError(new UploadItemErrorEventArgs { Exception = x });
                Logger.Error("Unable to read the data for upload: " + x.Message);
                return 0;
            }
        }

        public bool IsLocked
        {
            get
            {
                if (Monitor.TryEnter(_syncRoot, 100))
                {
                    Monitor.Exit(_syncRoot);
                    return false;
                }
                return true;
            }
        }

        public virtual void Dispose()
        {
            lock (_syncRoot)
            {
                if (_fileStream != null)
                {
                    _fileStream.Dispose();
                    _fileStream = null;
                    Interlocked.Decrement(ref _fileStreamsCount);
                }
                _isDisposed = true;
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