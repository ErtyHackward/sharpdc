// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

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
        private static readonly ILogger Logger = LogManager.GetLogger();

        internal static int DefaultConnectionBufferSize = 1024 * 64;
        
        protected struct SendTask
        {
            public byte[] Buffer;
            public int Offset;
            public int Length;
            public ManualResetEvent Sync;
        }

        private Socket _socket;
        protected IPEndPoint RemoteEndPoint;
        private ConnectionStatus _connectionStatus;
        private readonly Queue<SendTask> _delayedMessages = new Queue<SendTask>();
        private long _lastUpdate = Stopwatch.GetTimestamp();
        protected bool _closingSocket;
        private byte[] _connectionBuffer;
        private int _sendThreadActive;
        private int _receiveTimeout;
        private int _sendTimeout = 4000;
        private SendTask _currentTask;

        private readonly object _sendLock = new object();
        private readonly object _threadLock = new object();

        private readonly SpeedAverage _uploadSpeed = new SpeedAverage();
        private readonly SpeedAverage _downloadSpeed = new SpeedAverage();

        private readonly AsyncCallback _receiveCallback;
        private readonly AsyncCallback _sendCallback;
        private readonly ThreadStart _sendDelayedDelegate;

        /// <summary>
        /// If set the separate thread will be used for the read process
        /// </summary>
        public bool DontUseAsync { get; set; }

        /// <summary>
        /// If set the read thread will have high priority
        /// </summary>
        public bool HighPriorityReadThread { get; set; }

        public Socket Socket
        {
            get { return _socket; }
        }
        
        public int ReceiveTimeout
        {
            get { return _receiveTimeout; }
            set { _receiveTimeout = value; }
        }

        public int SendTimeout
        {
            get { return _sendTimeout; }
            set { _sendTimeout = value; }
        }

        /// <summary>
        /// Gets current connection status
        /// </summary>
        public ConnectionStatus ConnectionStatus
        {
            get { return _connectionStatus; }
        }

        /// <summary>
        /// Gets seconds passed from the last event
        /// </summary>
        public int IdleSeconds {
            get { return (int)((Stopwatch.GetTimestamp() - _lastUpdate) / Stopwatch.Frequency); }
        }

        public IPEndPoint LocalAddress { get; set; }

        public IPEndPoint RemoteAddress
        {
            get { return RemoteEndPoint; }
        }

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

        /// <summary>
        /// Occurs only if ReceiveTimeout is not zero
        /// Allows to interrupt the connection
        /// </summary>
        public event EventHandler ReceiveTimeoutHit;

        protected virtual void OnReadTimeout()
        {
            var handler = ReceiveTimeoutHit;
            if (handler != null) handler(this, EventArgs.Empty);
            Logger.Error("TcpConnection read timeout reached {0} ({1}ms)", RemoteEndPoint, ReceiveTimeout);
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

        protected TcpConnection(string address) : this(ParseAddress(address))
        {
        }

        protected TcpConnection()
        {
            Initialize();
            _receiveCallback = ReceiveCallback;
            _sendCallback = SendCallback;
            _sendDelayedDelegate = SendDelayed;
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
            _socket.ReceiveTimeout = ReceiveTimeout;
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
            _readThread = null;
            if (_socket != null)
            {
                _socket.Close();
                _socket = null;
            }
            SetConnectionStatus(ConnectionStatus.Disconnected);
        }

        public void DisconnectAsync()
        {
            if (_connectionStatus == ConnectionStatus.Disconnected || _connectionStatus == ConnectionStatus.Disconnecting)
                return;

            SetConnectionStatus(ConnectionStatus.Disconnecting);
            
            lock (_sendLock)
            {
                _socket.BeginDisconnect(false, SocketDisconnected, _socket);
            }            
        }

        private void SocketDisconnected(IAsyncResult ar)
        {
            var socket = (Socket)ar.AsyncState;
            Exception ex = null;
            try
            {
                socket.EndDisconnect(ar);
            }
            catch (Exception x)
            {
                ex = x;
            }

            SetConnectionStatus(ConnectionStatus.Disconnected, ex);
        }


        private Thread _readThread;
        private void StartRead()
        {
            if (DontUseAsync)
            {
                if (HighPriorityReadThread)
                {
                    if (_readThread != null)
                    {
                        _readThread.Abort();
                    }

                    _readThread = new Thread(SocketReadThread);
                    _readThread.IsBackground = true;
                    _readThread.Priority = ThreadPriority.Highest;
                    _readThread.Start();
                }
                else
                {
                    // take a thread pool thread and run socket in it
                    DcEngine.ThreadPool.QueueWorkItem(SocketReadThread);
                }
            }
            else
                BeginRead();
        }

        public void ListenAsync()
        {
            _closingSocket = false;
            StartRead();
        }

        public void ConnectAsync()
        {
            try
            {
                lock (_sendLock)
                {
                    if (_socket == null)
                    {
                        _socket = new Socket(RemoteEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                        _socket.SendTimeout = SendTimeout;
                        _socket.ReceiveTimeout = ReceiveTimeout;
                    }
                    _closingSocket = false;
                }

                if (LocalAddress != null)
                {
                    _socket.Bind(LocalAddress);
                }

                StartRead();
            }
            catch (Exception x)
            {
                SetConnectionStatus(ConnectionStatus.Disconnected, x);
            }
        }

        private void SocketReadThread()
        {
            try
            {
                if (!_socket.Connected)
                {
                    SetConnectionStatus(ConnectionStatus.Connecting);

                    _lastUpdate = Stopwatch.GetTimestamp();
                    _socket.Connect(RemoteEndPoint);

                    LocalAddress = (IPEndPoint)_socket.LocalEndPoint;

                    SetConnectionStatus(ConnectionStatus.Connected);

                    SendFirstMessages();
                }

                if (_connectionBuffer == null || _connectionBuffer.Length != ConnectionBufferSize)
                    _connectionBuffer = new byte[ConnectionBufferSize];

                while (_connectionStatus == ConnectionStatus.Connected)
                {
                    if (ReceiveTimeout > 0)
                    {
                        if (_socket.Poll(1000 * ReceiveTimeout, SelectMode.SelectRead))
                        {
                            if (!ReceiveInternal())
                                break;
                        }
                        else
                        {
                            OnReadTimeout();
                        }
                    }
                    else
                    {
                        if (!ReceiveInternal())
                            break;
                    }
                }

                SetConnectionStatus(ConnectionStatus.Disconnected);
            }
            catch (Exception x)
            {
                SetConnectionStatus(ConnectionStatus.Disconnected, x);
            }
        }

        private bool ReceiveInternal()
        {
            if (_socket == null)
                return false;

            var bytesReceived = _socket.Receive(_connectionBuffer);

            if (bytesReceived == 0)
                return false;

            if (_connectionBuffer != null)
            {
                HandleReceived(bytesReceived);
            }

            return true;
        }
        
        private void HandleReceived(int bytesReceived)
        {
            _downloadSpeed.Update(bytesReceived);
            _lastUpdate = Stopwatch.GetTimestamp();
            ParseRaw(_connectionBuffer, bytesReceived);
            DownloadSpeedLimit.Update(bytesReceived);
            DownloadSpeedLimitGlobal.Update(bytesReceived);
        }

        protected virtual void SendFirstMessages()
        {
        }
        

        private void BeginRead()
        {
            try
            {
                if (!_socket.Connected)
                {
                    SetConnectionStatus(ConnectionStatus.Connecting);

                    _lastUpdate = Stopwatch.GetTimestamp();
                    _socket.BeginConnect(RemoteEndPoint, ConnectCallback, null);
                    return;
                }
                
                if (_connectionBuffer == null || _connectionBuffer.Length != ConnectionBufferSize)
                    _connectionBuffer = new byte[ConnectionBufferSize];

                _socket.BeginReceive(_connectionBuffer, 0, _connectionBuffer.Length, SocketFlags.None, _receiveCallback, _socket);

            }
            catch (Exception x)
            {
                SetConnectionStatus(ConnectionStatus.Disconnected, x);
            }
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            try
            {
                var socket = (Socket)ar.AsyncState;
                var bytesReceived = socket.EndReceive(ar);

                if (bytesReceived == 0)
                {
                    SetConnectionStatus(ConnectionStatus.Disconnected);
                    return;
                }
                
                if (_connectionBuffer != null)
                {
                    HandleReceived(bytesReceived);
                    _socket.BeginReceive(_connectionBuffer, 0, _connectionBuffer.Length, SocketFlags.None, _receiveCallback, _socket);
                }
            }
            catch (Exception x)
            {
                SetConnectionStatus(ConnectionStatus.Disconnected, x);
            }
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                _socket.EndConnect(ar);
                LocalAddress = (IPEndPoint)_socket.LocalEndPoint;
                SetConnectionStatus(ConnectionStatus.Connected);
                SendFirstMessages();
                BeginRead();
            }
            catch (Exception x)
            {
                SetConnectionStatus(ConnectionStatus.Disconnected, x);
            }
        }

        protected abstract void ParseRaw(byte[] buffer, int length);

        protected bool Send(SendTask task)
        {
            var result = Send(task.Buffer, task.Offset, task.Length);
            if (task.Sync != null)
                task.Sync.Set();
            return result;
        }

        /// <summary>
        /// Sends data synchronously, could be executed before the async request sent
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public bool Send(byte[] buffer, int offset, int length)
        {
            try
            {
                if (ConnectionStatus != ConnectionStatus.Connected)
                    return false;

                _lastUpdate = Stopwatch.GetTimestamp();
                lock (_sendLock)
                {
                    if (_socket != null)
                    {
                        int sent = 0;
                        int needToSend = length;
                        int curOffset = offset;

                        while (sent < length)
                        {
                            var s = _socket.Send(buffer, curOffset, needToSend, SocketFlags.None);
                            sent += s;
                            curOffset += s;
                            needToSend -= s;
                        }

                        _uploadSpeed.Update(length);
                        UploadSpeedLimit.Update(length);
                        UploadSpeedLimitGlobal.Update(length);
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

        public void Send(string msg)
        {
            var bytes = Encoding.Default.GetBytes(msg);
            Send(bytes, 0, bytes.Length);
        }

        public void SendAsync(string msg)
        {
            var bytes = Encoding.Default.GetBytes(msg);

            lock (_delayedMessages)
                _delayedMessages.Enqueue(new SendTask { Buffer = bytes, Length = bytes.Length });

            BeginSend();
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

            BeginSend();
        }

        public void SendAsync(byte[] buffer, int offset, int length)
        {
            lock (_delayedMessages)
            {
                _delayedMessages.Enqueue(new SendTask
                {
                    Buffer = buffer,
                    Offset = offset,
                    Length = length
                });
            }

            BeginSend();
        }

        private void BeginSend()
        {
            if (Interlocked.Exchange(ref _sendThreadActive, 1) == 0)
            {
                DcEngine.ThreadPool.QueueWorkItem(_sendDelayedDelegate);
            }
        }

        public void WaitSendQueue(int minCount = 0)
        {
            while (true)
            {
                lock (_delayedMessages)
                {
                    if (_delayedMessages.Count <= minCount)
                        break;
                }    
                Thread.Sleep(0);
            }
        }

        private bool SendNext()
        {
            lock (_delayedMessages)
            {
                if (_delayedMessages.Count == 0)
                    return false;

                _currentTask = _delayedMessages.Dequeue();
            }

            try
            {
                _socket.BeginSend(_currentTask.Buffer, 0, _currentTask.Length, SocketFlags.None, _sendCallback, null);
                return true;
            }
            catch (Exception x)
            {
                SetConnectionStatus(ConnectionStatus.Disconnected, x);
                return false;
            }
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                _lastUpdate = Stopwatch.GetTimestamp();

                var sent = _socket.EndSend(ar);

                if (sent != _currentTask.Length)
                {
                    Logger.Error("Sent less than asked!");
                }

                _uploadSpeed.Update(_currentTask.Length);
                UploadSpeedLimit.Update(_currentTask.Length);
                UploadSpeedLimitGlobal.Update(_currentTask.Length);

                if (_currentTask.Sync != null)
                    _currentTask.Sync.Set();
                
                if (!SendNext())
                {
                    Interlocked.Exchange(ref _sendThreadActive, 0);
                }
            }
            catch (Exception x)
            {
                if (_currentTask.Sync != null)
                    _currentTask.Sync.Set();

                SetConnectionStatus(ConnectionStatus.Disconnected, x);

                lock (_delayedMessages)
                {
                    foreach (var t in _delayedMessages)
                    {
                        if (t.Sync != null)
                            t.Sync.Set();
                    }
                    _delayedMessages.Clear();
                    Interlocked.Exchange(ref _sendThreadActive, 0);
                }
            }
        }

        private void SendDelayed()
        {
            while (true)
            {
                lock (_delayedMessages)
                {
                    if (_delayedMessages.Count == 0)
                    {
                        Interlocked.Exchange(ref _sendThreadActive, 0);
                        return;
                    }
                }

                bool wait;
                SendTask task;
                lock (_delayedMessages)
                {
                    task = _delayedMessages.Dequeue();
                    wait = _delayedMessages.Count == 0;
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
                        Interlocked.Exchange(ref _sendThreadActive, 0);
                    }
                    break;
                }

                if (wait)
                    Thread.Sleep(10);
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

            var ea = new ConnectionStatusEventArgs
                         {
                             Status = status,
                             Previous = old,
                             Exception = x
                         };

            _connectionStatus = status;

            OnConnectionStatusChanged(ea);

            if (x != null)
                Logger.Error("TcpConnection disconnected by error {0} {1}", RemoteAddress, x.Message);

            if (_connectionStatus == ConnectionStatus.Disconnected)
                Dispose();
        }

        /// <summary>
        /// Tries to parse a string containging address
        /// </summary>
        /// <param name="address">String like dchub://hub.host.com:411</param>
        /// <param name="defaultPort"></param>
        /// <returns></returns>
        [DebuggerStepThrough]
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