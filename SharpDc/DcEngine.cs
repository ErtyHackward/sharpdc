//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Diagnostics;
using SharpDc.Collections;
using SharpDc.Connections;
using SharpDc.Events;
using SharpDc.Exceptions;
using SharpDc.Helpers;
using SharpDc.Interfaces;
using SharpDc.Logging;
using SharpDc.Managers;
using SharpDc.Messages;
using SharpDc.Structs;

namespace SharpDc
{
    /// <summary>
    /// Represents a main dc manager
    /// </summary>
    public class DcEngine
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private UdpConnection _udpConnection;
        private string _localAddress;
        private TcpConnectionListener _tcpConnectionListener;
        private int _active;
        private Timer _updateTimer;
        private IShare _share;
        private long _totalUploaded;
        private long _totalDownloaded;
        private readonly object _speedSyncRoot = new object();

        #region Properties
        /// <summary>
        /// Gets current engine settings
        /// </summary>
        public EngineSettings Settings { get; private set; }

        /// <summary>
        /// Public information that is used by default
        /// </summary>
        public TagInfo TagInfo { get; set; }

        /// <summary>
        /// Hubs collection
        /// </summary>
        public HubCollection Hubs { get; protected set; }

        /// <summary>
        /// Gets an udp connection.
        /// Sends active searches, receives active search requests
        /// </summary>
        public UdpConnection UdpConnection
        {
            get { return _udpConnection; }
        }
        
        /// <summary>
        /// Gets a TcpListener
        /// Allows to accept new connections
        /// </summary>
        public TcpConnectionListener TcpConnectionListener
        {
            get { return _tcpConnectionListener; }
        }

        /// <summary>
        /// Gets or sets local ip-address. This filed should contain external(router) ip to work in active mode.
        /// </summary>
        public string LocalAddress
        {
            get { return _localAddress; }
            set { 
                _localAddress = value;
                LocalUdpAddress = string.Format("{0}:{1}", _localAddress, Settings.UdpPort);
                LocalTcpAddress = string.Format("{0}:{1}", _localAddress, Settings.TcpPort);
            }
        }

        /// <summary>
        /// Gets local upd end-point (address:port)
        /// </summary>
        public string LocalUdpAddress { get; private set; }

        /// <summary>
        /// Gets local tcp end-point (address:port)
        /// </summary>
        public string LocalTcpAddress { get; private set; }
        
        public SearchManager SearchManager { get; set; }

        public DownloadManager DownloadManager { get; set; }

        public TransferManager TransferManager { get; set; }

        public SourceManager SourceManager { get; set; }

        /// <summary>
        /// Gets amount of bytes uploaded by the transfers since the program start
        /// </summary>
        public long TotalUploaded
        {
            get { return _totalUploaded + TransferManager.Transfers().TotalUpload(); }
        }

        /// <summary>
        /// Gets amount of bytes downloaded by the transfers since the program start
        /// </summary>
        public long TotalDownloaded
        {
            get { return _totalDownloaded + TransferManager.Transfers().TotalDownload(); }
        }

        /// <summary>
        /// Indicates if search and download can be performed
        /// </summary>
        public bool Active
        {
            get { return _active > 0; }
        }
        
        /// <summary>
        /// Gets or sets current share
        /// </summary>
        public IShare Share
        {
            get { return _share; }
            set {

                if (_share != null)
                {
                    _share.TotalSharedChanged -= ShareTotalSharedChanged;
                }

                _share = value;

                if (_share != null)
                {
                    _share.TotalSharedChanged += ShareTotalSharedChanged;
                }

            }
        }
        #endregion

        #region Events

        /// <summary>
        /// Global event to intercept incoming protocol messages
        /// Should be activated in the engine settings
        /// </summary>
        public event EventHandler<MessageEventArgs> IncomingMessage;

