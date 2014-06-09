﻿// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2014
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
        private Timer _updateTimer;

        /// <summary>
        /// Gets current cache read speed
        /// </summary>
        public SpeedAverage CacheUseSpeed { get; private set; }

        public long UploadedFromCache
        {
            get { return _uploadedFromCache; }
        }

        public IEnumerable<CachedItem> CachedItems()
        {
            lock (_syncRoot)
            {
                foreach (var value in _items.Values)
                {
                    yield return value;    
                }
            }
        }

        public UploadCacheManager(DcEngine engine)
        {
            _engine = engine;
            CacheUseSpeed = new SpeedAverage();
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

                    CacheUseSpeed.Update(e.Length);

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
                    return; // no statistics on the item, ignore
                
                if (statItem.Rate < 1)
                    return; // the rate is too low, ignore

                // find current cached items rate

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

                // sort them by the rate ascending
                list = list.OrderBy(i => i.Value.TotalUploaded).ToList();

                // check for free point
                var point = _points.FirstOrDefault(p => p.FreeSpace > e.Magnet.Size);
                
                if (point == null)
                {
                    // check if the item has higher rate than one in the cache with gap 1
                    var possibleFreeSize =
                        list.Where(i => i.Value.TotalUploaded * 1.5 < statItem.TotalUploaded)
                            .Select(i => i.Value.Magnet.Size)
                            .DefaultIfEmpty(0)
                            .Sum();

                    if (possibleFreeSize < statItem.Magnet.Size)
                        return; // not enough space could be freed for this item

                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i].Value.TotalUploaded * 1.5 >= statItem.TotalUploaded)
                            return;

                        Logger.Info("Remove less important item from cache {0}", list[i].Key.Magnet);
                        try
                        {
                            RemoveItemFromCache(list[i].Key);
                        }
                        catch (Exception x)
                        {
                            Logger.Error("Can't delete cache file {0}", list[i].Key.Magnet);
                        }

                        point = _points.FirstOrDefault(p => p.FreeSpace > e.Magnet.Size);
                        if (point != null)
                        {
                            break;
                        }
                    }

                    if (point == null)
                    {
                        return;
                    }
                }

                item = new CachedItem(e.Magnet, e.Length)
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

        private void RemoveItemFromCache(CachedItem item)
        {
            File.Delete(item.CachePath);
            lock (_syncRoot)
            {
                _items.Remove(item.Magnet.TTH);
                var pInd = _points.FindIndex(cp => item.CachePath.StartsWith(cp.SystemPath));

                if (pInd != -1)
                {
                    _points[pInd].CachedSpace -= item.Magnet.Size;
                }
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
                _updateTimer = new Timer(EachDayAction, null, TimeSpan.FromHours(12), TimeSpan.FromDays(1));
            }
        }

        private void EachDayAction(object state)
        {
            var items = _engine.StatisticsManager.AllItems().ToList();

            foreach (var statItem in items)
            {
                var item = statItem;
                if (item.Rate > 1)
                {
                    item.TotalUploaded -= (long)(item.Magnet.Size * (item.Rate / 2));
                }

                _engine.StatisticsManager.SetItem(item);
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
