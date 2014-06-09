// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using SharpDc.Connections;
using SharpDc.Logging;
using SharpDc.Structs;

namespace SharpDc.Managers
{
    /// <summary>
    /// Allows to download multiple segments with connections reusing 
    /// </summary>
    public class HttpDownloadManager : IDisposable
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly List<HttpPool> _pools;

        private readonly Queue<HttpCacheSegment> _cache = new Queue<HttpCacheSegment>();
        private readonly object _syncRoot = new object();
        private long _cacheSize;
        private int _connectionsPerServer;
        private int _queueLimit;

        public List<HttpPool> Pools
        {
            get { return _pools; }
        }

        /// <summary>
        /// Maximum connections allowed per-server 
        /// </summary>
        public int ConnectionsPerServer
        {
            get { return _connectionsPerServer; }
            set { 
                if (_connectionsPerServer == value)
                    return;

                _connectionsPerServer = value;

                lock (_pools)
                {
                    foreach (var pool in _pools)
                    {
                        pool.ConnectionsLimit = _connectionsPerServer;
                    }
                }
            }
        }

        public int ReceiveTimeout { get; set; }

        /// <summary>
        /// Gets maximum queue size per pool
        /// </summary>
        public int QueueLimit
        {
            get { return _queueLimit; }
            set { 
                if (_queueLimit == value)
                    return;
                _queueLimit = value;

                lock (_pools)
                {
                    foreach (var pool in _pools)
                    {
                        pool.QueueLimit = _queueLimit;
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets maximum cache size for http segments (in bytes)
        /// </summary>
        public long CacheSize
        {
            get { return _cacheSize; }
            set { 
                _cacheSize = value;
                FreeMemory(0);
            }
        }

        /// <summary>
        /// Gets bytes used for cache
        /// </summary>
        public long MemoryUsed { get; private set; }

        /// <summary>
        /// Gets total bytes downloaded using this manager
        /// </summary>
        public long TotalDownloaded { get; private set; }

        /// <summary>
        /// Gets total bytes were taken from the cache in this manager
        /// </summary>
        public long TotalFromCache { get; private set; }

        public MovingAverage SegmentDelay { get; set; }

        public HttpDownloadManager()
        {
            _pools = new List<HttpPool>();
            ConnectionsPerServer = 3;
            QueueLimit = 10;
            SegmentDelay = new MovingAverage(TimeSpan.FromSeconds(30));
        }

        private void FreeMemory(int length)
        {
            while (_cache.Count > 0 && MemoryUsed + length > CacheSize)
            {
                var seg = _cache.Dequeue();
                seg.Buffer = null;
                MemoryUsed -= seg.Length;
            }
        }

        public bool DownloadChunk(string url, byte[] buffer, long filePos, int length)
        {
            HttpCacheSegment segment;
            lock (_syncRoot)
            {
                segment = _cache.FirstOrDefault(s => s.Url == url && s.Position <= filePos && length <= s.Length - (filePos - s.Position));
            }

            if (segment.Buffer != null)
            {
                Buffer.BlockCopy(segment.Buffer, (int)(filePos - segment.Position), buffer, 0, length);
                lock (_syncRoot)
                {
                    TotalFromCache += length;
                }
                return true;
            }
            
            var parsedUrl = new HttpUrl(url);

            var pool = GetPool(parsedUrl.Server);

            var task = new HttpTask
                           {
                               Url = url,
                               Buffer = buffer,
                               FilePosition = filePos,
                               Length = length
                           };

            pool.StartTask(task);

            task.Event.WaitOne();

            if (task.Completed)
                SegmentDelay.Update((int)task.ExecutionTime.TotalMilliseconds);

            if (CacheSize > 0 && task.Completed)
            {
                segment.Url = url;
                segment.Position = filePos;
                segment.Length = length;
                segment.Buffer = new byte[segment.Length];
                Buffer.BlockCopy(task.Buffer, 0, segment.Buffer, 0, segment.Length);

                lock (_syncRoot)
                {
                    FreeMemory(length);
                    _cache.Enqueue(segment);
                    MemoryUsed += segment.Length;
                }
            }

            lock (_syncRoot)
            {
                TotalDownloaded += length;    
            }
            
            if (!task.Completed)
                logger.Error("Unable to complete the task");

            return task.Completed;
        }

        private HttpPool GetPool(string server)
        {
            lock (_pools)
            {
                foreach (var pool in _pools)
                {
                    if (pool.Server == server)
                        return pool;
                }

                var p = new HttpPool
                            {
                                Server = server,
                                ConnectionsLimit = ConnectionsPerServer,
                                QueueLimit = QueueLimit,
                                ReceiveTimeout = ReceiveTimeout
                            };

                _pools.Add(p);
                return p;
            }
        }

        public void Dispose()
        {
            lock (_pools)
            {
                foreach (var pool in _pools)
                {
                    pool.Dispose();
                }
                _pools.Clear();
            }
        }
    }

    public struct HttpCacheSegment
    {
        public byte[] Buffer;
        public long Position;
        public int Length;
        public string Url;
    }
}