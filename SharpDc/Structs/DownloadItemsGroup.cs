// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using SharpDc.Collections;

namespace SharpDc.Structs
{
    /// <summary>
    /// Logical union of download items. Some kind of folder.
    /// </summary>
    [Serializable]
    public class DownloadItemsGroup
    {
        private readonly List<DownloadItem> _list = new List<DownloadItem>();
        private int _maxFiles;

        /// <summary>
        /// Base folder name
        /// </summary>
        public string Name { get; set; }

        private string _path;

        /// <summary>
        /// Folder system path
        /// </summary>
        public string Path
        {
            get { return _path; }
            set
            {
                if (_path != value)
                {
                    _path = value;

                    foreach (var downloadItem in _list)
                    {
                        downloadItem.FolderUnitPath = _path;
                    }
                }
            }
        }

        [XmlIgnore]
        public object SyncRoot { get; private set; }

        /// <summary>
        /// Common source list for all items
        /// </summary>
        public SourceList CommonSources { get; set; }

        public DownloadItem this[int index]
        {
            get { return _list[index]; }
            set
            {
                if (value == null)
                    throw new NullReferenceException("DownloadItem should not be null");
                _list[index] = value;
            }
        }

        public DownloadItemsGroup()
        {
            SyncRoot = new object();
            CommonSources = new SourceList();
        }

        public void Add(DownloadItem di)
        {
            lock (SyncRoot)
            {
                if (di == null) throw new NullReferenceException("DownloadItem should not be null");

                _list.Add(di);
                di.FolderUnit = this;
                if (_maxFiles < _list.Count) _maxFiles = _list.Count;
                TotalSize += di.Magnet.Size;
            }
        }

        public bool Contains(string tth)
        {
            return IndexOf(tth) != -1;
        }

        public int IndexOf(string tth)
        {
            lock (SyncRoot)
            {
                for (int i = 0; i < _list.Count; i++)
                {
                    if (_list[i] != null && _list[i].Magnet.TTH == tth)
                        return i;
                }
            }
            return -1;
        }

        public void Remove(DownloadItem di)
        {
            lock (SyncRoot)
            {
                TotalDoneSize += di.Magnet.Size;
                di.FolderUnit = null;
                _list.Remove(di);
            }
        }

        public void Remove(string tth)
        {
            lock (SyncRoot)
            {
                var index = IndexOf(tth);
                if (index != -1)
                {
                    TotalDoneSize += _list[index].Magnet.Size;
                    _list[index].FolderUnit = null;
                    _list.RemoveAt(index);
                }
            }
        }

        public long TotalSize { get; set; }

        public long TotalDoneSize { get; set; }

        public int MaxFiles
        {
            get { return _maxFiles; }
            set { _maxFiles = value; }
        }

        public int Count
        {
            get { return _list.Count; }
        }

        public IEnumerable<DownloadItem> DownloadItems
        {
            get { return _list; }
        }

        #region IEnumerable<DownloadItem> Members

        public IEnumerator<DownloadItem> GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        #endregion

        internal void RemoveAt(int i)
        {
            lock (SyncRoot)
                _list.RemoveAt(i);
        }

        /// <summary>
        /// Gets or sets online sources count
        /// </summary>
        [XmlIgnore]
        public int OnlineSourcesCount { get; set; }
    }
}