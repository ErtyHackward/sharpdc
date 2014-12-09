using System;
using System.IO;

namespace SharpDc.Structs
{
    public class UploadItemSegmentEventArgs : EventArgs
    {
        public ProxyUploadItem UploadItem { get; set; }
        public Stream Stream { get; set; }
        public Magnet Magnet { get; set; }
        public long Position { get; set; }
        public long Length { get; set; }
        public bool FromCache { get; set; }
        public DateTime RequestedAt { get; set; }
        public TimeSpan DownloadingTime { get; set; }
    }
}