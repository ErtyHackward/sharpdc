//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SharpDc.Messages;

namespace SharpDc.Connections
{
    public class UdpConnection
    {
        private UdpClient _client;
        private IPEndPoint _endPoint;
        
        public event EventHandler<SearchResultEventArgs> SearchResult;

        private void OnSearchResult(SearchResultEventArgs e)
        {
            var handler = SearchResult;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<IncomingUdpMessageEventArgs> IncomingMessage;

        private void OnIncomingMessage(IncomingUdpMessageEventArgs e)
        {
            var handler = IncomingMessage;
            if (handler != null) handler(this, e);
        }

        public UdpConnection(int listenPort)
        {
            _client = new UdpClient(listenPort);
            _endPoint = new IPEndPoint(IPAddress.Any, listenPort);
            _client.BeginReceive(OnReceived, null);
            Port = listenPort;
        }

        private void OnReceived(IAsyncResult result)
        {
            try
            {
                var ea = new IncomingUdpMessageEventArgs();
                ea.Message = Encoding.Default.GetString(_client.EndReceive(result, ref _endPoint));
                OnIncomingMessage(ea);

                if (ea.Message.StartsWith("$SR"))
                {
                    var args = new SearchResultEventArgs { Message = SRMessage.Parse(ea.Message) };
                    OnSearchResult(args);
                }

            }
            catch (Exception x)
            {

            }
            finally
            {
                if (_client != null)
                    _client.BeginReceive(OnReceived, null);
            }
        }

        public void Dispose()
        {
            var cl = _client;
            _client = null;
            cl.Close();
        }

        public int Port { get; private set; }

        public void SendMessage(string res, string address)
        {
            var bytes = Encoding.UTF8.GetBytes(res);
            _client.Send(bytes, bytes.Length, Utils.CreateIPEndPoint(address));
        }
    }

    public class SearchResultEventArgs : EventArgs
    {
        public SRMessage Message;
    }

    public class IncomingUdpMessageEventArgs : EventArgs
    {
        public string Message { get; set; }
    }
}
