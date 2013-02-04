//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SharpDc.Connections;
using SharpDc.Events;
using SharpDc.Logging;
using SharpDc.Messages;
using SharpDc.Structs;

namespace SharpDc.Managers
{
    public class TransferManager
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly DcEngine _engine;
        private readonly List<TransferConnection> _connections = new List<TransferConnection>();
        private readonly object _synRoot = new object();
        private readonly List<TransferRequest> _allowedUsers = new List<TransferRequest>();

        private int _downloadThreadsCount;
        private int _uploadThreadsCount;

        /// <summary>
        /// Maximum time between connection request (hub) and connection authorization(direct), in seconds
        /// Default: 10
        /// </summary>
        public int ConnectionWaitTimeout { get; set; }

        /// <summary>
        /// Maximum idle time of download connections before disconnect, in seconds
        /// Default: 5
        /// </summary>
        public int DownloadInactivityTimeout { get; set; }

        /// <summary>
        /// Maximum idle time of upload connections before disconnect, in seconds
        /// Default: 30
        /// </summary>
        public int UploadInactivityTimeout { get; set; }

        /// <summary>
        /// Indicates if new requests can be made
        /// </summary>
        public bool RequestsAvailable
        {
            get
            {
                return _engine.Settings.MaxDownloadThreads == 0 || (_downloadThreadsCount < _engine.Settings.MaxDownloadThreads || _allowedUsers.Count < _engine.Settings.MaxDownloadThreads);
            }
        }

        /// <summary>
        /// Gets total connections count
        /// </summary>
        public int TransfersCount
        {
            get { return _connections.Count; }
        }

        #region Event 

        public event EventHandler<UploadItemErrorEventArgs> Error;

        private void OnError(UploadItemErrorEventArgs e)
        {
            var handler = Error;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<TransferEventArgs> TransferAdded;
        
        private void OnTransferAdded(TransferEventArgs e)
        {
            var handler = TransferAdded;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<TransferEventArgs> TransferRemoved;

        private void OnTransferRemoved(TransferEventArgs e)
        {
            var handler = TransferRemoved;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<TransferManagerAuthorizationEventArgs> TransferAuthorization;

        private void OnTransferAuthorization(TransferManagerAuthorizationEventArgs e)
        {
            var handler = TransferAuthorization;
            if (handler != null) handler(this, e);
        }

        #endregion

        public TransferManager(DcEngine engine)
        {
            ConnectionWaitTimeout = 10;
            DownloadInactivityTimeout = 5;
            UploadInactivityTimeout = 30;

            _engine = engine;
        }

        /// <summary>
        /// Returns total download speed of connections that satisfy the condition
        /// </summary>
        /// <param name="pred"></param>
        /// <returns></returns>
        public double GetDownloadSpeed(Predicate<TransferConnection> pred)
        {
            lock (_synRoot)
            {
                return _connections.Where(t => pred(t)).Sum(t => t.DownloadSpeed.GetSpeed());
            }
        }

        /// <summary>
        /// Returns total upload speed of connections that satisfy the condition
        /// </summary>
        /// <param name="pred"></param>
        /// <returns></returns>
        public double GetUploadSpeed(Predicate<TransferConnection> pred)
        {
            lock (_synRoot)
            {
                return _connections.Where(t => pred(t)).Sum(t => t.UploadSpeed.GetSpeed());
            }
        }

        public int GetUsedSlotsCount()
        {
            lock (_synRoot)
            {
                return _connections.Count(t => t.SlotUsed);
            }
        }

        public void AddTransfer(TransferConnection transfer)
        {
            lock (_synRoot)
            {
                _connections.Add(transfer);
            }

            transfer.ConnectionStatusChanged += TransferConnectionStatusChanged;
            transfer.UploadItemNeeded += TransferUploadItemNeeded;
            transfer.DirectionChanged += TransferDirectionChanged;
            transfer.DownloadItemNeeded += TransferDownloadItemNeeded;
            transfer.Authorization += TransferAuthorizationHandler;
            transfer.Error += TransferError;
            transfer.SlotRequest += TransferSlotRequest;

            OnTransferAdded(new TransferEventArgs { Transfer = transfer });

        }

        void TransferConnectionStatusChanged(object sender, ConnectionStatusEventArgs e)
        {
            if (e.Status == ConnectionStatus.Disconnected)
            {
                var transfer = (TransferConnection)sender;
                lock (_synRoot)
                {
                    _connections.Remove(transfer);

                    if (transfer.Direction == TransferDirection.Upload)
                        _uploadThreadsCount--;
                    if (transfer.Direction == TransferDirection.Download)
                        _downloadThreadsCount--;
                }

                transfer.ConnectionStatusChanged -= TransferConnectionStatusChanged;
                transfer.UploadItemNeeded -= TransferUploadItemNeeded;
                transfer.DirectionChanged -= TransferDirectionChanged;
                transfer.DownloadItemNeeded -= TransferDownloadItemNeeded;
                transfer.Authorization -= TransferAuthorizationHandler;
                transfer.Error -= TransferError;
                transfer.SlotRequest -= TransferSlotRequest;

                OnTransferRemoved(new TransferEventArgs { Transfer = transfer, Exception = e.Exception });
            }
        }

        void TransferSlotRequest(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_engine.Settings.MaxUploadThreads == 0)
                return;

            if (GetUsedSlotsCount() >= _engine.Settings.MaxUploadThreads)
            {
                e.Cancel = true;
            }
        }

        void TransferUploadItemNeeded(object sender, UploadItemNeededEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Content.Magnet.TTH))
            {
                if (e.Content.Magnet.FileName == "files.xml.bz2")
                {
                    // asked for file list
                }
            }
            else
            {
                var share = _engine.Share;
                if (share == null) return;

                var results = share.Search(new SearchQuery { 
                    Query = e.Content.Magnet.TTH, 
                    SearchType = SearchType.TTH 
                }, 1);

                if (results.Count == 1)
                {
                    e.UploadItem = new UploadItem (_engine.Settings.FileReadBufferSize) { Content = results[0] };
                    e.UploadItem.Error += UploadItemError;
                    e.UploadItem.Disposed += UploadItemDisposed;
                }
            }
        }

