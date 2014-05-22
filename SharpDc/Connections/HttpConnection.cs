// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SharpDc.Connections
{
    public class HttpConnection : TcpConnection
    {
        private byte[] _buffer;
        private byte[] _request;
        private bool _headerReceived;
        private int _writePos;
        private int _headerEnd;
        private int _reqPos;
        private int _reqLength;

        public string Server { get; set; }

        public Dictionary<string, string> Headers { get; set; }

        public int ResponseCode { get; set; }

        public event EventHandler RequestComplete;

        protected virtual void OnRequestComplete()
        {
            EventHandler handler = RequestComplete;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public event EventHandler<HttpDataEventArgs> DataRecieved;

        protected virtual void OnDataRecieved(HttpDataEventArgs e)
        {
            var handler = DataRecieved;
            if (handler != null) handler(this, e);
        }

        public HttpConnection()
        {
            Headers = new Dictionary<string, string>();
            _buffer = new byte[64 * 1024];
            DontUseAsync = true;
            HighPriorityReadThread = true;
        }

        public void SetRange(long start, long end)
        {
            if (Headers.ContainsKey("Range"))
                Headers.Remove("Range");

            _reqLength = (int)(end - start) + 1;

            Headers.Add("Range", string.Format("bytes={0}-{1}", start, end));
        }

        protected override void SendFirstMessages()
        {
            SendNow(_request, 0, _request.Length);
        }

        protected override void ParseRaw(byte[] buffer, int length)
        {
            if (!_headerReceived)
            {
                Buffer.BlockCopy(buffer, 0, _buffer, _writePos, length);
                _writePos += length;

                for (int i = 0; i < _writePos - 3; i++)
                {
                    if (_buffer[i] == '\r' && _buffer[i + 1] == '\n' && _buffer[i + 2] == '\r' &&
                        _buffer[i + 3] == '\n')
                    {
                        _headerReceived = true;
                        _headerEnd = i + 4;
                        break;
                    }
                }

                if (_headerReceived)
                {
                    // parse header
                    var responseHeader = Encoding.UTF8.GetString(_buffer, 0, _headerEnd);

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
                    return;

                if (_writePos - _headerEnd > 0)
                {
                    OnDataRecieved(new HttpDataEventArgs
                                       {
                                           Buffer = buffer,
                                           BufferOffset = _headerEnd,
                                           ResponseOffset = _reqPos,
                                           Length = _writePos - _headerEnd
                                       });
                    _reqPos += _writePos - _headerEnd;

                    if (_reqPos == _reqLength)
                    {
                        OnRequestComplete();
                    }
                }
                return;
            }

            OnDataRecieved(new HttpDataEventArgs
                               {
                                   Buffer = buffer,
                                   BufferOffset = 0,
                                   ResponseOffset = _reqPos,
                                   Length = length
                               });
            _reqPos += length;

            if (_reqPos == _reqLength)
            {
                OnRequestComplete();
            }
        }

        public void Request(string uri)
        {
            _reqPos = 0;
            _headerEnd = 0;
            _writePos = 0;
            _headerReceived = false;

            var parsed = new HttpUrl(uri);

            Server = parsed.Server;

            PrepareHeaders();

            var sb = new StringBuilder();

            sb.AppendFormat("GET {0} HTTP/1.1\r\n", parsed.Request);
            sb.AppendFormat("Host: {0}\r\n", parsed.Server);

            foreach (var header in Headers)
            {
                sb.AppendFormat("{0}: {1}\r\n", header.Key, header.Value);
            }

            sb.AppendLine();
            _request = Encoding.UTF8.GetBytes(sb.ToString());

            if (ConnectionStatus == Events.ConnectionStatus.Connected)
                SendFirstMessages();
            else
            {
                RemoteEndPoint = ParseAddress(parsed.Server + ":" + parsed.Port);
                ConnectAsync();
            }
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