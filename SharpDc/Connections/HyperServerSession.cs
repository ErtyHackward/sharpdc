using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SharpDc.Events;
using SharpDc.Logging;
using SharpDc.Structs;

namespace SharpDc.Connections
{
    /// <summary>
    /// Provides server session management
    /// </summary>
    public class HyperServerSession : IHyperResponseProvider
    {
        private readonly HyperServer _server;
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly List<HyperServerConnection> _controlConnections = new List<HyperServerConnection>(); 
        private readonly List<HyperServerConnection> _transferConnections = new List<HyperServerConnection>();

        private readonly ConcurrentQueue<HyperSegmentDataMessage> _responses = new ConcurrentQueue<HyperSegmentDataMessage>();
        private readonly ConcurrentQueue<HyperFileResultMessage> _fileCheckQueue = new ConcurrentQueue<HyperFileResultMessage>();

        public event EventHandler Closed;

        protected virtual void OnClosed()
        {
            var handler = Closed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public long SessionToken { get; private set; }

        public int MaxQueueSize { get; set; }

        public int MaxFileCheckQueueSize { get; set; }
        
        public SpeedAverage SegmentReqPerSecond { get; set; }
        public SpeedAverage FileCheckReqPerSecond { get; set; }
        public SpeedAverage SkippedSegments { get; set; }

        public int QueuedResponsesCount
        {
            get { return _responses.Count; }
        }

        public int QueuedFileResponsesCount
        {
            get { return _fileCheckQueue.Count; }
        }

        public HyperServerSession(long sessionToken, HyperServer server)
        {
            _server = server;
            SessionToken = sessionToken;
            SegmentReqPerSecond = new SpeedAverage();
            FileCheckReqPerSecond = new SpeedAverage();
            SkippedSegments = new SpeedAverage();
        }

        public void EnqueueSend(HyperServerTask task)
        {
            if (_responses.Count >= MaxQueueSize)
            {
                SkippedSegments.Update(1);
                return;
            }
            
            _responses.Enqueue(new HyperSegmentDataMessage { Buffer = task.Buffer, Token = task.Token });

            for (int i = 0; i < _transferConnections.Count; i++)
            {
                _transferConnections[i].FlushResponseQueueAsync();
            }
        }

        public void EnqueueSend(HyperFileResultMessage msg)
        {
            if (_fileCheckQueue.Count >= MaxFileCheckQueueSize)
            {

                return;
            }

            _fileCheckQueue.Enqueue(msg);

            for (int i = 0; i < _transferConnections.Count; i++)
            {
                _transferConnections[i].FlushResponseQueueAsync();
            }
        }

        public void AddConnection(HyperServerConnection connection)
        {
            if (connection.IsControl)
            {
                lock (_controlConnections)
                    _controlConnections.Add(connection);

                connection.SegmentRequested += connection_SegmentRequested;
            }
            else
            {
                lock (_transferConnections)
                    _transferConnections.Add(connection);
            }

            connection.ConnectionStatusChanged += connection_ConnectionStatusChanged;
            connection.SetResponsesRovider(this);
        }

        void connection_SegmentRequested(object sender, HyperSegmentRequestEventArgs e)
        {
            //Logger.Info("Requested {0} {1}", e.Task.Length == -1 ? "file check" : "segment", e.Task.Path);
            
            var task = e.Task;

            task.Session = this;

            if (task.Length >= 0)
            {
                SegmentReqPerSecond.Update(1);
                task.Buffer = HyperDownloadManager.SegmentsPool.GetObject();
            }
            else
            {
                FileCheckReqPerSecond.Update(1);
            }
            
            var storage = _server.ResolveStorage(task.Path);

            if (storage == null)
            {
                if (task.Buffer == null)
                    EnqueueSend(new HyperFileResultMessage{ Token = task.Token, Size = -1});
                else
                    Logger.Error("Can't resolve storage for {0}", task.Path);
                return;
            }

            storage.EnqueueTask(task);
        }

        void connection_ConnectionStatusChanged(object sender, Events.ConnectionStatusEventArgs e)
        {
            var connection = (HyperServerConnection)sender;

            if (e.Status == ConnectionStatus.Disconnected)
            {
                Logger.Info("Session connection closed {0} {1}", this.SessionToken, connection.RemoteAddress);

                lock (_controlConnections)
                    lock (_transferConnections)
                    {
                        if (connection.IsControl)
                        {
                            _controlConnections.Remove(connection);
                            connection.SegmentRequested -= connection_SegmentRequested;

                            if (_controlConnections.Count == 0)
                                Logger.Info("No more control connections alive");

                        }
                        else
                        {
                            _transferConnections.Remove(connection);
                        }
                        if (_controlConnections.Count == 0 && _transferConnections.Count == 0)
                            OnClosed();
                    }

                connection.ConnectionStatusChanged -= connection_ConnectionStatusChanged;
            }
        }

        public IEnumerable<HyperServerConnection> TransferConnections()
        {
            lock (_transferConnections)
            {
                foreach (var transfer in _transferConnections)
                {
                    yield return transfer;
                }
            }
        }

        public bool TryGetSegmentResponse(out HyperSegmentDataMessage response)
        {
            return _responses.TryDequeue(out response);
        }

        public bool TryGetFileCheckResponse(out HyperFileResultMessage response)
        {
            return _fileCheckQueue.TryDequeue(out response);
        }
    }
}