// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

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
        private readonly object _updateSynRoot = new object();
        private IShare _share;
        private long _totalUploaded;
        private long _totalDownloaded;
        private readonly object _speedSyncRoot = new object();
        private readonly List<DcStream> _streams = new List<DcStream>();

        /// <summary>
        /// Gets or sets thread pool used by engine
        /// </summary>
        public static IThreadPoolProxy ThreadPool { get; set; }

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
            set
            {
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

        public FileSourceManager FileSourceManager { get; set; }

        public StatisticsManager StatisticsManager { get; set; }

        public UploadCacheManager UploadCacheManager { get; set; }

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
            set
            {
                if (_share != null)
                {
                    _share.TotalSharedChanged -= _share_TotalSharedChanged;
                }

                _share = value;

                if (_share != null)
                {
                    _share.TotalSharedChanged +=_share_TotalSharedChanged;
                }
                UpdateShared();
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

        static DcEngine()
        {
            DcEngine.ThreadPool = new ThreadPoolProxy();
        }

        public DcEngine() : this(EngineSettings.Default)
        {
        }

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
            DownloadManager.DownloadCompleted += DownloadManager_DownloadCompleted;

            TransferManager = new TransferManager(this);
            TransferManager.TransferAdded += TransferManagerTransferAdded;
            TransferManager.TransferRemoved += TransferManagerTransferRemoved;
            TransferManager.TransferUploadItemError += TransferManagerTransferUploadItemError;
            TransferManager.TransferUploadItemRequest += TransferManager_TransferUploadItemRequest;
            
            SourceManager = new SourceManager();
            FileSourceManager = new FileSourceManager();

            StatisticsManager = new StatisticsManager(this);

            UploadCacheManager = new UploadCacheManager(this);

            if (Settings.AutoSelectPort)
            {
                var tcpPort = Settings.TcpPort;
                var udpPort = Settings.UdpPort;

                while (!TcpConnectionListener.IsPortFree(tcpPort))
                {
                    tcpPort++;
                }
                if (Settings.TcpPort != tcpPort)
                    Settings.TcpPort = tcpPort;
                else
                {
                    InitTcp(Settings.TcpPort);
                }
                
                while (!UdpConnection.IsPortFree(udpPort))
                {
                    udpPort++;
                }
                if (Settings.UdpPort != udpPort)
                    Settings.UdpPort = udpPort;
                else
                {
                    InitUdp(Settings.UdpPort);
                }
            }
            else
            {
                InitUdp(Settings.UdpPort);
                InitTcp(Settings.TcpPort);
            }
            
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

        /// <summary>
        /// Returns stream to read file from DC or from share
        /// Allows to read non-downloaded files
        /// </summary>
        /// <param name="magnet"></param>
        /// <returns>Stream to read data according to the magnet or null</returns>
        public DcStream GetStream(Magnet magnet)
        {
            DcStream stream = null;
            // first of all try to find an item in the share
            if (Share != null)
            {
                var items = Share.Search(new SearchQuery { Query = magnet.TTH, SearchType = SearchType.TTH });

                if (items.Count > 0)
                {
                    // we have an item in the share...
                    
                    stream = new DcStream(items[0].SystemPath, magnet);
                }
            }

            if (stream == null)
            {
                var item = DownloadManager.GetDownloadItem(magnet.TTH);

                if (item != null)
                    stream = new DcStream(item);
            }

            if (stream != null)
            {
                lock (_streams)
                {
                    _streams.Add(stream);    
                }
                stream.Disposed += StreamDisposed;
            }

            return stream;
        }

        void StreamDisposed(object sender, EventArgs e)
        {
            var stream = (DcStream)sender;
            lock (_streams)
            {
                _streams.Remove(stream);
            }
            stream.Disposed -= StreamDisposed;
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
        public void Update()
        {
            foreach (var hub in Hubs)
            {
                if (Settings.ReconnectTimeout != 0 && hub.ConnectionStatus == ConnectionStatus.Disconnected &&
                    hub.LastEventTime.AddSeconds(Settings.ReconnectTimeout) < DateTime.Now)
                {
                    Logger.Info("{0}: Hub inactivity timeout reached [{1}]. Reconnecting", hub.Settings.HubName,
                                Settings.ReconnectTimeout);
                    hub.ConnectAsync();
                }
            }

            // no need to do anything before we have at least one connection
            if (!Active)
                return;

            if (!Monitor.TryEnter(_updateSynRoot))
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
                    if (downloadItem.Priority == DownloadPriority.Pause)
                        continue;

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
                Monitor.Exit(_updateSynRoot);
            }

            if (sw.ElapsedMilliseconds > 300)
                Logger.Warn("Slow engine update Total:{0}ms R:{1}ms T:{2}ms S:{3}ms", sw.ElapsedMilliseconds,
                            swRequest.ElapsedMilliseconds, swTransfers.ElapsedMilliseconds,
                            swSearches.ElapsedMilliseconds);
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
                    _udpConnection = new UdpConnection(port, Settings.NetworkInterface);
                    _udpConnection.SearchResult += UdpConnectionSearchResult;
                }
                catch
                {
                    Logger.Error("Unable to initialize udp port {0}", port);
                }
            }
        }

        #region Handlers

        private void DownloadManagerDownloadAdding(object sender, CancelDownloadEventArgs e)
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
                // maximum file size is 4 Gb
                if (e.DownloadItem.Magnet.Size > 4L * 1024 * 1024 * 1024)
                    throw new FileTooBigException(4L * 1024 * 1024 * 1024, e.DownloadItem.Magnet.Size);
            }

            if (fileSystem1 == "NTFS")
            {
                // maximum file size is 16 Tb - 64 Kb
                if (e.DownloadItem.Magnet.Size > 16L * 1024 * 1024 * 1024 * 1024 - 64 * 1024)
                    throw new FileTooBigException(16L * 1024 * 1024 * 1024 * 1024 - 64 * 1024,
                                                  e.DownloadItem.Magnet.Size);
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

        private void DownloadManager_DownloadCompleted(object sender, DownloadCompletedEventArgs e)
        {
            lock (_streams)
            {
                var stream = _streams.FirstOrDefault(s => s.Magnet.TTH == e.DownloadItem.Magnet.TTH);

                if (stream != null)
                {
                    stream.ReplaceDownloadItemWithFile(e.DownloadItem.SaveTargets[0]);
                }
            }

            if (Share != null)
            {
                Share.AddFile(new ContentItem(e.DownloadItem));
            }
        }

        private void TcpConnectionListenerIncomingConnection(object sender, IncomingConnectionEventArgs e)
        {
            if (e.Socket.Connected)
            {
                var connLimit = Settings.ConnectionsLimit;

                if (connLimit != 0 && TransferManager.TransfersCount >= connLimit)
                {
                    Logger.Warn("Connection limit {0} reached, dropping incoming connection", connLimit);
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

        private void TransferManagerTransferRemoved(object sender, TransferEventArgs e)
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

        private void TransferManagerTransferAdded(object sender, TransferEventArgs e)
        {
            if (Settings.DumpTransferProtocolMessages)
            {
                e.Transfer.IncomingMessage += IncomingMessageHandler;
                e.Transfer.OutgoingMessage += OutgoingMessageHandler;
            }

            if (Settings.BackgroundSeedMode)
            {
                e.Transfer.UseBackgroundSeedMode = true;
            }
        }

        private void TransferManagerTransferUploadItemError(object sender, UploadItemEventArgs e)
        {
            if (Settings.UploadSourceQualityEnabled)
                FileSourceManager.RegisterError(e.UploadItem.SystemPath);
        }

        void TransferManager_TransferUploadItemRequest(object sender, UploadItemEventArgs e)
        {
            FileSourceManager.RegisterRequest(e.UploadItem.SystemPath);
        }

        private void UdpConnectionSearchResult(object sender, SearchResultEventArgs e)
        {
            SearchManager.InjectResult(e.Message);
        }

        private void SettingsChanged(object sender, EngineSettingsEventArgs e)
        {
            switch (e.SettingType)
            {
                case EngineSettingType.TcpPort:
                    InitTcp(Settings.TcpPort);
                    break;
                case EngineSettingType.UdpPort:
                    InitUdp(Settings.UdpPort);
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
                case EngineSettingType.TcpBacklog:
                    InitTcp(Settings.TcpPort);
                    break;
                case EngineSettingType.TcpReceiveBufferSize:
                    TcpConnection.DefaultConnectionBufferSize = Settings.TcpReceiveBufferSize;
                    break;
            }
        }

        private void HubsHubRemoved(object sender, HubsChangedEventArgs e)
        {
            e.Hub.ActiveStatusChanged -= HubActiveStatusChanged;
            e.Hub.IncomingConnectionRequest -= HubConnectionRequest;
            e.Hub.OutgoingConnectionRequest -= HubOutgoingConnectionRequest;
            e.Hub.SearchRequest -= HubSearchRequest;
            e.Hub.PassiveSearchResult -= HubPassiveSearchResult;
            e.Hub.OwnIpReceived -= HubOwnIpReceived;

            if (Settings.DumpHubProtocolMessages)
            {
                e.Hub.IncomingMessage -= IncomingMessageHandler;
                e.Hub.OutgoingMessage -= OutgoingMessageHandler;
            }
        }

        private void HubsHubAdded(object sender, HubsChangedEventArgs e)
        {
            if (Settings.NetworkInterface != null)
            {
                e.Hub.LocalAddress = new IPEndPoint(Settings.NetworkInterface, 0);
            }

            e.Hub.ActiveStatusChanged += HubActiveStatusChanged;
            e.Hub.IncomingConnectionRequest += HubConnectionRequest;
            e.Hub.OutgoingConnectionRequest += HubOutgoingConnectionRequest;
            e.Hub.SearchRequest += HubSearchRequest;
            e.Hub.PassiveSearchResult += HubPassiveSearchResult;
            e.Hub.OwnIpReceived += HubOwnIpReceived;

            if (Settings.DumpHubProtocolMessages)
            {
                e.Hub.IncomingMessage += IncomingMessageHandler;
                e.Hub.OutgoingMessage += OutgoingMessageHandler;
            }

            if (e.Hub.TagInfo == null)
                e.Hub.TagInfo = TagInfo;
        }

        private void HubOwnIpReceived(object sender, EventArgs e)
        {
            var hub = (HubConnection)sender;
            LocalAddress = hub.CurrentUser.IP;
        }

        private void HubPassiveSearchResult(object sender, SearchResultEventArgs e)
        {
            SearchManager.InjectResult(e.Message);
        }

        private void HubSearchRequest(object sender, SearchRequestEventArgs e)
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
                var results =
                    Share.Search(new SearchQuery { Query = e.Message.SearchRequest, SearchType = SearchType.TTH });
                if (results.Count > 0)
                {
                    var result = results[0];
                    var hub = (HubConnection)sender;
                    var res = new SRMessage
                                  {
                                      FileName = result.VirtualPath,
                                      FileSize = result.Magnet.Size,
                                      Nickname = hub.Settings.Nickname,
                                      FreeSlots =
                                          Settings.MaxUploadThreads > 0
                                              ? Settings.MaxUploadThreads - TransferManager.TransfersCount
                                              : 0,
                                      HubAddress = hub.RemoteAddressString,
                                      HubName = "TTH:" + result.Magnet.TTH,
                                      TotalSlots = Settings.MaxUploadThreads
                                  };

                    if (e.Message.SearchAddress.StartsWith("Hub:"))
                    {
                        res.TargetNickname = e.Message.SearchAddress.Remove(0, 4);
                        hub.SendMessage(res.Raw);
                    }
                    else
                    {
                        UdpConnection.SendMessage(res.Raw, e.Message.SearchAddress);
                    }
                }
            }
        }

        private void HubOutgoingConnectionRequest(object sender, OutgoingConnectionRequestEventArgs e)
        {
            var hubConnection = (HubConnection)sender;

            var ea = new ConnectionRequestEventArgs
                         {
                             UserNickname = "",
                             Address = e.Message.SenderAddress,
                             HubConnection = hubConnection
                         };

            OnConnectionRequest(ea);

            if (ea.Cancel)
            {
                return;
            }

            TransferConnection transfer;
            try
            {
                transfer = new TransferConnection(e.Message.SenderAddress)
                               {
                                   AllowedToConnect = true,
                                   Source = new Source { HubAddress = hubConnection.RemoteAddressString }
                               };

                if (Settings.NetworkInterface != null)
                {
                    transfer.LocalAddress = new IPEndPoint(Settings.NetworkInterface, 0);
                }
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

        private void HubConnectionRequest(object sender, IncomingConnectionRequestEventArgs e)
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

        private void OutgoingMessageHandler(object sender, MessageEventArgs e)
        {
            e.Connection = (TcpConnection)sender;
            OnOutgoingMessage(e);
        }

        private void IncomingMessageHandler(object sender, MessageEventArgs e)
        {
            e.Connection = (TcpConnection)sender;
            OnIncomingMessage(e);
        }

        private void HubActiveStatusChanged(object sender, EventArgs e)
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

        private void UpdateShared()
        {
            foreach (var hub in Hubs)
            {
                var userInfo = hub.CurrentUser;
                userInfo.Share = _share == null ? 0 : _share.TotalShared;
                hub.CurrentUser = userInfo;
            }
        }

        void _share_TotalSharedChanged(object sender, EventArgs e)
        {
            UpdateShared();
        }

        #endregion

        /// <summary>
        /// Allows to start a file download easily
        /// </summary>
        /// <param name="magnet">Magnet-link to search file</param>
        /// <param name="fullSystemPath">optional complete file path to be saved</param>
        /// <param name="sources">optional set of sources to use</param>
        public DownloadItem DownloadFile(Magnet magnet, string fullSystemPath = null, IEnumerable<Source> sources = null)
        {
            if (string.IsNullOrEmpty(fullSystemPath))
            {
                fullSystemPath = Path.Combine(Settings.PathDownload, magnet.FileName);
            }
            else
            {
                if (Directory.Exists(fullSystemPath))
                    throw new ArgumentException("Provide full system path to the file, not the directory", fullSystemPath);
            }
            

            if (!FileHelper.IsValidFilePath(fullSystemPath))
                throw new InvalidFileNameException("Storage path of the file is invalid") { FileName = fullSystemPath };

            if (!FileHelper.IsValidFileName(Path.GetFileName(fullSystemPath)))
                throw new InvalidFileNameException("Storage path of the file is invalid") { FileName = fullSystemPath };

            if (!string.IsNullOrEmpty(magnet.FileName))
            {
                if (!FileHelper.IsValidFileName(magnet.FileName))
                    throw new InvalidFileNameException("Name of the file is invalid") { FileName = magnet.FileName };
            }

            var di = new DownloadItem(magnet)
                         {
                             SaveTargets = new List<string> { fullSystemPath }
                         };

            

            if (sources != null)
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
                    lock (DownloadManager.SyncRoot)
                        SearchManager.Search(di);
                }
                else
                {
                    TransferManager.RequestTransfers(di);
                }
            }

            return di;
        }

        /// <summary>
        /// Stops download process and removes download
        /// </summary>
        /// <param name="di"></param>
        public void RemoveDownload(DownloadItem di)
        {
            di.Priority = DownloadPriority.Pause;
            TransferManager.StopTransfers(di);

            while (di.ActiveSegmentsCount > 0)
            {
                Thread.Sleep(0);
            }

            DownloadManager.RemoveDownload(di);
        }

        public void PauseDownload(DownloadItem item)
        {
            item.Priority = DownloadPriority.Pause;
            TransferManager.StopTransfers(item);
        }

        /// <summary>
        /// Asynchronously starts all hub connections
        /// If we have more than 20 connections each of them will start gradually with small delay
        /// </summary>
        public void Connect()
        {
            if (Hubs.Count == 0)
                return;

            if (Hubs.Count < 20)
            {
                Hubs.ForEach(h => h.ConnectAsync());
            }
            else
            {
                ThreadPool.QueueWorkItem(delegate
                                                      {
                                                          // start new connections gradually

                                                          var list = new List<HubConnection>(Hubs);

                                                          for (int i = 0; i < list.Count; i++)
                                                          {
                                                              list[i].ConnectAsync();
                                                              Thread.Sleep(100);
                                                          }
                                                      });
            }
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

            HttpUploadItem.Manager.Dispose();

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
        /// Gets remote user nickname if possible (in case of active search will be empty)
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