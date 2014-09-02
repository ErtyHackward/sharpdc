// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpDc.Connections;
using SharpDc.Helpers;
using SharpDc.Logging;
using SharpDc.Managers;

namespace SharpDc.Structs
{
    public class HttpUploadItem : UploadItem
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        public static HttpDownloadManager Manager = new HttpDownloadManager();
        
        private readonly int _requestLength;

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
            _requestLength = bufferSize;
        }

        protected override async Task<long> InternalCopyChunk(Stream stream, long filePos, long bytesRequired)
        {
            var startTime = DateTime.UtcNow;

            if (HttpSegmentNeeded != null)
            {
                var ea = new HttpSegmentEventArgs
                {
                    UploadItem = this,
                    Magnet = Content.Magnet,
                    Position = filePos,
                    Length = (int)bytesRequired
                };

                OnHttpSegmentNeeded(ea);

                if (ea.FromCache)
                {
                    return bytesRequired;
                }
            }

            long bytesCopied = 0;

            var sw = PerfTimer.StartNew();
            using (new PerfLimit(() => string.Format("Slow http request {0} pos: {1} len: {2} filelen: {3}", SystemPath, filePos, bytesRequired, Content.Magnet.Size), 4000))
            {
                try
                {
                    var responseStream = await HttpHelper.GetHttpChunkAsync(SystemPath, filePos, bytesRequired);
                    bytesCopied = await CopyTo(responseStream, stream, bytesRequired);
                }
                catch (Exception x)
                {
                    Logger.Error("DownloadChunk error: {0}", x.Message);
                }
            }
            sw.Stop();

            HttpHelper.RegisterDownloadTime((int)sw.ElapsedMilliseconds);

            if (HttpSegmentDownloaded != null)
            {
                OnHttpSegmentDownloaded(new HttpSegmentEventArgs
                {
                    UploadItem = this,
                    Magnet = Content.Magnet,
                    Position = filePos,
                    Length = bytesRequired,
                    RequestedAt = startTime,
                    DownloadingTime = sw.Elapsed
                });
            }

            return bytesCopied;
        }

        private async Task<long> CopyTo(Stream source, Stream destination, long bytesRequired)
        {
            long readSoFar = 0L;
            var buffer = new byte[64 * 1024];
            do
            {
                var toRead = Math.Min(bytesRequired - readSoFar, buffer.Length);
                var readNow = await source.ReadAsync(buffer, 0, (int)toRead);
                if (readNow == 0)
                    break; // End of stream
                await destination.WriteAsync(buffer, 0, readNow);
                readSoFar += readNow;
                Interlocked.Add(ref _uploadedBytes, readNow);
            } while (readSoFar < bytesRequired);
            return readSoFar;
        }
    }

    public class HttpSegmentEventArgs : EventArgs
    {
        public HttpUploadItem UploadItem { get; set; }
        public Stream Stream { get; set; }
        public Magnet Magnet { get; set; }
        public long Position { get; set; }
        public long Length { get; set; }
        public bool FromCache { get; set; }
        public DateTime RequestedAt { get; set; }
        public TimeSpan DownloadingTime { get; set; }
    }
}