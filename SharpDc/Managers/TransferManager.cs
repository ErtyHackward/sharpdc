﻿// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

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

        /// <summary>
        /// list of passive users requests
        /// </summary>
        private readonly List<TransferRequest> _allowedUsers = new List<TransferRequest>();

        private int _downloadThreadsCount;
        private int _uploadThreadsCount;

        /// <summary>
        /// Maximum time between connection request (hub) and connection authorization(direct), in seconds
        /// Default: 30
        /// </summary>
        public int ConnectionWaitTimeout { get; set; }

        /// <summary>
        /// Maximum idle time of download connections before disconnect, in seconds
        /// Default: 60
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
                return _engine.Settings.MaxDownloadThreads == 0 ||
                       (_downloadThreadsCount < _engine.Settings.MaxDownloadThreads ||
                        _allowedUsers.Count < _engine.Settings.MaxDownloadThreads);
            }
        }

        /// <summary>
        /// Gets total connections count
        /// </summary>
        public int TransfersCount
        {
            get { return _connections.Count; }
        }

        #region Events 

        public event EventHandler<UploadItemEventArgs> TransferUploadItemError;

        private void OnUploadItemError(UploadItemEventArgs e)
        {
            var handler = TransferUploadItemError;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<UploadItemEventArgs> TransferUploadItemRequest;

        private void OnUploadItemRequest(UploadItemEventArgs e)
        {
            var handler = TransferUploadItemRequest;
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

        /// <summary>
        /// Occurs when transfer requests for an upload item
        /// </summary>
        public event EventHandler<UploadItemEventArgs> TransferUploadItemNeeded;

        protected virtual void OnTransferUploadItemNeeded(UploadItemEventArgs e)
        {
            var handler = TransferUploadItemNeeded;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// Occurs when transfer tries to dispose its UploadItem
        /// </summary>
        public event EventHandler<UploadItemEventArgs> TransferUploadItemDispose;

        protected virtual void OnTransferUploadItemDispose(UploadItemEventArgs e)
        {
            var handler = TransferUploadItemDispose;
            if (handler != null) handler(this, e);
        }

        #endregion

        public TransferManager(DcEngine engine)
        {
            ConnectionWaitTimeout = 30;
            DownloadInactivityTimeout = 60;
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
            transfer.UploadItemNeeded += TransferUploadItemNeededHandler;
            transfer.UploadItemDispose += TransferUploadItemDisposeHandler;
            transfer.DirectionChanged += TransferDirectionChanged;
            transfer.DownloadItemNeeded += TransferDownloadItemNeeded;
            transfer.Authorization += TransferAuthorizationHandler;
            transfer.Error += TransferError;
            transfer.SlotRequest += TransferSlotRequest;

            OnTransferAdded(new TransferEventArgs { Transfer = transfer });
        }

        private void TransferUploadItemDisposeHandler(object sender, UploadItemEventArgs e)
        {
            OnTransferUploadItemDispose(e);
        }

        private void TransferConnectionStatusChanged(object sender, ConnectionStatusEventArgs e)
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
                transfer.UploadItemNeeded -= TransferUploadItemNeededHandler;
                transfer.UploadItemDispose -= TransferUploadItemDisposeHandler;
                transfer.DirectionChanged -= TransferDirectionChanged;
                transfer.DownloadItemNeeded -= TransferDownloadItemNeeded;
                transfer.Authorization -= TransferAuthorizationHandler;
                transfer.Error -= TransferError;
                transfer.SlotRequest -= TransferSlotRequest;

                OnTransferRemoved(new TransferEventArgs { Transfer = transfer, Exception = e.Exception });
            }
        }

        private void TransferSlotRequest(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_engine.Settings.MaxUploadThreads == 0)
                return;

            if (GetUsedSlotsCount() >= _engine.Settings.MaxUploadThreads)
            {
                e.Cancel = true;
            }
        }

        private void TransferUploadItemNeededHandler(object sender, UploadItemEventArgs e)
        {
            OnTransferUploadItemNeeded(e);

            if (!e.Handled)
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
                    if (share == null)
                        return;

                    var results = share.Search(new SearchQuery
                                                   {
                                                       Query = e.Content.Magnet.TTH,
                                                       SearchType = SearchType.TTH
                                                   }, 1);

                    if (results.Count == 1)
                    {
                        e.UploadItem = new UploadItem(results[0], _engine.Settings.FileReadBufferSize);
                    }
                }
            }

            if (e.UploadItem != null)
            {
                if (e.UploadItem.Content.SystemPaths != null && e.UploadItem.Content.SystemPaths.Length > 1)
                {
                    e.UploadItem.SystemPath = _engine.FileSourceManager.GetBestSource(e.UploadItem);
                }

                e.UploadItem.EnableRequestEventFire = _engine.Settings.UploadSourceQualityEnabled;

                e.UploadItem.Error += UploadItemError;
                e.UploadItem.Disposed += UploadItemDisposed;
                e.UploadItem.Request += UploadItemRequest;
            }
        }

        void UploadItemRequest(object sender, UploadItemEventArgs e)
        {
            OnUploadItemRequest(e);
        }

        private void UploadItemDisposed(object sender, EventArgs e)
        {
            var item = (UploadItem)sender;
            item.Error -= UploadItemError;
            item.Request -= UploadItemRequest;
            item.Disposed -= UploadItemDisposed;
        }

        private void UploadItemError(object sender, UploadItemEventArgs e)
        {
            OnUploadItemError(e);
        }

        private void TransferError(object sender, TransferErrorEventArgs e)
        {
            var transfer = (TransferConnection)sender;
            _engine.SourceManager.Error(transfer.Source);

            if (transfer.DownloadItem != null)
                _engine.DownloadManager.RemoveSource(transfer.Source, transfer.DownloadItem);
        }

        private void TransferDirectionChanged(object sender, TransferDirectionChangedEventArgs e)
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
            var lockTime = new PerfLimit("DeleteOldReq Lock", 500);

            lock (_synRoot)
            {
                lockTime.Dispose();
                
                using (new PerfLimit("DeleteOldReq Remove"))
                for (var i = _allowedUsers.Count - 1; i >= 0; i--)
                {
                    if ((DateTime.Now - _allowedUsers[i].Added).TotalSeconds > ConnectionWaitTimeout)
                    {
                        _allowedUsers.RemoveAt(i);
                    }
                }
            }
        }

        private void TransferAuthorizationHandler(object sender, TransferAuthorizationEventArgs e)
        {
            var transfer = (TransferConnection)sender;

            // NOTE: we should remember that active users don't have allow records
            // They will have an address of a hub in the Source property
            // defined in ConnectToMe command handle section

            List<TransferConnection> list;
            TransferRequest req;
            Source source;

            lock (_synRoot)
            {

                var index = _allowedUsers.FindIndex(p => p.Nickname == e.UserNickname);

                if (index != -1)
                {
                    // passive user
                    req = _allowedUsers[index];
                    _allowedUsers.RemoveAt(index);
                }
                else
                {
                    // active user
                    if (!transfer.AllowedToConnect)
                        return;
                    req = TransferRequest.Empty;
                }

                source = new Source
                {
                    UserNickname = e.UserNickname,
                    HubAddress = req.IsEmpty ? transfer.Source.HubAddress : req.Hub.RemoteAddressString
                };

                // find connections with the same user
                list = _connections.Where(transferConnection => transferConnection.Source == source).ToList();
            }

            // do not allow duplicate connections
            using (new PerfLimit("TransferManager disconnecting transfers"))
            foreach (var connection in list)
            {
                connection.DisconnectAsync();
                Logger.Info("Disconnecting old transfer {0}", source);
            }


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

            lock (_synRoot)
            {
                if (req.IsEmpty)
                {
                    e.HubAddress = source.HubAddress;

                    HubConnection hub;

                    using (new PerfLimit("TransferManager Hub obtaining"))
                        hub = _engine.Hubs.FirstOrDefault(h => h.RemoteAddressString == e.HubAddress);

                    if (hub != null)
                    {
                        e.OwnNickname = hub.Settings.Nickname;
                    }
                    else
                    {
                        Logger.Error("Authorisation failed for {0}. There is no such hub: {1} ", e.UserNickname,
                            e.HubAddress);
                        e.Allowed = false;
                    }
                }
                else
                {
                    e.OwnNickname = req.Hub.CurrentUser.Nickname;
                    e.HubAddress = req.Hub.RemoteAddressString;
                }

                using (new PerfLimit("TransferManager SourceUpdate"))
                    _engine.SourceManager.UpdateRequests(source, -1);
            }
        }

        private void TransferDownloadItemNeeded(object sender, DownloadItemNeededEventArgs e)
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

            int disconnects = 0;

            List<TransferConnection> listCopy;

            using (new PerfLimit("TransferUpdate List creation", 300))
            lock (_synRoot)
            {
                listCopy = new List<TransferConnection>(_connections);
            }

            using (new PerfLimit("TransferUpdate Cycle", 300))
            foreach (var transferConnection in listCopy)
            {
                var idleSeconds = transferConnection.IdleSeconds;

                if (transferConnection.DownloadItem != null && DownloadInactivityTimeout > 0 &&
                    idleSeconds > DownloadInactivityTimeout)
                {
                    Logger.Info("Download inactivity timeout reached [{0}, {1}]. Disconnect.",
                        DownloadInactivityTimeout.ToString(), transferConnection.Source.ToString());
                    transferConnection.DisconnectAsync();
                    disconnects++;
                }
                else if (UploadInactivityTimeout > 0 && idleSeconds > UploadInactivityTimeout)
                {
                    Logger.Info("Upload inactivity timeout reached [{0}, {1}]. Disconnect.",
                        UploadInactivityTimeout.ToString(),
                        transferConnection.Source.ToString());
                    transferConnection.DisconnectAsync();
                    disconnects++;
                }
            }

            if (disconnects > 20)
            {
                Logger.Warn("Many disconnects at once: {0}", disconnects.ToString());
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

            var freeSegments = di.TotalSegmentsCount == 0
                                   ? (int)(di.Magnet.Size / di.SegmentLength + 1)
                                   : di.TotalSegmentsCount - di.DoneSegmentsCount - di.ActiveSegmentsCount;

            reqLimit = Math.Min(reqLimit, freeSegments);

            reqLimit = Math.Min(sortedSources.Count, reqLimit);

            if (reqLimit == 0) return;

            for (var i = 0; i < reqLimit; i++)
            {
                var source = sortedSources[i];

                foreach (var hub in GetHubsForSource(source))
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

                    Logger.Info("Requesting connection {0}", source);

                    if (_engine.Settings.ActiveMode)
                        hub.SendMessage(
                            new ConnectToMeMessage
                                {
                                    RecipientNickname = source.UserNickname,
                                    SenderAddress = _engine.LocalTcpAddress
                                }.Raw);
                    else
                        hub.SendMessage(
                            new RevConnectToMeMessage
                                {
                                    SenderNickname = hub.Settings.Nickname,
                                    TargetNickname = source.UserNickname
                                }.Raw);
                }
            }
        }

        private IEnumerable<HubConnection> GetHubsForSource(Source src)
        {
            var hub = _engine.Hubs.Find(h => h.RemoteAddressString == src.HubAddress);

            if (hub != null)
                yield return hub;
            else
            {
                // we don't know the exact hub so send request to every possible
                foreach (var hub1 in _engine.Hubs)
                {
                    if (hub1.Settings.GetUsersList && !hub1.Users.ContainsKey(src.UserNickname))
                    {
                        continue;
                    }

                    yield return hub1;
                }
            }
        }


        public void DropSource(Source source)
        {
            lock (_synRoot)
            {
                using (new PerfLimit("Connection dispose", 500))
                foreach (var transferConnection in _connections)
                {
                    if (transferConnection.Source == source)
                        transferConnection.DisconnectAsync();
                }
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
                using (new PerfLimit("Transfers enumeration", 300))
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
        static TransferRequest()
        {
            Empty = new TransferRequest();
        }

        public string Nickname { get; set; }
        public HubConnection Hub { get; set; }
        public DateTime Added { get; set; }

        public bool IsEmpty
        {
            get { return string.IsNullOrEmpty(Nickname); }
        }

        public static TransferRequest Empty { get; private set; }
    }
}