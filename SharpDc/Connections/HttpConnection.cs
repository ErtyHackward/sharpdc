// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using SharpDc.Helpers;

namespace SharpDc.Connections
{
    public class HttpConnection : TcpConnection
    {
        private byte[] _headerBuffer;
        private bool _headerReceived;
        private int _headerBufferWritePos;
        private int _headerEnd;
        private int _reqPos;
        private int _reqLength;

        public string Server { get; set; }

        public Dictionary<string, string> Headers { get; set; }

        public int ResponseCode { get; set; }

        public event EventHandler RequestComplete;

        protected virtual void OnRequestComplete()
        {
            var handler = RequestComplete;
            if (handler != null) handler(this, EventArgs.Empty);
        }
        
        public HttpConnection()
        {
            Headers = new Dictionary<string, string>();
            _headerBuffer = new byte[16 * 1024];
        }

        protected override void PrepareSocket(Socket socket)
        {
            base.PrepareSocket(socket);

            // we will override default buffers to maximize incoming throughput because that is the only usage of this class
            socket.SendBufferSize = 64 * 1024;
            socket.ReceiveBufferSize = 1024 * 1024 + 1024 * 4; // 1MB + 4 KB (for header)
        }

        private void SetRange(long start, long end)
        {
            if (Headers.ContainsKey("Range"))
                Headers.Remove("Range");

            _reqLength = (int)(end - start) + 1;

            Headers.Add("Range", string.Format("bytes={0}-{1}", start.ToString(), end.ToString()));
        }

        public async Task CopyHttpChunkToAsync(TransferConnection transfer, string uri, long filePos, long length)
        {
            _headerBufferWritePos = 0;
            _headerReceived = false;

            var parsed = new HttpUrl(uri);

            Server = parsed.Server;

            PrepareHeaders();
            SetRange(filePos, filePos + length - 1);
            
            var sb = new StringBuilder();

            sb.AppendFormat("GET {0} HTTP/1.1\r\n", parsed.Request);
            sb.AppendFormat("Host: {0}\r\n", parsed.Server);

            foreach (var header in Headers)
            {
                sb.AppendFormat("{0}: {1}\r\n", header.Key, header.Value);
            }

            sb.AppendLine();
            var request = sb.ToString();

            if (ConnectionStatus != Events.ConnectionStatus.Connected)
            {
                RemoteEndPoint = ParseAddress(parsed.Server + ":" + parsed.Port);
            }
            
            await EnsureConnected().ConfigureAwait(false);

            await SendAsync(request).ConfigureAwait(false);

            int bytesReceived = 0;
            
            while ( bytesReceived < _reqLength )
            {
                var awaitable = await ReceiveAsync();
                
                if (awaitable == null)
                    throw new InvalidOperationException("Http operation failed");

                var args = awaitable.m_eventArgs;

                try
                {
                    #region Header parsing
                    if (!_headerReceived)
                    {
                        var bytesToCopy = Math.Min(args.BytesTransferred, _headerBuffer.Length - _headerBufferWritePos);

                        Buffer.BlockCopy(args.Buffer, args.Offset, _headerBuffer, _headerBufferWritePos, bytesToCopy);

                        var argsBufferOffset = _headerBufferWritePos;
                        _headerBufferWritePos += bytesToCopy;
                        

                        for (int i = 0; i < _headerBufferWritePos - 3; i++)
                        {
                            if (_headerBuffer[i] == '\r' && _headerBuffer[i + 1] == '\n' && _headerBuffer[i + 2] == '\r' &&
                                _headerBuffer[i + 3] == '\n')
                            {
                                _headerReceived = true;
                                _headerEnd = i + 4;
                                break;
                            }
                        }

                        if (_headerReceived)
                        {
                            // parse header
                            var responseHeader = Encoding.UTF8.GetString(_headerBuffer, 0, _headerEnd);

                            using (var reader = new StringReader(responseHeader))
                            {
                                var status = reader.ReadLine();
                                // HTTP/1.1 200 OK
                                var spl = status.Split(' ');
                                ResponseCode = int.Parse(spl[1]);

                                string line;
                                while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                                {
                                }
                            }
                        }
                        else
                            // continue receiving the header
                            continue;

                        var dataStart = _headerEnd - argsBufferOffset;

                        if (dataStart < args.BytesTransferred)
                        {
                            // send the rest part of the buffer (first real data)
                            int dataLength = args.BytesTransferred - dataStart;
                            await transfer.SendAsync(args.Buffer, args.Offset + dataStart, dataLength);
                            bytesReceived += dataLength;
                        }
                        
                        continue;
                    }
                    #endregion

                    await transfer.SendAsync(args.Buffer, args.Offset, args.BytesTransferred);
                    bytesReceived += args.BytesTransferred;
                }
                finally
                {
                    ReleaseAwaitable(awaitable);
                }
            }

            OnRequestComplete();
        }
        
        private void PrepareHeaders()
        {
            if (!Headers.ContainsKey("Connection"))
            {
                Headers.Add("Connection", "Keep-Alive");
            }

            if (!Headers.ContainsKey("User-Agent"))
            {
                Headers.Add("User-Agent", "SharpDC");
            }
        }

        protected override void ParseRaw(byte[] buffer, int offset, int length)
        {
            
        }
    }

    public class HttpDataEventArgs : EventArgs
    {
        public byte[] Buffer;
        public int BufferOffset;
        public int ResponseOffset;
        public int Length;
    }

    public struct HttpUrl
    {
        public string Server;
        public string Request;
        public int Port;

        public HttpUrl(string url)
        {
            // http://127.0.0.1:88/1/test.avi

            if (!url.StartsWith("http://"))
                throw new ArgumentException();

            Server = url.Remove(0, 7);

            var ind = Server.IndexOf('/');

            if (ind == -1)
            {
                Request = "/";
            }
            else
            {
                Request = Server.Substring(ind);
                Server = Server.Remove(ind);
            }

            ind = Server.IndexOf(':');

            if (ind == -1)
            {
                Port = 80;
            }
            else
            {
                Port = int.Parse(Server.Substring(ind + 1));
                Server = Server.Remove(ind);
            }
        }
    }
}