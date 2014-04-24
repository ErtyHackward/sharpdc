// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2014
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using SharpDc.Connections;
using SharpDc.Structs;

namespace SharpDc.Managers
{
    public class StatisticsManager
    {
        private readonly Dictionary<string, StatItem> _items = new Dictionary<string, StatItem>();
        private readonly object _synRoot = new object();
        
        public StatisticsManager(DcEngine engine)
        {
            engine.TransferManager.TransferAdded += TransferManager_TransferAdded;
            engine.TransferManager.TransferRemoved += TransferManager_TransferRemoved;
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
                var rateChange = (double)item.UploadedBytes / item.Content.Magnet.Size;

                StatItem statItem;
                if (_items.TryGetValue(magnet.TTH, out statItem))
                {
                    statItem.Rate += rateChange;
                    statItem.LastUsage = DateTime.Now;
                    _items[magnet.TTH] = statItem;
                }
                else
                {
                    statItem = new StatItem();
                    statItem.Magnet = magnet;
                    statItem.LastUsage = DateTime.Now;
                    statItem.Rate = rateChange;
                    _items.Add(magnet.TTH, statItem);
                }
            }
        }
    }
}
