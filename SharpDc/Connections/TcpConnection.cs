//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using SharpDc.Events;
using SharpDc.Logging;
using SharpDc.Structs;

namespace SharpDc.Connections
{
    /// <summary>
    /// Base class for session based connections on TCP stack
    /// </summary>
    public abstract class TcpConnection : IDisposable
    {
        private const int SendTimeout = 4000;

        private static readonly ILogger Logger = LogManager.GetLogger();

        internal static int DefaultConnectionBufferSize = 1024 * 64;

        private Socket _socket;
        protected IPEndPoint RemoteEndPoint;
        private ConnectionStatus _connectionStatus;
        private readonly Queue<string> _delayedMessages = new Queue<string>();
        private DateTime _lastUpdate = DateTime.Now;
        private bool _closingSocket;
        private byte[] _connectionBuffer;

        private readonly object _sendLock = new object();
        
        private readonly Queue<double> _perfomance = new Queue<double>();
        
        public double SendAverageTime
        {
            get {
                lock (_perfomance)
                {
                    if (_perfomance.Any())
                        return _perfomance.Average();
                    return -1;
                }
            }
        }

        public Socket Socket
        {
            get { return _socket; }
        }

        /// <summary>
        /// Gets current connection status
        /// </summary>
        public ConnectionStatus ConnectionStatus
        {
            get { return _connectionStatus; }
        }

        /// <summary>
        /// Gets last event time (send or receive)
        /// </summary>
        public DateTime LastEventTime { get { return _lastUpdate; } }

        public IPEndPoint LocalAddress { get; set; }

        public IPEndPoint RemoteAddress { get { return RemoteEndPoint; } }

        private readonly SpeedAverage _uploadSpeed = new SpeedAverage(10);
        private readonly SpeedAverage _downloadSpeed = new SpeedAverage(10);

        public SpeedAverage UploadSpeed
        {
            get { return _uploadSpeed; }
        }
        
        public SpeedAverage DownloadSpeed
        {
            get { return _downloadSpeed; }
        }

        /// <summary>
        /// Gets or sets current connection buffer size
        /// </summary>
        public int ConnectionBufferSize { get; set; }

        #region Events

        /// <summary>
        /// Occurs when connection status changed
        /// </summary>
        public event EventHandler<ConnectionStatusEventArgs> ConnectionStatusChanged;

        public void OnConnectionStatusChanged(ConnectionStatusEventArgs e)
        {
            var handler = ConnectionStatusChanged;
            if (handler != null) handler(this, e);
        }

        #endregion

        protected TcpConnection(string address) : this(ParseAddress(address)) { }

        protected TcpConnection(IPEndPoint remoteEndPoint)
        {
            ConnectionBufferSize = DefaultConnectionBufferSize;
            RemoteEndPoint = remoteEndPoint;
            _connectionStatus = ConnectionStatus.Disconnected;
        }

        protected TcpConnection(Socket socket)
        {
            if (socket == null) 
                throw new ArgumentNullException("socket");

            ConnectionBufferSize = DefaultConnectionBufferSize;

            _socket = socket;
            _socket.SendTimeout = SendTimeout;
            _connectionStatus = _socket.Connected ? ConnectionStatus.Connected : ConnectionStatus.Disconnected;
            try
            {
                RemoteEndPoint = socket.RemoteEndPoint as IPEndPoint;
            }
            catch (Exception x)
            {
                Logger.Error("When trying to get the remote address: " + x.Message);
            }

            if (_connectionStatus == ConnectionStatus.Disconnected)
            {
                Logger.Error("Added socket is disconnected!");
            }
        }

        public virtual void Dispose()
        {
            _connectionBuffer = null;
            CloseSocket();
        }

        private void CloseSocket()
        {
            lock (_sendLock)
            {
                if (_socket != null)
                {
                    _socket.Close();
                    _socket = null;   
                }
            }
            SetConnectionStatus(ConnectionStatus.Disconnected);
        }

        public void DisconnectAsync()
        {
            if (!_closingSocket)
            {
                _closingSocket = true;
                new ThreadStart(CloseSocket).BeginInvoke(null, null);
            }
        }

        public void ListenAsync()
        {
            _closingSocket = false;
            new ThreadStart(SocketReadThread).BeginInvoke(null, null);
        }

        public void ConnectAsync()
        {
            try
            {
                lock (_sendLock)
                {
                    if (_socket == null)
                    {
                        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        _socket.SendTimeout = SendTimeout;
                    }
                    _closingSocket = false;
                }

                if (LocalAddress != null)
                {
                    _socket.Bind(LocalAddress);
                }

                // take a windows thread pool thread and run socket in it
                new ThreadStart(SocketReadThread).BeginInvoke(null, null);
            }
            catch (Exception x)
            {
                SetConnectionStatus(ConnectionStatus.Disconnected, x);
            }

        }

        protected virtual void SendFirstMessages()
        {
        }


