// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Mime;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SharpDc.Logging;
using SharpDc.Structs;

namespace SharpDc.Helpers
{
    public static class HttpHelper
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private static readonly MovingAverage SegmentDownloadTime = new MovingAverage(TimeSpan.FromSeconds(30));
        
        private static readonly SpeedAverage DownloadSpeed = new SpeedAverage(TimeSpan.FromSeconds(10));

        private static MethodInfo httpWebRequestAddRangeHelper = typeof (WebHeaderCollection).GetMethod
            ("AddWithoutValidate", BindingFlags.Instance | BindingFlags.NonPublic);

        public static int HttpSegmentAverageLoadTime
        {
            get { return (int)SegmentDownloadTime.GetAverage(); }
        }

        public static long HttpDownloadSpeed {
            get { return (long)DownloadSpeed.GetSpeed(); }
        }

        public static void RegisterDownloadTime(int ms)
        {
            SegmentDownloadTime.Update(ms);
        }

        /// <summary>Adds a byte range header to the request for a specified range.</summary>
        /// <param name="request">The <see cref="HttpWebRequest"/> to add the range specifier to.</param>
        /// <param name="start">The position at which to start sending data.</param>
        /// <param name="end">The position at which to stop sending data.</param>
        public static void AddRangeTrick(this HttpWebRequest request, long start, long end)
        {
            if (request == null)
                throw new ArgumentNullException("request");
            if (start < 0)
                throw new ArgumentOutOfRangeException("start", "Starting byte cannot be less than 0.");
            if (end < start)
                end = -1;

            string key = "Range";
            string val = string.Format("bytes={0}-{1}", start, end == -1 ? "" : end.ToString());

            httpWebRequestAddRangeHelper.Invoke(request.Headers, new object[] { key, val });
        }

        public static void DownloadChunk(string uri, byte[] buffer, long filePosition, int readLength)
        {
            if (readLength > buffer.Length)
                throw new ArgumentException("Requested to read more than buffer size");

            var sw = PerfTimer.StartNew();

            var req = (HttpWebRequest)WebRequest.Create(uri);
            req.ReadWriteTimeout = 4000;
            req.AddRangeTrick(filePosition, filePosition + readLength - 1);
            req.Proxy = null;
            req.KeepAlive = true;
            req.Timeout = 4000;
            req.ServicePoint.ConnectionLimit = HttpUploadItem.Manager.ConnectionsPerServer;

            using (var response = req.GetResponse())
            using (var stream = response.GetResponseStream())
            {
                stream.ReadTimeout = 4000;

                int read = 0;
                while (read < readLength)
                {
                    read += stream.Read(buffer, read, Math.Min(readLength - read, 64 * 1024));
                }
            }

            sw.Stop();
            SegmentDownloadTime.Update((int)sw.ElapsedMilliseconds);
            DownloadSpeed.Update(readLength);
        }

        public static async Task<Stream> GetHttpChunkAsync(string uri, long filePosition, long readLength)
        {
            var req = (HttpWebRequest)WebRequest.Create(uri);
            req.ReadWriteTimeout = 4000;
            req.AddRangeTrick(filePosition, filePosition + readLength - 1);
            req.Proxy = null;
            req.KeepAlive = true;
            req.Timeout = 4000;
            req.ServicePoint.ConnectionLimit = HttpUploadItem.Manager.ConnectionsPerServer;
            var response = await req.GetResponseAsync();
            return response.GetResponseStream();
        }

        public static long GetFileSize(string uri)
        {
            var request = WebRequest.Create(new Uri(uri));
            request.Method = "HEAD";
            request.Timeout = 4000;
            try
            {
                using (var response = request.GetResponse())
                {
                    return response.ContentLength;
                }
            }
            catch (Exception)
            {
                return -1;
            }
        }

        public static long GetFileSize(string uri, out Exception exception)
        {
            exception = null;
            var request = WebRequest.Create(new Uri(uri));
            request.Method = "HEAD";
            request.Timeout = 4000;
            try
            {
                using (var response = request.GetResponse())
                {
                    return response.ContentLength;
                }
            }
            catch (Exception x)
            {
                exception = x;
                return -1;
            }
        }

        public static void GetFileNameAndSize(string uri, out string fileName, out long size, out Exception exception)
        {
            exception = null;
            size = -1;
            fileName = null;
            
            var request = (HttpWebRequest)WebRequest.Create(new Uri(uri));
            request.Method = "HEAD";
            request.Timeout = 4000;
            try
            {
                using (var response = request.GetResponse())
                {
                    var headerContentDisposition = response.Headers["content-disposition"];
                    if (headerContentDisposition != null)
                    {
                        fileName = new ContentDisposition(headerContentDisposition).FileName;
                    }
                    size = response.ContentLength;
                }
            }
            catch (Exception x)
            {
                exception = x;
            }
        }
        
        public static bool FileExists(string uri)
        {
            return GetFileSize(uri) > 0;
        }
    }
}