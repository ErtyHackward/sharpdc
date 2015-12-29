using System;
using System.Threading;
using System.Threading.Tasks;
using SharpDc.Connections;
using SharpDc.Logging;
using SharpDc.Managers;

namespace SharpDc.Structs
{
    /// <summary>
    /// Allows to use HYPER sources (high throughput connections 10Gbit +)
    /// </summary>
    public class HyperUploadItem : ProxyUploadItem
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        public static HyperDownloadManager Manager = new HyperDownloadManager();
        
        public HyperUploadItem(ContentItem item, int bufferSize = 1024 * 1024, int uploadDelay = 0) : base(item, bufferSize, uploadDelay)
        {
            
        }

        public async override Task<long> SendChunkAsync(TransferConnection transfer, long filePos, int bytesRequired)
        {
            var startTime = DateTime.UtcNow;

            if (IsSegmentNeededAttached)
            {
                var ea = new UploadItemSegmentEventArgs
                {
                    UploadItem = this,
                    Magnet = Content.Magnet,
                    Position = filePos,
                    Length = (int)bytesRequired
                };

                OnSegmentNeeded(ea);

                if (ea.FromCache)
                {
                    await transfer.SendAsync(ea.Stream).ConfigureAwait(false);
                    Interlocked.Add(ref _uploadedBytes, ea.Length);
                    return bytesRequired;
                }
            }

            long bytesCopied = 0;

            var sw = PerfTimer.StartNew();

            try
            {
                if (UploadDelay != 0)
                {
                    await Task.Delay(UploadDelay);
                }

                using (new PerfLimit(() => $"Slow HYPER request {SystemPath} pos: {filePos} len: {bytesRequired} filelen: {Content.Magnet.Size}", 4000))
                using (var buffer = await Manager.DownloadSegment(SystemPath, filePos, (int)bytesRequired))
                {
                    if (buffer.Object != null)
                    {
                        await transfer.SendAsync(buffer.Object, 0, buffer.Object.Length).ConfigureAwait(false);
                    }
                }
                bytesCopied = bytesRequired;

                Interlocked.Add(ref _uploadedBytes, bytesCopied);
            }
            catch (Exception x)
            {
                Logger.Error("DownloadChunk error: {0}", x.Message);
            }
            
            sw.Stop();

            //HttpHelper.RegisterDownloadTime((int)sw.ElapsedMilliseconds);

            if (IsSegmentDownloadedAttached)
            {
                OnSegmentDownloaded(new UploadItemSegmentEventArgs
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
    }
}