        void UploadItemDisposed(object sender, EventArgs e)
        {
            var item = (UploadItem)sender;
            item.Error -= UploadItemError;
            item.Disposed -= UploadItemDisposed;
        }

        void UploadItemError(object sender, UploadItemErrorEventArgs e)
        {
            OnError(e);
        }

        void TransferError(object sender, TransferErrorEventArgs e)
        {
            var transfer = (TransferConnection)sender;
            _engine.SourceManager.Error(transfer.Source);

            if (transfer.DownloadItem != null)
                _engine.DownloadManager.RemoveSource(transfer.Source, transfer.DownloadItem);
        }

        void TransferDirectionChanged(object sender, TransferDirectionChangedEventArgs e)
        {
            lock (_synRoot)
            {
                if (e.Previous == TransferDirection.Download)
                    _downloadThreadsCount--;
                if (e.Previous == TransferDirection.Upload)
                    _uploadThreadsCount--;
                

                if (e.Download)
                    _downloadThreadsCount++;
                else
                    _uploadThreadsCount++;
            }
        }

        public void DeleteOldRequests()
        {
            var swLock = Stopwatch.StartNew();
            var swRemove = new Stopwatch();
            lock (_synRoot)
            {
                swLock.Stop();

                swRemove.Start();

                for (var i = _allowedUsers.Count - 1; i >= 0; i--)
                {
                    if ((DateTime.Now - _allowedUsers[i].Added).TotalSeconds > ConnectionWaitTimeout)
                    {
                        _allowedUsers.RemoveAt(i);
                    }
                }

                swRemove.Stop();
            }

            if (swLock.ElapsedMilliseconds > 500 || swRemove.ElapsedMilliseconds > 500)
            {
                Logger.Warn("Slow DeleteOldReq L:{0} D:{1}", swLock.ElapsedMilliseconds, swRemove.ElapsedMilliseconds);
            }

        }

        void TransferAuthorizationHandler(object sender, TransferAuthorizationEventArgs e)
        {
            var sw = Stopwatch.StartNew();
            var swMsg = new Stopwatch();
            var swWhere = new Stopwatch();
            var swLock = Stopwatch.StartNew();

            lock (_synRoot)
            {
                swLock.Stop();

                var index = _allowedUsers.FindIndex(p => p.Nickname == e.UserNickname);

                if (index != -1)
                {
                    var source = new Source
                    {
                        UserNickname = e.UserNickname,
                        HubAddress = _allowedUsers[index].Hub.RemoteAddress
                    };


                    swWhere = Stopwatch.StartNew();
                    var list = _connections.Where(transferConnection => transferConnection.Source == source).ToList();
                    swWhere.Stop();

                    // do not allow duplicate connections
                    foreach (var connection in list)
                    {
                        connection.DisconnectAsync();
                    }

                    swMsg = Stopwatch.StartNew();
                    foreach (var transferConnection in list)
                    {
                        Logger.Info("Disconnecting old transfer {0}", source);
                    }
                    swMsg.Stop();
                    
                    var ea = new TransferManagerAuthorizationEventArgs
                    {
                        Connection = (TransferConnection)sender,
                        Nickname = e.UserNickname
                    };

                    OnTransferAuthorization(ea);

                    if (ea.Cancel)
                    {
                        Logger.Info("Transfer connection cancelled at top level {0}", e.UserNickname);
                        return;
                    }

                    e.Allowed = true;
                    e.OwnNickname = _allowedUsers[index].Hub.CurrentUser.Nickname;
                    e.HubAddress = _allowedUsers[index].Hub.RemoteAddress;
                    _allowedUsers.RemoveAt(index);

                    _engine.SourceManager.UpdateRequests(source, -1);
                }
                else
                {
                    Logger.Info("Can't find allow record for {0}", e.UserNickname);
                }
            }

            sw.Stop();
            if (sw.ElapsedMilliseconds > 300)
                Logger.Warn("Slow transfer authorization {0}ms L:{4}ms W:{1}ms M:{2}ms Connections:{3}", 
                    sw.ElapsedMilliseconds,
                    swWhere.ElapsedMilliseconds,
                    swMsg.ElapsedMilliseconds,
                    _connections.Count,
                    swLock.ElapsedMilliseconds);
        }

