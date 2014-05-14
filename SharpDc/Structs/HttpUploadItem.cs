// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.IO;
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
        
        public HttpUploadItem(ContentItem item) : base(item, 0)
        {
            
        }

        public override void RequestChunkAsync(long start, int length, Action<Stream, Exception> callback)
        {
            HttpHelper.DownloadChunkAsync(SystemPath, start, length, callback);
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