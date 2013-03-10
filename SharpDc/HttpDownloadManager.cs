using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using SharpDc.Connections;
using SharpDc.Logging;

namespace SharpDc
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

        public HttpDownloadManager()
        {
            _pools = new List<HttpPool>();
            ConnectionsPerServer = 10;
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

                var p = new HttpPool { Server = server, ConnectionsLimit = ConnectionsPerServer };

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

    public class HttpTask
    {
        public byte[] Buffer;
        public long FilePosition;
        public int Length;
        public long CreatedTimestamp;
        public bool Completed;
        public string Url;
        public ManualResetEventSlim Event;
        public HttpConnection Connection;

        private int _pos;

        public HttpTask()
        {
            CreatedTimestamp = Stopwatch.GetTimestamp();
            Event = new ManualResetEventSlim();
        }

        public void SetConnection(HttpConnection connection)
        {
            Connection = connection;
            connection.DataRecieved += connection_DataRecieved;
            connection.ConnectionStatusChanged += connection_ConnectionStatusChanged;

            connection.SetRange(FilePosition, FilePosition + Length - 1);
            connection.RequestAsync(Url);
        }

        void connection_ConnectionStatusChanged(object sender, Events.ConnectionStatusEventArgs e)
        {
            if (e.Status == Events.ConnectionStatus.Disconnected)
            {
                Event.Set();
                Cleanup();
            }
        }

        void connection_DataRecieved(object sender, HttpDataEventArgs e)
        {
            System.Buffer.BlockCopy(e.Buffer, e.BufferOffset, Buffer, _pos, e.Length);
            _pos += e.Length;

            if (_pos == Length)
            {
                Completed = true;
                Event.Set();
                Cleanup();
            }
        }

        private void Cleanup()
        {
            if (Connection != null)
            {
                Connection.ConnectionStatusChanged -= connection_ConnectionStatusChanged;
                Connection.DataRecieved -= connection_DataRecieved;
            }
            Connection = null;
        }
    }

    public class HttpPool : IDisposable
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        public List<HttpConnection> Connections;
        public List<HttpTask> Tasks;
        public object SyncRoot;
        public string Server;

        public int ConnectionsLimit;

        private List<HttpConnection> _freeList;

        public HttpPool()
        {
            SyncRoot = new object();
            Connections = new List<HttpConnection>();
            Tasks = new List<HttpTask>();
            _freeList = new List<HttpConnection>();
            ConnectionsLimit = 10;
        }

        private void DeleteOldTasks()
        {
            lock (SyncRoot)
            {
                foreach (var httpTask in Tasks.Where(t =>  (Stopwatch.GetTimestamp() - t.CreatedTimestamp) / Stopwatch.Frequency > 4))
                {
                    httpTask.Event.Set();
                }

                Tasks.RemoveAll(t => (Stopwatch.GetTimestamp() - t.CreatedTimestamp) / Stopwatch.Frequency > 4);
            }
        }
        
        public void StartTask(HttpTask task)
        {
            DeleteOldTasks();

            lock (SyncRoot)
            {
                if (_freeList.Count > 0)
                {
                    task.SetConnection(_freeList[0]);
                    _freeList.RemoveAt(0);
                    return;
                }
                
                if (Connections.Count < ConnectionsLimit)
                {
                    var httpCon = new HttpConnection();
                    httpCon.ConnectionStatusChanged += HttpConConnectionStatusChanged;
                    httpCon.RequestComplete += HttpConRequestComplete;
                    Connections.Add(httpCon);

                    task.SetConnection(httpCon);
                    return;
                }

                Tasks.Add(task);
            }
        }

        void HttpConRequestComplete(object sender, EventArgs e)
        {
            var httpCon = (HttpConnection)sender;

            DeleteOldTasks();

            lock (SyncRoot)
            {
                if (Tasks.Count > 0)
                {
                    var task = Tasks[0];
                    Tasks.RemoveAt(0);

                    task.SetConnection(httpCon);
                }
                else
                {
                    _freeList.Add(httpCon);
                }
            }
        }

        void HttpConConnectionStatusChanged(object sender, Events.ConnectionStatusEventArgs e)
        {
            if (e.Status == Events.ConnectionStatus.Disconnected)
            {
                var httpCon = (HttpConnection)sender;

                httpCon.ConnectionStatusChanged -= HttpConConnectionStatusChanged;
                httpCon.RequestComplete -= HttpConRequestComplete;

                lock (SyncRoot)
                {
                    Connections.Remove(httpCon);
                    _freeList.Remove(httpCon);
                }
            }
        }
        
        public void Dispose()
        {
            lock (SyncRoot)
            {
                foreach (var httpConnection in Connections)
                {
                    httpConnection.DisconnectAsync();
                }
            }
        }
    }

}
