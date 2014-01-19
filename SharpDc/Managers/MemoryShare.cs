// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using SharpDc.Helpers;
using SharpDc.Interfaces;
using SharpDc.Messages;

namespace SharpDc.Managers
{
    /// <summary>
    /// Represents a memory share
    /// </summary>
    [Serializable]
    public class MemoryShare : IShare
    {
        private readonly Dictionary<string, ContentItem> _tthIndex = new Dictionary<string, ContentItem>();

        private long _totalShared;
        private int _totalFiles;

        [XmlIgnore]
        public bool IsDirty { get; private set; }

        /// <summary>
        /// Gets total amount of bytes in the share
        /// </summary>
        [XmlIgnore]
        public long TotalShared
        {
            get { return _totalShared; }
        }

        /// <summary>
        /// Gets total amount of files in the share
        /// </summary>
        [XmlIgnore]
        public int TotalFiles
        {
            get { return _totalFiles; }
        }

        /// <summary>
        /// Don't use reserved for serialization/deserialization
        /// </summary>
        [XmlArray("Items")]
        public ContentItem[] SerializationItems
        {
            get 
            {
                lock (_tthIndex)
                    return _tthIndex.Values.ToArray();
            }
            set 
            { 
                Clear();
                AddFiles(value);
                IsDirty = false;
            }
        }

        public event EventHandler TotalSharedChanged;

        protected virtual void OnTotalSharedChanged()
        {
            IsDirty = true;
            var handler = TotalSharedChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public void AddFiles(IEnumerable<ContentItem> items)
        {
            lock (_tthIndex)
            {
                foreach (var item in items)
                {
                    _tthIndex.Add(item.Magnet.TTH, item);
                    _totalShared += item.Magnet.Size;
                    _totalFiles++;
                }
            }
            OnTotalSharedChanged();
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

        public void RemoveFile(string tth)
        {
            bool updated = false;

            lock (_tthIndex)
            {
                ContentItem item;

                if (_tthIndex.TryGetValue(tth, out item))
                {
                    _totalShared -= item.Magnet.Size;
                    _totalFiles--;
                    updated = true;
                    _tthIndex.Remove(tth);
                }
            }
            if (updated)
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

        public IEnumerable<ContentItem> OldestItems()
        {
            lock (_tthIndex)
            {
                return _tthIndex.Values.OrderBy(i => i.CreateDate);
            }
        }

        /// <summary>
        /// Not supported
        /// </summary>
        /// <param name="path"></param>
        public void AddIgnoreDirectory(string path)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Not supported
        /// </summary>
        public void RemoveIgnoreDirectory(string path)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Not supported
        /// </summary>
        public void AddDirectory(string systemPath, string virtualPath = null)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Not supported
        /// </summary>
        public void RemoveDirectory(string systemPath)
        {
            throw new NotSupportedException();
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
                    if (pair.Value.SystemPath.IndexOf(query.Query, System.StringComparison.CurrentCultureIgnoreCase) >=
                        0)
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

                    if (!FileHelper.FileExists(item.SystemPath))
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

        public void ExportAsXml(string filePath)
        {
            var xml = new XmlSerializer(GetType());

            if (File.Exists(filePath))
                File.Delete(filePath);

            using (var fs = File.OpenWrite(filePath))
            {
                xml.Serialize(fs, this);
            }
        }

        public void ImportFromXml(string filePath)
        {
            var share = CreateFromXml(filePath);
            Clear();
            AddFiles(share.Items());
        }

        public static MemoryShare CreateFromXml(string filePath)
        {
            var xml = new XmlSerializer(typeof(MemoryShare));
            
            using (var fs = File.OpenRead(filePath))
            {
                return (MemoryShare)xml.Deserialize(fs);
            }
        }
    }
}