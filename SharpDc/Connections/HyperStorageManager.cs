using System.Collections.Generic;
using System.IO;
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
            {
                Logger.Error("Cannot register storage {0} because it is not exists", systemPath);
                return;
            }

            _storages.Add(isAsync ? 
                (IHyperStorage)new HyperAsyncDriveReader(systemPath) : 
                new HyperDriveReader(systemPath) { MaxWorkers = WorkersPerStorage });

            Logger.Info($"Registered {(isAsync ? "async" : "")} file storage {systemPath}");
        }

        public void RegisterRelayStorage(HyperDownloadManager downloadManager, UploadCacheManager cacheManager, IShare share, List<string> baseList)
        {
            _storages.Add(new HyperRelayReader(downloadManager, cacheManager, share, baseList));
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
                IHyperStorage manager;
                if (_cachedStorages.TryGetValue(path, out manager))
                    return manager;
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