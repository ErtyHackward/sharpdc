using System;
using System.Net;
using System.Reflection;

namespace SharpDc.Helpers
{
    public static class HttpHelper
    {
        static MethodInfo httpWebRequestAddRangeHelper = typeof(WebHeaderCollection).GetMethod
                                        ("AddWithoutValidate", BindingFlags.Instance | BindingFlags.NonPublic);

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

            var req = (HttpWebRequest)WebRequest.Create(uri);
            req.ReadWriteTimeout = 4000;
            req.AddRangeTrick(filePosition, filePosition + readLength - 1);
            req.Proxy = null;
            req.KeepAlive = true;
            req.Timeout = 4000;

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
        }

        public static long GetFileSize(string uri)
        {
            var request = WebRequest.Create(new Uri(uri));
            request.Method = "HEAD";
            try
            {
                using (var response = request.GetResponse())
                {
                    return response.ContentLength;
                }
            }
            catch (Exception x)
            {
                return -1;
            }
        }

        public static bool FileExists(string uri)
        {
            return GetFileSize(uri) > 0;
        }
    }
}
