// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2014
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SharpDc
{
    /// <summary>
    /// Provides memory cache for read operations
    /// Write is not recommended
    /// </summary>
    public class CacheStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly int _cacheSize;
        private readonly Queue<CacheSegment> _segments = new Queue<CacheSegment>();
        private readonly object _syncRoot = new object();
        
        public int MemoryUsed { get; private set; }
        
        public CacheStream(Stream baseStream, int cacheLength)
        {
            if (baseStream == null) 
                throw new ArgumentNullException("baseStream");

            _baseStream = baseStream;
            _cacheSize = cacheLength;
        }

        public override void Flush()
        {
            _baseStream.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _baseStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _baseStream.SetLength(value);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            count = Math.Min((int)(_baseStream.Length - _baseStream.Position), count);

            CacheSegment segment;

            lock (_syncRoot)
            {
                segment = _segments.FirstOrDefault(s => s.Position <= _baseStream.Position && count <= s.Length - (_baseStream.Position - s.Position));
            }

            var basePos = _baseStream.Position;

            if (segment.Data == null)
            {
                segment.Position = basePos;
                segment.Length = count;
                segment.Data = new byte[count];

                var read = _baseStream.Read(segment.Data, 0, count);
                segment.Length = read;

                if (read == 0)
                    return 0;

                lock (_syncRoot)
                {
                    FreeMemory(count);
                    _segments.Enqueue(segment);
                    MemoryUsed += segment.Length;
                }
            }

            Buffer.BlockCopy(segment.Data, (int)(basePos - segment.Position), buffer, offset, count);
            return count;
        }

        private void FreeMemory(int length)
        {
            while (_segments.Count > 0 && MemoryUsed + length > _cacheSize)
            {
                var seg = _segments.Dequeue();
                seg.Data = null;
                MemoryUsed -= seg.Length;
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _baseStream.Write(buffer, offset, count);
        }

        public override bool CanRead
        {
            get { return _baseStream.CanRead; }
        }

        public override bool CanSeek
        {
            get { return _baseStream.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return _baseStream.CanWrite; }
        }

        public override long Length
        {
            get { return _baseStream.Length; }
        }

        public override long Position { get { return _baseStream.Position; } set { _baseStream.Position = value; } }
    }

    struct CacheSegment
    {
        public long Position;
        public int Length;
        public byte[] Data;
    }
}
