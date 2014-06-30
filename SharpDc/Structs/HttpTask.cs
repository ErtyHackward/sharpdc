// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;
using SharpDc.Connections;
using SharpDc.Logging;

namespace SharpDc.Structs
{
    public class HttpTask
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private int _pos;

        public byte[] Buffer;
        public long FilePosition;
        public int Length;
        public long CreatedTimestamp;
        public long AssignedTimestamp;
        public bool Completed;
        public string Url;
        public ManualResetEvent Event;
        public HttpConnection Connection;
        
        /// <summary>
        /// Gets total wait time
        /// </summary>
        public TimeSpan ExecutionTime
        {
            get { return TimeSpan.FromSeconds((double)(Stopwatch.GetTimestamp() - CreatedTimestamp) / Stopwatch.Frequency); }
        }

        /// <summary>
        /// Gets time spent in the queue
        /// </summary>
        public TimeSpan QueueTime {
            get
            {
                return AssignedTimestamp == 0 ? 
                    TimeSpan.FromSeconds((double)(Stopwatch.GetTimestamp() - CreatedTimestamp) / Stopwatch.Frequency) : 
                    TimeSpan.FromSeconds((double)(AssignedTimestamp - CreatedTimestamp) / Stopwatch.Frequency);
            }
        }

        public HttpTask()
        {
            CreatedTimestamp = Stopwatch.GetTimestamp();
            Event = new ManualResetEvent(false);
        }

        public void SetConnection(HttpConnection connection)
        {
            AssignedTimestamp = Stopwatch.GetTimestamp();
            Connection = connection;
            connection.DataRecieved += ConnectionDataRecieved;
            connection.ConnectionStatusChanged += ConnectionConnectionStatusChanged;

            connection.SetRange(FilePosition, FilePosition + Length - 1);
            using (new PerfLimit("Http segment request", 50))
                connection.Request(Url);
        }

        private void ConnectionConnectionStatusChanged(object sender, Events.ConnectionStatusEventArgs e)
        {
            if (e.Status == Events.ConnectionStatus.Disconnected)
            {
                Logger.Error("Dropping task because of disconnect");
                Event.Set();
                Cleanup();
            }
        }

        private void ConnectionDataRecieved(object sender, HttpDataEventArgs e)
        {
            System.Buffer.BlockCopy(e.Buffer, e.BufferOffset, Buffer, _pos, e.Length);
            _pos += e.Length;

            if (_pos == Length)
            {
                Cleanup();
                Completed = true;
                Event.Set();
            }
        }

        private void Cleanup()
        {
            lock (Event)
            {
                if (Connection != null)
                {
                    Connection.ConnectionStatusChanged -= ConnectionConnectionStatusChanged;
                    Connection.DataRecieved -= ConnectionDataRecieved;
                }
                Connection = null;
            }
        }
    }
}