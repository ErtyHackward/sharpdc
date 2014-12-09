using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using SharpDc.Logging;
using SharpDc.Structs;

namespace SharpDc.Connections
{
    /// <summary>
    /// Provides work distribution across workers
    /// </summary>
    public class HyperStorageManager
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly ConcurrentQueue<HyperServerTask> _tasks = new ConcurrentQueue<HyperServerTask>();

        public MovingAverage SegmentService { get; private set; }

        public SpeedAverage SegmentsPerSecond { get; private set; }

        public int QueueSize
        {
            get { return _tasks.Count; }
        }

        public string SystemPath { get; private set; }

        public int MaxWorkers { get; set; }

        public bool IsEnabled { get; set; }

        public HyperStorageManager(string systemPath)
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

        private readonly List<Thread> _workers = new List<Thread>(); 

        public void Start()
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
                HyperServerTask task;
                while (_tasks.TryDequeue(out task))
                {
                    try
                    {
                        var path = Path.Combine(SystemPath, task.Path);

                        if (task.Length > 0)
                        {
                            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,task.Length))
                            {
                                fs.Position = task.Offset;

                                var read = fs.Read(task.Buffer, 0, task.Length);

                                if (read != task.Length)
                                {
                                    Logger.Error("Can't read all bytes {0}/{1} {2}", read, task.Length, task.Path);
                                    continue;
                                }
                                
                                SegmentService.Update((int)((Stopwatch.GetTimestamp() - task.Created) / (Stopwatch.Frequency / 1000)));
                                SegmentsPerSecond.Update(1);

                                task.Session.EnqueueSend(task);
                            }
                        }
                        else
                        {
                            var msg = new HyperFileResultMessage();

                            msg.Token = task.Token;
                            msg.Size = new FileInfo(path).Length;
                            
                            task.Session.EnqueueSend(msg);
                        }
                    }
                    catch (Exception x)
                    {
                        Logger.Error("Error during reading of data {0} {1}", task.Path, x.Message);
                    }
                }
                Thread.Sleep(10);
            }
        }
    }
}