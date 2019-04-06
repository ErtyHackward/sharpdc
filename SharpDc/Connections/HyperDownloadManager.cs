using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpDc.Helpers;
using SharpDc.Logging;
using SharpDc.Managers;
using SharpDc.Structs;

namespace SharpDc.Connections
{
    public class HyperDownloadManager
    {
        private readonly List<HyperClientSession> _sessions = new List<HyperClientSession>();
        private static readonly ILogger Logger = LogManager.GetLogger();

        private Timer _eachSecond;
        
        private void EachSecondCallback(object state)
        {
            Update();
        }

        private struct HyperMeta
        {
            public TaskCompletionSource<ReusableObject<byte[]>> SegmentAwaitable;
            public TaskCompletionSource<long> FileCheckAwaitable;
            public long Created;
        }

        private readonly ConcurrentDictionary<int, HyperMeta> _aliveTasks = new ConcurrentDictionary<int, HyperMeta>();

        public static ObjectPool<byte[]> SegmentsPool { get; } = new ObjectPool<byte[]>(() => new byte[1024 * 1024]);

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

        public SpeedAverage TimeoutSegments { get; private set; }

        public SpeedAverage TimeoutFilechecks { get; private set; }

        public int AliveTasks => _aliveTasks.Count;

        public HyperDownloadManager()
        {
            TransferConnections = 6;
            ControlConnections = 1;

            _eachSecond = new Timer(EachSecondCallback, null, 1000, 1000);
            MissedCheckResponses = new SpeedAverage();
            MissedSegmentResponses = new SpeedAverage();
            SegmentDownloadTime = new MovingAverage(TimeSpan.FromSeconds(10));
            TimeoutSegments = new SpeedAverage();
            TimeoutFilechecks = new SpeedAverage();
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
                        meta.SegmentAwaitable.SetResult(new ReusableObject<byte[]>());
                    
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
            int droppedSegmentRequests = 0;
            int droppedFileChecks = 0;

            foreach (var aliveTask in _aliveTasks)
            {
                var executionTimeS = (Stopwatch.GetTimestamp() - aliveTask.Value.Created) / Stopwatch.Frequency;

                if (executionTimeS > 4 && aliveTask.Value.SegmentAwaitable != null)
                {
                    if (_aliveTasks.TryRemove(aliveTask.Key, out var timeoutentry))
                        timeoutentry.SegmentAwaitable.SetResult(new ReusableObject<byte[]>());
                    TimeoutSegments.Update(1);

                    Logger.Warn($"Timeout request token:{aliveTask.Key}");

                    droppedSegmentRequests++;
                }

                if (executionTimeS > 60 && aliveTask.Value.FileCheckAwaitable != null)
                {
                    if (_aliveTasks.TryRemove(aliveTask.Key, out var timeoutentry))
                        timeoutentry.FileCheckAwaitable.SetResult(-1);
                    TimeoutFilechecks.Update(1);

                    Logger.Warn($"Timeout f-chk-request token:{aliveTask.Key}");

                    droppedFileChecks++;
                }
            }

            if (droppedFileChecks > 0 || droppedSegmentRequests > 0)
                Logger.Warn($"Dropping timeouted requests filecheck: {droppedFileChecks} segments: {droppedSegmentRequests}");

            foreach (var hyperClientSession in _sessions)
            {
                hyperClientSession.ValidateConnections();
            }
        }

        public async Task DownloadFile(string virtualPath, string systemPath, bool lowPriority = false)
        {
            var size = await GetFileSize(virtualPath).ConfigureAwait(false);
            long position = 0;

            if (size < 0)
                throw new FileNotFoundException($"There is no {virtualPath} on the server");

            using (lowPriority ? ThreadUtility.EnterBackgroundProcessingMode() : null)
            using (var fs = File.OpenWrite(systemPath))
            {
                while (position < size)
                {
                    var chunkLength = (int)Math.Min(1024 * 1024, size - position);
                    using (var bytes = await DownloadSegment(virtualPath, position, chunkLength).ConfigureAwait(false))
                    {
                        if (bytes.Object == null)
                            throw new IOException();

                        await fs.WriteAsync(bytes.Object, 0, chunkLength).ConfigureAwait(false);
                    }

                    position += chunkLength;
                }
            }
        }

        /// <summary>
        /// Downloads chunk asynchronously and returns reusable object
        /// </summary>
        /// <param name="path"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public Task<ReusableObject<byte[]>> DownloadSegment(string path, long offset, int length)
        {
            var session = _sessions.FirstOrDefault(s => path.StartsWith(s.Server));

            if (session == null)
            {
                if (_sessions.Count == 0)
                    throw new InvalidOperationException("No hyper sessions available for the request");

                session = _sessions.First();
            }

            var token = session.CreateToken();
            
            var awaitable = new TaskCompletionSource<ReusableObject<byte[]>>();
            
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
                throw new IOException("Session for the file is not found");

            if (!session.IsActive)
                throw new IOException("Session is not active yet");

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

        public async Task CopyFileTo(string path, Stream stream, long offset = 0, long length = -1, int reqAhead = 10)
        {
            var endPosition = await GetFileSize(path).ConfigureAwait(false);
            var position = offset;
            var segQueue = new Queue<KeyValuePair<Task<ReusableObject<byte[]>>, int>>();

            if (length != -1)
                endPosition = position + length;
            else if (position != 0)
            {
                endPosition -= position;
            }

            int tries = 0;

            while (position < endPosition || segQueue.Count > 0)
            {
                while (position < endPosition && segQueue.Count < reqAhead)
                {
                    var chunkLength = (int)Math.Min(1024 * 1024, endPosition - position);
                    segQueue.Enqueue(new KeyValuePair<Task<ReusableObject<byte[]>>, int>(DownloadSegment(path, position, chunkLength), chunkLength));
                    position += chunkLength;
                }

                var pair = segQueue.Dequeue();

                using (var bytes = await pair.Key.ConfigureAwait(false))
                {
                    if (bytes.Object == null)
                        throw new IOException("Can't download segment from hyper connection");

                    await stream.WriteAsync(bytes.Object, 0, pair.Value).ConfigureAwait(false);
                }
            }

            stream.Flush();
        }
    }
}