        private void SocketReadThread()
        {
            try
            {
                if (!_socket.Connected)
                {
                    SetConnectionStatus(ConnectionStatus.Connecting);

                    _lastUpdate = DateTime.Now;
                    _socket.Connect(RemoteEndPoint);

                    SetConnectionStatus(ConnectionStatus.Connected);
                }

                SendFirstMessages();
                
                if (_connectionBuffer == null || _connectionBuffer.Length != ConnectionBufferSize)
                    _connectionBuffer = new byte[ConnectionBufferSize];

                int bytesReceived;

                while (_connectionStatus == ConnectionStatus.Connected && (bytesReceived = _socket.Receive(_connectionBuffer)) != 0)
                {
                    _downloadSpeed.Update(bytesReceived);
                    _lastUpdate = DateTime.Now;
                    ParseRaw(_connectionBuffer, bytesReceived);
                }

                SetConnectionStatus(ConnectionStatus.Disconnected);
            }
            catch (Exception x)
            {
                SetConnectionStatus(ConnectionStatus.Disconnected, x);
            }

        }

        protected abstract void ParseRaw(byte[] buffer, int length);

        protected void Send(byte[] buffer, int offset, int length)
        {
            try
            {
                _lastUpdate = DateTime.Now;
                lock (_sendLock)
                {
                    if (_socket != null)
                        _socket.Send(buffer, offset, length, SocketFlags.None);
                    else
                    {
                        SetConnectionStatus(ConnectionStatus.Disconnected);
                    }
                }
                _uploadSpeed.Update(length);
            }
            catch (Exception x)
            {
                SetConnectionStatus(ConnectionStatus.Disconnected, x);
            }
        }

        public void SendRaw(byte[] buffer, int length)
        {
            begin:
            while (_sendThreadActive)
                Thread.Yield();
            
            Monitor.Enter(_delayedMessages);

            if (_delayedMessages.Count > 0)
            {
                Monitor.Exit(_delayedMessages);
                goto begin;
            }

            var sw = Stopwatch.StartNew();
            Send(buffer, 0, length);
            sw.Stop();

            lock (_perfomance)
            {
                _perfomance.Enqueue(sw.Elapsed.TotalMilliseconds);
                if (_perfomance.Count > 5)
                    _perfomance.Dequeue();
            }

            Monitor.Exit(_delayedMessages);
        }

        public void SendAsync(string msg)
        {
            lock (_delayedMessages)
                _delayedMessages.Enqueue(msg);
            if (!_sendThreadActive)
            {
                _sendThreadActive = true;
                new ThreadStart(SendDelayed).BeginInvoke(null, null);
            }
        }

        public void SendAsync(params string[] msgs)
        {
            lock (_delayedMessages)
            {
                for (var i = 0; i < msgs.Length; i++)
                {
                    _delayedMessages.Enqueue(msgs[i]);
                }
            }
            if (!_sendThreadActive)
            {
                _sendThreadActive = true;
                new ThreadStart(SendDelayed).BeginInvoke(null, null);
            }
        }

        private volatile bool _sendThreadActive;

        private void SendDelayed()
        {
        start:

            _sendThreadActive = true;
            while (_delayedMessages.Count > 0)
            {
                string msg;
                lock (_delayedMessages)
                {
                    msg = _delayedMessages.Dequeue();
                }
                var bytes = Encoding.Default.GetBytes(msg);

                Send(bytes, 0, bytes.Length);
            }
                
            // allow next thread to start
            _sendThreadActive = false;

            // we need to try get messages again because we may lose next thread start when setting _sendThreadActive = false
            if (_delayedMessages.Count > 0)
            {
                goto start;
            }
            
        }

        /// <summary>
        /// Updates current connection status
        /// </summary>
        /// <param name="status"></param>
        /// <param name="x"></param>
        protected void SetConnectionStatus(ConnectionStatus status, Exception x = null)
        {
            var old = _connectionStatus;
            if (_connectionStatus == status)
                return;

            var ea = new ConnectionStatusEventArgs {
                Status = status, 
                Previous = old, 
                Exception = x
            };

            _connectionStatus = status;

            OnConnectionStatusChanged(ea);

            if (_connectionStatus == ConnectionStatus.Disconnected)
                Dispose();

        }

        /// <summary>
        /// Tries to parse a string containging address
        /// </summary>
        /// <param name="address">String like dchub://hub.host.com:411</param>
        /// <param name="defaultPort"></param>
        /// <returns></returns>
        public static IPEndPoint ParseAddress(string address, int defaultPort = 411)
        {
            if (address.StartsWith("dchub://", StringComparison.CurrentCultureIgnoreCase))
            {
                address = address.Remove(0, 8);
            }
            if (address.StartsWith("adc://", StringComparison.CurrentCultureIgnoreCase))
            {
                address = address.Remove(0, 6);
            }
            int port = defaultPort;
            int i = address.IndexOf(':');
            if (i != -1)
            {
                port = int.Parse(address.Substring(i + 1));
                address = address.Remove(i, address.Length - i);
            }
            IPAddress addy;
            try
            {
                addy = IPAddress.Parse(address);
            }
            catch (Exception)
            {
                addy = Dns.GetHostEntry(address).AddressList[0];
            }
            return new IPEndPoint(addy, port);
        }
    }
}
