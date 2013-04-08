// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.IO;
using System.Threading;

namespace SharpDc.Structs
{
    /// <summary>
    /// Direct Connect stream, allows to read data from item in a share or currently downloading item.
    /// Does not support write operations
    /// Adjusts downloading process if an item is downloading now
    /// </summary>
    public class DcStream : Stream
    {
        private readonly DownloadItem _downloadItem;
        private readonly FileStream _fileStream;
        private long _position;

        /// <summary>
        /// Gets stream magnet
        /// </summary>
        public Magnet Magnet { get; private set; }

        /// <summary>
        /// Creates a new stream from the file in a share
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="magnet"></param>
        internal DcStream(string filePath, Magnet magnet)
        {
            if (filePath == null)
                throw new ArgumentNullException("filePath");

            Magnet = magnet;
            _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }

        /// <summary>
        /// Creates a new stream from currently downloading item
        /// </summary>
        /// <param name="downloadItem"></param>
        internal DcStream(DownloadItem downloadItem)
        {
            if (downloadItem == null)
                throw new ArgumentNullException("downloadItem");

            Magnet = downloadItem.Magnet;
            _downloadItem = downloadItem;
        }

        /// <summary>
        /// Always returns true
        /// </summary>
        public override bool CanRead
        {
            get { return true; }
        }

        /// <summary>
        /// Always returns true
        /// </summary>
        public override bool CanSeek
        {
            get { return true; }
        }

        /// <summary>
        /// Always returns false
        /// </summary>
        public override bool CanWrite
        {
            get { return false; }
        }

        /// <summary>
        /// Throws NotSupportedException
        /// </summary>
        public override void Flush()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Returns total size of the file (the same as Magnet.Size)
        /// </summary>
        public override long Length
        {
            get { return Magnet.Size; }
        }

        /// <summary>
        /// Gets or sets current read position
        /// </summary>
        public override long Position
        {
            get { return _fileStream == null ? _position : _fileStream.Position; }
            set
            {
                if (_fileStream == null)
                    _position = value;
                else
                    _fileStream.Position = value;
            }
        }

        /// <summary>
        /// Reads a sequence of bytes from the current stream and advances the position within the stream by the number of bytes read.
        /// </summary>
        /// <returns>
        /// The total number of bytes read into the buffer. This can be less than the number of bytes requested if that many bytes are not currently available, or zero (0) if the end of the stream has been reached.
        /// </returns>
        /// <param name="buffer">An array of bytes. When this method returns, the buffer contains the specified byte array with the values between <paramref name="offset"/> and (<paramref name="offset"/> + <paramref name="count"/> - 1) replaced by the bytes read from the current source. </param><param name="offset">The zero-based byte offset in <paramref name="buffer"/> at which to begin storing the data read from the current stream. </param><param name="count">The maximum number of bytes to be read from the current stream. </param><exception cref="T:System.ArgumentException">The sum of <paramref name="offset"/> and <paramref name="count"/> is larger than the buffer length. </exception><exception cref="T:System.ArgumentNullException"><paramref name="buffer"/> is null. </exception><exception cref="T:System.ArgumentOutOfRangeException"><paramref name="offset"/> or <paramref name="count"/> is negative. </exception><exception cref="T:System.IO.IOException">An I/O error occurs. </exception><exception cref="T:System.NotSupportedException">The stream does not support reading. </exception><exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed. </exception><filterpriority>1</filterpriority>
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_fileStream != null)
            {
                return _fileStream.Read(buffer, offset, count);
            }

            while (!_downloadItem.Read(buffer, _position + offset, count))
                Thread.Sleep(50);

            return count;
        }

        /// <summary>
        /// Sets the position within the current stream.
        /// </summary>
        /// <returns>
        /// The new position within the current stream.
        /// </returns>
        /// <param name="offset">A byte offset relative to the <paramref name="origin"/> parameter. </param><param name="origin">A value of type <see cref="T:System.IO.SeekOrigin"/> indicating the reference point used to obtain the new position. </param><exception cref="T:System.IO.IOException">An I/O error occurs. </exception><exception cref="T:System.NotSupportedException">The stream does not support seeking, such as if the stream is constructed from a pipe or console output. </exception><exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed. </exception><filterpriority>1</filterpriority>
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (_fileStream != null)
            {
                return _fileStream.Seek(offset, origin);
            }

            switch (origin)
            {
                case SeekOrigin.Begin:
                    _position = offset;
                    break;
                case SeekOrigin.Current:
                    _position += offset;
                    break;
                case SeekOrigin.End:
                    _position = Length + offset;
                    break;
            }

            return _position;
        }

        /// <summary>
        /// Throws NotSupportedException
        /// </summary>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Throws NotSupportedException
        /// </summary>
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (_fileStream != null)
                _fileStream.Dispose();

            base.Dispose(disposing);
        }
    }
}