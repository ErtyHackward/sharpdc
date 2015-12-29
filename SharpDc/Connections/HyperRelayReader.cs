using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using SharpDc.Helpers;
using SharpDc.Interfaces;
using SharpDc.Logging;
using SharpDc.Managers;
using SharpDc.Messages;
using SharpDc.Structs;

namespace SharpDc.Connections
{
    /// <summary>
    /// Provides read from another HYPER server with local cache support
    /// </summary>
    public class HyperRelayReader : IHyperStorage 
    {
        private readonly HyperDownloadManager _manager;
        private readonly UploadCacheManager _cacheManager;
        private readonly IShare _share;
        private readonly List<string> _basePaths;
        private static readonly ILogger Logger = LogManager.GetLogger();
        private int _activeOperations;
        private readonly Dictionary<string, string> _pathIndex = new Dictionary<string, string>();
        private readonly Timer _updateTimer;

        /// <summary>
        /// Gets total segment service time of the relay
        /// </summary>
        public MovingAverage SegmentService { get; }

        /// <summary>
        /// Gets total segments per second of the relay
        /// </summary>
        public SpeedAverage SegmentsPerSecond { get; }

        /// <summary>
        /// Gets average time of proxy requests
        /// </summary>
        public MovingAverage SegmentServiceProxy { get; }

        /// <summary>
        /// Gets total segments per second of the proxy requests
        /// </summary>
        public SpeedAverage SegmentsPerSecondProxy { get; }

        /// <summary>
        /// Gets average time of cached requests
        /// </summary>
        public MovingAverage SegmentServiceCached { get; }

        /// <summary>
        /// Gets total segments per second of the cached requests
        /// </summary>
        public SpeedAverage SegmentsPerSecondCached { get; }
        
        /// <summary>
        /// Gets amount of currently active operations
        /// </summary>
        public int QueueSize => _activeOperations;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="manager"></param>
        /// <param name="cacheManager"></param>
        /// <param name="share"></param>
        /// <param name="basePaths">a list of base paths to exclude from full uri</param>
        public HyperRelayReader(HyperDownloadManager manager, UploadCacheManager cacheManager, IShare share, List<string> basePaths)
        {
            _manager = manager;
            _cacheManager = cacheManager;
            _share = share;
            _basePaths = basePaths;
            _share.TotalSharedChanged += _share_TotalSharedChanged;
            _updateTimer = new Timer(o => RebuildDictionary(), null, Timeout.Infinite, Timeout.Infinite);

            SegmentService = new MovingAverage(TimeSpan.FromSeconds(10));
            SegmentsPerSecond = new SpeedAverage(TimeSpan.FromSeconds(10));

            SegmentServiceProxy = new MovingAverage(TimeSpan.FromSeconds(10));
            SegmentsPerSecondProxy = new SpeedAverage(TimeSpan.FromSeconds(10));

            SegmentServiceCached = new MovingAverage(TimeSpan.FromSeconds(10));
            SegmentsPerSecondCached = new SpeedAverage(TimeSpan.FromSeconds(10));

        }

        private void _share_TotalSharedChanged(object sender, EventArgs e)
        {
            _updateTimer.Change(1000, Timeout.Infinite);
        }

        private void RebuildDictionary()
        {
            Logger.Info("Rebuilding share path index");

            var allShare = _share.Items().ToList();

            lock (_pathIndex)
            {
                _pathIndex.Clear();

                foreach (var contentItem in allShare)
                {
                    // http://192.168.11.11/share2/data/Eskadron_gusar_letuchih/01.avi

                    var path = contentItem.SystemPath;

                    foreach (var basePath in _basePaths)
                    {
                        if (path.StartsWith(basePath))
                            path = path.Substring(basePath.Length);
                    }

                    // hyper operates with windows style path separators
                    path = Uri.UnescapeDataString(path.Replace("/", "\\"));


                    if (!_pathIndex.ContainsKey(path))
                        _pathIndex.Add(path, contentItem.Magnet.TTH);
                    else
                    {
                        Logger.Error($"Duplicate path entry detected {path} {contentItem.Magnet.TTH} {(_pathIndex[path])}");
                    }
                }
            }
            Logger.Info("Rebuilding share path index done");

        }

        public async void EnqueueTask(HyperServerTask task)
        {
            try
            {
                Interlocked.Increment(ref _activeOperations);

                var normalizedPath = task.Path.Replace("/", "\\");

                if (normalizedPath.StartsWith("/"))
                    normalizedPath = normalizedPath.Remove(0,1);


                string tth;
                lock (_pathIndex)
                {
                    if (!_pathIndex.TryGetValue(normalizedPath, out tth))
                    {
                        Logger.Error($"Error when requesting hyper segment {normalizedPath} No such file in share index yet");
                        return;
                    }
                }

                var ci = _share.SearchByTth(tth);

                if (ci == null)
                {
                    Logger.Error($"TTH {tth} not found in share");
                    return;
                }

                if (task.IsSegmentRequest)
                {
                    if (!_cacheManager.ReadCacheSegment(tth, task.Offset, task.Length, task.Buffer.Object, 0))
                    {
                        if (ci.Value.SystemPath.StartsWith("hyp://"))
                        {
                            var segment = await _manager.DownloadSegment(task.Path, task.Offset, task.Length);

                            task.Buffer.Dispose();
                            task.Buffer = segment;
                        }
                        else if (ci.Value.SystemPath.StartsWith("http://"))
                        {
                            using (var stream = await HttpHelper.GetHttpChunkAsync(ci.Value.SystemPath, task.Offset, task.Length))
                            using (var ms = new MemoryStream(task.Buffer.Object))
                            {
                                stream.CopyTo(ms);
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException("Not supported source type " + ci.Value.SystemPath);
                        }

                        SegmentServiceProxy.Update((int)((Stopwatch.GetTimestamp() - task.Created) / (Stopwatch.Frequency / 1000)));
                        SegmentsPerSecondProxy.Update(1);
                    }
                    else
                    {
                        SegmentServiceCached.Update((int)((Stopwatch.GetTimestamp() - task.Created) / (Stopwatch.Frequency / 1000)));
                        SegmentsPerSecondCached.Update(1);
                    }

                    SegmentService.Update(
                        (int)((Stopwatch.GetTimestamp() - task.Created) / (Stopwatch.Frequency / 1000)));
                    SegmentsPerSecond.Update(1);
                }
                else
                {
                    task.FileLength = ci.Value.Magnet.Size;
                }
            }
            catch (Exception x)
            {
                Logger.Error($"Error when requesting hyper segment {task.Path} {x.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref _activeOperations);
                task.Done();
            }
        }

        public bool Contains(string path)
        {
            lock (_pathIndex)
            {
                return _pathIndex.ContainsKey(path);
            }
        }

        public string DebugLine()
        {
            return $"Relay SVC: {(int)SegmentService.GetAverage()} RPS: {(int)SegmentsPerSecond.GetSpeed()} Q: {QueueSize}";
        }

        public void StartAsync()
        {
            // nothing to do here
        }
    }
}