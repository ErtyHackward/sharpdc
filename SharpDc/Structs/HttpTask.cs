// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SharpDc.Connections;
using SharpDc.Logging;

namespace SharpDc.Structs
{
    public class HttpTask
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        
        public TransferConnection Transfer;
        public long FilePosition;
        public long Length;
        public long CreatedTimestamp;
        public long AssignedTimestamp;
        public bool Completed;
        public string Url;
        public HttpConnection Connection;
        private TaskCompletionSource<bool> _taskCompletionSource;
        
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
            _taskCompletionSource = new TaskCompletionSource<bool>();
        }

        public Task<bool> GetTask()
        {
            return _taskCompletionSource.Task;
        }

        public async void Execute(HttpConnection connection)
        {
            AssignedTimestamp = Stopwatch.GetTimestamp();
            Connection = connection;

            try
            {
                await connection.CopyHttpChunkToAsync(Transfer, Url, FilePosition, Length).ConfigureAwait(false);
                Completed = true;
                _taskCompletionSource.TrySetResult(true);
            }
            catch (Exception x)
            {
                Logger.Error("Async copy failed {0} {1}", x.Message, x.StackTrace);
                _taskCompletionSource.TrySetResult(false);
            }
        }

        public void Cancel()
        {
            _taskCompletionSource.TrySetResult(false);
        }
    }
}