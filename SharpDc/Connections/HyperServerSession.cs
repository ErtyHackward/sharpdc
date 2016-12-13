using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
            Closed?.Invoke(this, EventArgs.Empty);
        }

        public long SessionToken { get; }

        public int MaxQueueSize { get; set; }

        public int MaxFileCheckQueueSize { get; set; }
        
        public SpeedAverage SegmentReqPerSecond { get; set; }
        public SpeedAverage FileCheckReqPerSecond { get; set; }
        public SpeedAverage SkippedSegments { get; set; }
        public SpeedAverage SkippedFileChecks { get; set; }

        public int QueuedResponsesCount => _responses.Count;

        public int QueuedFileResponsesCount => _fileCheckQueue.Count;

        public HyperServerSession(long sessionToken, HyperServer server)
        {
            _server = server;
            SessionToken = sessionToken;
            SegmentReqPerSecond = new SpeedAverage();
            FileCheckReqPerSecond = new SpeedAverage();
            SkippedSegments = new SpeedAverage();
            SkippedFileChecks = new SpeedAverage();
        }

        public void EnqueueSend(HyperServerTask task)
        {
            if (task.IsFileCheck)
            {
                if (_fileCheckQueue.Count >= MaxFileCheckQueueSize)
                {
                    Logger.Trace("Skip fchk send because of queue size is exceeded {0}", task.Token);
                    SkippedFileChecks.Update(1);
                    return;
                }

                _fileCheckQueue.Enqueue(new HyperFileResultMessage { Size = task.FileLength, Token = task.Token });
            }
            else
            {
                if (_responses.Count >= MaxQueueSize)
                {
                    Logger.Trace("Skip seg send because of queue size is exceeded {0}", task.Token);
                    SkippedSegments.Update(1);
                    return;
                }

                _responses.Enqueue(new HyperSegmentDataMessage { Buffer = task.Buffer, Token = task.Token });
            }
            lock (_transferConnections)
            {
                foreach (var t in _transferConnections)
                {
                    t.FlushResponseQueueAsync();
                }
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

        private void connection_SegmentRequested(object sender, HyperSegmentRequestEventArgs e)
        {
            //Logger.Info("Requested {0} {1}", e.Task.Length == -1 ? "file check" : "segment", e.Task.Path);

            var task = e.Task;

            task.Session = this;

            if (task.Length >= 0)
            {
                SegmentReqPerSecond.Update(1);
                task.Buffer = HyperDownloadManager.SegmentsPool.UseObject();
            }
            else
            {
                FileCheckReqPerSecond.Update(1);
            }
            
            var storage = _server.Storage.ResolveStorage(task.Path);

            if (storage == null)
            {
                if (task.IsFileCheck)
                {
                    task.FileLength = -1;
                    Logger.Trace("Task chk file is not found {0}", task.Token);
                    task.Done();
                }
                else
                    Logger.Error("Can't resolve storage for {0}", task.Path);
                return;
            }

            storage.EnqueueTask(task);
        }


        void connection_ConnectionStatusChanged(object sender, ConnectionStatusEventArgs e)
        {
            var connection = (HyperServerConnection)sender;

            if (e.Status == ConnectionStatus.Disconnected)
            {
                Logger.Info("Session connection closed {0} {1}", SessionToken, connection.RemoteAddress);

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