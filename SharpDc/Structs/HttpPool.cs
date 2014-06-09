// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SharpDc.Connections;
using SharpDc.Logging;

namespace SharpDc.Structs
{
    public class HttpPool : IDisposable
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        public List<HttpConnection> Connections;
        /// <summary>
        /// Tasks waiting for free connection
        /// </summary>
        public List<HttpTask> TasksQueue;
        public object SyncRoot;
        public string Server;

        public int ConnectionsLimit { get; set; }

        public int QueueLimit { get; set; }

        public int ReceiveTimeout { get; set; }

        /// <summary>
        /// Connections without task assigned, can be used at any time
        /// </summary>
        private readonly List<HttpConnection> _freeList;

        public HttpPool()
        {
            SyncRoot = new object();
            Connections = new List<HttpConnection>();
            TasksQueue = new List<HttpTask>();
            _freeList = new List<HttpConnection>();
        }

        private void DeleteOldTasksFromQueue()
        {
            if (ReceiveTimeout == 0)
                return;

            // this operation is not reasonable to run multiple times in the short period
            if (Monitor.TryEnter(SyncRoot))
            {
                try
                {
                    using (new PerfLimit("Delete old tasks int"))
                    {
                        var removeList = new List<HttpTask>();

                        foreach (
                            var httpTask in
                                TasksQueue.Where(t => t.ExecutionTime.TotalMilliseconds > ReceiveTimeout))
                        {
                            Logger.Error("Dropping task because of time out {0}. QueueWait {1}", ReceiveTimeout, httpTask.QueueTime.TotalMilliseconds);
                            httpTask.Event.Set();
                            removeList.Add(httpTask);
                        }

                        TasksQueue.RemoveAll(removeList.Contains);
                    }
                }
                finally 
                {
                    Monitor.Exit(SyncRoot);
                }
            }
        }

        public void StartTask(HttpTask task)
        {
            DeleteOldTasksFromQueue();

            HttpConnection conn = null;
            
            using (new PerfLimit("Start task"))
            lock (SyncRoot)
            {
                if (_freeList.Count > 0)
                {
                    conn = _freeList[0];
                    _freeList.RemoveAt(0);
                }

                if (conn == null && Connections.Count < ConnectionsLimit)
                {
                    conn = new HttpConnection();
                    conn.ReceiveTimeout = ReceiveTimeout;
                    conn.ConnectionStatusChanged += HttpConConnectionStatusChanged;
                    conn.RequestComplete += HttpConRequestComplete;
                    conn.ReceiveTimeoutHit += conn_ReceiveTimeoutHit;
                    Connections.Add(conn);
                }

                if (conn == null)
                {
                    if (TasksQueue.Count < QueueLimit)
                        TasksQueue.Add(task);
                    else
                    {
                        Logger.Error("Dropping task because of full queue");
                        task.Event.Set();
                    }
                }
            }

            if (conn != null)
                task.SetConnection(conn);
        }

        private void conn_ReceiveTimeoutHit(object sender, EventArgs e)
        {
            var httpCon = (HttpConnection)sender;
            httpCon.DisconnectAsync();
        }

        private void HttpConRequestComplete(object sender, EventArgs e)
        {
            var httpCon = (HttpConnection)sender;
            
            DeleteOldTasksFromQueue();

            HttpTask task = null;
            lock (SyncRoot)
            {
                if (TasksQueue.Count > 0)
                {
                    task = TasksQueue[0];
                    TasksQueue.RemoveAt(0);
                }
                else
                {
                    _freeList.Add(httpCon);
                }
            }

            if (task != null)
                task.SetConnection(httpCon);
            
        }

        private void HttpConConnectionStatusChanged(object sender, Events.ConnectionStatusEventArgs e)
        {
            if (e.Status == Events.ConnectionStatus.Disconnected)
            {
                var httpCon = (HttpConnection)sender;

                httpCon.ConnectionStatusChanged -= HttpConConnectionStatusChanged;
                httpCon.RequestComplete -= HttpConRequestComplete;
                httpCon.ReceiveTimeoutHit -= conn_ReceiveTimeoutHit;

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