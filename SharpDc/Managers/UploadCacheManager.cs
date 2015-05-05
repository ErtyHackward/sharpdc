// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2014
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Xml.Serialization;
using SharpDc.Connections;
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
        private readonly Queue<Tuple<string, CachedItem>> _downloadQueue = new Queue<Tuple<string, CachedItem>>();
        private readonly Dictionary<string, CachedItem> _items = new Dictionary<string, CachedItem>();
        private readonly object _syncRoot = new object();

        private long _uploadedFromCache;
        private bool _listening;
        private Timer _updateTimer;
        private const float CacheGap = 10f;
        private const float RemoveThresold = 3f;
        
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

        /// <summary>
        /// Gets average time in milliseconds of segment read from the cache storage
        /// </summary>
        public MovingAverage CacheReadAverage { get; private set; }

        /// <summary>
        /// Defines day time ranges when download to a cache is disabled
        /// </summary>
        public List<DayTimeSpan> DisabledTime { get; set; }

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
            CacheReadAverage = new MovingAverage(TimeSpan.FromSeconds(10));
        }

        void HttpUploadItem_HttpSegmentNeeded(object sender, UploadItemSegmentEventArgs e)
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
                var pt = PerfTimer.StartNew();
                using (new PerfLimit(() => string.Format("Cache segment read {0}", item.CachePath), 1000))
                using (var fs = new FileStream(item.CachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 1024))
                {
                    var buffer = new byte[e.Length];
                    
                    fs.Position = e.Position;
                    if (fs.Read(buffer, 0, buffer.Length) != e.Length)
                        return;

                    e.Stream = new MemoryStream(buffer);

                    e.FromCache = true;

                    CacheUseSpeed.Update(e.Length);
                    CacheReadAverage.Update((int)pt.ElapsedMilliseconds);

                    Interlocked.Add(ref _uploadedFromCache, e.Length);
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

        void HttpUploadItem_HttpSegmentDownloaded(object sender, UploadItemSegmentEventArgs e)
        {
            CachedItem item;
            
            lock (_syncRoot)
            {
                _items.TryGetValue(e.Magnet.TTH, out item);
            }

            if (item == null)
            {
                var disabled = DisabledTime;
                if (disabled != null)
                { 
                    if (disabled.Any(s => s.IsMatch()))
                        return; // it's not the time to download items...
                }

                StatItem statItem;
                if (!_engine.StatisticsManager.TryGetValue(e.Magnet.TTH, out statItem))
                    return; // no statistics on the item, ignore

                if (statItem.CacheEffectivity < CacheGap)
                    return; // the eff is too low, ignore

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
                                            i.Key.Created + TimeSpan.FromMinutes(30) < DateTime.Now &&
                                            i.Key.CachePath.StartsWith(cachePoint.SystemPath) &&
                                            i.Value.CacheEffectivity * RemoveThresold < statItem.CacheEffectivity)
                                        .Select(i => i.Value.Magnet.Size)
                                        .DefaultIfEmpty(0)
                                        .Sum();

                                if (possibleFreeSize + cachePoint.FreeSpace < statItem.Magnet.Size)
                                    continue; // not enough space could be freed for this item

                                Logger.Info("Need more room for {0} {1} eff: {2:0.0}", statItem.Magnet.FileName, statItem.Magnet.TTH, statItem.CacheEffectivity);

                                for (int i = 0; i < list.Count; i++)
                                {
                                    if (list[i].Key.Created + TimeSpan.FromMinutes(30) > DateTime.Now)
                                        continue;

                                    if (!list[i].Key.CachePath.StartsWith(cachePoint.SystemPath))
                                        continue;

                                    if (list[i].Value.CacheEffectivity * RemoveThresold >= statItem.CacheEffectivity)
                                        break; // break because of sorted list

                                    Logger.Info("Remove less important item from cache {0} {1} {2} eff: {3:0.0} < {4:0.0}", list[i].Key.Magnet.FileName, Utils.FormatBytes(list[i].Key.Magnet.Size), list[i].Key.Magnet.TTH, list[i].Value.CacheEffectivity, statItem.CacheEffectivity);
                                    try
                                    {
                                        RemoveItemFromCache(list[i].Key);
                                    }
                                    catch (Exception x)
                                    {
                                        Logger.Error("Can't delete cache file {0}", list[i].Key.Magnet);
                                    }

                                    if (cachePoint.FreeSpace > e.Magnet.Size)
                                    {
                                        point = cachePoint;
                                        break;
                                    }
                                }

                                if (point != null)
                                    break;
                            }

                            if (point == null)
                            {
                                return;
                            }
                        }

                        item = new CachedItem(e.Magnet, (int)e.Length)
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

                                Logger.Info("Requesting download {0} to {1}", item.Magnet.FileName, item.CachePath);
                                _downloadQueue.Enqueue(Tuple.Create(e.UploadItem.SystemPath, item));
                                if (!_downloadThreadAlive)
                                {
                                    _downloadThreadAlive = true;
                                    new ThreadStart(DownloadCacheFiles).BeginInvoke(null, null);
                                }
                            }
                        }
                        
                        AddItemToCache(item, point);
                    }
                }

            }

            //if (LazyCacheDownload)
            //{
            //    using (new PerfLimit("Cache flush"))
            //    using (var fs = new FileStream(item.CachePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite,1024 * 64))
            //    {
            //        fs.Position = e.Position;
            //        using (var ms = new MemoryStream(e.Buffer, 0, e.Length, false))
            //        {
            //            ms.CopyTo(fs);
            //        }
            //    }

            //    lock (_syncRoot)
            //    {
            //        item.CachedSegments.Set(DownloadItem.GetSegmentIndex(e.Position, item.SegmentLength), true);
            //        if (item.CachedSegments.FirstFalse() == -1)
            //            item.Complete = true;
            //    }

            //    WriteBitfieldFile(item);
            //}
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

        private object _downloadLock = new object();
        private void DownloadCacheFiles()
        {
            lock (_downloadLock)
            {
                while (_downloadQueue.Count > 0)
                {
                    Tuple<string, CachedItem> tuple;
                    lock (_downloadQueue)
                    {
                        tuple = _downloadQueue.Dequeue();
                    }

                    var httpUri = tuple.Item1;
                    var item = tuple.Item2;

                    try
                    {
                        var point = _points.First(p => item.CachePath.StartsWith(p.SystemPath));

                        var driveInfo = new DriveInfo(Path.GetPathRoot(point.SystemPath));

                        Logger.Info("Downloading file to the cache {0} Estimated free space {1} Real free space {2} {3}", httpUri, point.FreeSpace, driveInfo.AvailableFreeSpace, point.SystemPath);

                        if (File.Exists(item.CachePath))
                            File.Delete(item.CachePath);

                        if (httpUri.StartsWith("http"))
                        {
                            using (var wc = new WebClient())
                                wc.DownloadFile(httpUri, item.CachePath);
                        }
                        else if (httpUri.StartsWith("hyp"))
                        {
                            HyperUploadItem.Manager.DownloadFile(httpUri, item.CachePath).RunSynchronously();
                        }

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
                            Logger.Error("Error occured when processing download of cache item {0} {1}", tuple.Item1,
                                ex.Message);
                            ex = ex.InnerException;
                        }

                        RemoveItemFromCache(item);
                        
                        Thread.Sleep(TimeSpan.FromSeconds(1));
                    }
                }

                lock (_downloadQueue)
                {
                    _downloadThreadAlive = false;
                }
            }
        }

        private void AddItemToCache(CachedItem item, CachePoint point)
        {
            if (_items.ContainsKey(item.Magnet.TTH))
                throw new InvalidOperationException("The item is already registered");

            _items.Add(item.Magnet.TTH, item);
            point.AddItem(item);
        }

        private void RemoveItemFromCache(CachedItem item)
        {
            // remove from the cache registry to prevent new requests to the cache 
            lock (_syncRoot)
            {
                if (_items.ContainsKey(item.Magnet.TTH))
                {
                    _items.Remove(item.Magnet.TTH);
                }
            }
            
            while (true)
            {
                try
                {
                    File.Delete(item.CachePath);
                }
                catch (Exception exception)
                {
                    Logger.Error("Cannot delete the cache file: {0} {1}", item.CachePath, exception.Message);
                    Thread.Sleep(100);
                    continue;
                }

                if (File.Exists(item.BitFileldFilePath))
                    File.Delete(item.BitFileldFilePath);

                break;
            }
            
            // free the space allowing new items to be placed to the cache
            lock (_syncRoot)
            {
                var pInd = _points.FindIndex(cp => item.CachePath.StartsWith(cp.SystemPath));

                if (pInd != -1)
                {
                    _points[pInd].RemoveItem(item);
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
                    Magnet magnet;
                    try
                    {
                        magnet = new Magnet
                        {
                            TTH = reader.ReadString(),
                            FileName = reader.ReadString(),
                            Size = reader.ReadInt64()
                        };
                    }
                    catch (Exception x)
                    {
                        Logger.Error("Error when loading bitfield: {0}", x.Message);
                        fs.Close();
                        File.Delete(filePath);
                        var dataFile = Path.ChangeExtension(filePath, "");
                        if (File.Exists(dataFile))
                            File.Delete(dataFile);
                        continue;
                    }


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
                                AddItemToCache(item, point);
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

            Logger.Info("Cache loaded {0}", Utils.FormatBytes(point.UsedSpace));

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
                ProxyUploadItem.SegmentDownloaded += HttpUploadItem_HttpSegmentDownloaded;
                ProxyUploadItem.SegmentNeeded += HttpUploadItem_HttpSegmentNeeded;
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

    [Serializable]
    public struct DayTimeSpan
    {
        [XmlAttribute("Start")]
        public string StartString
        {
            get { return Start.ToString(); }
            set { Start = TimeSpan.Parse(value); }
        }

        [XmlAttribute("End")]
        public string EndString
        {
            get { return End.ToString(); }
            set { End = TimeSpan.Parse(value); }
        }


        [XmlIgnore]
        public TimeSpan Start { get; set; }
        [XmlIgnore]
        public TimeSpan End { get; set; }

        public bool IsMatch()
        {
            return IsMatch(DateTime.Now);
        }

        public bool IsMatch(DateTime now)
        {
            return now.TimeOfDay > Start && now.TimeOfDay < End;
        }
    }
}
