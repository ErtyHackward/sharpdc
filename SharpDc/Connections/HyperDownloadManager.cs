using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpDc.Logging;
using SharpDc.Managers;
using SharpDc.Structs;

namespace SharpDc.Connections
{
    public class HyperDownloadManager
    {
        private readonly List<HyperClientSession> _sessions = new List<HyperClientSession>();
        private static readonly ILogger Logger = LogManager.GetLogger();
        private static readonly ObjectPool<byte[]> segmentsPool = new ObjectPool<byte[]>(() => new byte[1024 * 1024]);
        
        private Timer _eachSecond;
        
        private void EachSecondCallback(object state)
        {
            Update();
        }

        private struct HyperMeta
        {
            public TaskCompletionSource<byte[]> SegmentAwaitable;
            public TaskCompletionSource<long> FileCheckAwaitable;
            public long Created;
        }

        private readonly ConcurrentDictionary<int, HyperMeta> _aliveTasks = new ConcurrentDictionary<int, HyperMeta>();

        public static ObjectPool<byte[]> SegmentsPool
        {
            get { return segmentsPool; }
        }

        /// <summary>
        /// Gets or sets transfer connections count per session
        /// </summary>
        public int TransferConnections { get; set; }

        /// <summary>
        /// Gets or sets control connections count per session
        /// </summary>
        public int ControlConnections { get; set; }

        public SpeedAverage MissedCheckResponses { get; private set; }

        public SpeedAverage MissedSegmentResponses { get; private set; }

        public MovingAverage SegmentDownloadTime { get; private set; }

        public int AliveTasks
        {
            get { return _aliveTasks.Count; }
        }

        public HyperDownloadManager()
        {
            TransferConnections = 6;
            ControlConnections = 1;

            _eachSecond = new Timer(EachSecondCallback, null, 1000, 1000);
            MissedCheckResponses = new SpeedAverage();
            MissedSegmentResponses = new SpeedAverage();
            SegmentDownloadTime = new MovingAverage(TimeSpan.FromSeconds(10));
        }

        public void RegisterHyperServer(string server)
        {
            if (!_sessions.Any(s => s.Server == server))
            {
                Logger.Info("Registering HYPER server {0}", server);

                var session = new HyperClientSession(server);

                session.TransferConnections = TransferConnections;
                session.ControlConnections = ControlConnections;

                _sessions.Add(session);

                session.SegmentReceived += SessionOnSegmentReceived;
                session.FileFound += SessionOnFileFound;
                session.Connect();
            }
        }

        private void SessionOnFileFound(object sender, HyperFileCheckEventArgs e)
        {
            HyperMeta meta;
            if (_aliveTasks.TryRemove(e.Token, out meta))
            {
                if (meta.FileCheckAwaitable == null)
                {
                    if (meta.SegmentAwaitable != null)
                        meta.SegmentAwaitable.SetResult(null);
                    
                    Logger.Error("No awaitable for file check!");
                    return;
                }

                meta.FileCheckAwaitable.SetResult(e.FileSize);
            }
            else
            {
                MissedCheckResponses.Update(1);
            }

        }

        private void SessionOnSegmentReceived(object sender, HyperSegmentEventArgs e)
        {
            HyperMeta meta;
            if (_aliveTasks.TryRemove(e.Token, out meta))
            {
                if (meta.SegmentAwaitable == null)
                {
                    if (meta.FileCheckAwaitable != null)
                        meta.FileCheckAwaitable.SetResult(-1);

                    Logger.Error("No awaitable for segment request!");
                    return;
                }

                meta.SegmentAwaitable.SetResult(e.Buffer);
                SegmentDownloadTime.Update((int)((Stopwatch.GetTimestamp() - meta.Created) / (Stopwatch.Frequency / 1000)));
            }
            else
            {
                MissedSegmentResponses.Update(1);
            }
        }

        public void Update()
        {
            foreach (var aliveTask in _aliveTasks)
            {
                var executionTimeS = (Stopwatch.GetTimestamp() - aliveTask.Value.Created) / Stopwatch.Frequency;

                if (executionTimeS > 4 && aliveTask.Value.SegmentAwaitable != null)
                {
                    HyperMeta timeoutentry;
                    _aliveTasks.TryRemove(aliveTask.Key, out timeoutentry);
                    timeoutentry.SegmentAwaitable.SetResult(null);
                }

                if (executionTimeS > 60 && aliveTask.Value.FileCheckAwaitable != null)
                {
                    HyperMeta timeoutentry;
                    _aliveTasks.TryRemove(aliveTask.Key, out timeoutentry);
                    timeoutentry.FileCheckAwaitable.SetResult(-1);
                }
            }

            foreach (var hyperClientSession in _sessions)
            {
                hyperClientSession.ValidateConnections();
            }
        }

        public Task<byte[]> DownloadSegment(string path, long offset, int length)
        {
            var session = _sessions.First(s => path.StartsWith(s.Server));
            var token = session.CreateToken();
            
            var awaitable = new TaskCompletionSource<byte[]>();
            
            _aliveTasks.TryAdd(token, new HyperMeta 
            {
                SegmentAwaitable = awaitable,
                Created = Stopwatch.GetTimestamp()
            });

            session.RequestSegment(Uri.UnescapeDataString(path.Remove(0, session.Server.Length)), offset, length, token);

            return awaitable.Task;
        }

        public Task<long> GetFileSize(string path)
        {
            var session = _sessions.FirstOrDefault(s => path.StartsWith(s.Server));

            if (session == null)
                return Task.FromResult(-1L);

            while (!session.IsActive)
            {
                Thread.Sleep(1000);
            }

            var token = session.CreateToken();
            
            var awaitable = new TaskCompletionSource<long>();

            _aliveTasks.TryAdd(token, new HyperMeta
            {
                FileCheckAwaitable = awaitable,
                Created = Stopwatch.GetTimestamp()
            });

            session.RequestSegment(Uri.UnescapeDataString(path.Remove(0, session.Server.Length)), 0, -1, token);

            return awaitable.Task;
        }

        public IEnumerable<HyperClientSession> Sessions()
        {
            foreach (var hyperClientSession in _sessions)
            {
                yield return hyperClientSession;
            }
        }
    }
}