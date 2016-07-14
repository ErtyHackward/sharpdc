// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2014
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Threading;
using System.Xml.Serialization;
using SharpDc.Connections;
using SharpDc.Logging;
using SharpDc.Structs;

namespace SharpDc.Managers
{
    public class StatisticsManager
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly Dictionary<string, StatItem> _items = new Dictionary<string, StatItem>();
        private readonly object _synRoot = new object();
        private readonly Timer _updateTimer;
        private readonly DcEngine _engine;

        public StatisticsManager(DcEngine engine, bool decayRates = true)
        {
            _engine = engine;
            engine.TransferManager.TransferAdded += TransferManager_TransferAdded;
            engine.TransferManager.TransferRemoved += TransferManager_TransferRemoved;

            if (decayRates)
            {
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

        void TransferManager_TransferRemoved(object sender, TransferEventArgs e)
        {
            e.Transfer.UploadItemChanged -= Transfer_UploadItemChanged;
        }

        void TransferManager_TransferAdded(object sender, TransferEventArgs e)
        {
            e.Transfer.UploadItemChanged += Transfer_UploadItemChanged;
        }

        void Transfer_UploadItemChanged(object sender, UploadItemChangedEventArgs e)
        {
            if (e.PreviousItem != null)
            {
                HandleUploaded(e.PreviousItem);
            }
        }

        /// <summary>
        /// Saves statistics information to the file
        /// </summary>
        /// <param name="filePath"></param>
        public void SaveToFile(string filePath)
        {
            var xml = new XmlSerializer(typeof(List<StatItem>));

            lock (_synRoot)
            {
                using (var fs = File.OpenWrite(filePath))
                using (var zip = new GZipStream(fs, CompressionMode.Compress))
                {
                    xml.Serialize(zip, AllItems().ToList());    
                }
            }
        }

        /// <summary>
        /// Loads statistics database from the file
        /// </summary>
        /// <param name="filePath"></param>
        public void LoadFromFile(string filePath)
        {
            var xml = new XmlSerializer(typeof(List<StatItem>));

            lock (_synRoot)
            {
                using (var fs = File.OpenRead(filePath))
                using (var zip = new GZipStream(fs, CompressionMode.Decompress))
                {
                    var list = (List<StatItem>)xml.Deserialize(zip);
                    
                    Clear();

                    lock (_synRoot)
                    {
                        foreach (var statItem in list)
                        {
                            _items.Add(statItem.Magnet.TTH, statItem);
                        }
                    }
                }
            }
        }

        public void RemoveItem(string tth)
        {
            lock (_synRoot)
            {
                _items.Remove(tth);
            }
        }

        public bool TryGetValue(string tth, out StatItem item)
        {
            lock (_synRoot)
            {
                return _items.TryGetValue(tth, out item);
            }
        }

        public StatItem GetItem(string tth)
        {
            StatItem result;
            TryGetValue(tth, out result);
            return result;
        }

        public void SetItem(StatItem item)
        {
            lock (_synRoot)
            {
                if (_items.ContainsKey(item.Magnet.TTH))
                    _items[item.Magnet.TTH] = item;
                else
                {
                    _items.Add(item.Magnet.TTH, item);
                }
            }
        }

        public void Clear()
        {
            lock (_synRoot)
            {
                _items.Clear();
            }
        }

        public IEnumerable<StatItem> AllItems()
        {
            lock (_synRoot)
            {
                foreach (var statItem in _items.Values)
                {
                    yield return statItem;
                }
            }
        }

        private void HandleUploaded(UploadItem item)
        {
            lock (_synRoot)
            {
                var magnet = item.Content.Magnet;

                StatItem statItem;
                if (_items.TryGetValue(magnet.TTH, out statItem))
                {
                    statItem.TotalUploaded += item.UploadedBytes;
                    statItem.LastUsage = DateTime.Now;
                    _items[magnet.TTH] = statItem;
                }
                else
                {
                    statItem = new StatItem();
                    statItem.Magnet = magnet;
                    statItem.LastUsage = DateTime.Now;
                    statItem.TotalUploaded = item.UploadedBytes;
                    _items.Add(magnet.TTH, statItem);
                }
            }
        }
    }
}
