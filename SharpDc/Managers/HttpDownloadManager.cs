//  -------------------------------------------------------------
//  LiveDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.Collections.Generic;
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

        public HttpDownloadManager()
        {
            _pools = new List<HttpPool>();
            ConnectionsPerServer = 3;
            QueueLimit = 10;
        }


        public bool DownloadChunk(string url, byte[] buffer, long filePos, int length)
        {
            var parsedUrl = new HttpUrl(url);

            var pool = GetPool(parsedUrl.Server);

            var task = new HttpTask { 
                Url = url, 
                Buffer = buffer, 
                FilePosition = filePos, 
                Length = length 
            };

            pool.StartTask(task);
            
            task.Event.Wait();
            
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

                var p = new HttpPool { 
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
}
