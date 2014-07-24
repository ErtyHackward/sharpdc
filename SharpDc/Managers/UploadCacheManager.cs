// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2014
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using SharpDc.Hash;
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
        private const float CacheGap = 2f;
        private const float RemoveThresold = 3f;

        private readonly Queue<Tuple<string, CachedItem>> _downloadQueue = new Queue<Tuple<string, CachedItem>>();
        private bool _downloadThreadAlive;


        /// <summary>
        /// If set only requested segments will be saved to the cache, otherwise the file will be cached completely when added
        /// </summary>
        public bool LazyCacheDownload { get; set; }

        /// <summary>
        /// Do we need to verify the data saved to the cache?
        /// </summary>
        public bool CacheVerification { get; set; }

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
                using (new PerfLimit(() => string.Format("Cache segment read {0}", item.CachePath), 300))
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

                if (!File.Exists(item.CachePath))
                {
                    RemoveItemFromCache(item);
                }
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

                if (statItem.Rate < CacheGap)
                    return; // the rate is too low, ignore

                lock (_syncRoot)
                {
                    if (!_items.TryGetValue(e.Magnet.TTH, out item))
                    {
                        // find current cached items rate
                        var list = new List<KeyValuePair<CachedItem, StatItem>>();

                        foreach (var cachedItem in _items.Values)
                        {
                            StatItem si;
                            if (_engine.StatisticsManager.TryGetValue(cachedItem.Magnet.TTH, out si))
                                list.Add(new KeyValuePair<CachedItem, StatItem>(cachedItem, si));
                            else
                            {
                                list.Add(new KeyValuePair<CachedItem, StatItem>(cachedItem,
                                    new StatItem { Magnet = e.Magnet }));
                            }
                        }

                        // check for free point
                        var point = _points.FirstOrDefault(p => p.FreeSpace > e.Magnet.Size);

                        if (point == null)
                        {
                            // sort them by the cache efficency ascending
                            list = list.OrderBy(i => i.Value.CacheEffectivity).ToList();

                            foreach (var cachePoint in _points)
                            {
                                // check if the item has higher rate than one in the cache with gap
                                var possibleFreeSize =
                                    list.Where(
                                        i =>
                                            i.Key.CachePath.StartsWith(cachePoint.SystemPath) &&
                                            i.Value.CacheEffectivity * RemoveThresold < statItem.CacheEffectivity)
                                        .Select(i => i.Value.Magnet.Size)
                                        .DefaultIfEmpty(0)
                                        .Sum();

                                if (possibleFreeSize < statItem.Magnet.Size)
                                    continue; // not enough space could be freed for this item

                                for (int i = 0; i < list.Count; i++)
                                {
                                    if (!list[i].Key.CachePath.StartsWith(cachePoint.SystemPath))
                                        continue;

                                    if (list[i].Value.CacheEffectivity * RemoveThresold >= statItem.CacheEffectivity)
                                        break;

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

                        if (LazyCacheDownload)
                        {
                            Exception ex;
                            if (!FileHelper.AllocateFile(item.CachePath, e.Magnet.Size, out ex))
                            {
                                Logger.Error("Cannot allocate file {0} {1} {2}", item.CachePath,
                                    Utils.FormatBytes(e.Magnet.Size), ex == null ? "" : ex.Message);
                                return;
                            }
                        }
                        else
                        {
                            lock (_downloadQueue)
                            {
                                if (_downloadQueue.Count > 5)
                                    return;

                                _downloadQueue.Enqueue(Tuple.Create(e.UploadItem.SystemPath, item));
                                if (!_downloadThreadAlive)
                                {
                                    _downloadThreadAlive = true;
                                    new ThreadStart(DownloadCacheFiles).BeginInvoke(null, null);
                                }
                            }
                        }

                        _items.Add(e.Magnet.TTH, item);
                        point.CachedSpace += e.Magnet.Size;
                    }
                }

            }

            if (LazyCacheDownload)
            {
                using (new PerfLimit("Cache flush"))
                using (var fs = new FileStream(item.CachePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite,1024 * 64))
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

                WriteBitfieldFile(item);
            }
        }

        private void WriteBitfieldFile(CachedItem item)
        {
            using (new PerfLimit("Bitfield flush"))
            using (var fs = new FileStream(item.BitFileldFilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite, 1024 * 64))
            using (var writer = new BinaryWriter(fs))
            {
                writer.Write(item.Magnet.TTH);
                writer.Write(item.Magnet.FileName);
                writer.Write(item.Magnet.Size);
                writer.Write(item.SegmentLength);

                var bytes = item.CachedSegments.ToBytes();
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
        }

        private void DownloadCacheFiles()
        {
            while (_downloadQueue.Count > 0)
            {
                Tuple<string, CachedItem> tuple;
                lock (_downloadQueue)
                {
                    tuple = _downloadQueue.Dequeue();
                }

                try
                {
                    var httpUri = tuple.Item1;
                    var item = tuple.Item2;

                    Logger.Info("Downloading file to the cache {0}", httpUri);

                    if (File.Exists(item.CachePath))
                        File.Delete(item.CachePath);

                    using (var wc = new WebClient())
                        wc.DownloadFile(httpUri, item.CachePath);

                    if (CacheVerification)
                    {
                        Logger.Info("Verifying the data");
                        var hasher = new ThexThreaded<TigerNative>();
                        hasher.LowPriority = true;
                        var tth = Base32Encoding.ToString(hasher.GetTTHRoot(item.CachePath));

                        if (tth == item.Magnet.TTH)
                        {
                            Logger.Info("File cache match {0}", tth);
                            item.Complete = true;
                            item.CachedSegments.SetAll(true);
                            WriteBitfieldFile(item);
                        }
                        else
                        {
                            Logger.Info("Error! File cache mismatch {0}, expected {1} repeating the download", tth,
                                item.Magnet.TTH);
                            File.Delete(item.CachePath);
                            lock (_downloadQueue)
                            {
                                _downloadQueue.Enqueue(tuple);
                            }
                            Thread.Sleep(TimeSpan.FromSeconds(1));
                        }
                    }
                    else
                    {
                        Logger.Info("Adding file without verification");
                        item.Complete = true;
                        item.CachedSegments.SetAll(true);
                        WriteBitfieldFile(item);
                    }
                }
                catch (Exception ex)
                {
                    while (ex != null)
                    {
                        Logger.Error("Error occured when processing download of cache item {0} {1}", tuple.Item1, ex.Message);
                        ex = ex.InnerException;
                    }

                    Thread.Sleep(TimeSpan.FromSeconds(1));
                }
            }

            lock (_downloadQueue)
            {
                _downloadThreadAlive = false;
            }
        }

        private void RemoveItemFromCache(CachedItem item)
        {
            File.Delete(item.CachePath);

            if (File.Exists(item.BitFileldFilePath))
                File.Delete(item.BitFileldFilePath);

            lock (_syncRoot)
            {
                if (_items.ContainsKey(item.Magnet.TTH))
                {
                    _items.Remove(item.Magnet.TTH);
                    var pInd = _points.FindIndex(cp => item.CachePath.StartsWith(cp.SystemPath));

                    if (pInd != -1)
                    {
                        _points[pInd].CachedSpace -= item.Magnet.Size;
                    }
                }
            }
        }

        public void RegisterCachePoint(string path, long totalFreeSpace)
        {
            var point = new CachePoint
            {
                SystemPath = path,
                TotalSpace = totalFreeSpace
            };
            lock (_syncRoot)
            {
                _points.Add(point);
            }

            foreach (var filePath in Directory.EnumerateFiles(path, "*.bitfield"))
            {
                using (var fs = File.OpenRead(filePath))
                using (var reader = new BinaryReader(fs))
                {
                    var magnet = new Magnet
                    {
                        TTH = reader.ReadString(),
                        FileName = reader.ReadString(),
                        Size = reader.ReadInt64()
                    };
                    var segmentLength = reader.ReadInt32();
                    var bytesLength = reader.ReadInt32();
                    var bitarray = new BitArray(reader.ReadBytes(bytesLength));
                    bitarray.Length = DownloadItem.SegmentsCount(magnet.Size, segmentLength);

                    var item = new CachedItem(magnet, segmentLength, bitarray)
                    {
                        CachePath = Path.Combine(point.SystemPath, magnet.TTH),
                        Created = new FileInfo(filePath).CreationTime
                    };

                    if (!LazyCacheDownload && bitarray.TrueCount() != bitarray.Count)
                    {
                        Logger.Error("Non-finished item found, removing {0}", item.CachePath);
                        fs.Close();
                        File.Delete(item.BitFileldFilePath);
                        if (File.Exists(item.CachePath))
                            File.Delete(item.CachePath);
                        continue;
                    }

                    if (File.Exists(item.CachePath))
                    {
                        lock (_syncRoot)
                        {
                            if (_items.ContainsKey(magnet.TTH))
                            {
                                Logger.Error("Duplicate cache item found, removing {0}", item.CachePath);
                                fs.Close();
                                File.Delete(item.BitFileldFilePath);
                                if (File.Exists(item.CachePath))
                                    File.Delete(item.CachePath);
                            }
                            else
                            {
                                point.CachedSpace += magnet.Size;
                                _items.Add(magnet.TTH, item);
                            }
                        }
                    }
                    else
                    {
                        fs.Close();
                        File.Delete(item.BitFileldFilePath);
                    }
                }
            }

            Logger.Info("Cache loaded {0}", Utils.FormatBytes(point.CachedSpace));

            // delete files without bitfields
            foreach (var filePath in Directory.EnumerateFiles(path))
            {
                if (Path.GetExtension(filePath) == ".bitfield")
                    continue;

                var file = Path.GetFileName(filePath);

                if (!_items.ContainsKey(file))
                {
                    Logger.Info("Removing invalid cache file {0}", file);
                    File.Delete(filePath);
                }
            }

            if (!_listening)
            {
                HttpUploadItem.HttpSegmentDownloaded += HttpUploadItem_HttpSegmentDownloaded;
                HttpUploadItem.HttpSegmentNeeded += HttpUploadItem_HttpSegmentNeeded;
                _listening = true;

                var updateInterval = TimeSpan.FromHours(12);
                _updateTimer = new Timer(PeriodicAction, null, updateInterval, updateInterval);
            }
        }

        private void PeriodicAction(object state)
        {
            Logger.Info("Decreasing statistics rates...");
            var items = _engine.StatisticsManager.AllItems().ToList();

            foreach (var statItem in items)
            {
                var item = statItem;

                item.TotalUploaded -= item.TotalUploaded / 2;
                _engine.StatisticsManager.SetItem(item);
            }
            Logger.Info("Decreasing statistics rates done");
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
