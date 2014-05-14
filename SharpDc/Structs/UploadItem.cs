// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using SharpDc.Connections;
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

        internal bool EnableRequestEventFire;

        private bool _isDisposed;
        private readonly object _syncRoot = new object();
        private FileStream _fileStream;
        private string _systemPath;
        protected long _uploadedBytes;

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

        public long UploadedBytes
        {
            get { return _uploadedBytes; }
        }

        public event EventHandler<UploadItemEventArgs> Error;

        protected void OnError(UploadItemEventArgs e)
        {
            e.UploadItem = this;
            var handler = Error;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<UploadItemEventArgs> Request;

        protected virtual void OnRequest(UploadItemEventArgs e)
        {
            e.UploadItem = this;
            var handler = Request;
            if (handler != null) handler(this, e);
        }
        
        public event EventHandler Disposed;

        private void OnDisposed()
        {
            var handler = Disposed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public UploadItem(ContentItem item, int bufferSize = 1024 * 4)
        {
            Content = item;
            FileStreamReadBufferSize = bufferSize;
        }

        public virtual void Dispose()
        {
            lock (_syncRoot)
            {
                if (_fileStream != null)
                {
                    _fileStream.Dispose();
                    _fileStream = null;
                    _fileStreamsCount--;
                }
                _isDisposed = true;
            }
            OnDisposed();
        }

        /// <summary>
        /// Returns a stream for reading a chunk
        /// </summary>
        /// <param name="start"></param>
        /// <param name="length"></param>
        /// <param name="callback"></param>
        /// <returns></returns>
        public virtual void RequestChunkAsync(long start, int length, Action<Stream,Exception> callback)
        {
            if (EnableRequestEventFire)
            {
                OnRequest(new UploadItemEventArgs
                {
                    Content = Content,
                    UploadItem = this
                });
            }

            new ThreadStart(() =>
            {
                Exception ex = null;
                if (_fileStream == null)
                {
                    FileStream fs = null;
                    
                    try
                    {
                        using (new PerfLimit("Slow open " + Content.SystemPath, 4000))
                        {
                            fs = new FileStream(SystemPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                                FileStreamReadBufferSize, true);
                        }
                    }
                    catch (Exception x)
                    {
                        OnError(new UploadItemEventArgs
                        {
                            Content = Content,
                            UploadItem = this,
                            Exception = x
                        });
                        ex = x;
                    }

                    lock (_syncRoot)
                    {
                        if (fs == null)
                        {
                            callback(null, ex);
                            return;
                        }

                        if (_isDisposed)
                        {
                            fs.Dispose();
                            return;
                        }
                        _fileStreamsCount++;
                        _fileStream = fs;
                    }
                }

                _fileStream.Position = start;

                callback(_fileStream, null);

            }).BeginInvoke(null, null);
        }
    }
}