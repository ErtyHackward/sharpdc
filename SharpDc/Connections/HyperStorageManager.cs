using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpDc.Interfaces;
using SharpDc.Logging;
using SharpDc.Managers;

namespace SharpDc.Connections
{
    public class HyperStorageManager
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly Dictionary<string, IHyperStorage> _cachedStorages = new Dictionary<string, IHyperStorage>();
        private readonly List<IHyperStorage> _storages = new List<IHyperStorage>();

        /// <summary>
        /// How many threads to use for each storage
        /// </summary>
        public int WorkersPerStorage { get; set; }

        public HyperStorageManager()
        {
            WorkersPerStorage = 32;
        }

        public void RegisterFileStorage(string systemPath, bool isAsync = false)
        {
            if (!Directory.Exists(systemPath))
                throw new DirectoryNotFoundException($"Cannot register storage {systemPath} because it is not exists");

            AddStorage(isAsync ?
                (IHyperStorage)new HyperAsyncDriveReader(systemPath) :
                new HyperDriveReader(systemPath) { MaxWorkers = WorkersPerStorage });

            Logger.Info($"Registered {(isAsync ? "async" : "")} file storage {systemPath}");
        }

        public void RegisterRelayStorage(HyperDownloadManager downloadManager, UploadCacheManager cacheManager, IShare share, List<string> baseList)
        {
            AddStorage(new HyperRelayReader(downloadManager, cacheManager, share, baseList));
            Logger.Info($"Registered relay file storage {string.Join(", ", downloadManager.Sessions().Select(s => s.Server))}");
        }

        private void AddStorage(IHyperStorage storage)
        {
            _storages.Add(storage);

            storage.FileGone += Storage_FileGone;
        }

        private void Storage_FileGone(object sender, FileGoneEventArgs e)
        {
            lock (_cachedStorages)
            {
                if (_cachedStorages.ContainsKey(e.RelativePath))
                    _cachedStorages.Remove(e.RelativePath);
            }
        }

        public IEnumerable<IHyperStorage> AllStorages()
        {
            foreach (var hyperStorageManager in _storages)
            {
                yield return hyperStorageManager;
            }
        }

        public IHyperStorage ResolveStorage(string path)
        {
            lock (_cachedStorages)
            {
                if (_cachedStorages.TryGetValue(path, out var storage))
                    return storage;
            }
            lock (_cachedStorages)
            {
                foreach (var hyperStorageManager in _storages)
                {
                    if (hyperStorageManager.Contains(path))
                    {
                        _cachedStorages.Add(path, hyperStorageManager);
                        return hyperStorageManager;
                    }
                }

                _cachedStorages.Add(path, null);
            }

            return null;
        }

        public void StartAsync()
        {
            foreach (var storage in _storages)
            {
                storage.StartAsync();
            }
        }
    }
}