        void TransferDownloadItemNeeded(object sender, DownloadItemNeededEventArgs e)
        {
            var transfer = (TransferConnection)sender;
            e.DownloadItem = _engine.DownloadManager.GetDownloadItem(transfer.Source);
        }

        public void AllowConnection(string nickname, HubConnection hub)
        {
            lock (_synRoot)
            {
                if (HaveRequest(nickname, hub))
                    return;

                _allowedUsers.Add(new TransferRequest { Nickname = nickname, Added = DateTime.Now, Hub = hub });
            }
        }

        public bool HaveRequest(string nickname, HubConnection hub)
        {
            lock (_synRoot)
                return _allowedUsers.Any(tr => tr.Nickname == nickname && tr.Hub == hub);
        }

        public void Update()
        {
            DeleteOldRequests();

            var swLock = Stopwatch.StartNew();

            var swUpdate = new Stopwatch();
            lock (_synRoot)
            {
                swLock.Stop();

                swUpdate.Start();
                foreach (var transferConnection in Transfers())
                {
                    var idleSeconds = (DateTime.Now - transferConnection.LastEventTime).TotalSeconds;

                    if (transferConnection.DownloadItem != null && idleSeconds > DownloadInactivityTimeout)
                        transferConnection.DisconnectAsync();
                    else if (idleSeconds > UploadInactivityTimeout)
                        transferConnection.DisconnectAsync();
                }
                swUpdate.Stop();
            }

            if (swLock.ElapsedMilliseconds > 500 || swUpdate.ElapsedMilliseconds > 500)
            {
                Logger.Warn("Slow TransferUpdate L:{0} U:{1}", swLock.ElapsedMilliseconds, swUpdate.ElapsedMilliseconds);
            }

        }

        public void RequestTransfers(DownloadItem di)
        {
            if (di.Sources.Count == 0 || di.Priority == DownloadPriority.Pause) 
                return;

            if (_engine.Settings.MaxDownloadThreads != 0 && _allowedUsers.Count >= _engine.Settings.MaxDownloadThreads)
                return;

            var maxThreads = _engine.Settings.MaxDownloadThreads;
            if (maxThreads == 0) maxThreads = int.MaxValue;

            var reqLimit = maxThreads - _downloadThreadsCount;
            if (reqLimit <= 0) return;
            
            var sources = di.Sources;
            var sortedSources = _engine.SourceManager.SortByQualityDesc(sources);


            var freeSegments = di.TotalSegmentsCount == 0 ? (int)(di.Magnet.Size / DownloadItem.SegmentSize + 1) : di.TotalSegmentsCount - di.DoneSegmentsCount - di.ActiveSegmentsCount;

            reqLimit = Math.Min(reqLimit, freeSegments);

            reqLimit = Math.Min(sortedSources.Count, reqLimit);

            if (reqLimit == 0) return;

            for (var i = 0; i < reqLimit; i++)
            {
                var source = sortedSources[i];

                var hub = _engine.Hubs.Find(h => h.RemoteAddress == source.HubAddress);

                if (hub != null)
                {
                    lock (_synRoot)
                    {
                        if (_connections.Any(t => t.Source == source))
                            // we already have connection with that source
                            continue;
                        
                        if (_allowedUsers.Any(r => r.Nickname == source.UserNickname && r.Hub == hub))
                            // we already sent him a request
                            continue;

                        AllowConnection(source.UserNickname, hub);
                    }
                    _engine.SourceManager.UpdateRequests(source, 1);

                    Logger.Info("Requesting connection {0}", source.UserNickname);

                    if (_engine.Settings.ActiveMode)
                        hub.SendMessage(new ConnectToMeMessage { Nickname = source.UserNickname, Address = _engine.LocalTcpAddress }.Raw);
                    else
                        hub.SendMessage(new RevConnectToMeMessage { SenderNickname = hub.Settings.Nickname, TargetNickname = source.UserNickname }.Raw);
                }
            }
        }

