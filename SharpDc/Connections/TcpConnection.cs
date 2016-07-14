// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2016
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpDc.Events;
using SharpDc.Helpers;
using SharpDc.Logging;
using SharpDc.Managers;
using SharpDc.Structs;

namespace SharpDc.Connections
{
    /// <summary>
    /// Base class for session based connections on TCP stack
    /// </summary>
    public abstract class TcpConnection : IDisposable
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private class CounterStream : Stream
        {
            private readonly Stream _baseStream;
            private readonly SpeedAverage _rederSpeedAverage;
            private readonly SpeedAverage _writeSpeedAverage;

            public CounterStream(Stream baseStream, SpeedAverage readSpeed, SpeedAverage writeSpeed)
            {
                _baseStream = baseStream;
                _rederSpeedAverage = readSpeed;
                _writeSpeedAverage = writeSpeed;
            }

            public override void Flush()
            {
                _baseStream.Flush();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                return _baseStream.Seek(offset, origin);
            }

            public override void SetLength(long value)
            {
                _baseStream.SetLength(value);
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var read = _baseStream.Read(buffer, offset, count);
                _rederSpeedAverage.Update(read);
                return read;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                _baseStream.Write(buffer, offset, count);
                _writeSpeedAverage.Update(count);
            }

            public override bool CanRead => _baseStream.CanRead;

            public override bool CanSeek => _baseStream.CanSeek;

            public override bool CanWrite => _baseStream.CanWrite;

            public override long Length => _baseStream.Length;

            public override long Position {
                get { return _baseStream.Position; }
                set { _baseStream.Position = value; }
            }
        }

        private Socket _socket;
        protected IPEndPoint RemoteEndPoint;
        private long _lastUpdate = Stopwatch.GetTimestamp();
        protected bool _closingSocket;

        private Stream _stream;

        private readonly SpeedAverage _uploadSpeed = new SpeedAverage();
        private readonly SpeedAverage _downloadSpeed = new SpeedAverage();
        
        private static readonly ObjectPool<TransferSocketAwaitable> _transferAwaitablesPool;
        private static readonly ObjectPool<VoidSocketAwaitable> _voidAwaitablesPool;
        private static int _awaitablesCount;

        private static bool _bufferInitialized;
        private static int _operationBufferLength = 65536;
        private static int _defaultSocketReceiveBufferLength = 65536;
        private static int _defaultSocketSendBufferLength = 65536;

        private readonly Queue<string> _sendQueue = new Queue<string>();
        private int _flushingQueue;
        private readonly SpeedAverage _sendQueuedSkips = new SpeedAverage();

        /// <summary>
        /// Gets or sets async operation buffer length this value multiplied to the maxSimultaneousOperations will define socket big_memory_buffer
        /// You can set this value only before any network operation
        /// Default is 65536
        /// </summary>
        public static int OperationBufferLength
        {
            get { return _operationBufferLength; }
            set
            {
                if (_bufferInitialized)
                    throw new InvalidOperationException(
                        $"TcpConnection BufferLength is already initialized to {_operationBufferLength} and cannot be changed");
                _operationBufferLength = value;
            }
        }

        /// <summary>
        /// Gets number of objects alive for the transfer operations
        /// </summary>
        public static int AwaitablesAlive => _awaitablesCount;

        public static int IdleAwaitables => _transferAwaitablesPool.Count;

        /// <summary>
        /// Defines system-level receive buffer length
        /// Default value is 65536
        /// </summary>
        public static int DefaultSocketReceiveBufferLength
        {
            get { return _defaultSocketReceiveBufferLength; }
            set { _defaultSocketReceiveBufferLength = value; }
        }

        /// <summary>
        /// Defines system-level send buffer length
        /// Default value is 65536
        /// </summary>
        public static int DefaultSocketSendBufferLength
        {
            get { return _defaultSocketSendBufferLength; }
            set { _defaultSocketSendBufferLength = value; }
        }

        /// <summary>
        /// Gets network stream. Provides alternative way of communicating with the socket
        /// </summary>
        public Stream Stream => _stream;

