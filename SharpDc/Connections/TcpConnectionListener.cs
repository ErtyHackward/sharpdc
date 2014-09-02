// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net;
using System;
using System.Threading;
using SharpDc.Logging;

namespace SharpDc.Connections
{
    /// <summary>
    /// Allows to accept tcp connections
    /// </summary>
    public class TcpConnectionListener : IDisposable
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly Socket _listenSocket;
        private readonly IPEndPoint _ep;
        private readonly int _backlog = 10;
        private readonly AsyncCallback _incomingConnectionCallback;

        /// <summary>
        /// Occurs when we receive a new connection, if event is not handled the socket will be closed
        /// </summary>
        public event EventHandler<IncomingConnectionEventArgs> IncomingConnection;

        public void OnIncomingConnection(IncomingConnectionEventArgs e)
        {
            var handler = IncomingConnection;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// Occurs when listener meets some exception on other threads
        /// </summary>
        public event EventHandler<ExceptionEventArgs> Exception;

        public void OnException(ExceptionEventArgs e)
        {
            var handler = Exception;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// Creates new TcpConnectionListener on port specified
        /// </summary>
        /// <param name="port">port to listen on</param>
        /// <param name="backlog">amount of pending connections queue of the socket</param>
        public TcpConnectionListener(int port, int backlog = 10)
        {
            _incomingConnectionCallback = OnIncomingConnection;

            if (backlog <= 0)
            {
                Logger.Warn("Invalid backlog value {0}, using 10 instead", backlog);
                backlog = 10;
            }

            _backlog = backlog;
            _listenSocket = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp);
            _ep = new IPEndPoint(IPAddress.Any, port);
            _listenSocket.Bind(_ep);
            Port = port;

            Logger.Info("{0} tcp port binded", port);
        }

        /// <summary>
        /// Starts listening for incoming connections
        /// </summary>
        public void Listen()
        {
            _listenSocket.Listen(_backlog);
            while (true)
            {
                try
                {
                    var socket = _listenSocket.Accept();
                    var ea = new IncomingConnectionEventArgs { Socket = socket };

                    using (new PerfLimit("Tcp connection listener connection handle"))
                        OnIncomingConnection(ea);

                    if (!ea.Handled)
                    {
                        using (new PerfLimit("Tcp connection listener close"))
                            socket.Close();
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException e)
                {
                    OnException(new ExceptionEventArgs { Exception = e });
                }
            }
        }

        public void ListenAsync()
        {
            _listenSocket.Listen(_backlog);

            for (int i = 0; i < 3; i++)
            {
                AcceptNext(_listenSocket);    
            }
        }

        protected void AcceptNext(Socket sc)
        {
            try
            {
                sc.BeginAccept(_incomingConnectionCallback, sc);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException e)
            {
                OnException(new ExceptionEventArgs { Exception = e });
            }
        }

        protected void OnIncomingConnection(IAsyncResult ar)
        {
            try
            {
                var s = (Socket)ar.AsyncState;
                var incomingSocket = s.EndAccept(ar);
                var ea = new IncomingConnectionEventArgs { Socket = incomingSocket };

                OnIncomingConnection(ea);

                if (!ea.Handled)
                {
                    incomingSocket.BeginDisconnect(false, null, null);
                }

                // continue accepting
                AcceptNext(s);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException e)
            {
                OnException(new ExceptionEventArgs { Exception = e });

                // try to continue listening
                try
                {
                    var s = (Socket)ar.AsyncState;
                    AcceptNext(s);
                }
                catch (Exception x)
                {
                    OnException(new ExceptionEventArgs { Exception = x });
                }
            }
        }

        public static bool IsPortFree(int port)
        {
            Socket socket = null;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork,
                                    SocketType.Stream,
                                    ProtocolType.Tcp);
                var ep = new IPEndPoint(IPAddress.Any, port);
                socket.Bind(ep);

                var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
                var tcpConnInfoArray = ipGlobalProperties.GetActiveTcpConnections();

                foreach (var tcpi in tcpConnInfoArray)
                {
                    if (tcpi.LocalEndPoint.Port == port)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (socket != null)
                    socket.Dispose();
            }
        }

        /// <summary>
        /// Release all acquired resources 
        /// </summary>
        public void Dispose()
        {
            _listenSocket.Close();
        }

        public int Port { get; set; }
    }

    public class IncomingConnectionEventArgs : BaseEventArgs
    {
        public Socket Socket { get; set; }
    }

    public class BaseEventArgs : EventArgs
    {
        public bool Handled { get; set; }
    }

    public class ExceptionEventArgs : EventArgs
    {
        public Exception Exception { get; set; }
    }
}