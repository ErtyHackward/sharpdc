using System;
using System.Collections.Generic;
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
        
        private readonly List<HyperServerSession> _sessions = new List<HyperServerSession>(); 
        private TcpConnectionListener _listener;
        private readonly List<HyperServerConnection> _unknownConnections = new List<HyperServerConnection>(); 

        /// <summary>
        /// Port to accept incoming connections
        /// </summary>
        public int ListenPort { get; set; }
        
        public int SessionSegmentQueueSize { get; set; }

        public int SessionFileCheckQueueSize { get; set; }

        /// <summary>
        /// Gets or sets hyper storage instance
        /// </summary>
        public HyperStorageManager Storage { get; set; }

        public HyperServer()
        {
            Storage = new HyperStorageManager();
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

            Storage.StartAsync();
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