        /// <summary>
        /// Gets the socket
        /// </summary>
        public Socket Socket => _socket;

        /// <summary>
        /// Gets or sets a socket receive timeout in milliseconds
        /// </summary>
        public int ReceiveTimeout { get; set; }

        /// <summary>
        /// Gets or sets a socket send timeout in milliseconds
        /// </summary>
        public int SendTimeout { get; set; } = 1000;

        /// <summary>
        /// Gets current connection status
        /// </summary>
        public ConnectionStatus ConnectionStatus { get; private set; }

        /// <summary>
        /// Gets seconds passed from the last network event
        /// </summary>
        public int IdleSeconds => (int)((Stopwatch.GetTimestamp() - _lastUpdate) / Stopwatch.Frequency);

        public IPEndPoint LocalAddress { get; set; }

        public IPEndPoint RemoteAddress => RemoteEndPoint;

        /// <summary>
        /// Gets an object to obtain upload speed
        /// </summary>
        public SpeedAverage UploadSpeed => _uploadSpeed;

        /// <summary>
        /// Gets an object to obtain download speed
        /// </summary>
        public SpeedAverage DownloadSpeed => _downloadSpeed;

        /// <summary>
        /// Allows to control global tcpConnection upload speed limit
        /// </summary>
        public static SpeedLimiter UploadSpeedLimitGlobal { get; }

        /// <summary>
        /// Allows to control global tcpConnection download speed limit
        /// </summary>
        public static SpeedLimiter DownloadSpeedLimitGlobal { get; }

        /// <summary>
        /// Allows to control this tcpConnection upload speed limit
        /// </summary>
        public SpeedLimiter UploadSpeedLimit { get; private set; }

        /// <summary>
        /// Allows to control this tcpConnection download speed limit
        /// </summary>
        public SpeedLimiter DownloadSpeedLimit { get; private set; }

        /// <summary>
        /// Gets average messages dropped using SendQueued during last 10 seconds
        /// </summary>
        public SpeedAverage SendQueuedSkips => _sendQueuedSkips;

        #region Events

        /// <summary>
        /// Occurs when connection status changed
        /// </summary>
        public event EventHandler<ConnectionStatusEventArgs> ConnectionStatusChanged;

        public void OnConnectionStatusChanged(ConnectionStatusEventArgs e)
        {
            ConnectionStatusChanged?.Invoke(this, e);
        }

        #endregion

        static TcpConnection()
        {
            UploadSpeedLimitGlobal = new SpeedLimiter();
            DownloadSpeedLimitGlobal = new SpeedLimiter();

            _transferAwaitablesPool = new ObjectPool<TransferSocketAwaitable>(() =>
            {
                if (!_bufferInitialized)
                {
                    // lock to avoid multiple initialization
                    lock (_transferAwaitablesPool)
                    {
                        if (!_bufferInitialized)
                        {
                            if (_operationBufferLength <= 0)
                            {
                                _operationBufferLength = 64 * 1024;
                                Logger.Info("Using default operation buffer length");
                            }
                        }

                        _bufferInitialized = true;
                    }
                }

                Interlocked.Increment(ref _awaitablesCount);
                
                var arg = new SocketAsyncEventArgs();
                arg.SetBuffer(new byte[_operationBufferLength], 0, _operationBufferLength);
                
                return new TransferSocketAwaitable(arg); 
            });

            _voidAwaitablesPool = new ObjectPool<VoidSocketAwaitable>(() => {
                var arg = new SocketAsyncEventArgs();
                return new VoidSocketAwaitable(arg);
            });
        }

        private void Initialize()
        {
            UploadSpeedLimit = new SpeedLimiter();
            DownloadSpeedLimit = new SpeedLimiter();
        }

        protected TcpConnection(string address) : this(ParseAddress(address))
        {
        }

        protected TcpConnection()
        {
            Initialize();
        }

        protected TcpConnection(IPEndPoint remoteEndPoint) : this()
        {
            RemoteEndPoint = remoteEndPoint;
            ConnectionStatus = ConnectionStatus.Disconnected;
        }

