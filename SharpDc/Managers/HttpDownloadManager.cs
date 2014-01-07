// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using SharpDc.Connections;
using SharpDc.Structs;

namespace SharpDc.Managers
{
    /// <summary>
    /// Allows to download multiple segments with connections reusing 
    /// </summary>
    public class HttpDownloadManager : IDisposable
    {
        private List<HttpPool> _pools;

        private readonly Queue<HttpCacheSegment> _cache = new Queue<HttpCacheSegment>();
        private readonly object _syncRoot = new object();
        private int _cacheSize;

        public List<HttpPool> Pools
        {
            get { return _pools; }
        }

        /// <summary>
        /// Maximum connections allowed per-server 
        /// </summary>
        public int ConnectionsPerServer { get; set; }

        /// <summary>
        /// Gets maximum queue size per pool
        /// </summary>
        public int QueueLimit { get; set; }

        public int CacheSize
        {
            get { return _cacheSize; }
            set { 
                _cacheSize = value;
                FreeMemory(0);
            }
        }

        public int MemoryUsed { get; private set; }

        public HttpDownloadManager()
        {
            _pools = new List<HttpPool>();
            ConnectionsPerServer = 3;
            QueueLimit = 10;
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
                Buffer.BlockCopy(segment.Buffer, 0, buffer, 0, length);
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

            task.Event.Wait();

            if (CacheSize > 0)
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
                                QueueLimit = QueueLimit
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