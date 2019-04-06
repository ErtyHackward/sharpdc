using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SharpDc.Logging;
using SharpDc.Structs;

namespace SharpDc.Connections
{
    /// <summary>
    /// Provides work distribution across workers of a single drive
    /// </summary>
    public class HyperDriveReader : IHyperStorage
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly ConcurrentQueue<HyperServerTask> _tasks = new ConcurrentQueue<HyperServerTask>();
        private readonly List<Thread> _workers = new List<Thread>();

        public MovingAverage SegmentService { get; }

        public SpeedAverage SegmentsPerSecond { get; }

        public int QueueSize => _tasks.Count;

        public string SystemPath { get; }

        public int MaxWorkers { get; set; }

        public bool IsEnabled { get; set; }

        public event EventHandler<FileGoneEventArgs> FileGone;

        protected virtual void OnFileGone(FileGoneEventArgs e)
        {
            FileGone?.Invoke(this, e);
        }

        public HyperDriveReader(string systemPath)
        {
            SystemPath = systemPath;
            IsEnabled = true;

            SegmentService = new MovingAverage(TimeSpan.FromSeconds(10));
            SegmentsPerSecond = new SpeedAverage(TimeSpan.FromSeconds(10));
        }

        public void EnqueueTask(HyperServerTask task)
        {
            _tasks.Enqueue(task);
        }

        

        public bool Contains(string path)
        {
            return File.Exists(Path.Combine(SystemPath, path));
        }

        public string DebugLine()
        {
            return $"Storage {SystemPath} SVC: {(int)SegmentService.GetAverage()} RPS: {(int)SegmentsPerSecond.GetSpeed()} Q: {QueueSize}";
        }
        
        public void StartAsync()
        {
            Logger.Info("Starting {1} workers for {0}", SystemPath, MaxWorkers);
            foreach (var worker in _workers)
            {
                worker.Abort();
            }

            _workers.Clear();

            for (int i = 0; i < MaxWorkers; i++)
            {
                _workers.Add(new Thread(Worker));
            }

            foreach (var worker in _workers)
            {
                worker.Start();
            }
        }

        private void Worker()
        {
            while (IsEnabled)
            {
                while (_tasks.TryDequeue(out var task))
                {
                    var path = Path.Combine(SystemPath, task.Path);

                    try
                    {
                        if (task.Length > 0)
                        {
                            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, task.Length))
                            {
                                fs.Position = task.Offset;

                                var read = fs.Read(task.Buffer.Object, 0, task.Length);

                                if (read != task.Length)
                                {
                                    Logger.Error("Can't read all bytes {0}/{1} {2}", read, task.Length, task.Path);
                                    continue;
                                }
                                
                                SegmentService.Update(task.SinceCreatedMs);
                                SegmentsPerSecond.Update(1);
                            }
                        }
                        else
                        {
                            var fi = new FileInfo(path);

                            if (!fi.Exists)
                                task.FileLength = -1;
                            else
                                task.FileLength = fi.Length;
                        }
                        
                        task.Done();
                    }
                    catch (Exception x)
                    {
                        if (x is FileNotFoundException)
                        {
                            OnFileGone(new FileGoneEventArgs
                            {
                                RelativePath = task.Path,
                                SystemPath = path
                            });
                        }

                        Logger.Error("Error during reading of data {0} {1}", task.Path, x.Message);
                    }
                }
                Thread.Yield();
            }
        }


    }
}