        protected TcpConnection(Socket socket) : this()
        {
            if (socket == null)
                throw new ArgumentNullException(nameof(socket));

            _socket = socket;
            PrepareSocket(_socket);
            LocalAddress = (IPEndPoint)_socket.LocalEndPoint;
            ConnectionStatus = _socket.Connected ? ConnectionStatus.Connected : ConnectionStatus.Disconnected;
            try
            {
                RemoteEndPoint = socket.RemoteEndPoint as IPEndPoint;
            }
            catch (Exception x)
            {
                Logger.Error("When trying to get the remote address: " + x.Message);
            }

            if (ConnectionStatus == ConnectionStatus.Disconnected)
            {
                Logger.Error("Added socket is disconnected!");
            }
        }

        public virtual void Dispose()
        {
            var socket = _socket;
            if (socket != null)
            {
                try
                {
                    socket.Close();
                }
                catch (Exception x)
                {
                    Logger.Error("Failed to close the socket {0}", x.Message);
                }
                
                _socket = null;
            }
            SetConnectionStatus(ConnectionStatus.Disconnected);
        }

        public void DisconnectAsync()
        {
            if (ConnectionStatus == ConnectionStatus.Disconnected ||
                ConnectionStatus == ConnectionStatus.Disconnecting)
                return;

            SetConnectionStatus(ConnectionStatus.Disconnecting);

            var socket = _socket;

            if (socket != null)
                socket.BeginDisconnect(false, SocketDisconnected, socket);
            else
            {
                SetConnectionStatus(ConnectionStatus.Disconnected);
            }
        }

