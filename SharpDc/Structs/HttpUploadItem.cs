using System;
using System.Net;
using SharpDc.Helpers;
using SharpDc.Logging;
using SharpDc.Managers;

namespace SharpDc.Structs
{
    public class HttpUploadItem : UploadItem
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private byte[] _buffer;
        private long _position;
        
        
        public HttpUploadItem(ContentItem item, int bufferSize = 1024 * 1024) : base(item, bufferSize)
        {
            _buffer = new byte[bufferSize];
            _position = -1;
        }

        private bool ValidateBuffer(long pos, int count)
        {
            if (_position <= pos && _position + _buffer.Length >= pos + count)
                return true;

            _position = pos;

            int length = _buffer.Length;

            if (_position + _buffer.Length > Content.Magnet.Size)
                length = (int)(Content.Magnet.Size - _position);

            try
            {
                using (new PerfLimit(string.Format("Http request {0} {1} bytes", Content.SystemPath, length), 4000))
                {
                    HttpHelper.DownloadChunk(Content.SystemPath, _buffer, _position, length);
                    return true;
                }
            }
            catch (Exception x)
            {
                OnError(new UploadItemErrorEventArgs { Exception = x });
                Logger.Error("Http read error: " + x.Message);
                return false;
            }
        }

        protected override int InternalRead(byte[] array, long start, int count)
        {
            if (!ValidateBuffer(start, count))
                return 0;

            Buffer.BlockCopy(_buffer, (int)(start - _position), array, 0, count);

            return count;
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}