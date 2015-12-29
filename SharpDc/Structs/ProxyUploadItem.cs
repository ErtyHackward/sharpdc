using System;
using SharpDc.Logging;
using SharpDc.Managers;

namespace SharpDc.Structs
{
    /// <summary>
    /// Provides proxy upload possibility like from http or hyper sources
    /// </summary>
    public class ProxyUploadItem : UploadItem
    {
        protected int UploadDelay;
        private static readonly ILogger Logger = LogManager.GetLogger();
        
        public static event EventHandler<UploadItemSegmentEventArgs> SegmentDownloaded;

        protected static void OnSegmentDownloaded(UploadItemSegmentEventArgs e)
        {
            SegmentDownloaded?.Invoke(null, e);
        }

        public static event EventHandler<UploadItemSegmentEventArgs> SegmentNeeded;

        protected static void OnSegmentNeeded(UploadItemSegmentEventArgs e)
        {
            SegmentNeeded?.Invoke(null, e);
        }

        protected bool IsSegmentNeededAttached => SegmentNeeded != null;

        protected bool IsSegmentDownloadedAttached => SegmentDownloaded != null;

        public ProxyUploadItem(ContentItem item, int bufferSize, int uploadDelay) : base(item, bufferSize)
        {
            UploadDelay = uploadDelay;
        }
    }
}