        public void Disconnect()
        {
            Dispose();
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

        protected async Task EnsureConnected()
        {
            _closingSocket = false;

            var socket = _socket;

            if (socket == null)
            {
                _closingSocket = false;
                socket = new Socket(RemoteEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                PrepareSocket(socket);

                if (LocalAddress != null)
                {
                    socket.Bind(LocalAddress);
                }

                _socket = socket;
            }

            if (!socket.Connected)
            {
                SetConnectionStatus(ConnectionStatus.Connecting);

                _lastUpdate = Stopwatch.GetTimestamp();

                var args = _voidAwaitablesPool.GetObject();

                try
                {
                    args.m_eventArgs.RemoteEndPoint = RemoteEndPoint;
                    await socket.ConnectAsync(args);

                    LocalAddress = (IPEndPoint)socket.LocalEndPoint;
                    _stream =
                        new BufferedStream(
                            new CounterStream(new NetworkStream(socket, FileAccess.Write), _downloadSpeed, _uploadSpeed),
                            _operationBufferLength);
                    SetConnectionStatus(ConnectionStatus.Connected);
                    SendFirstMessages();
                }
                catch (Exception x)
                {
                    SetConnectionStatus(ConnectionStatus.Disconnected, x);
                }
                finally
                {
                    _voidAwaitablesPool.PutObject(args);
                }
            }
            else if (_stream == null)
            {
                _stream = new BufferedStream(new CounterStream(new NetworkStream(socket, FileAccess.Write), _downloadSpeed, _uploadSpeed), _operationBufferLength);
            }
        }

        public async void StartAsync()
        {
            await EnsureConnected().ConfigureAwait(false);

            var socket = _socket;

            while (true)
            {
                var args = _transferAwaitablesPool.GetObject();
                try
                {
                    var bytesReceived = await socket.ReceiveAsync(args);
                    if (bytesReceived != 0)
                        HandleReceived(args.m_eventArgs.Buffer, args.m_eventArgs.Offset, bytesReceived);
                    else
                    {
                        SetConnectionStatus(ConnectionStatus.Disconnected);
                        break;
                    }
                }
                catch (Exception x)
                {
                    SetConnectionStatus(ConnectionStatus.Disconnected, x);
                    break;
                }
                finally
                {
                    _transferAwaitablesPool.PutObject(args);
                }
            }
        }

        /// <summary>
        /// Always return used object back to the pool with ReleaseAwaitable method
        /// </summary>
        /// <returns></returns>
        protected async Task<TransferSocketAwaitable> ReceiveAsync()
        {
            var socket = _socket;

            if (socket == null)
                return null;

            var awaitable = _transferAwaitablesPool.GetObject();
            
            try
            {
                var bytesReceived = await socket.ReceiveAsync(awaitable);
                HandleReceived(awaitable.m_eventArgs.Buffer, awaitable.m_eventArgs.Offset, bytesReceived);
                return awaitable;
            }
            catch (Exception x)
            {
                SetConnectionStatus(ConnectionStatus.Disconnected, x);
                _transferAwaitablesPool.PutObject(awaitable);
                return null;
            }
        }

        protected void ReleaseAwaitable(TransferSocketAwaitable awaitable)
        {
            _transferAwaitablesPool.PutObject(awaitable);
        }

        protected virtual void PrepareSocket(Socket socket)
        {
            socket.SendTimeout = SendTimeout;
            socket.ReceiveTimeout = ReceiveTimeout;
            socket.SendBufferSize = _defaultSocketSendBufferLength;
            socket.ReceiveBufferSize = _defaultSocketReceiveBufferLength;
        }

        private void HandleReceived(byte[] buffer, int bufferOffset, int bytesReceived)
        {
            _downloadSpeed.Update(bytesReceived);
            _lastUpdate = Stopwatch.GetTimestamp();
            ParseRaw(buffer, bufferOffset, bytesReceived);
            DownloadSpeedLimit.Update(bytesReceived);
            DownloadSpeedLimitGlobal.Update(bytesReceived);
        }

        private void HandleSent(int bytesSent)
        {
            _uploadSpeed.Update(bytesSent);
            UploadSpeedLimit.Update(bytesSent);
            UploadSpeedLimitGlobal.Update(bytesSent);
        }

        protected virtual void SendFirstMessages()
        {
        }

        protected abstract void ParseRaw(byte[] buffer, int offset, int length);

        public async Task SendAsync(string data)
        {
            var bytes = Encoding.Default.GetBytes(data);

            if (bytes.Length <= _operationBufferLength)
            {
                // copy bytes to the big buffer...
                var socket = _socket;

                if (socket == null)
                    return;

                if (bytes.Length == 0)
                {
                    Logger.Warn("Skipping send of 0 bytes...");
                    return;
                }

                var awaitable = _transferAwaitablesPool.GetObject();
                try
                {
                    Buffer.BlockCopy(bytes, 0, awaitable.m_eventArgs.Buffer, awaitable.m_eventArgs.Offset, bytes.Length);
                    awaitable.m_eventArgs.SetBuffer(awaitable.m_eventArgs.Offset, bytes.Length);
                    var bytesSent = await socket.SendAsync(awaitable);
                    HandleSent(bytesSent);
                }
                catch (Exception x)
                {
                    SetConnectionStatus(ConnectionStatus.Disconnected, x);
                }
                finally
                {
                    awaitable.m_eventArgs.SetBuffer(awaitable.m_eventArgs.Offset, _operationBufferLength);
                    _transferAwaitablesPool.PutObject(awaitable);
                }
            }
            else
            {
                Logger.Warn("Using temp buffer for string operations. This could create performance issues.");
                await SendAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }
        }

        public Task SendAsync(byte[] buffer)
        {
            return SendAsync(buffer, 0, buffer.Length);
        }

        public async Task SendAsync(byte[] buffer, int offset, int length)
        {
            var socket = _socket;

            if (socket == null)
                return;

            var awaitable = _transferAwaitablesPool.GetObject();
            var previousBuffer = awaitable.m_eventArgs.Buffer;
            var previousOffset = awaitable.m_eventArgs.Offset;

            try
            {
                awaitable.m_eventArgs.SetBuffer(buffer, offset, length);
                var bytesSent = await socket.SendAsync(awaitable);

                if (bytesSent != length)
                    Debugger.Break();

                HandleSent(bytesSent);
            }
            catch (Exception x)
            {
                SetConnectionStatus(ConnectionStatus.Disconnected, x);
            }
            finally
            {
                awaitable.m_eventArgs.SetBuffer(previousBuffer, previousOffset, _operationBufferLength);
                _transferAwaitablesPool.PutObject(awaitable);
            }
        }

        public async Task SendAsync(Stream stream, int count = -1)
        {
            var socket = _socket;

            if (socket == null)
                return;

            var awaitable = _transferAwaitablesPool.GetObject();
            var buffer = awaitable.m_eventArgs.Buffer;
            var offset = awaitable.m_eventArgs.Offset;

            try
            {
                var sent = 0;
                while (true)
                {
                    var bytesToRead = count == -1 ? _operationBufferLength : Math.Min(count - sent, _operationBufferLength);
                    if (bytesToRead == 0)
                        break;

                    int bytesRead = await stream.ReadAsync(buffer, offset, bytesToRead).ConfigureAwait(false);
                    if (bytesRead > 0)
                    {
                        awaitable.m_eventArgs.SetBuffer(buffer, offset, bytesRead);
                        var bytesSent = await socket.SendAsync(awaitable);
                        HandleSent(bytesSent);
                        sent += bytesSent;
                    }
                    else
                        break;
                }
            }
            finally
            {
                awaitable.m_eventArgs.SetBuffer(buffer, offset, _operationBufferLength);
                _transferAwaitablesPool.PutObject(awaitable);
            }
        }

        public async void SendQueued(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                Logger.Warn("Cannot send empty string. Check your code.");
                return;
            }
            
            lock (_sendQueue)
            {
                if (_sendQueue.Count > 1000)
                    _sendQueuedSkips.Update(1);
                else
                    _sendQueue.Enqueue(message);
            }

            if (Interlocked.Exchange(ref _flushingQueue, 1) == 0)
            {
                try
                {
                    await FlushQueue().ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Exchange(ref _flushingQueue, 0);
                }
            }
        }

        private async Task FlushQueue()
        {
            string stringToSend;

            lock (_sendQueue)
            {
                if (_sendQueue.Count > 1)
                {
                    stringToSend = string.Join("", _sendQueue);
                    _sendQueue.Clear();
                }
                else if (_sendQueue.Count == 1)
                {
                    stringToSend = _sendQueue.Dequeue();
                }
                else
                {
                    return;
                }
            }

            await SendAsync(stringToSend).ConfigureAwait(false);
        }


        /// <summary>
        /// Updates current connection status
        /// </summary>
        /// <param name="status"></param>
        /// <param name="x"></param>
        protected void SetConnectionStatus(ConnectionStatus status, Exception x = null)
        {
            var old = ConnectionStatus;
            if (ConnectionStatus == status)
                return;

            var ea = new ConnectionStatusEventArgs
                         {
                             Status = status,
                             Previous = old,
                             Exception = x
                         };

            ConnectionStatus = status;

            OnConnectionStatusChanged(ea);

            if (x != null)
                Logger.Error("TcpConnection disconnected by error: {0} {1} {2}", RemoteAddress, x.Message, x.StackTrace);

            if (ConnectionStatus == ConnectionStatus.Disconnected)
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

    public interface INotifyOnSend
    {
        bool NotificationsEnabled { get; }
        void OnOutgoingMessage(MessageEventArgs args);
    }

    /// <summary>
    /// Allows to send multiple NMDC messages in one async send operation
    /// </summary>
    public class SendTransaction : IDisposable
    {
        private readonly TcpConnection _connection;
        private readonly StringBuilder _builder;

        public SendTransaction(TcpConnection connection)
        {
            _connection = connection;
            _builder = new StringBuilder();
        }

        public void Send(string message)
        {
            _builder.Append(message + "|");

            var notify = _connection as INotifyOnSend;
            if (notify != null && notify.NotificationsEnabled)
            {
                var ea = new MessageEventArgs { Message = message };
                notify.OnOutgoingMessage(ea);
            }
        }

        public void Dispose()
        {
            if (_builder != null && _connection != null)
            {
                var bytes = Encoding.Default.GetBytes(_builder.ToString());
                _connection.SendAsync(bytes, 0, bytes.Length).NoWarning();
            }
        }
    }
}