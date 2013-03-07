using System;
using System.Net;

namespace SharpDc.Helpers
{
    public class HttpHelper
    {
        public static void DownloadChunk(string uri, byte[] buffer, long filePosition, int readLength)
        {
            if (readLength > buffer.Length)
                throw new ArgumentException("Requested to read more than buffer size");

            var req = (HttpWebRequest)WebRequest.Create(uri);
            req.AddRange(filePosition, filePosition + readLength - 1);
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
                    read += stream.Read(buffer, read, readLength - read);
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
