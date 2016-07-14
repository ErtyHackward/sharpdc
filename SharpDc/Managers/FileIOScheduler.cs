using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpDc.Helpers;

namespace SharpDc.Managers
{

    public static class FileStreamFactory
    {
        public static bool UsePriorityFileStreams { get; set; }

        public static FileStream CreateFileStream(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize = 4096, FileOptions options = FileOptions.None)
        {
            if (UsePriorityFileStreams)
            {
                options |= FileOptions.Asynchronous;
                return new PriorityFileStream(path, mode, access, share, bufferSize, options);
            }
            return new FileStream(path, mode, access, share, bufferSize, options);
        }

    }

    public class PriorityFileStream : FileStream
    {
        private readonly FileIOPriority _priority;

        public PriorityFileStream(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options ) : 
            base(path, mode, access, share, bufferSize, options)
        {
            _priority = ThreadUtility.InBackgorundMode() ? FileIOPriority.Background : FileIOPriority.Default;
        }

        public PriorityFileStream(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options, FileIOPriority ioPriority) :
            base(path, mode, access, share, bufferSize, options)
        {
            _priority = ioPriority;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer, offset, count).Result;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteAsync(buffer, offset, count).Wait();
        }

        public Task<int> BaseReadAsync(byte[] buffer, int offset, int count)
        {
            return Task<int>.Factory.FromAsync(base.BeginRead(buffer, offset, count, null, null), base.EndRead);
        }

        public Task BaseWriteAsync(byte[] buffer, int offset, int count)
        {
            return Task.Factory.FromAsync(base.BeginWrite(buffer, offset, count, null, null), base.EndWrite);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return FileIOScheduler.Instance.Operation(
                FileOperationType.Read,
                Name,
                Position,
                count,
                _priority,
                buffer,
                offset,
                this);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return FileIOScheduler.Instance.Operation(
                FileOperationType.Write,
                Name,
                Position,
                count,
                _priority,
                buffer,
                offset,
                this);
        }

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            throw new NotSupportedException("Use Async methods instead");
        }

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            throw new NotSupportedException("Use Async methods instead");
        }
    }


    public class FileIOScheduler
    {
        internal static FileIOScheduler Instance = new FileIOScheduler();

        private readonly List<KeyValuePair<string, DriveIOScheduler >> _drives = new List<KeyValuePair<string, DriveIOScheduler>>();

        public int MaxConcurrentOperations { get; set; } = 16;

        public Task<int> Operation(
            FileOperationType operation,
            string systemPath,
            long position,
            int length,
            FileIOPriority priority,
            byte[] buffer,
            int bufferOffset,
            PriorityFileStream fs = null)
        {
            var drive = Path.GetPathRoot(systemPath);

            DriveIOScheduler scheduler;

            lock (_drives)
                scheduler = _drives.FirstOrDefault(p => p.Key == drive).Value;

            if (scheduler == null)
            {
                scheduler = new DriveIOScheduler();
                lock (_drives)
                    _drives.Add(new KeyValuePair<string, DriveIOScheduler>(drive, scheduler));
            }

            return scheduler.Operation(operation, systemPath, position, length, priority, buffer, bufferOffset, fs);
        }
    }

    public class DriveIOScheduler
    {
        private readonly Queue<FileIOOperation> _defaultQueue = new Queue<FileIOOperation>();
        private readonly Queue<FileIOOperation> _backgroundQueue = new Queue<FileIOOperation>();

        private int _activeOperations = 0;

        /// <summary>
        /// Gets how much opertions are active at this moment (simultaneous)
        /// </summary>
        public int ActiveOperations => _activeOperations;

        public int DefaultQueueSize => _defaultQueue.Count;

        public int BackgroundQueueSize => _backgroundQueue.Count;

        public DriveIOScheduler()
        {

        }

        public Task<int> Operation(
            FileOperationType operation,
            string systemPath, 
            long position, 
            int length, 
            FileIOPriority priority, 
            byte[] buffer,
            int bufferOffset,
            PriorityFileStream fs = null)
        {
            var op = new FileIOOperation
            {
                SystemPath = systemPath,
                Buffer = buffer,
                BufferOffset = bufferOffset,
                Position = position,
                Length = length,
                Operation = operation,
                TaskCompletionSource = new TaskCompletionSource<int>(),
                FileStream = fs
            };

            switch (priority)
            {
                case FileIOPriority.Default:
                    lock (_defaultQueue)
                        _defaultQueue.Enqueue(op);
                    break;
                case FileIOPriority.Background:
                    lock (_backgroundQueue)
                        _backgroundQueue.Enqueue(op);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(priority), priority, null);
            }


            if (Interlocked.Increment(ref _activeOperations) <= FileIOScheduler.Instance.MaxConcurrentOperations)
            {
                StartNewReadTask();
            }
            else
            {
                Interlocked.Decrement(ref _activeOperations);
            }


            return op.TaskCompletionSource.Task;
        }

        private async void StartNewReadTask()
        {
            while (true)
            {
                var op = TakeNextOperation();

                if (op.TaskCompletionSource == null)
                {
                    Interlocked.Decrement(ref _activeOperations);
                    return;
                }

                try
                {
                    switch (op.Operation)
                    {
                        case FileOperationType.Read:
                            op.TaskCompletionSource.SetResult(await op.FileStream.BaseReadAsync(op.Buffer, op.BufferOffset, op.Length).ConfigureAwait(false));
                            break;
                        case FileOperationType.Write:
                            await op.FileStream.BaseWriteAsync(op.Buffer, op.BufferOffset, op.Length).ConfigureAwait(false);
                            op.TaskCompletionSource.SetResult(op.Length);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException("Unknown operation");
                    }
                }
                catch (Exception x)
                {
                    op.TaskCompletionSource.SetException(x);
                }
            }
        }

        private FileIOOperation TakeNextOperation()
        {
            while (true)
            {
                if (_defaultQueue.Count == 0)
                {
                    if (_backgroundQueue.Count == 0)
                    {
                        return new FileIOOperation();
                    }

                    lock (_backgroundQueue)
                    {
                        if (_backgroundQueue.Count == 0)
                        {
                            return new FileIOOperation();
                        }

                        return _backgroundQueue.Dequeue();
                    }
                }
                else
                {
                    lock (_defaultQueue)
                    {
                        if (_defaultQueue.Count == 0)
                        {
                            return new FileIOOperation();
                        }

                        return _defaultQueue.Dequeue();
                    }
                }
            }
        }
    }

    public enum FileIOPriority
    {
        Default,
        Background
    }

    public enum FileOperationType
    {
        Read,
        Write
    }

    public struct FileIOOperation
    {
        public string SystemPath { get; set; }

        public long Position { get; set; }

        public int Length { get; set; }

        public byte[] Buffer { get; set; }

        public int BufferOffset { get; set; }

        public FileOperationType Operation { get; set; }

        public TaskCompletionSource<int> TaskCompletionSource { get; set; }

        public PriorityFileStream FileStream { get; set; }
    }
}
