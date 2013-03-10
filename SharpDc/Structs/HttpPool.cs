using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SharpDc.Connections;
using SharpDc.Logging;

namespace SharpDc.Structs
{
    public class HttpPool : IDisposable
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        public List<HttpConnection> Connections;
        public List<HttpTask> Tasks;
        public object SyncRoot;
        public string Server;

        public int ConnectionsLimit { get; set; }

        public int QueueLimit { get; set; }

        private List<HttpConnection> _freeList;

        public HttpPool()
        {
            SyncRoot = new object();
            Connections = new List<HttpConnection>();
            Tasks = new List<HttpTask>();
            _freeList = new List<HttpConnection>();
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

            HttpConnection conn = null;

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
                    conn.ConnectionStatusChanged += HttpConConnectionStatusChanged;
                    conn.RequestComplete += HttpConRequestComplete;
                    Connections.Add(conn);
                }

                if (conn == null)
                {
                    if (Tasks.Count < QueueLimit)
                        Tasks.Add(task);
                    else
                    {
                        task.Event.Set();
                    }
                }
            }

            if (conn != null)
                task.SetConnection(conn);
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