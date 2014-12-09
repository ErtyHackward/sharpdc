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
        private static readonly ILogger Logger = LogManager.GetLogger();
        
        public static event EventHandler<UploadItemSegmentEventArgs> SegmentDownloaded;

        protected static void OnSegmentDownloaded(UploadItemSegmentEventArgs e)
        {
            var handler = SegmentDownloaded;
            if (handler != null) handler(null, e);
        }

        public static event EventHandler<UploadItemSegmentEventArgs> SegmentNeeded;

        protected static void OnSegmentNeeded(UploadItemSegmentEventArgs e)
        {
            var handler = SegmentNeeded;
            if (handler != null) handler(null, e);
        }

        protected bool IsSegmentNeededAttached
        {
            get { return SegmentNeeded != null; }
        }

        protected bool IsSegmentDownloadedAttached
        {
            get { return SegmentDownloaded != null; }
        }
        
        public ProxyUploadItem(ContentItem item, int bufferSize) : base(item, bufferSize)
        {

        }

    }
}