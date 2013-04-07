//  -------------------------------------------------------------
//  LiveDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
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

        protected struct SendTask
        {
            public byte[] Buffer;
            public int Offset;
            public int Length;
            public ManualResetEventSlim Sync;
        }

        private Socket _socket;
        protected IPEndPoint RemoteEndPoint;
        private ConnectionStatus _connectionStatus;
        private readonly Queue<SendTask> _delayedMessages = new Queue<SendTask>();
        private DateTime _lastUpdate = DateTime.Now;
        private bool _closingSocket;
        private byte[] _connectionBuffer;

        private readonly object _sendLock = new object();
        private readonly object _threadLock = new object();
        
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

        private readonly SpeedAverage _uploadSpeed = new SpeedAverage();
        private readonly SpeedAverage _downloadSpeed = new SpeedAverage();

        /// <summary>
        /// Gets an object to obtain upload speed
        /// </summary>
        public SpeedAverage UploadSpeed
        {
            get { return _uploadSpeed; }
        }
        
        /// <summary>
        /// Gets an object to obtain download speed
        /// </summary>
        public SpeedAverage DownloadSpeed
        {
            get { return _downloadSpeed; }
        }
        
        /// <summary>
        /// Allows to control global tcpConnection upload speed limit
        /// </summary>
        public static SpeedLimiter UploadSpeedLimitGlobal { get; private set; }

        /// <summary>
        /// Allows to control global tcpConnection download speed limit
        /// </summary>
        public static SpeedLimiter DownloadSpeedLimitGlobal { get; private set; }

        /// <summary>
        /// Allows to control this tcpConnection upload speed limit
        /// </summary>
        public SpeedLimiter UploadSpeedLimit { get; private set; }

        /// <summary>
        /// Allows to control this tcpConnection download speed limit
        /// </summary>
        public SpeedLimiter DownloadSpeedLimit { get; private set; }

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

        static TcpConnection()
        {
            UploadSpeedLimitGlobal = new SpeedLimiter();
            DownloadSpeedLimitGlobal = new SpeedLimiter();
        }

        private void Initialize()
        {
            ConnectionBufferSize = DefaultConnectionBufferSize;

            UploadSpeedLimit = new SpeedLimiter();
            DownloadSpeedLimit = new SpeedLimiter();
        }

        protected TcpConnection(string address) : this(ParseAddress(address)) { }

        protected TcpConnection()
        {
            Initialize();
        }

        protected TcpConnection(IPEndPoint remoteEndPoint) : this()
        {
            RemoteEndPoint = remoteEndPoint;
            _connectionStatus = ConnectionStatus.Disconnected;
        }

        protected TcpConnection(Socket socket) : this()
        {
            if (socket == null) 
                throw new ArgumentNullException("socket");

            _socket = socket;
            _socket.SendTimeout = SendTimeout;
            LocalAddress = (IPEndPoint)_socket.LocalEndPoint;
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
            if (_connectionStatus == Events.ConnectionStatus.Disconnected)
                return;

            if (!_closingSocket)
            {
                _closingSocket = true;
                DcEngine.ThreadPool.QueueWorkItem(CloseSocket);
            }
        }

        public void ListenAsync()
        {
            _closingSocket = false;
            DcEngine.ThreadPool.QueueWorkItem(SocketReadThread);
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

                // take a thread pool thread and run socket in it
                DcEngine.ThreadPool.QueueWorkItem(SocketReadThread);
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

                    LocalAddress = (IPEndPoint)_socket.LocalEndPoint;

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
                    DownloadSpeedLimit.Update(bytesReceived);
                    DownloadSpeedLimitGlobal.Update(bytesReceived);
                }

                SetConnectionStatus(ConnectionStatus.Disconnected);
            }
            catch (Exception x)
            {
                SetConnectionStatus(ConnectionStatus.Disconnected, x);
            }

        }

        protected abstract void ParseRaw(byte[] buffer, int length);

        protected bool Send(SendTask task)
        {
            try
            {
                if (ConnectionStatus != ConnectionStatus.Connected)
                    return false;

                _lastUpdate = DateTime.Now;
                lock (_sendLock)
                {
                    if (_socket != null)
                    {
                        int sent = 0;
                        int needToSend = task.Length;
                        int curOffset = task.Offset;

                        while (sent < task.Length)
                        {
                            var s = _socket.Send(task.Buffer, curOffset, needToSend, SocketFlags.None);
                            sent += s;
                            curOffset += s;
                            needToSend -= s;
                        }

                        if (task.Sync != null)
                            task.Sync.Set();

                        _uploadSpeed.Update(task.Length);
                        UploadSpeedLimit.Update(task.Length);
                        UploadSpeedLimitGlobal.Update(task.Length);
                        return true;
                    }
                }
                SetConnectionStatus(ConnectionStatus.Disconnected);
            }
            catch (Exception x)
            {
                SetConnectionStatus(ConnectionStatus.Disconnected, x);
            }
            return false;
        }

        /// <summary>
        /// Creates sent task and wait until it is finished
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public int Send(byte[] buffer, int offset, int length)
        {

            using (var sync = new ManualResetEventSlim())
            {

                lock (_delayedMessages)
                {
                    _delayedMessages.Enqueue(new SendTask
                                                 {
                                                     Buffer = buffer,
                                                     Offset = offset,
                                                     Length = length,
                                                     Sync = sync
                                                 });
                }

                lock (_threadLock)
                {
                    if (!_sendThreadActive)
                    {
                        _sendThreadActive = true;
                        DcEngine.ThreadPool.QueueWorkItem(SendDelayed);
                    }
                }

                sync.Wait(SendTimeout);
            }

            return ConnectionStatus == Events.ConnectionStatus.Connected ? length : 0;
        }

        public void SendAsync(string msg)
        {
            var bytes = Encoding.Default.GetBytes(msg);

            lock (_delayedMessages)
                _delayedMessages.Enqueue(new SendTask { Buffer = bytes, Length = bytes.Length });

            lock (_threadLock)
            {
                if (!_sendThreadActive)
                {
                    _sendThreadActive = true;
                    DcEngine.ThreadPool.QueueWorkItem(SendDelayed);
                }
            }
        }

        public void SendAsync(params string[] msgs)
        {
            lock (_delayedMessages)
            {
                for (var i = 0; i < msgs.Length; i++)
                {
                    var bytes = Encoding.Default.GetBytes(msgs[i]);
                    _delayedMessages.Enqueue(new SendTask { Buffer = bytes, Length = bytes.Length });
                }
            }

            lock (_threadLock)
            {
                if (!_sendThreadActive)
                {
                    _sendThreadActive = true;
                    DcEngine.ThreadPool.QueueWorkItem(SendDelayed);
                }
            }
        }

        private volatile bool _sendThreadActive;

        private void SendDelayed()
        {
            while (true)
            {
                lock (_delayedMessages)
                {
                    if (_delayedMessages.Count == 0)
                    {
                        _sendThreadActive = false;
                        return;
                    }
                }
                
                SendTask task;
                lock (_delayedMessages)
                {
                    task = _delayedMessages.Dequeue();
                }

                if (!Send(task))
                {
                    lock (_delayedMessages)
                    {
                        foreach (var t in _delayedMessages)
                        {
                            if (t.Sync != null)
                                t.Sync.Set();
                        }
                        _delayedMessages.Clear();
                    }
                    break;
                }
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
                using (new PerfLimit("Dns resolve of " + address))
                    addy = Dns.GetHostEntry(address).AddressList[0];
            }
            return new IPEndPoint(addy, port);
        }
    }
}