        public void DropSource(Source source)
        {
            lock (_synRoot)
            {
                var swDispose = Stopwatch.StartNew();
                foreach (var transferConnection in _connections)
                {
                    if (transferConnection.Source == source)
                        transferConnection.Dispose();
                }
                swDispose.Stop();
                if (swDispose.ElapsedMilliseconds > 500)
                    Logger.Warn("!!! Slow connections dispose {0}ms", swDispose.ElapsedMilliseconds);
            }
        }

        public List<TransferConnection> GetUploadTransfers()
        {
            var list = new List<TransferConnection>();

            lock (_synRoot)
            {
                list.AddRange(_connections.Where(transferConnection => transferConnection.UploadItem != null));
            }
            return list;
        }

        public void Dispose()
        {
            lock (_synRoot)
            {
                foreach (var transferConnection in _connections)
                {
                    transferConnection.Dispose();
                }
            }
        }

        /// <summary>
        /// Threadsafe way to iterate the transfers
        /// </summary>
        /// <returns></returns>
        public IEnumerable<TransferConnection> Transfers()
        {
            lock (_synRoot)
            {
                foreach (var transferConnection in _connections)
                {
                    yield return transferConnection;    
                }
            }
        }

        public void StopTransfers(DownloadItem di)
        {
            foreach (var tr in Transfers().Where(t => t.DownloadItem == di))
            {
                tr.Dispose();
            }
        }
    }

    public class TransferManagerAuthorizationEventArgs : EventArgs
    {
        /// <summary>
        /// Set to true to disallow the connection
        /// </summary>
        public bool Cancel { get; set; }

        /// <summary>
        /// Gets user nickname
        /// </summary>
        public string Nickname { get; set; }

        /// <summary>
        /// Gets the transfer connection object
        /// </summary>
        public TransferConnection Connection { get; set; }
    }

    public static class TransfersHelper
    {
        /// <summary>
        /// Takes only upload transfers
        /// </summary>
        /// <param name="enumerable"></param>
        /// <returns></returns>
        public static IEnumerable<TransferConnection> Uploads(this IEnumerable<TransferConnection> enumerable)
        {
            return enumerable.Where(transferConnection => transferConnection.UploadItem != null);
        }

        /// <summary>
        /// Takes only download transfers
        /// </summary>
        /// <param name="enumerable"></param>
        /// <returns></returns>
        public static IEnumerable<TransferConnection> Downloads(this IEnumerable<TransferConnection> enumerable)
        {
            return enumerable.Where(transferConnection => transferConnection.DownloadItem != null);
        }

        /// <summary>
        /// Gets current average download speed
        /// </summary>
        /// <param name="enumerable"></param>
        /// <returns></returns>
        public static long DownloadSpeed(this IEnumerable<TransferConnection> enumerable)
        {
            return (long)enumerable.Sum(t => t.DownloadSpeed.GetSpeed());
        }

        /// <summary>
        /// Gets current average upload speed
        /// </summary>
        /// <param name="enumerable"></param>
        /// <returns></returns>
        public static long UploadSpeed(this IEnumerable<TransferConnection> enumerable)
        {
            return (long)enumerable.Sum(t => t.UploadSpeed.GetSpeed());
        }

        /// <summary>
        /// Gets total amount of bytes uploaded
        /// </summary>
        /// <param name="enumerable"></param>
        /// <returns></returns>
        public static long TotalUpload(this IEnumerable<TransferConnection> enumerable)
        {
            return enumerable.Sum(t => t.UploadSpeed.Total);
        }

        /// <summary>
        /// Gets total amount of bytes downloaded
        /// </summary>
        /// <param name="enumerable"></param>
        /// <returns></returns>
        public static long TotalDownload(this IEnumerable<TransferConnection> enumerable)
        {
            return enumerable.Sum(t => t.DownloadSpeed.Total);
        }

    }

    public class TransferEventArgs : EventArgs
    {
        public TransferConnection Transfer { get; set; }

        public Exception Exception { get; set; }
    }

    /// <summary>
    /// Contains information about allowed connection
    /// </summary>
    public struct TransferRequest
    {
        public string Nickname { get; set; }
        public HubConnection Hub { get; set; }
        public DateTime Added { get; set; }
    }
}
