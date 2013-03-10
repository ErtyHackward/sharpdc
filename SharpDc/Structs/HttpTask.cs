using System.Diagnostics;
using System.Threading;
using SharpDc.Connections;

namespace SharpDc.Structs
{
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
            connection.DataRecieved += ConnectionDataRecieved;
            connection.ConnectionStatusChanged += ConnectionConnectionStatusChanged;

            connection.SetRange(FilePosition, FilePosition + Length - 1);
            connection.RequestAsync(Url);
        }

        void ConnectionConnectionStatusChanged(object sender, Events.ConnectionStatusEventArgs e)
        {
            if (e.Status == Events.ConnectionStatus.Disconnected)
            {
                Event.Set();
                Cleanup();
            }
        }

        void ConnectionDataRecieved(object sender, HttpDataEventArgs e)
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
                Connection.ConnectionStatusChanged -= ConnectionConnectionStatusChanged;
                Connection.DataRecieved -= ConnectionDataRecieved;
            }
            Connection = null;
        }
    }
}