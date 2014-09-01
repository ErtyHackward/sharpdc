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

        private Socket _socket;
        protected IPEndPoint RemoteEndPoint;
        private ConnectionStatus _connectionStatus;
        private long _lastUpdate = Stopwatch.GetTimestamp();
        protected bool _closingSocket;
        private int _receiveTimeout;
        private int _sendTimeout = 1000;
        
        private readonly SpeedAverage _uploadSpeed = new SpeedAverage();
        private readonly SpeedAverage _downloadSpeed = new SpeedAverage();

        private readonly AsyncCallback _receiveCallback;
        private readonly AsyncCallback _defaultSendCallback;

        private byte[] _receiveBuffer;
        
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
        }

        private void Initialize()
        {
            UploadSpeedLimit = new SpeedLimiter();
            DownloadSpeedLimit = new SpeedLimiter();
            _receiveBuffer = new byte[1024 * 64];
        }

        protected TcpConnection(string address) : this(ParseAddress(address))
        {
        }

        protected TcpConnection()
        {
            Initialize();
            _receiveCallback = ReceiveCallback;
            _defaultSendCallback = DefaultEndSend;
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

        public void ListenAsync()
        {
            _closingSocket = false;
            BeginRead();
        }

        private void PrepareSocket(Socket socket)
        {
            socket.SendTimeout = SendTimeout;
            socket.ReceiveTimeout = ReceiveTimeout;
            socket.SendBufferSize = 1024 * 1024;
            socket.ReceiveBufferSize = 1024 * 64;
        }

        public void ConnectAsync()
        {
            try
            {
                var socket = _socket;

                if (socket == null)
                {
                    _socket = new Socket(RemoteEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    PrepareSocket(_socket);
                }

                _closingSocket = false;
                
                if (LocalAddress != null)
                {
                    _socket.Bind(LocalAddress);
                }

                BeginRead();
            }
            catch (Exception x)
            {
                SetConnectionStatus(ConnectionStatus.Disconnected, x);
            }
        }

        
        private void HandleReceived(int bytesReceived)
        {
            _downloadSpeed.Update(bytesReceived);
            _lastUpdate = Stopwatch.GetTimestamp();
            ParseRaw(_receiveBuffer, bytesReceived);
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
        

        private void BeginRead()
        {
            var socket = _socket;

            if (socket == null)
                return;

            try
            {
                if (!socket.Connected)
                {
                    SetConnectionStatus(ConnectionStatus.Connecting);

                    _lastUpdate = Stopwatch.GetTimestamp();
                    socket.BeginConnect(RemoteEndPoint, ConnectCallback, null);
                    return;
                }

                socket.BeginReceive(_receiveBuffer, 0, _receiveBuffer.Length, SocketFlags.None, _receiveCallback, socket);
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
                
                HandleReceived(bytesReceived);
                socket.BeginReceive(_receiveBuffer, 0, _receiveBuffer.Length, SocketFlags.None, _receiveCallback, socket);
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

        public void Send(string data)
        {
            var bytes = Encoding.Default.GetBytes(data);

            var result = BeginSend(bytes, 0, bytes.Length, null);

            if (result != null)
                EndSend(result);
        }

        public void Send(byte[] buffer, int offset, int length)
        {
            var result = BeginSend(buffer, offset, length, null);

            if (result != null)
                EndSend(result);
        }

        public void BeginSend(string data)
        {
            var bytes = Encoding.Default.GetBytes(data);
            BeginSend(bytes, 0, bytes.Length, _defaultSendCallback);
        }

        public void BeginSend(byte[] buffer, int offset, int length)
        {
            BeginSend(buffer, offset, length, _defaultSendCallback);
        }

        public IAsyncResult BeginSend(byte[] buffer, int offset, int length, AsyncCallback callback)
        {
            try
            {
                var socket = _socket;

                if (socket == null)
                {
                    SetConnectionStatus(ConnectionStatus.Disconnected);
                    return null;
                }

                return socket.BeginSend(buffer, offset, length, SocketFlags.None, callback, socket);
            }
            catch (Exception x)
            {
                SetConnectionStatus(ConnectionStatus.Disconnected, x);
            }
            return null;
        }

        private void DefaultEndSend(IAsyncResult result)
        {
            EndSend(result);
        }

        public int EndSend(IAsyncResult result)
        {
            var socket = (Socket)result.AsyncState;

            try
            {
                var sent = socket.EndSend(result);
                HandleSent(sent);
                return sent;
            }
            catch (Exception x)
            {
                SetConnectionStatus(ConnectionStatus.Disconnected, x);
            }
            return 0;
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
                _connection.BeginSend(bytes, 0, bytes.Length);
            }
        }
    }
}