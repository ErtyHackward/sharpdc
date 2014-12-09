using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using SharpDc.Events;
using SharpDc.Logging;

namespace SharpDc.Connections
{
    /// <summary>
    /// Provides control mechanism for communicating with one server
    /// </summary>
    public class HyperClientSession : IHyperRequestsProvider
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly List<HyperClientConnection> _transferConnections = new List<HyperClientConnection>();
        private readonly List<HyperClientConnection> _controlConnections = new List<HyperClientConnection>();

        private readonly ConcurrentQueue<HyperRequestMessage> _requests = new ConcurrentQueue<HyperRequestMessage>();

        private int _currentToken;
        private HyperUrl _serverUrl;

        /// <summary>
        /// Gets or sets transfer connections count
        /// </summary>
        public int TransferConnections { get; set; }

        /// <summary>
        /// Gets or sets control connections count
        /// </summary>
        public int ControlConnections { get; set; }

        /// <summary>
        /// Gets server address
        /// </summary>
        public string Server { get; private set; }

        /// <summary>
        /// Gets session token (common for all connections)
        /// </summary>
        public long SessionToken { get; private set; }

        public bool IsOpen { get; set; }

        public bool IsActive {
            get
            {
                return  _controlConnections.Any(c => c.ConnectionStatus == ConnectionStatus.Connected) &&
                       _transferConnections.Any(c => c.ConnectionStatus == ConnectionStatus.Connected);
            }
        }

        public int QueuedRequestsCount
        {
            get { return _requests.Count; }
        }

        public event EventHandler<HyperSegmentEventArgs> SegmentReceived;

        protected virtual void OnSegmentReceived(HyperSegmentEventArgs e)
        {
            var handler = SegmentReceived;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<HyperFileCheckEventArgs> FileFound;

        protected virtual void OnFileFound(HyperFileCheckEventArgs e)
        {
            var handler = FileFound;
            if (handler != null) handler(this, e);
        }

        public HyperClientSession(string server)
        {
            TransferConnections = 2;
            ControlConnections = 1;
            Server = server;
            _serverUrl = new HyperUrl(Server);
        }

        public void Connect()
        {
            Close();
            IsOpen = true;
            var rng = new RNGCryptoServiceProvider();

            var bytes = new byte[8];
            rng.GetBytes(bytes);
            SessionToken = BitConverter.ToInt64(bytes, 0);

            Logger.Info("Initialized hyper session to {0}", SessionToken);

            ValidateConnections();
        }

        public void ValidateConnections()
        {
            if (!IsOpen)
                return;

            try
            {
                while (ControlConnections < _controlConnections.Count)
                {
                    Logger.Info("Reducing hyper control connections... {0} => {1}", _controlConnections.Count,
                        ControlConnections);
                    var connection = _controlConnections.Last();
                    connection.DisconnectAsync();
                    _controlConnections.RemoveAt(_controlConnections.Count - 1);
                }

                while (TransferConnections < _transferConnections.Count)
                {
                    Logger.Info("Reducing hyper transfer connections... {0} => {1}", _transferConnections.Count,
                        TransferConnections);
                    var connection = _transferConnections.Last();
                    connection.SegmentReceived -= connection_SegmentReceived;
                    connection.FileFound -= ConnectionOnFileFound;
                    connection.DisconnectAsync();
                    _transferConnections.RemoveAt(_transferConnections.Count - 1);
                }

                while (ControlConnections > _controlConnections.Count)
                {
                    Logger.Info("Increasing hyper control connections... {0} => {1}", _controlConnections.Count,
                        ControlConnections);
                    var connection = new HyperClientConnection(_serverUrl, true, SessionToken);
                    connection.SetRequestRovider(this);
                    _controlConnections.Add(connection);
                    connection.StartAsync();
                }

                while (TransferConnections > _transferConnections.Count)
                {
                    Logger.Info("Increasing hyper transfer connections... {0} => {1}", _transferConnections.Count,
                        TransferConnections);
                    var connection = new HyperClientConnection(_serverUrl, false, SessionToken);
                    _transferConnections.Add(connection);
                    connection.SegmentReceived += connection_SegmentReceived;
                    connection.FileFound += ConnectionOnFileFound;
                    connection.StartAsync();
                }

                foreach (var controlConnection in _controlConnections)
                {
                    if (controlConnection.ConnectionStatus == ConnectionStatus.Disconnected &&
                        controlConnection.IdleSeconds > 5)
                    {
                        Logger.Info("Restaring hyper control connection");
                        controlConnection.StartAsync();
                    }
                }

                foreach (var transferConnection in _transferConnections)
                {
                    if (transferConnection.ConnectionStatus == ConnectionStatus.Disconnected &&
                        transferConnection.IdleSeconds > 5)
                    {
                        Logger.Info("Restaring hyper transfer connection");
                        transferConnection.StartAsync();
                    }
                }
            }
            catch (Exception x)
            {
                Logger.Error("Validate connection error: {0}", x.Message);
            }
        }

        private void ConnectionOnFileFound(object sender, HyperFileCheckEventArgs e)
        {
            OnFileFound(e);
        }

        void connection_SegmentReceived(object sender, HyperSegmentEventArgs e)
        {
            OnSegmentReceived(e);
        }

        public void Close()
        {
            IsOpen = false;
            foreach (var controlConnection in _controlConnections)
            {
                controlConnection.DisconnectAsync();
            }

            foreach (var transferConnection in _transferConnections)
            {
                transferConnection.DisconnectAsync();
            }

            _controlConnections.Clear();
            _transferConnections.Clear();
        }

        internal int CreateToken()
        {
            return Interlocked.Add(ref _currentToken, 1);
        }

        public int RequestSegment(string path, long offset, int length, int token)
        {
            var req = new HyperRequestMessage();

            req.Token = token;
            req.Path = path;
            req.Offset = offset;
            req.Length = length;
            

            _requests.Enqueue(req);
            
            for (int i = 0; i < _controlConnections.Count; i++)
            {
                if (_controlConnections[i].ConnectionStatus == ConnectionStatus.Connected)
                    _controlConnections[i].FlushRequestQueueAsync();
            }

            return req.Token;
        }

        public IEnumerable<HyperClientConnection> Transfers()
        {
            foreach (var hyperClientConnection in _transferConnections)
            {
                yield return hyperClientConnection;
            }
        }

        public IEnumerable<HyperClientConnection> Controls()
        {
            foreach (var hyperClientConnection in _controlConnections)
            {
                yield return hyperClientConnection;
            }
        }

        public bool TryGetRequest(out HyperRequestMessage request)
        {
            return _requests.TryDequeue(out request);
        }
    }

    public class HyperSegmentEventArgs : EventArgs
    {
        public byte[] Buffer { get; set; }
        public int Token { get; set; }

    }

    public class HyperFileCheckEventArgs : EventArgs
    {
        public int Token { get; set; }

        public long FileSize { get; set; }
    }
}