        private void OnIncomingMessage(MessageEventArgs e)
        {
            var handler = IncomingMessage;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// Global event to intercept outgoing protocol messages
        /// Should be activated in the engine settings
        /// </summary>
        public event EventHandler<MessageEventArgs> OutgoingMessage;

        private void OnOutgoingMessage(MessageEventArgs e)
        {
            var handler = OutgoingMessage;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// Occurs when engine changes its Active property
        /// </summary>
        public event EventHandler ActiveStatusChanged;

        public void OnActiveStatusChanged()
        {
            var handler = ActiveStatusChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>
        /// Allows to prevent connection
        /// </summary>
        public event EventHandler<ConnectionRequestEventArgs> ConnectionRequest;

        public void OnConnectionRequest(ConnectionRequestEventArgs e)
        {
            var handler = ConnectionRequest;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// Occurs when the engine receives a search request. Allows to block it.
        /// </summary>
        public event EventHandler<EngineSearchRequestEventArgs> SearchRequest;

        private void OnSearchRequest(EngineSearchRequestEventArgs e)
        {
            var handler = SearchRequest;
            if (handler != null) handler(this, e);
        }

        #endregion

        #region Constructor

        public DcEngine() : this(EngineSettings.Default) { }

        public DcEngine(EngineSettings settings)
        {
            Settings = settings;
            Settings.Changed += SettingsChanged;

            TagInfo = new TagInfo();

            Hubs = new HubCollection();
            Hubs.HubAdded += HubsHubAdded;
            Hubs.HubRemoved += HubsHubRemoved;

            SearchManager = new SearchManager(this);
            DownloadManager = new DownloadManager(this);
            DownloadManager.DownloadAdding += DownloadManagerDownloadAdding;

            TransferManager = new TransferManager(this);
            TransferManager.TransferAdded += TransferManagerTransferAdded;
            TransferManager.TransferRemoved += TransferManagerTransferRemoved;
            SourceManager = new SourceManager();

            InitUdp(Settings.UdpPort);
            InitTcp(Settings.TcpPort);

            if (string.IsNullOrEmpty(Settings.LocalAddress))
            {
                // find local ip
                var host = Dns.GetHostEntry(Dns.GetHostName());

                //Array.Sort(host.AddressList, (one, two) => (one.ToString().StartsWith("192.168")?1:0)+ (two.ToString().StartsWith("192.168")?-1:0));

                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily.ToString() == "InterNetwork")
                    {
                        LocalAddress = ip.ToString();
                        break;
                    }
                }
            }
            else 
                LocalAddress = Settings.LocalAddress;
        }

        #endregion


        void DownloadManagerDownloadAdding(object sender, CancelDownloadEventArgs e)
        {
            if (e.DownloadItem.SaveTargets == null)
                return;

            var drive1 = Path.GetPathRoot(e.DownloadItem.SaveTargets[0]);
           
            // here we should check for free space, 
            if (!string.IsNullOrEmpty(drive1) && FileHelper.GetFreeDiskSpace(drive1) < e.DownloadItem.Magnet.Size)
            {
                throw new NoFreeSpaceException(drive1);
            }

            if (string.IsNullOrEmpty(drive1))
                return;

            // lets check for file size restriction
            var driveInfo1 = new DriveInfo(drive1);
            var fileSystem1 = driveInfo1.DriveFormat;
            
            if (fileSystem1 == "FAT32")
            {
                // maximum file size 4 Gb
                if (e.DownloadItem.Magnet.Size > 4L * 1024 * 1024 * 1024)
                    throw new FileTooBigException(4L * 1024 * 1024 * 1024, e.DownloadItem.Magnet.Size);
            }

            if (fileSystem1 == "NTFS")
            {
                // maximum file size 16 Tb - 64 Kb
                if (e.DownloadItem.Magnet.Size > 16L * 1024 * 1024 * 1024 * 1024 - 64 * 1024)
                    throw new FileTooBigException(16L * 1024 * 1024 * 1024 * 1024 - 64 * 1024, e.DownloadItem.Magnet.Size);
            }

            if (Settings.InstantAllocate)
            {
                var path = e.DownloadItem.SaveTargets[0];
                var dirInfo = new DirectoryInfo(Path.GetDirectoryName(path));
                if (!dirInfo.Exists)
                {
                    dirInfo.Create();
                }
                FileHelper.AllocateFile(path, e.DownloadItem.Magnet.Size);
            }

        }

        /// <summary>
        /// Returns stream to read file from DC or from share
        /// Allows to read non-downloaded files
        /// </summary>
        /// <param name="magnet"></param>
        /// <returns>Stream to read data according to the magnet or null</returns>
        public DcStream GetStream(Magnet magnet)
        {
            // first of all try to find an item in the share
            if (Share != null)
            {
                var items = Share.Search(new SearchQuery { Query = magnet.TTH, SearchType = SearchType.TTH });

                if (items.Count > 0)
                {
                    // we have an item in the share...
                    return new DcStream(items[0].SystemPath, magnet);
                }
            }

            var item = DownloadManager.GetDownloadItem(magnet.TTH);

            if (item != null)
                return new DcStream(item);

            return null;
        }

        /// <summary>
        /// Starts async engine update by System.Threading.Timer
        /// </summary>
        /// <param name="updateInterval">Update interval in milliseconds</param>
        public void StartAsync(int updateInterval = 200)
        {
            _updateTimer = new Timer(o => Update(), null, 0, updateInterval);
        }

        /// <summary>
        /// Performs engine control operations, need to be called periodically
        /// - Checks hub connections (and restores if needed)
        /// - Sends connection requests if needed
        /// - Sends search requests
        /// - Disconnects timeouted connections
        /// </summary>
        void Update()
        {
            foreach (var hub in Hubs)
            {
                if (hub.ConnectionStatus == ConnectionStatus.Disconnected &&
                    hub.LastEventTime.AddSeconds(Settings.ReconnectTimeout) < DateTime.Now)
                {
                    Logger.Info("{0}: Hub inactivity timeout reached [{1}]. Reconnecting", hub.Settings.HubName, Settings.ReconnectTimeout);
                    hub.ConnectAsync();
                }
            }
            
            // no need to do anything before we have at least one connection
            if (!Active) 
                return;

            if (!Monitor.TryEnter(_updateTimer))
            {
                Logger.Warn("Unable to update engine, it is locked");
                return;
            }

            var sw = Stopwatch.StartNew();
            var swRequest = new Stopwatch();
            var swTransfers = new Stopwatch();
            var swSearches = new Stopwatch();
            try
            {
                swRequest.Start();
                foreach (var downloadItem in DownloadManager.EnumeratesItemsForProcess())
                {
                    if (TransferManager.RequestsAvailable)
                        TransferManager.RequestTransfers(downloadItem);

                    SearchManager.CheckItem(downloadItem);
                }
                swRequest.Stop();

                swTransfers.Start();
                TransferManager.Update();
                swTransfers.Stop();

                swSearches.Start();
                SearchManager.CheckPendingSearches();
                swSearches.Stop();
            }
            catch (Exception x)
            {
                Logger.Error("Exception when update: {0}", x.Message);
            }
            finally
            {
                Monitor.Exit(_updateTimer);
            }

            if (sw.ElapsedMilliseconds > 500)
                Logger.Warn("Slow engine update Total:{0}ms R:{1}ms T:{2}ms S:{3}ms", sw.ElapsedMilliseconds, swRequest.ElapsedMilliseconds, swTransfers.ElapsedMilliseconds, swSearches.ElapsedMilliseconds);
        }

        void TransferManagerTransferRemoved(object sender, TransferEventArgs e)
        {
            lock (_speedSyncRoot)
            {
                _totalUploaded += e.Transfer.UploadSpeed.Total;
                _totalDownloaded += e.Transfer.DownloadSpeed.Total;
            }

            if (Settings.DumpTransferProtocolMessages)
            {
                e.Transfer.IncomingMessage -= IncomingMessageHandler;
                e.Transfer.OutgoingMessage -= OutgoingMessageHandler;
            }

            e.Transfer.Dispose();
        }

        void TransferManagerTransferAdded(object sender, TransferEventArgs e)
        {
            if (Settings.DumpTransferProtocolMessages)
            {
                e.Transfer.IncomingMessage += IncomingMessageHandler;
                e.Transfer.OutgoingMessage += OutgoingMessageHandler;
            }
        }

        private void InitTcp(int p)
        {
            if (_tcpConnectionListener != null)
            {
                _tcpConnectionListener.IncomingConnection -= TcpConnectionListenerIncomingConnection;
                _tcpConnectionListener.Dispose();
                _tcpConnectionListener = null;
            }

            if (p > 0)
            {
                _tcpConnectionListener = new TcpConnectionListener(p, Settings.TcpBacklog);
                _tcpConnectionListener.IncomingConnection += TcpConnectionListenerIncomingConnection;
                _tcpConnectionListener.ListenAsync();
            }
        }

        void TcpConnectionListenerIncomingConnection(object sender, IncomingConnectionEventArgs e)
        {
            if (e.Socket.Connected)
            {
                var connLimit = Settings.ConnectionsLimit;

                if (connLimit != 0 && TransferManager.TransfersCount >= connLimit)
                {
                    Logger.Info("Connection limit {0} reached, dropping incoming connection", connLimit);
                    return;
                }

                e.Handled = true;
                var transfer = new TransferConnection(e.Socket);
                TransferManager.AddTransfer(transfer);
                transfer.ListenAsync();
            }
            else
            {
                Logger.Warn("We have disconnected incoming socket");
            }
        }
        

        private void InitUdp(int port)
        {
            if (_udpConnection != null)
            {
                _udpConnection.SearchResult -= UdpConnectionSearchResult;
                _udpConnection.Dispose();
                _udpConnection = null;
            }

            if (port > 0)
            {
                try
                {
                    _udpConnection = new UdpConnection(port);
                    _udpConnection.SearchResult += UdpConnectionSearchResult;
                }
                catch
                {
                    Logger.Error("Unable to initialize udp port {0}", port);
                }
            }
        }

        #region Handlers
        void UdpConnectionSearchResult(object sender, SearchResultEventArgs e)
        {
            SearchManager.InjectResult(e.Message);
        }

        void SettingsChanged(object sender, EngineSettingsEventArgs e)
        {
            switch (e.SettingType)
            {
                case EngineSettingType.TcpPort:
                    InitTcp(Settings.TcpPort);
                    break;
                case EngineSettingType.UdpPort:
                    InitUdp(Settings.UdpPort);
                    break;
                case EngineSettingType.PathDownload:
                    break;
                case EngineSettingType.PathFileLists:
                    break;
                case EngineSettingType.MaxDownloadThreads:
                    break;
                case EngineSettingType.MaxUploadThreads:
                    break;
                case EngineSettingType.ReconnectTimeout:
                    break;
                case EngineSettingType.ActiveMode:
                    break;
                case EngineSettingType.MaxFiles:
                    break;
                case EngineSettingType.VerifyFiles:
                    break;
                case EngineSettingType.InstantAllocate:
                    break;
                case EngineSettingType.GetUsersList:
                    break;
                case EngineSettingType.DumpHubProtocolMessages:
                    if (Settings.DumpHubProtocolMessages)
                    {
                        Hubs.ForEach(h =>
                            {
                                h.IncomingMessage += IncomingMessageHandler;
                                h.OutgoingMessage += OutgoingMessageHandler;
                            }
                        );
                    }
                    else
                    {
                        Hubs.ForEach(h =>
                            {
                                h.IncomingMessage -= IncomingMessageHandler;
                                h.OutgoingMessage -= OutgoingMessageHandler;
                            }
                        );
                    }
                    break;
                case EngineSettingType.DumpTransferProtocolMessages:
                    if (Settings.DumpTransferProtocolMessages)
                    {
                        foreach (var transferConnection in TransferManager.Transfers())
                        {
                            transferConnection.IncomingMessage += IncomingMessageHandler;
                            transferConnection.OutgoingMessage += OutgoingMessageHandler;
                        }
                    }
                    else
                    {
                        foreach (var transferConnection in TransferManager.Transfers())
                        {
                            transferConnection.IncomingMessage -= IncomingMessageHandler;
                            transferConnection.OutgoingMessage -= OutgoingMessageHandler;
                        }
                    }
                    break;
                case EngineSettingType.LocalAddress:
                    break;
                case EngineSettingType.SearchAlternativesInterval:
                    break;
                case EngineSettingType.TcpBacklog:
                    InitTcp(Settings.TcpPort);
                    break;
                case EngineSettingType.TcpReceiveBufferSize:
                    TcpConnection.DefaultConnectionBufferSize = Settings.TcpReceiveBufferSize;
                    break;
                case EngineSettingType.FileReadBufferSize:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        void HubsHubRemoved(object sender, HubsChangedEventArgs e)
        {
            e.Hub.ActiveStatusChanged -= HubActiveStatusChanged;
            e.Hub.IncomingConnectionRequest -= HubConnectionRequest;
            e.Hub.OutgoingConnectionRequest -= HubOutgoingConnectionRequest;
            e.Hub.SearchRequest -= HubSearchRequest;

            if (Settings.DumpHubProtocolMessages)
            {
                e.Hub.IncomingMessage -= IncomingMessageHandler;
                e.Hub.OutgoingMessage -= OutgoingMessageHandler;
            }
        }

        void HubsHubAdded(object sender, HubsChangedEventArgs e)
        {
            e.Hub.ActiveStatusChanged += HubActiveStatusChanged;
            e.Hub.IncomingConnectionRequest += HubConnectionRequest;
            e.Hub.OutgoingConnectionRequest += HubOutgoingConnectionRequest;
            e.Hub.SearchRequest += HubSearchRequest;

            if (Settings.DumpHubProtocolMessages)
            {
                e.Hub.IncomingMessage += IncomingMessageHandler;
                e.Hub.OutgoingMessage += OutgoingMessageHandler;
            }

            if (e.Hub.TagInfo == null)
                e.Hub.TagInfo = TagInfo;
        }
        
        void HubSearchRequest(object sender, SearchRequestEventArgs e)
        {
            var ea = new EngineSearchRequestEventArgs 
            {
                HubConnection = (HubConnection)sender, 
                Message = e.Message 
            };

            OnSearchRequest(ea);

            if (ea.Cancel)
                return;

            if (Share != null && e.Message.SearchType == SearchType.TTH)
            {
                var results = Share.Search(new SearchQuery { Query = e.Message.SearchRequest, SearchType = SearchType.TTH });
                if (results.Count > 0)
                {
                    var result = results[0];
                    var hub = (HubConnection)sender;
                    if (e.Message.SearchAddress.StartsWith("Hub:"))
                    {
                        var res = new SRMessage
                        {
                            FileName = result.VirtualPath,
                            FileSize = result.Magnet.Size,
                            TargetNickname = e.Message.SearchAddress.Remove(0, 4),
                            Nickname = hub.Settings.Nickname,
                            FreeSlots = 200,
                            HubAddress = hub.RemoteAddress,
                            HubName = "TTH:" + result.Magnet.TTH,
                            TotalSlots = 200
                        }.Raw;
                        hub.SendMessage(res);
                    }
                }
            }
        }

        void HubOutgoingConnectionRequest(object sender, OutgoingConnectionRequestEventArgs e)
        {
            var hubConnection = (HubConnection)sender;

            var ea = new ConnectionRequestEventArgs
            {
                UserNickname = e.Message.Nickname, 
                Address = e.Message.Address, 
                HubConnection = hubConnection
            };
            
            OnConnectionRequest(ea);

            if (ea.Cancel)
                return;

            TransferConnection transfer;
            try
            {
                transfer = new TransferConnection(e.Message.Address);
            }
            catch (Exception x)
            {
                Logger.Error("Unable to create outgoing transfer thread {0}", x.Message);
                return;
            }

            TransferManager.AddTransfer(transfer);
            transfer.FirstMessages = new[]
                                             {
                                                 new MyNickMessage { Nickname = hubConnection.Settings.Nickname }.Raw,
                                                 new LockMessage { ExtendedProtocol = true }.Raw
                                             };
            transfer.ConnectAsync();
        }

        void HubConnectionRequest(object sender, IncomingConnectionRequestEventArgs e)
        {
            // we have a request from a passive user, we need to be active to connect to him
            if (Settings.ActiveMode)
            {
                var hubConnection = (HubConnection)sender;

                var ea = new ConnectionRequestEventArgs
                {
                    UserNickname = e.Message.SenderNickname,
                    HubConnection = hubConnection
                };

                OnConnectionRequest(ea);

                if (ea.Cancel)
                    return;

                if (TransferManager.HaveRequest(e.Message.SenderNickname, sender as HubConnection))
                    return;

                // we need to set LocalAddress to allow connection
                e.LocalAddress = LocalTcpAddress;
                TransferManager.AllowConnection(e.Message.SenderNickname, sender as HubConnection);
            }
        }

        void OutgoingMessageHandler(object sender, MessageEventArgs e)
        {
            e.Connection = (TcpConnection)sender;
            OnOutgoingMessage(e);
        }

        void IncomingMessageHandler(object sender, MessageEventArgs e)
        {
            e.Connection = (TcpConnection)sender;
            OnIncomingMessage(e);
        }

        void HubActiveStatusChanged(object sender, EventArgs e)
        {
            var prevActive = _active;

            _active = Hubs.All().Count(h => h.Active);

            if (prevActive == 0 && _active > 0)
            {
                Logger.Info("Engine activated");
                OnActiveStatusChanged();
            }
            else if (prevActive > 0 && _active == 0)
            {
                Logger.Info("Engine deactivated");
                OnActiveStatusChanged();
            }
        }

        void ShareTotalSharedChanged(object sender, EventArgs e)
        {
            foreach (var hub in Hubs)
            {
                var userInfo = hub.CurrentUser;
                userInfo.Share = _share.TotalShared;
                hub.CurrentUser = userInfo;
            }
        }

        #endregion

        /// <summary>
        /// Allows to start a file download easily
        /// </summary>
        /// <param name="magnet">Magnet-link to search file</param>
        /// <param name="savePath">optional complete file path to be saved</param>
        /// <param name="sources">optional set of sources to use</param>
        public void DownloadFile(Magnet magnet, string savePath = null, IEnumerable<Source> sources = null)
        {
            if (string.IsNullOrEmpty(savePath))
            {
                savePath = Path.Combine(Settings.PathDownload, magnet.FileName);
            }
            
            if (!FileHelper.IsValidFilePath(savePath))
                throw new InvalidFileNameException("Storage path of the file is invalid") { FileName = savePath };

            if (!FileHelper.IsValidFileName(Path.GetFileName(savePath)))
                throw new InvalidFileNameException("Storage path of the file is invalid") { FileName = savePath };
            
            if (!string.IsNullOrEmpty(magnet.FileName))
            {
                if (!FileHelper.IsValidFileName(magnet.FileName))
                    throw new InvalidFileNameException("Name of the file is invalid") { FileName = magnet.FileName };
            }
            
            var di = new DownloadItem {
                Magnet = magnet, 
                SaveTargets = new List<string> {savePath}
            };
            
            if(sources != null)
            {
                di.Sources.AddRange(sources);
            }

            var result = SearchManager.GetHubResultByTTH(di.Magnet.TTH);

            if (result != null)
            {
                di.Sources.AddRange(result.Sources);
            }

            if (DownloadManager.AddDownload(di) && Active)
            {
                if (di.Sources.Count == 0)
                {
                    SearchManager.Search(di);
                }
                else
                {
                    TransferManager.RequestTransfers(di);
                }
            }
        }

        public void Connect()
        {
            Hubs.ForEach(h => h.ConnectAsync());
        }

        /// <summary>
        /// Releases all resources taken by this engine
        /// </summary>
        public void Dispose()
        {
            if (_updateTimer != null)
            {
                _updateTimer.Dispose();
                _updateTimer = null;
            }

            Hubs.ForEach(h => h.Dispose());
            TransferManager.Dispose();
            InitTcp(0);
            InitUdp(0);
        }
    }

    public class EngineSearchRequestEventArgs : EventArgs
    {
        public HubConnection HubConnection { get; set; }

        public bool Cancel { get; set; }

        public SearchMessage Message { get; set; }

    }

    public class ConnectionRequestEventArgs : EventArgs
    {
        /// <summary>
        /// Set this property to true if you don't want to establish connection
        /// </summary>
        public bool Cancel { get; set; }

        /// <summary>
        /// Gets user nickname
        /// </summary>
        public string UserNickname { get; set; }

        /// <summary>
        /// Get an optional address of the user
        /// </summary>
        public string Address { get; set; }

        /// <summary>
        /// Gets hub
        /// </summary>
        public HubConnection HubConnection { get; set; }

    }
}
