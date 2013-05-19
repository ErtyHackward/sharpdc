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
using SharpDc.Collections;
using SharpDc.Events;
using SharpDc.Logging;
using SharpDc.Storage;
using SharpDc.Structs;

namespace SharpDc.Managers
{
    public class DownloadManager
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly DcEngine _engine;
        private readonly object _synObject = new object();
        private readonly SortedList<string, DownloadItem> _tthList = new SortedList<string, DownloadItem>();

        private readonly SortedList<Source, HashSet<DownloadItem>> _sourcesList =
            new SortedList<Source, HashSet<DownloadItem>>();

        private readonly List<DownloadItem> _itemsWithoutTth = new List<DownloadItem>();
        private readonly List<DownloadItemsGroup> _groups = new List<DownloadItemsGroup>();
        private int _updateIndex;
        private int _itemsCount;

        #region Events

        /// <summary>
        /// Downloading have been completed of segment
        /// </summary>
        public event EventHandler<SegmentEventArgs> SegmentCompleted;

        public int Count
        {
            get { return _itemsCount; }
        }

        public object SyncRoot
        {
            get { return _synObject; }
        }

        public IEnumerable<DownloadItem> Items()
        {
            lock (_synObject)
            {
                foreach (var pair in _tthList)
                {
                    yield return pair.Value;
                }
            }
        }

