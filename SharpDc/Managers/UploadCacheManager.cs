// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2014
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpDc.Helpers;
using SharpDc.Logging;
using SharpDc.Structs;

namespace SharpDc.Managers
{
    /// <summary>
    /// Provides local caching of remote uploads (server usage mostly)
    /// </summary>
    public class UploadCacheManager
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly DcEngine _engine;
        private readonly List<CachePoint> _points = new List<CachePoint>(); 
        private readonly object _syncRoot = new object();
        
        private readonly Dictionary<string, CachedItem> _items = new Dictionary<string, CachedItem>();
        private long _uploadedFromCache;
        private bool _listening;

        public long UploadedFromCache
        {
            get { return _uploadedFromCache; }
        }

        public UploadCacheManager(DcEngine engine)
        {
            _engine = engine;
        }

        void HttpUploadItem_HttpSegmentNeeded(object sender, HttpSegmentEventArgs e)
        {
            CachedItem item;

            lock (_syncRoot)
            {
                _items.TryGetValue(e.Magnet.TTH, out item);
            }

            if (item == null)
                return;

            if (!item.IsAreaCached(e.Position, e.Length))
                return;

            try
            {
                using (var fs = new FileStream(item.CachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 128))
                {
                    fs.Position = e.Position;
                    if (fs.Read(e.Buffer, 0, e.Length) != e.Length)
                        return;

                    e.FromCache = true;
                    lock (_syncRoot)
                    {
                        _uploadedFromCache += e.Length;
                    }
                }
            }
            catch (Exception exception)
            {
                Logger.Error("Cache read error {0}", exception.Message);
            }

        }

        void HttpUploadItem_HttpSegmentDownloaded(object sender, HttpSegmentEventArgs e)
        {
            CachedItem item;
            
            lock (_syncRoot)
            {
                _items.TryGetValue(e.Magnet.TTH, out item);
            }

            if (item == null)
            {
                StatItem statItem;
                if (!_engine.StatisticsManager.TryGetValue(e.Magnet.TTH, out statItem))
                    return;
                
                if (statItem.Rate < 2)
                    return;

                var list = new List<KeyValuePair<CachedItem, StatItem>>();
                
                lock (_syncRoot)
                {
                    foreach (var cachedItem in _items.Values)
                    {
                        StatItem si;
                        _engine.StatisticsManager.TryGetValue(e.Magnet.TTH, out si);
                        list.Add(new KeyValuePair<CachedItem, StatItem>(cachedItem, si));
                    }
                }

                // check for free point
                var point = _points.FirstOrDefault(p => p.FreeSpace > e.Magnet.Size);
                
                if (point == null)
                {
                    var removeList = new List<string>();

                    // remove expired items
                    foreach (var keyValuePair in list)
                    {
                        if (keyValuePair.Value.Expired)
                        {
                            try
                            {
                                File.Delete(keyValuePair.Key.CachePath);
                                lock (_syncRoot)
                                {
                                    _items.Remove(keyValuePair.Key.Magnet.TTH);
                                    _engine.StatisticsManager.RemoveItem(keyValuePair.Key.Magnet.TTH);
                                    var pInd = _points.FindIndex(cp => keyValuePair.Key.CachePath.StartsWith(cp.SystemPath));

                                    if (pInd != -1)
                                    {
                                        _points[pInd].CachedSpace -= keyValuePair.Key.Magnet.Size;
                                    }
                                }
                                removeList.Add(keyValuePair.Key.CachePath);
                            }
                            catch (Exception exception)
                            {
                                Logger.Error("Failed to delete expired item {0}, {1}", keyValuePair.Key.CachePath, exception.Message);
                            }
                        }
                    }

                    list.RemoveAll(p => removeList.Contains(p.Key.CachePath));

                    // check for free point again
                    point = _points.FirstOrDefault(p => p.FreeSpace > e.Magnet.Size);

                    if (point == null)
                    {
                        return;
                    }
                }

                item = new CachedItem(e.Magnet, 1024 * 1024)
                {
                    CachePath = Path.Combine(point.SystemPath, e.Magnet.TTH)
                };
                lock (_syncRoot)
                {
                    point.CachedSpace += e.Magnet.Size;
                    _items.Add(e.Magnet.TTH, item);
                }
            }

            if (item.SegmentLength != e.Length)
                return;

            using (new PerfLimit("Cache flush"))
            using (var fs = new FileStream(item.CachePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite, 1024 * 64))
            {
                fs.Position = e.Position;
                using (var ms = new MemoryStream(e.Buffer, 0, e.Length, false))
                {
                    ms.CopyTo(fs);
                }
            }

            lock (_syncRoot)
            {
                item.CachedSegments.Set(DownloadItem.GetSegmentIndex(e.Position, item.SegmentLength), true);
                if (item.CachedSegments.FirstFalse() == -1)
                    item.Complete = true;
            }
        }

        public void RegisterCachePoint(string path, long totalFreeSpace)
        {
            foreach (var filePath in Directory.EnumerateFiles(path))
            {
                File.Delete(filePath);
            }

            lock (_syncRoot)
            {
                _points.Add(new CachePoint
                {
                    SystemPath = path,
                    TotalSpace = totalFreeSpace
                });
            }

            if (!_listening)
            {
                HttpUploadItem.HttpSegmentDownloaded += HttpUploadItem_HttpSegmentDownloaded;
                HttpUploadItem.HttpSegmentNeeded += HttpUploadItem_HttpSegmentNeeded;
                _listening = true;
            }
        }
    }

    internal class CachePoint
    {
        public string SystemPath { get; set; }
        public long TotalSpace { get; set; }

        public long CachedSpace { get; set; }

        public long FreeSpace {
            get { return TotalSpace - CachedSpace; }
        }
    }
}
