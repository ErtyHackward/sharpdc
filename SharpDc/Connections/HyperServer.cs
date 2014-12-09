using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpDc.Events;
using SharpDc.Logging;

namespace SharpDc.Connections
{
    /// <summary>
    /// Main HYPER server
    /// </summary>
    public class HyperServer
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly Dictionary<string, HyperStorageManager> _cachedStorages = new Dictionary<string, HyperStorageManager>();

        private readonly List<HyperStorageManager> _storages = new List<HyperStorageManager>();

        private readonly List<HyperServerSession> _sessions = new List<HyperServerSession>(); 

        private TcpConnectionListener _listener;

        private readonly List<HyperServerConnection> _unknownConnections = new List<HyperServerConnection>(); 

        /// <summary>
        /// Port to accept incoming connections
        /// </summary>
        public int ListenPort { get; set; }

        /// <summary>
        /// How many threads to use for each storage
        /// </summary>
        public int WorkersPerStorage { get; set; }

        public int SessionSegmentQueueSize { get; set; }

        public int SessionFileCheckQueueSize { get; set; }

        public HyperServer()
        {
            WorkersPerStorage = 32;
        }

        public void RegisterStorage(string systemPath)
        {
            if (!Directory.Exists(systemPath))
            {
                Logger.Error("Cannot register storage {0} because it is not exists", systemPath);
                return;
            }

            _storages.Add(new HyperStorageManager(systemPath){ MaxWorkers = WorkersPerStorage });
        }

        public IEnumerable<HyperStorageManager> AllStorages()
        {
            foreach (var hyperStorageManager in _storages)
            {
                yield return hyperStorageManager;
            }
        }

        public IEnumerable<HyperServerSession> AllSessions()
        {
            foreach (var session in _sessions)
            {
                yield return session;
            }
        }

        public void StartAsync()
        {
            if (_listener == null)
            {
                _listener = new TcpConnectionListener(ListenPort);
                _listener.IncomingConnection += _listener_IncomingConnection;
            }

            _listener.ListenAsync();

            foreach (var storage in _storages)
            {
                storage.Start();
            }
        }

        public HyperStorageManager ResolveStorage(string path)
        {
            lock (_cachedStorages)
            {
                HyperStorageManager manager;
                if (_cachedStorages.TryGetValue(path, out manager))
                    return manager;
            }

            foreach (var hyperStorageManager in _storages)
            {
                if (File.Exists(Path.Combine(hyperStorageManager.SystemPath, path)))
                {
                    lock (_cachedStorages)
                    {
                        _cachedStorages.Add(path, hyperStorageManager);
                        return hyperStorageManager;
                    }
                }
            }

            return null;
        }

        void _listener_IncomingConnection(object sender, IncomingConnectionEventArgs e)
        {
            Logger.Info("Incoming connection");

            var connection = new HyperServerConnection(e.Socket);
            connection.Handshake += connection_Handshake;
            connection.ConnectionStatusChanged += connection_ConnectionStatusChanged;
            lock (_unknownConnections)
                _unknownConnections.Add(connection);

            connection.StartAsync();
            e.Handled = true;
        }

        void connection_ConnectionStatusChanged(object sender, ConnectionStatusEventArgs e)
        {
            var connection = (HyperServerConnection)sender;
            if (e.Status == ConnectionStatus.Disconnected)
            {
                lock (_unknownConnections)
                    _unknownConnections.Remove(connection);
                Logger.Info("Unknown connection is disconnected");
            }
        }

        void connection_Handshake(object sender, EventArgs e)
        {
            var connection = (HyperServerConnection)sender;
            connection.Handshake -= connection_Handshake;
            connection.ConnectionStatusChanged -= connection_ConnectionStatusChanged;
            
            lock (_unknownConnections)
                _unknownConnections.Remove(connection);

            Logger.Info("Connection handshake {0}", connection.RemoteAddress);

            lock (_sessions)
            {
                var session = _sessions.FirstOrDefault(s => s.SessionToken == connection.SessionToken);

                if (session == null)
                {
                    Logger.Info("Creating new session {0}", connection.SessionToken);
                    session = new HyperServerSession(connection.SessionToken, this);
                    session.MaxQueueSize = SessionSegmentQueueSize;
                    session.MaxFileCheckQueueSize = SessionFileCheckQueueSize;
                    session.Closed += session_Closed;
                    _sessions.Add(session);
                }

                session.AddConnection(connection);
            }

        }

        void session_Closed(object sender, EventArgs e)
        {
            var session = (HyperServerSession)sender;
            Logger.Info("Session {0} is closed", session.SessionToken);
            session.Closed -= session_Closed;
            lock (_sessions)
            {
                _sessions.Remove(session);
            }
        }
    }


    public class HyperSegmentRequestEventArgs : EventArgs
    {
        public HyperServerTask Task { get; set; }
    }
}