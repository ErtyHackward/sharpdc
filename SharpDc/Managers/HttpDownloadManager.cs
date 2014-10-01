// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        private readonly object _syncRoot = new object();
        private int _connectionsPerServer;
        private int _queueLimit;
        private long _totalDownloaded;

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
        /// Gets total bytes downloaded using this manager
        /// </summary>
        public long TotalDownloaded
        {
            get { return _totalDownloaded; }
            private set { _totalDownloaded = value; }
        }

        public MovingAverage SegmentDelay { get; set; }

        public HttpDownloadManager()
        {
            _pools = new List<HttpPool>();
            ConnectionsPerServer = 3;
            QueueLimit = 10;
            SegmentDelay = new MovingAverage(TimeSpan.FromSeconds(30));
        }

        public async Task<bool> CopyChunkToTransferAsync(TransferConnection transfer, string url, long filePos, long length)
        {
            var parsedUrl = new HttpUrl(url);

            var pool = GetPool(parsedUrl.Server);
            
            var task = new HttpTask
            {
                Url = url,
                Transfer = transfer,
                FilePosition = filePos,
                Length = length
            };

            await pool.ExecuteTaskAsync(task).ConfigureAwait(false);
            
            if (task.Completed)
                SegmentDelay.Update((int)task.ExecutionTime.TotalMilliseconds);

            Interlocked.Add(ref _totalDownloaded, length);
            
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