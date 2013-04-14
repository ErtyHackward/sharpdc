// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using SharpDc.Events;
using SharpDc.Interfaces;
using SharpDc.Logging;
using SharpDc.Messages;
using SharpDc.Structs;

namespace SharpDc.Managers
{
    public class SearchManager
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly DcEngine _engine;

        private readonly List<ISearchResult> _results = new List<ISearchResult>();
        private readonly Dictionary<string, HubSearchResult> _tthList = new Dictionary<string, HubSearchResult>();
        private readonly List<SearchMessage> _searchQueue = new List<SearchMessage>();

        private readonly object _syncRoot = new object();

        private SearchMessage? _currentSearch;

        private DateTime _lastSearchAt;

        /// <summary>
        /// Gets or sets minimum search interval in seconds
        /// </summary>
        public int MinimumSearchInterval { get; set; }

        public SearchMessage? CurrentSearch
        {
            get { return _currentSearch; }
        }

        #region Events

        /// <summary>
        /// Occurs when new search is started
        /// </summary>
        public event EventHandler<SearchEventArgs> SearchStarted;

        protected void OnSearchStarted(SearchEventArgs e)
        {
            var handler = SearchStarted;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// Occurs when new search result has come
        /// </summary>
        public event EventHandler<SearchManagerResultEventArgs> SearchResult;

        protected virtual void OnSearchResult(SearchManagerResultEventArgs e)
        {
            var handler = SearchResult;
            if (handler != null) handler(this, e);
        }

        #endregion

        public SearchManager(DcEngine engine)
        {
            _engine = engine;

            _engine.Hubs.HubAdded += Hubs_HubAdded;
            _engine.Hubs.HubRemoved += Hubs_HubRemoved;

            MinimumSearchInterval = 20;
        }

        public void InjectResult(SRMessage resultMsg)
        {
            var resultMagnet = new Magnet(resultMsg.HubName, resultMsg.FileSize, Path.GetFileName(resultMsg.FileName));
            var resultSource = new Source { UserNickname = resultMsg.Nickname, HubAddress = resultMsg.HubAddress };

            //Logger.Info("Found source {0}", resultSource);

            HubSearchResult result;

            lock (_syncRoot)
            {
                if (!_tthList.TryGetValue(resultMagnet.TTH, out result))
                {
                    result = new HubSearchResult(resultMagnet, resultSource, resultMsg.FileName);
                    _tthList.Add(resultMagnet.TTH, result);
                    _results.Add(result);
                }
                else
                {
                    if (result.Sources.FindIndex(s => s.UserNickname == resultSource.UserNickname && s.HubAddress == resultSource.HubAddress) == -1)
                    {
                        result.Sources.Add(resultSource);
                        result.VirtualDirs.Add(resultMsg.FileName);
                    }
                }
            }

            OnSearchResult(new SearchManagerResultEventArgs { Result = result });

            var item = _engine.DownloadManager.GetDownloadItem(resultMagnet.TTH);

            if (item != null)
            {
                item.Sources.Add(resultSource);
            }
        }

        private string GetReturnAddress()
        {
            if (_engine.Settings.ActiveMode)
                return _engine.LocalUdpAddress;
            return "";
        }

        public void Search(DownloadItem item)
        {
            Search(item, true);
        }

        public void SearchByTTH(string tth)
        {
            var msg = new SearchMessage
                          {
                              SearchRequest = tth,
                              SearchType = SearchType.TTH,
                              SearchAddress = _engine.LocalUdpAddress
                          };

            EnqueueSearch(ref msg, true);
        }

        public void Search(SearchMessage msg)
        {
            EnqueueSearch(ref msg, true);
        }

        private void Search(DownloadItem item, bool highPriority)
        {
            SearchMessage msg;

            if (!string.IsNullOrEmpty(item.Magnet.TTH))
            {
                msg = new SearchMessage
                          {
                              SearchRequest = item.Magnet.TTH,
                              SearchType = SearchType.TTH,
                              SearchAddress = GetReturnAddress()
                          };
            }
            else
            {
                msg = new SearchMessage
                          {
                              SearchType = SearchType.Any,
                              SizeRestricted = true,
                              Size = item.Magnet.Size,
                              SearchRequest = item.Magnet.FileName,
                              SearchAddress = GetReturnAddress()
                          };
            }

            EnqueueSearch(ref msg, highPriority);
        }

        private void EnqueueSearch(ref SearchMessage search, bool highPriority)
        {
            lock (_syncRoot)
            {
                if ((DateTime.Now - _lastSearchAt).TotalSeconds > MinimumSearchInterval)
                {
                    StartSearch(ref search);
                    return;
                }

                if (highPriority)
                {
                    _searchQueue.Insert(0, search);
                }
                else
                {
                    _searchQueue.Add(search);
                }
            }
        }

        private void StartSearch(ref SearchMessage msg)
        {
            if (msg.SearchType == SearchType.TTH)
            {
                var item = _engine.DownloadManager.GetDownloadItem(msg.SearchRequest);
                if (item != null)
                {
                    item.LastSearch = DateTime.Now;
                }
            }

            _lastSearchAt = DateTime.Now;
            _currentSearch = msg;
            _results.Clear();
            _tthList.Clear();

            _engine.Hubs.ForEach(h =>
                                     {
                                         if (h.Active)
                                         {
                                             var search = _currentSearch.Value;

                                             if (string.IsNullOrEmpty(search.SearchAddress))
                                                 search.SearchAddress = "Hub:" + h.Settings.Nickname;

                                             Logger.Info("Search for " + search.SearchRequest);

                                             h.SendMessage(search.Raw);
                                         }
                                     });

            if (SearchStarted != null)
            {
                OnSearchStarted(new SearchEventArgs { Message = _currentSearch.Value });
            }
        }

        private void Hubs_HubRemoved(object sender, HubsChangedEventArgs e)
        {
        }

        private void Hubs_HubAdded(object sender, HubsChangedEventArgs e)
        {
        }

        public HubSearchResult GetHubResultByTTH(string tth)
        {
            HubSearchResult result;
            _tthList.TryGetValue(tth, out result);
            return result;
        }

        private int IndexOf(DownloadItem item)
        {
            lock (_syncRoot)
            {
                bool tthSearch = !string.IsNullOrEmpty(item.Magnet.TTH);
                for (int i = 0; i < _searchQueue.Count; i++)
                {
                    if (tthSearch)
                    {
                        if (_searchQueue[i].SearchRequest == item.Magnet.TTH) return i;
                    }
                    else
                    {
                        if (_searchQueue[i].SearchType != SearchType.TTH &&
                            _searchQueue[i].SearchRequest == item.Magnet.FileName &&
                            _searchQueue[i].Size == item.Magnet.Size)
                            return i;
                    }
                }

                return -1;
            }
        }

        public void CheckPendingSearches()
        {
            if ((DateTime.Now - _lastSearchAt).TotalSeconds <= MinimumSearchInterval)
                return;

            lock (_syncRoot)
            {
                if (_searchQueue.Count > 0)
                {
                    var msg = _searchQueue[0];
                    _searchQueue.RemoveAt(0);
                    StartSearch(ref msg);
                }
            }
        }


        /// <summary>
        /// Returns estimated time when the search for the downloadItem will be started
        /// </summary>
        /// <param name="downloadItem"></param>
        /// <returns></returns>
        public TimeSpan EstimateSearch(DownloadItem downloadItem)
        {
            var index = _searchQueue.FindIndex(s => s.SearchType == SearchType.TTH && s.SearchRequest == downloadItem.Magnet.TTH);

            if (index < 0)
                return TimeSpan.MaxValue;

            return TimeSpan.FromSeconds((double)MinimumSearchInterval - (DateTime.Now - _lastSearchAt).TotalSeconds + index * MinimumSearchInterval);
        }

        public void CheckItem(DownloadItem downloadItem)
        {
            if (_searchQueue.Count > 30)
                return;

            if ((DateTime.Now - downloadItem.LastSearch).TotalMinutes < _engine.Settings.SearchAlternativesInterval)
                return;

            // skip current search
            if (_currentSearch.HasValue)
            {
                if (string.IsNullOrEmpty(downloadItem.Magnet.TTH))
                {
                    if (_currentSearch.Value.SearchRequest == downloadItem.Magnet.FileName &&
                        _currentSearch.Value.Size == downloadItem.Magnet.Size)
                        return;
                }
                else
                {
                    if (_currentSearch.Value.SearchRequest == downloadItem.Magnet.TTH)
                        return;
                }
            }

            if (IndexOf(downloadItem) != -1)
                return;

            Search(downloadItem, false);
        }
    }

    public class SearchManagerResultEventArgs : EventArgs
    {
        public HubSearchResult Result { get; set; }
    }

    public class SearchEventArgs : EventArgs
    {
        public SearchMessage Message { get; set; }
    }
}