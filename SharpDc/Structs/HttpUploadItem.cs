// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using SharpDc.Connections;
using SharpDc.Logging;
using SharpDc.Managers;

namespace SharpDc.Structs
{
    public class HttpUploadItem : UploadItem
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        public static HttpDownloadManager Manager = new HttpDownloadManager();

        private byte[] _buffer;
        private long _position;

        public static event EventHandler<HttpSegmentEventArgs> HttpSegmentDownloaded;

        private static void OnHttpSegmentDownloaded(HttpSegmentEventArgs e)
        {
            var handler = HttpSegmentDownloaded;
            if (handler != null) handler(null, e);
        }

        public static event EventHandler<HttpSegmentEventArgs> HttpSegmentNeeded;

        private static void OnHttpSegmentNeeded(HttpSegmentEventArgs e)
        {
            var handler = HttpSegmentNeeded;
            if (handler != null) handler(null, e);
        }


        public HttpUploadItem(ContentItem item, int bufferSize = 1024 * 1024) : base(item, bufferSize)
        {
            _buffer = new byte[bufferSize];
            _position = -1;
        }

        private bool ValidateBuffer(long pos, int count)
        {
            if (_position != -1 && _position <= pos && _position + _buffer.Length >= pos + count)
                return true;

            _position = pos;

            int length = _buffer.Length;

            if (_position + _buffer.Length > Content.Magnet.Size)
                length = (int)(Content.Magnet.Size - _position);

            bool done;

            using (new PerfLimit(string.Format("Slow http request {0} {1} bytes", SystemPath, length), 4000))
            {
                done = Manager.DownloadChunk(SystemPath, _buffer, _position, length);
            }

            if (HttpSegmentDownloaded != null)
            {
                OnHttpSegmentDownloaded(new HttpSegmentEventArgs { 
                    Buffer = _buffer, 
                    Magnet = Content.Magnet,
                    Position = _position,
                    Length = length
                });
            }

            return done;
        }

        protected override int InternalRead(byte[] array, long start, int count)
        {
            if (HttpSegmentNeeded != null)
            {
                var ea = new HttpSegmentEventArgs { 
                    Buffer = array, 
                    Magnet = Content.Magnet, 
                    Position = start, 
                    Length = count
                };
                OnHttpSegmentNeeded(ea);

                if (ea.FromCache)
                {
                    _uploadedBytes += count;
                    return count;
                }
            }

            try
            {            
                if (!ValidateBuffer(start, count))
                {
                    OnError(new UploadItemEventArgs());
                    return 0;
                }
            }
            catch (Exception x)
            {
                OnError(new UploadItemEventArgs { Exception = x });
                Logger.Error("Http read error: " + x.Message);
                return 0;
            }

            Buffer.BlockCopy(_buffer, (int)(start - _position), array, 0, count);

            _uploadedBytes += count;

            return count;
        }
    }

    public class HttpSegmentEventArgs : EventArgs
    {
        public byte[] Buffer { get; set; }
        public Magnet Magnet { get; set; }
        public long Position { get; set; }
        public int Length { get; set; }
        public bool FromCache { get; set; }
    }
}