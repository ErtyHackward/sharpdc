using System;
using System.Diagnostics;
using System.IO;
using SharpDc.Helpers;
using SharpDc.Logging;
using SharpDc.Managers;

namespace SharpDc.Structs
{
    /// <summary>
    /// Provides basic http file stream with random file access
    /// Use in pair with BufferedStream is strongly recommended
    /// </summary>
    public class HttpFileStream : Stream
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        public static HttpDownloadManager Manager = new HttpDownloadManager();

        private long _position;
        private long _length = -1;
        private string _uri;

        private readonly object _syncRoot = new object();

        
        public HttpFileStream(string url, long size = -1)
        {
            _uri = url;
            _length = size;
        }

        /// <summary>
        /// Throws NotSupportedException
        /// </summary>
        public override void Flush()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// When overridden in a derived class, sets the position within the current stream.
        /// </summary>
        /// <returns>
        /// The new position within the current stream.
        /// </returns>
        /// <param name="offset">A byte offset relative to the <paramref name="origin"/> parameter. </param><param name="origin">A value of type <see cref="T:System.IO.SeekOrigin"/> indicating the reference point used to obtain the new position. </param><exception cref="T:System.IO.IOException">An I/O error occurs. </exception><exception cref="T:System.NotSupportedException">The stream does not support seeking, such as if the stream is constructed from a pipe or console output. </exception><exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed. </exception><filterpriority>1</filterpriority>
        public override long Seek(long offset, SeekOrigin origin)
        {
            lock (_syncRoot)
            {
                switch (origin)
                {
                    case SeekOrigin.Begin:
                        _position = offset;
                        break;
                    case SeekOrigin.Current:
                        _position += offset;
                        break;
                    case SeekOrigin.End:
                        _position = Length - offset;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException("origin");
                }

                return _position;
            }
        }

        /// <summary>
        /// Throws NotSupportedException
        /// </summary>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Reads a sequence of bytes from the current stream and advances the position within the stream by the number of bytes read.
        /// </summary>
        /// <returns>
        /// The total number of bytes read into the buffer.
        /// </returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (offset != 0)
                Debugger.Break();

            lock (_syncRoot)
            {
                count = Math.Min((int)(Length - _position), count);

                if (count == 0)
                    return 0;
                
                var success = Manager.DownloadChunk(_uri, buffer, _position, count);
                
                if (success)
                {
                    _position += count;
                    return count;
                }
                return 0;
            }
        }

        /// <summary>
        /// Throws NotSupportedException
        /// </summary>
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Returns true
        /// </summary>
        public override bool CanRead
        {
            get { return true; }
        }

        /// <summary>
        /// Returns true
        /// </summary>
        public override bool CanSeek
        {
            get { return true; }
        }

        /// <summary>
        /// Returns false
        /// </summary>
        public override bool CanWrite
        {
            get { return false; }
        }

        /// <summary>
        /// Gets the length in bytes of the stream.
        /// </summary>
        /// <returns>
        /// A long value representing the length of the stream in bytes.
        /// </returns>
        /// <exception cref="T:System.NotSupportedException">A class derived from Stream does not support seeking. </exception><exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed. </exception><filterpriority>1</filterpriority>
        public override long Length
        {
            get {

                if (_length == -1)
                {
                    _length = HttpHelper.GetFileSize(_uri);
                }

                return _length; 
            }
        }

        /// <summary>
        /// Gets or sets the position within the current stream.
        /// </summary>
        /// <returns>
        /// The current position within the stream.
        /// </returns>
        public override long Position
        {
            get { return _position; }
            set {
                lock (_syncRoot)
                {
                    _position = value;
                }
            }
        }
    }
}
