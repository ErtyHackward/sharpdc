using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using SharpDc.Logging;
using SharpDc.Structs;

namespace SharpDc.Connections
{
    /// <summary>
    /// Provides work distribution across workers of a single drive
    /// </summary>
    public class HyperAsyncDriveReader : IHyperStorage
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private int _activeOperations;

        public MovingAverage SegmentService { get; }

        public SpeedAverage SegmentsPerSecond { get; }

        public int QueueSize => _activeOperations;

        public string SystemPath { get; }
        
        public bool IsEnabled { get; set; }

        /// <inheritdoc />
        public event EventHandler<FileGoneEventArgs> FileGone;

        protected virtual void OnFileGone(FileGoneEventArgs e)
        {
            FileGone?.Invoke(this, e);
        }

        public HyperAsyncDriveReader(string systemPath)
        {
            SystemPath = systemPath;
            IsEnabled = true;

            SegmentService = new MovingAverage(TimeSpan.FromSeconds(10));
            SegmentsPerSecond = new SpeedAverage(TimeSpan.FromSeconds(10));
        }

        public async void EnqueueTask(HyperServerTask task)
        {
            var path = Path.Combine(SystemPath, task.Path);
            try
            {
                Interlocked.Increment(ref _activeOperations);
                
                if (task.IsSegmentRequest)
                {

                    using (
                        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, task.Length,
                            FileOptions.Asynchronous))
                    {
                        fs.Position = task.Offset;
                        var read = await fs.ReadAsync(task.Buffer.Object, 0, task.Length).ConfigureAwait(false);
                        if (read != task.Length)
                        {
                            Logger.Error("Can't read all bytes {0}/{1} {2}", read, task.Length, task.Path);
                            return;
                        }

                        SegmentService.Update(task.SinceCreatedMs);
                        SegmentsPerSecond.Update(1);
                    }
                }
                else
                {
                    task.FileLength = new FileInfo(path).Length;
                }

                task.Done();
            }
            catch (Exception x)
            {
                if (x is FileNotFoundException)
                {
                    OnFileGone(new FileGoneEventArgs {
                        RelativePath = task.Path,
                        SystemPath = path
                    });
                }

                Logger.Error($"Error when reading the file {task.Path} {x.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref _activeOperations);
            }
        }
        
        public bool Contains(string path)
        {
            return File.Exists(Path.Combine(SystemPath, path));
        }

        public string DebugLine()
        {
            return $"AStorage {SystemPath} SVC: {(int)SegmentService.GetAverage()} RPS: {(int)SegmentsPerSecond.GetSpeed()} Q: {QueueSize}";
        }

        /// <summary>
        /// Does noting
        /// </summary>
        public void StartAsync()
        {

        }


    }
}