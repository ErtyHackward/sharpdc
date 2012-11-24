//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace SharpDc.WebServer
{
    /// <summary>
    /// WebServer response
    /// </summary>
    public class Response : Stream
    {
        protected readonly NetworkStream stream;
        protected bool isHeaderSent;
        protected readonly object synObject = new object();
        protected bool closed;
        protected static readonly Encoding encoding = Encoding.GetEncoding(1251);

        public int StatusCode { get; set; }
        public string StatusDescription { get; set; }
        public Dictionary<string, string> Headers { get; set; }
        public long ContentLength64 { get; set; }

        public Response(NetworkStream stream)
        {
            this.stream = stream;

            Headers = new Dictionary<string, string>();
            StatusCode = 404;
            StatusDescription = "Not Found";
            ContentLength64 = -1;
        }

        protected void WriteHttpHeader()
        {
            if (ContentLength64 >= 0)
            {
                if (!Headers.ContainsKey("Content-Length"))
                    Headers.Add("Content-Length", ContentLength64.ToString());
            }
            var sb = new StringBuilder();
            sb.Append(string.Format("HTTP/1.1 {0} {1}\r\n", StatusCode, StatusDescription));
            foreach (var pair in Headers)
            {
                sb.AppendFormat("{0}: {1}\r\n", pair.Key, pair.Value);
            }
            sb.AppendLine();
            byte[] result = encoding.GetBytes(sb.ToString());
            stream.Write(result, 0, result.Length);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!isHeaderSent)
            {
                WriteHttpHeader();
                isHeaderSent = true;
            }

            stream.Write(buffer, offset, count);
        }

        public override void Close()
        {
            try
            {
                lock (synObject)
                {
                    if (closed)
                        return;

                    if (!isHeaderSent)
                    {
                        WriteHttpHeader();
                        isHeaderSent = true;
                    }

                    closed = true;
                }
                stream.Close();
            }
            catch (Exception)
            {

            }

        }

        public override void Flush()
        {
            stream.Flush();
        }

        public override bool CanTimeout
        {
            get { return stream.CanTimeout; }
        }

        public override bool CanWrite
        {
            get { return stream.CanWrite; }
        }

        public override bool CanRead
        {
            get { return false; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Length
        {
            get { throw new NotSupportedException(); }
        }

        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            throw new NotSupportedException();
        }

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            throw new Exception("The method or operation is not implemented.");
        }

    }
}