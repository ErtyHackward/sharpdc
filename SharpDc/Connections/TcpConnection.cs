// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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
                var read =  _baseStream.Read(buffer, offset, count);
                _rederSpeedAverage.Update(read);
                return read;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                _baseStream.Write(buffer, offset, count);
                _writeSpeedAverage.Update(count);
            }

            public override bool CanRead
            {
                get { return _baseStream.CanRead; }
            }

            public override bool CanSeek
            {
                get { return _baseStream.CanSeek; }
            }

            public override bool CanWrite
            {
                get { return _baseStream.CanWrite; }
            }

            public override long Length
            {
                get { return _baseStream.Length; }
            }

            public override long Position {
                get { return _baseStream.Position; }
                set { _baseStream.Position = value; }
            }
        }

        private Socket _socket;
        protected IPEndPoint RemoteEndPoint;
        private ConnectionStatus _connectionStatus;
        private long _lastUpdate = Stopwatch.GetTimestamp();
        protected bool _closingSocket;
        private int _receiveTimeout;
        private int _sendTimeout = 1000;

        private Stream _stream;

        private readonly SpeedAverage _uploadSpeed = new SpeedAverage();
        private readonly SpeedAverage _downloadSpeed = new SpeedAverage();
        
        private static ObjectPool<TransferSocketAwaitable> _transferAwaitablesPool;
        private static ObjectPool<VoidSocketAwaitable> _voidAwaitablesPool;
        private static int _awaitablesCount;

        private Queue<string> _sendQueue = new Queue<string>();
        private int _flushingQueue;

        /// <summary>
        /// Gets number of objects alive for the transfer operations
        /// </summary>
        public static int AwaitablesAlive 
        {
            get { return _awaitablesCount; }
        }

        public Stream Stream
        {
            get { return _stream; }
        }

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
        public int IdleSeconds
        {
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

            _transferAwaitablesPool = new ObjectPool<TransferSocketAwaitable>(() =>
            {
                var arg = new SocketAsyncEventArgs();
                const int bufferLength = 1024 * 128;
                arg.SetBuffer(new byte[bufferLength], 0, bufferLength);
                Interlocked.Increment(ref _awaitablesCount);
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
            _connectionStatus = ConnectionStatus.Disconnected;
        }

        protected TcpConnection(Socket socket) : this()
        {
            if (socket == null)
                throw new ArgumentNullException("socket");

            _socket = socket;
            PrepareSocket(_socket);
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
            var socket = _socket;
            if (socket != null)
            {
                socket.Close();
                _socket = null;
            }
            SetConnectionStatus(ConnectionStatus.Disconnected);
        }

        public void DisconnectAsync()
        {
            if (_connectionStatus == ConnectionStatus.Disconnected ||
                _connectionStatus == ConnectionStatus.Disconnecting)
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
                    _stream = new BufferedStream(new CounterStream(new NetworkStream(socket, FileAccess.Write), _downloadSpeed, _uploadSpeed), 128 * 1024);
                    SetConnectionStatus(ConnectionStatus.Connected);
                    SendFirstMessages();
                }
                finally
                {
                    _voidAwaitablesPool.PutObject(args);
                }
            }
            else if (_stream == null)
            {
                _stream = new BufferedStream(new CounterStream(new NetworkStream(socket, FileAccess.Write), _downloadSpeed, _uploadSpeed), 128 * 1024);
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
                        HandleReceived(bytesReceived, args.m_eventArgs.Buffer);
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
                HandleReceived(bytesReceived, awaitable.m_eventArgs.Buffer);
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
            socket.SendBufferSize = 1024 * 1024 + 256;
            socket.ReceiveBufferSize = 1024 * 64;
        }
        
        private void HandleReceived(int bytesReceived, byte[] buffer)
        {
            _downloadSpeed.Update(bytesReceived);
            _lastUpdate = Stopwatch.GetTimestamp();
            ParseRaw(buffer, bytesReceived);
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

        protected abstract void ParseRaw(byte[] buffer, int length);

        public async Task SendAsync(string data)
        {
            var bytes = Encoding.Default.GetBytes(data);
            await SendAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        }

        public async Task SendAsync(byte[] buffer, int offset, int length)
        {
            var socket = _socket;

            if (socket == null)
                return;

            var awaitable = _transferAwaitablesPool.GetObject();
            var previousBuffer = awaitable.m_eventArgs.Buffer;

            try
            {
                awaitable.m_eventArgs.SetBuffer(buffer, offset, length);
                var bytesSent = await socket.SendAsync(awaitable);
                HandleSent(bytesSent);
            }
            catch (Exception x)
            {
                SetConnectionStatus(ConnectionStatus.Disconnected, x);
            }
            finally
            {
                awaitable.m_eventArgs.SetBuffer(previousBuffer, 0, previousBuffer.Length);
                _transferAwaitablesPool.PutObject(awaitable);
            }
        }

        public async Task SendAsync(Stream stream)
        {
            var socket = _socket;

            if (socket == null)
                return;

            var awaitable = _transferAwaitablesPool.GetObject();
            var buffer = awaitable.m_eventArgs.Buffer;

            try
            {
                while (true)
                {
                    int num = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    int bytesRead;
                    if ((bytesRead = num) != 0)
                    {
                        awaitable.m_eventArgs.SetBuffer(buffer, 0, bytesRead);
                        var bytesSent = await socket.SendAsync(awaitable);
                        HandleSent(bytesSent);
                    }
                    else
                        break;
                }
            }
            finally
            {
                awaitable.m_eventArgs.SetBuffer(buffer, 0, buffer.Length);
                _transferAwaitablesPool.PutObject(awaitable);
            }
        }

        public async void SendQueued(string message)
        {
            lock (_sendQueue)
            {
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

            await SendAsync(stringToSend);
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