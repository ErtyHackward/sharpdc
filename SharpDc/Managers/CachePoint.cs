using System.Collections.Generic;
using System.Linq;
using SharpDc.Structs;

namespace SharpDc.Managers
{
    internal class CachePoint
    {
        private long _usedSpace;
        private readonly List<CachedItem> _items = new List<CachedItem>();

        public string SystemPath { get; set; }

        public long TotalSpace { get; set; }
        
        public long FreeSpace {
            get { return TotalSpace - _usedSpace; }
        }

        public long UsedSpace
        {
            get { return _usedSpace; }
        }

        public void AddItem(CachedItem item)
        {
            _items.Add(item);
            _usedSpace = _items.Sum(i => i.Magnet.Size);
        }

        public void RemoveItem(CachedItem item)
        {
            if (_items.Remove(item))
            {
                if (_items.Count > 0)
                    _usedSpace = _items.Sum(i => i.Magnet.Size);
                else
                {
                    _usedSpace = 0;
                }
            }
        }


    }
}