        protected virtual void OnSegmentCompleted(SegmentEventArgs e)
        {
            _engine.SourceManager.UpdateReceived(e.Source);

            lock (e.DownloadItem.SyncRoot)
            {
                if (e.DownloadItem.ActiveSegmentsCount > 0 &&
                    e.DownloadItem.TotalSegmentsCount - e.DownloadItem.DoneSegmentsCount ==
                    e.DownloadItem.ActiveSegmentsCount)
                {
                    // sort list by quality
                    e.DownloadItem.ActiveSources.Add(e.Source);
                    var sortedList = _engine.SourceManager.SortByQualityDesc(e.DownloadItem.ActiveSources);
                    e.DownloadItem.ActiveSources.Remove(e.Source);
                    // check if we have any that worse than we
                    var ourIndex = sortedList.IndexOf(e.Source);

                    if (ourIndex < sortedList.Count - 1)
                    {
                        // release other segment with worse source
                        _engine.TransferManager.DropSource(sortedList[sortedList.Count - 1]);
                    }
                }
            }

            var handler = SegmentCompleted;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// Downloading of segment have been started
        /// </summary>
        public event EventHandler<SegmentEventArgs> SegmentStarted;

        protected virtual void OnSegmentStarted(SegmentEventArgs e)
        {
            var handler = SegmentStarted;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// Segment downloading have been canceled
        /// </summary>
        public event EventHandler<SegmentEventArgs> SegmentCancelled;

        protected virtual void OnSegmentCancelled(SegmentEventArgs e)
        {
            var handler = SegmentCancelled;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<VerifySegmentEventArgs> SegmentVerified;

        protected virtual void OnSegmentVerified(VerifySegmentEventArgs e)
        {
            var handler = SegmentVerified;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// DownloadItem have been completed (Download)
        /// </summary>
        public event EventHandler<DownloadCompletedEventArgs> DownloadCompleted;

        protected virtual void OnDownloadCompleted(DownloadCompletedEventArgs e)
        {
            var handler = DownloadCompleted;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// DownloadItem adding to DownloadManager. Can be cancelled.
        /// </summary>
        public event EventHandler<CancelDownloadEventArgs> DownloadAdding;

        protected virtual bool OnDownloadAdding(DownloadItem di)
        {
            if (DownloadAdding == null) return true;
            var e = new CancelDownloadEventArgs { DownloadItem = di };
            var handler = DownloadAdding;
            if (handler != null) handler(this, e);
            return !e.Cancel;
        }

        /// <summary>
        /// DownloadItem have been added to DownloadManager
        /// </summary>
        public event EventHandler<DownloadEventArgs> DownloadAdded;

        protected virtual void OnDownloadAdded(DownloadEventArgs e)
        {
            var handler = DownloadAdded;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// DownloadItem have been removed from DownloadManager
        /// </summary>
        public event EventHandler<DownloadEventArgs> DownloadRemoved;

        protected virtual void OnDownloadRemoved(DownloadEventArgs e)
        {
            var handler = DownloadRemoved;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// Occurs when some of download items gets TTH
        /// </summary>
        public event EventHandler<DownloadEventArgs> TthAssigned;

        protected virtual void OnTthAssigned(DownloadEventArgs e)
        {
            var handler = TthAssigned;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// Source have been added to DownloadManager
        /// </summary>
        public event EventHandler<SourceEventArgs> SourceAdded;

        protected virtual void OnSourceAdded(SourceEventArgs e)
        {
            var handler = SourceAdded;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// Source have been removed from DownloadManager
        /// </summary>
        public event EventHandler<SourceEventArgs> SourceRemoved;

        protected virtual void OnSourceRemoved(SourceEventArgs e)
        {
            var handler = SourceRemoved;
            if (handler != null) handler(this, e);
        }

        #endregion

        public DownloadManager(DcEngine engine)
        {
            _engine = engine;
        }

        /// <summary>
        /// Allows to check limited amount of items to process. If number of items is bigger than limit, each call returns next group of items to process
        /// </summary>
        /// <param name="limit">limit of items to return</param>
        /// <returns></returns>
        public IEnumerable<DownloadItem> EnumeratesItemsForProcess(int limit = 100)
        {
            lock (_synObject)
            {
                var updateCnt = Math.Min(_updateIndex + limit, _tthList.Count);

                for (var index = _updateIndex; index < updateCnt; index++)
                {
                    yield return _tthList.Values[index];
                }

                _updateIndex += updateCnt;

                if (_tthList.Count <= _updateIndex)
                {
                    _updateIndex = 0;
                }
            }
        }

        public void AddDownload(DownloadItemsGroup group)
        {
            lock (_synObject)
            {
                lock (group.SyncRoot)
                {
                    ImportDownloads(group.DownloadItems);
                }

                _groups.Add(group);
            }
        }

        public bool AddDownload(DownloadItem di, Source source)
        {
            if (!di.Sources.Contains(source))
                di.Sources.Add(source);
            return AddDownload(di);
        }

        public void RemoveSource(Source source, DownloadItem downloadItem)
        {
            lock (_synObject)
            {
                downloadItem.Sources.Remove(source);
                if (_sourcesList.ContainsKey(source))
                {
                    var list = _sourcesList[source];
                    list.Remove(downloadItem);
                    if (list.Count == 0)
                        _sourcesList.Remove(source);
                }

                if (downloadItem.FolderUnit != null)
                {
                    lock (downloadItem.FolderUnit.SyncRoot)
                        downloadItem.FolderUnit.CommonSources.Remove(source);
                }
            }
        }

        public void ImportDownloads(IEnumerable<DownloadItem> items)
        {
            lock (_synObject)
            {
                foreach (var di in items)
                {
                    var tth = di.Magnet.TTH;
                    if (!string.IsNullOrEmpty(tth))
                    {
                        if (!_tthList.ContainsKey(tth))
                            _tthList.Add(tth, di);
                    }
                    else _itemsWithoutTth.Add(di);

                    foreach (var source in di.Sources)
                    {
                        if (_sourcesList.ContainsKey(source))
                        {
                            if (!_sourcesList[source].Contains(di))
                                _sourcesList[source].Add(di);
                        }
                        else
                        {
                            _sourcesList.Add(source, new HashSet<DownloadItem> { di });
                        }
                    }

                    di.Sources.ItemAdded += SourcesItemAdded;
                    di.Sources.ItemRemoved += SourcesItemRemoved;

                    di.DownloadFinished += DDownloadFinished;
                    di.SegmentCancelled += DSegmentCancelled;
                    di.SegmentFinished += DSegmentFinished;
                    di.SegmentTaken += DSegmentTaken;
                    _itemsCount++;
                    //di.SegmentVerified += d_SegmentVerified;
                    //di.NeedVerification += d_NeedVerification;
                }
            }
        }

        public bool AddDownload(DownloadItem di)
        {
            if (!OnDownloadAdding(di))
                return false;

            if (di.StorageContainer == null)
            {
                var downloadPath = di.SaveTargets[0];
                var folder = Path.GetDirectoryName(downloadPath);
                var tempDownloadPath = Path.Combine(folder,
                                                    string.Format("{0}.{1}.dctmp", Path.GetFileName(downloadPath),
                                                                  di.Magnet.TTH));
                var fileStorageContainer = new FileStorageContainer(tempDownloadPath)
                {
                    UseSparseFiles = _engine.Settings.UseSparseFiles
                };
                di.StorageContainer = fileStorageContainer;
            }

            bool contains;
            lock (_synObject)
            {
                contains = _tthList.ContainsKey(di.Magnet.TTH);
            }
            if (!contains)
            {
                lock (_synObject)
                {
                    if (!string.IsNullOrEmpty(di.Magnet.TTH))
                    {
                        _tthList.Add(di.Magnet.TTH, di);
                    }
                    else _itemsWithoutTth.Add(di);
                    _itemsCount++;

                    foreach (var source in di.Sources)
                    {
                        if (_sourcesList.ContainsKey(source))
                        {
                            if (!_sourcesList[source].Contains(di))
                                _sourcesList[source].Add(di);
                        }
                        else
                        {
                            _sourcesList.Add(source, new HashSet<DownloadItem> { di });
                        }
                    }
                }

                di.Sources.ItemAdded += SourcesItemAdded;
                di.Sources.ItemRemoved += SourcesItemRemoved;

                di.DownloadFinished += DDownloadFinished;
                di.SegmentCancelled += DSegmentCancelled;
                di.SegmentFinished += DSegmentFinished;
                di.SegmentTaken += DSegmentTaken;
                //di.SegmentVerified += d_SegmentVerified;
                //di.NeedVerification += d_NeedVerification;

                OnDownloadAdded(new DownloadEventArgs { DownloadItem = di });
            }
            else
            {
                lock (_synObject)
                {
                    // saving using different name?
                    if (!string.IsNullOrEmpty(di.Magnet.FileName) && di.SaveTargets != null)
                    {
                        var newName = di.Magnet.FileName;
                        var path = di.SaveTargets[0];
                        // This is if we have a fake downloaditem (that we probably have)
                        di = _tthList[di.Magnet.TTH];

                        // maybe user want to save using different name?
                        if (di.Magnet.FileName != newName)
                        {
                            if (!di.SaveTargets.Contains(path))
                                di.SaveTargets.Add(path);
                            OnDownloadAdded(new DownloadEventArgs { DownloadItem = di });
                        }
                    }
                    else
                    {
                        Logger.Warn("Unable to add DownloadItem");
                    }
                }
            }
            return true;
        }

        public void RemoveDownload(DownloadItem di)
        {
            lock (_synObject)
            {
                di.Sources.ItemAdded -= SourcesItemAdded;
                di.Sources.ItemRemoved -= SourcesItemRemoved;

                di.DownloadFinished -= DDownloadFinished;
                di.SegmentCancelled -= DSegmentCancelled;
                di.SegmentFinished -= DSegmentFinished;
                di.SegmentTaken -= DSegmentTaken;
                //di.SegmentVerified -= d_SegmentVerified;
                //di.NeedVerification -= d_NeedVerification;
                _itemsCount--;
                foreach (var downloadItemsGroup in _groups)
                {
                    downloadItemsGroup.Remove(di);
                }

                _tthList.Remove(di.Magnet.TTH);
                foreach (var source in di.Sources)
                {
                    if (_sourcesList.ContainsKey(source))
                    {
                        var list = _sourcesList[source];

                        list.Remove(di);
                        if (list.Count == 0)
                            _sourcesList.Remove(source);
                    }
                }
            }

            if (di.StorageContainer is FileStorageContainer && !di.Downloaded)
            {
                var fileStorage = di.StorageContainer as FileStorageContainer;

                di.StorageContainer.Dispose();

                File.Delete(fileStorage.TempFilePath);
            }

            OnDownloadRemoved(new DownloadEventArgs { DownloadItem = di });
        }

        private void SourcesItemRemoved(object sender, ObservableListEventArgs<Source> e)
        {
            var slist = (SourceList)sender;
            var list = _sourcesList[e.Item];
            list.Remove(slist.DownloadItem);
            if (list.Count == 0)
                _sourcesList.Remove(e.Item);

            OnSourceRemoved(new SourceEventArgs { Source = e.Item, DownloadItem = slist.DownloadItem });
        }

        private void SourcesItemAdded(object sender, ObservableListEventArgs<Source> e)
        {
            var slist = (SourceList)sender;
            if (_sourcesList.ContainsKey(e.Item))
            {
                if (!_sourcesList[e.Item].Contains(slist.DownloadItem))
                    _sourcesList[e.Item].Add(slist.DownloadItem);
            }
            else
            {
                _sourcesList.Add(e.Item, new HashSet<DownloadItem> { slist.DownloadItem });
            }

            OnSourceAdded(new SourceEventArgs { Source = e.Item, DownloadItem = slist.DownloadItem });
        }

        private void DSegmentTaken(object sender, SegmentEventArgs e)
        {
            OnSegmentStarted(e);
        }

        private void DSegmentFinished(object sender, SegmentEventArgs e)
        {
            OnSegmentCompleted(e);
        }

        private void DSegmentCancelled(object sender, SegmentEventArgs e)
        {
            OnSegmentCancelled(e);
        }

        private void DDownloadFinished(object sender, EventArgs e)
        {
            var item = (DownloadItem)sender;

            item.StorageContainer.Dispose();

            if (item.StorageContainer is FileStorageContainer)
            {
                var fileStorage = item.StorageContainer as FileStorageContainer;

                // rename file
                try
                {
                    File.Move(fileStorage.TempFilePath, item.SaveTargets[0]);

                    for (int i = 1; i < item.SaveTargets.Count; i++)
                    {
                        File.Copy(item.SaveTargets[0], item.SaveTargets[i]);
                    }
                }
                catch (Exception x)
                {
                    Logger.Error("Unable to move or copy the file: {0}", x.Message);
                }
            }

            OnDownloadCompleted(new DownloadCompletedEventArgs { DownloadItem = item });
            RemoveDownload(item);
        }

        //void d_SegmentVerified(object sender, VerifySegmentEventArgs e)
        //{
        //    OnSegmentVerified(e);
        //}

        //void d_NeedVerification(object sender, NeedVerificationEventArgs e)
        //{
        //    lock (verifyStack)
        //    {
        //        verifyStack.Push(e.VerifySegment);
        //    }
        //}

        /// <summary>
        /// Returns DownloadItem with max priority associated with source or null
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        public DownloadItem GetDownloadItem(Source source)
        {
            lock (_synObject)
            {
                List<DownloadItem> subList = null;
                if (_sourcesList.ContainsKey(source))
                {
                    var list = _sourcesList[source];

                    if (list.Count > 1)
                    {
                        subList = new List<DownloadItem>(list);
                    }
                    else return list.FirstOrDefault();
                }
                else
                {
                    foreach (var group in _groups)
                    {
                        if (group.CommonSources.Contains(source))
                        {
                            subList = new List<DownloadItem>(group.DownloadItems);
                            break;
                        }
                    }
                }

                if (subList == null)
                {
                    // possible that we have invalid hub information here, check by nickname only

                    var pair = _sourcesList.FirstOrDefault(s => s.Key.UserNickname == source.UserNickname);

                    if (pair.Value != null)
                    {
                        var list = pair.Value;

                        if (list.Count > 1)
                        {
                            subList = new List<DownloadItem>(list);
                        }
                        else return list.FirstOrDefault();
                    }
                }

                if (subList != null)
                {
                    for (int i = subList.Count - 1; i >= 0; i--)
                    {
                        if (subList[i].TotalSegmentsCount == 0 ||
                            subList[i].DoneSegmentsCount <
                            subList[i].TotalSegmentsCount - subList[i].ActiveSegmentsCount)
                            continue;
                        subList.RemoveAt(i);
                    }
                    if (subList.Count == 0) return null;
                    if (subList.Count == 1) return subList[0];

                    var di = subList[0];
                    for (int i = 1; i < subList.Count; i++)
                    {
                        if (subList[i].Priority > di.Priority)
                            di = subList[i];
                    }
                    return di;
                }



            }
            return null;
        }

        /// <summary>
        /// Returns DownloadItem by TTH or null
        /// </summary>
        /// <param name="tth"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <returns></returns>
        public DownloadItem GetDownloadItem(string tth)
        {
            if (string.IsNullOrEmpty(tth)) throw new ArgumentOutOfRangeException("tth");
            lock (_synObject)
            {
                DownloadItem item;
                _tthList.TryGetValue(tth, out item);
                return item;
            }
        }

        public bool Contains(string tth)
        {
            if (string.IsNullOrEmpty(tth)) throw new ArgumentOutOfRangeException("tth");
            lock (_synObject)
            {
                return _tthList.ContainsKey(tth);
            }
        }

        public void Clear()
        {
            lock (_synObject)
            {
                _tthList.Clear();
                _sourcesList.Clear();
                _itemsWithoutTth.Clear();
                _groups.Clear();
                _itemsCount = 0;
            }
        }

        public void RemoveWhere(Predicate<DownloadItem> pred)
        {
            var list = new List<DownloadItem>();

            lock (_synObject)
            {
                list.AddRange(from pair in _tthList where pred(pair.Value) select pair.Value);

                if (list.Count > 0)
                {
                    foreach (var downloadItem in list)
                    {
                        RemoveDownload(downloadItem);
                    }
                }
            }
        }

        /// <summary>
        /// Returns exact number of bytes received for particular DownloadItem
        /// </summary>
        /// <param name="currentDownload"></param>
        /// <returns></returns>
        public long GetTotalDownloadBytes(DownloadItem currentDownload)
        {
            if (currentDownload == null) 
                throw new ArgumentNullException("currentDownload");

            if (currentDownload.TotalSegmentsCount == currentDownload.DoneSegmentsCount)
                return currentDownload.Magnet.Size;

            // remember that the last segment may be smaller in size than others, so note that
            var lastSegmentGap = 0L;

            if (currentDownload.DoneSegments[currentDownload.TotalSegmentsCount - 1] && currentDownload.Magnet.Size % DownloadItem.SegmentSize > 0)
                lastSegmentGap = DownloadItem.SegmentSize - currentDownload.Magnet.Size % DownloadItem.SegmentSize;

            return (long)DownloadItem.SegmentSize * currentDownload.DoneSegmentsCount + _engine.TransferManager.Transfers().Where(t => t.DownloadItem == currentDownload).Select(t => t.SegmentInfo.Position).Sum() - lastSegmentGap;
        }

        public void Save(string fileName)
        {
            var list = _tthList.Values.ToList();

            var xml = new XmlSerializer(typeof(List<DownloadItem>), new []{ typeof(FileStorageContainer) });

            if (File.Exists(fileName))
                File.Delete(fileName);

            using (var fs = File.OpenWrite(fileName))
            {
                xml.Serialize(fs, list);
            }
        }

        public void Load(string fileName)
        {
            var xml = new XmlSerializer(typeof(List<DownloadItem>), new[] { typeof(FileStorageContainer) });

            List<DownloadItem> list;
            using (var fs = File.OpenRead(fileName))
            {
                list = (List<DownloadItem>)xml.Deserialize(fs);
            }
 
            ImportDownloads(list);
        }
    }
}