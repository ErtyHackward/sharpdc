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
using SharpDc.Logging;
using SharpDc.Managers;

namespace SharpDc.Connections
{
    public class UploadItem : IDisposable
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        public ContentItem Content { get; set; }

        private FileStream _fileStream;

        private readonly object _syncRoot = new object();

        private readonly Queue<double> _perfomance = new Queue<double>();

        public int FileStreamReadBufferSize { get; private set; }

        public double ReadAverageTime
        {
            get {
                lock (_perfomance)
                {
                    if (_perfomance.Any())
                        return _perfomance.Average();
                    return -1;
                }
            }
        }

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

        public UploadItem(int bufferSize = 1024 * 100)
        {
            FileStreamReadBufferSize = bufferSize;
        }

        private int InternalRead(byte[] array, long start, int count)
        {
            var openSw = Stopwatch.StartNew();
            if (_fileStream == null)
                _fileStream = new FileStream(Content.SystemPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, FileStreamReadBufferSize, false);
            openSw.Stop();

            if (openSw.ElapsedMilliseconds > 1000)
            {
                Logger.Warn("Slow open {0}ms {1}", openSw.ElapsedMilliseconds, Content.SystemPath);
            }
            
            var sw = Stopwatch.StartNew();
            _fileStream.Position = start;
            var read = _fileStream.Read(array, 0, count);
            sw.Stop();

            lock (_perfomance)
            {
                _perfomance.Enqueue(sw.Elapsed.TotalMilliseconds);
                if (_perfomance.Count > 5)
                    _perfomance.Dequeue();
            }

            if (sw.ElapsedMilliseconds > 1000)
            {
                Logger.Warn("Slow read {0}ms {1}", sw.ElapsedMilliseconds, Content.SystemPath);
            }

            return read;
        }

        public int Read(byte[] array, long start, int count)
        {
            lock (_syncRoot)
            {
                try
                {
                    return InternalRead(array, start, count);
                }
                catch (Exception x)
                {
                    if (x is IOException)
                    {
                        try
                        {
                            // try to open file one more time
                            if (_fileStream != null)
                            {
                                _fileStream.Dispose();
                                _fileStream = null;
                            }

                            return InternalRead(array, start, count);
                        }
                        catch (Exception ex)
                        {
                            OnError(new UploadItemErrorEventArgs { Exception = ex });
                            Logger.Error("Unable to read the data for upload (SA): " + x.Message);
                            return 0;
                        }
                    }
                    
                    OnError(new UploadItemErrorEventArgs {Exception = x});
                    Logger.Error("Unable to read the data for upload: " + x.Message);
                    return 0;
                    
                }
            }
        }

        public void Dispose()
        {
            if (_fileStream != null)
            {
                _fileStream.Dispose();
                _fileStream = null;
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
