// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SharpDc.WebServer
{
    /// <summary>
    /// Allows to handle HTTP requests
    /// </summary>
    public class WebServer : IDisposable
    {
        private TcpListener _listener;
        private readonly AutoResetEvent _autoReset = new AutoResetEvent(false);

        public int Port { get; set; }

        public bool Alive
        {
            get { return _listener != null; }
        }

        public string LocalIPAddress
        {
            get
            {
                IPHostEntry host;
                string localIP = "";
                host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (IPAddress ip in host.AddressList)
                {
                    if (ip.AddressFamily.ToString() == "InterNetwork" &&
                        (ip.ToString().StartsWith("10") || ip.ToString().StartsWith("46.180") ||
                         ip.ToString().StartsWith("95")))
                    {
                        localIP = ip.ToString();
                        break;
                    }
                }

                if (string.IsNullOrEmpty(localIP))
                {
                    foreach (IPAddress ip in host.AddressList)
                    {
                        if (ip.AddressFamily.ToString() == "InterNetwork" && !ip.ToString().StartsWith("192.168"))
                        {
                            localIP = ip.ToString();
                            break;
                        }
                    }
                }

                return localIP;
            }
        }

        /// <summary>
        /// Occurs when client is connected
        /// </summary>
        public event EventHandler<WebServerConnectedEventArgs> Connected;

        protected void OnConnected(WebServerConnectedEventArgs e)
        {
            var handler = Connected;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// Occurs when HTTP request is accepted and server should reply. Here Response should be formed
        /// </summary>
        public event EventHandler<WebServerRequestEventArgs> Request;

        protected void OnRequest(WebServerRequestEventArgs e)
        {
            var handler = Request;
            if (handler != null) handler(this, e);
        }

        public WebServer()
        {
            Port = 80;
        }

        /// <summary>
        /// Begins listening HTTP requests
        /// </summary>
        public void Start()
        {
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();
            new Thread(delegate()
                           {
                               Thread.CurrentThread.Name = "WebServer";
                               while (_listener != null)
                               {
                                   _listener.BeginAcceptTcpClient(delegate(IAsyncResult result)
                                                                      {
                                                                          try
                                                                          {
                                                                              var client =
                                                                                  _listener.EndAcceptTcpClient(result);
                                                                              _autoReset.Set();
                                                                              var ea =
                                                                                  new WebServerConnectedEventArgs(client);
                                                                              OnConnected(ea);
                                                                              if (!ea.Handled)
                                                                              {
                                                                                  var ns = client.GetStream();
                                                                                  ns.ReadTimeout = 15 * 1000;
                                                                                  ns.WriteTimeout = 60 * 1000 * 5;
                                                                                  var req = new Request(ns);
                                                                                  var resp = new Response(ns);
                                                                                  var rea =
                                                                                      new WebServerRequestEventArgs(
                                                                                          req, resp);
                                                                                  OnRequest(rea);
                                                                                  resp.Close();
                                                                                  client.Close();
                                                                              }
                                                                          }
                                                                          catch (ObjectDisposedException)
                                                                          {
                                                                          }
                                                                          catch (NullReferenceException)
                                                                          {
                                                                          }
                                                                      }, null);
                                   _autoReset.WaitOne();
                               }
                           }).Start();
        }

        /// <summary>
        /// Stops listening for HTTP requests
        /// </summary>
        public void Dispose()
        {
            if (_listener != null)
            {
                _listener.Stop();
                _listener = null;
                _autoReset.Set();
            }
        }
    }
}