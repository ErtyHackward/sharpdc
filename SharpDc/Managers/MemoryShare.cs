//  -------------------------------------------------------------
//  LiveDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using SharpDc.Interfaces;
using SharpDc.Messages;

namespace SharpDc.Managers
{
    /// <summary>
    /// Represents a memory share
    /// </summary>
    public class MemoryShare : IShare
    {
        readonly Dictionary<string, ContentItem> _tthIndex = new Dictionary<string, ContentItem>();

        private long _totalShared;
        private int _totalFiles;

        /// <summary>
        /// Gets total amount of bytes in the share
        /// </summary>
        public long TotalShared
        {
            get { return _totalShared; }
        }

        /// <summary>
        /// Gets total amount of files in the share
        /// </summary>
        public int TotalFiles
        {
            get { return _totalFiles; }
        }

        public event System.EventHandler TotalSharedChanged;

        protected virtual void OnTotalSharedChanged()
        {
            var handler = TotalSharedChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public void AddFile(ContentItem item)
        {
            lock (_tthIndex)
            {
                _tthIndex.Add(item.Magnet.TTH, item);
                _totalShared += item.Magnet.Size;
                _totalFiles++;
            }
            OnTotalSharedChanged();
        }

        /// <summary>
        /// Allows to enumerate all items in a share
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ContentItem> Items()
        {
            lock (_tthIndex)
            {
                return _tthIndex.Values;
            }
        }

        public List<ContentItem> SearchByTth(string tth)
        {
            return Search(new SearchQuery { Query = tth, SearchType = SearchType.TTH });
        }

        public List<ContentItem> SearchByName(string name)
        {
            return Search(new SearchQuery { Query = name, SearchType = SearchType.Any });
        }

        public List<ContentItem> Search(SearchQuery query, int limit = 0)
        {
            lock (_tthIndex)
            {
                var results = new List<ContentItem>();

                if (query.SearchType == SearchType.TTH)
                {
                    ContentItem item;
                    if (_tthIndex.TryGetValue(query.Query, out item))
                    {
                        results.Add(item);
                    }
                    return results;
                }

                //return _tthIndex.Where(p => p.Value.SystemPath.IndexOf(query.Query, System.StringComparison.CurrentCultureIgnoreCase) >= 0).Select(p => p.Value).ToList();

                foreach (var pair in _tthIndex)
                {
                    if (pair.Value.SystemPath.IndexOf(query.Query, System.StringComparison.CurrentCultureIgnoreCase) >= 0)
                    {
                        results.Add(pair.Value);
                        if (limit > 0 && results.Count == limit)
                            break;
                    }
                }
                return results;
            }
        }

        /// <summary>
        /// Checks all content
        /// </summary>
        public void Reload()
        {
            var shared = _totalShared;

            lock (_tthIndex)
            {
                var list = new List<string>(_tthIndex.Keys);

                foreach (var tth in list)
                {
                    var item = _tthIndex[tth];

                    if (!File.Exists(item.SystemPath))
                    {
                        _tthIndex.Remove(tth);
                        _totalShared -= item.Magnet.Size;
                    }
                }
            }

            if (_totalShared != shared)
                OnTotalSharedChanged();

        }

        /// <summary>
        /// Erases all files from share
        /// </summary>
        public void Clear()
        {
            lock (_tthIndex)
            {
                _tthIndex.Clear();
                _totalFiles = 0;
                _totalShared = 0;
            }

            OnTotalSharedChanged();
        }

        
    }
}
