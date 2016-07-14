// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
        public static int TotalFileStreamsCount => _fileStreamsCount;

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

        public long UploadedBytes => _uploadedBytes;

        public event EventHandler<UploadItemEventArgs> Error;

        protected void OnError(UploadItemEventArgs e)
        {
            e.UploadItem = this;
            Error?.Invoke(this, e);
        }

        public event EventHandler<UploadItemEventArgs> Request;

        protected virtual void OnRequest(UploadItemEventArgs e)
        {
            e.UploadItem = this;
            Request?.Invoke(this, e);
        }
        
        public event EventHandler Disposed;

        private void OnDisposed()
        {
            Disposed?.Invoke(this, EventArgs.Empty);
        }

        public UploadItem(ContentItem item, int bufferSize = 1024 * 100)
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
                    Interlocked.Decrement(ref _fileStreamsCount);
                }
                _isDisposed = true;
            }
            OnDisposed();
        }

        private async Task<long> InternalCopyChunk(Stream stream, long filePos, int bytesRequired)
        {
            if (_fileStream == null)
            {
                FileStream fs;
                using (new PerfLimit("Slow open " + Content.SystemPath, 4000))
                {
                    fs = FileStreamFactory.CreateFileStream(SystemPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                                        FileStreamReadBufferSize, FileOptions.Asynchronous);
                }

                lock (_syncRoot)
                {
                    if (_isDisposed)
                    {
                        fs.Dispose();
                        return 0;
                    }
                    _fileStream = fs;
                }

                Interlocked.Increment(ref _fileStreamsCount);
            }

            _fileStream.Position = filePos;

            long readSoFar = 0L;
            var buffer = new byte[FileStreamReadBufferSize];
            do
            {
                var toRead = Math.Min(bytesRequired - readSoFar, buffer.Length);
                var readNow = await _fileStream.ReadAsync(buffer, 0, (int)toRead).ConfigureAwait(false);
                if (readNow == 0)
                    break; // End of stream
                await stream.WriteAsync(buffer, 0, readNow).ConfigureAwait(false);
                readSoFar += readNow;
                Interlocked.Add(ref _uploadedBytes, readNow);
            } while (readSoFar < bytesRequired);
            return readSoFar;
        }

        public virtual async Task<long> SendChunkAsync(TransferConnection transfer, long filePos, int bytesRequired)
        {
            if (EnableRequestEventFire)
                OnRequest(new UploadItemEventArgs());

            using (new PerfLimit("Send fs chunk time", 2000))
            {
                try
                {
                    return await InternalCopyChunk(transfer.Stream, filePos, bytesRequired).ConfigureAwait(false);
                }
                catch (Exception x)
                {
                    OnError(new UploadItemEventArgs { Exception = x });
                    Logger.Error("Unable to read the data for upload: " + x.Message);
                    return 0;
                }
            }
        }
    }
}