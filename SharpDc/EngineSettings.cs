// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.IO;
using System.Net;
using SharpDc.Events;

namespace SharpDc
{
    /// <summary>
    /// Contains settings for engine
    /// </summary>
    public class EngineSettings
    {
        private int _udpPort;
        private int _tcpPort;
        private string _pathDownload;
        private string _pathFileLists;
        private int _maxDownloadThreads;
        private int _maxUploadThreads;
        private int _reconnectTimeout;
        private bool _activeMode;
        private int _maxFiles;
        private bool _verifyFiles;
        private bool _instantAllocate;
        private bool _getUsersList;
        private bool _dumpHubMessages;
        private bool _dumpTransferMessages;
        private string _localAddress;
        private int _tcpBackLog;
        private int _tcpReceiveBufferSize;
        private int _fileReadBufferSize;
        private int _connectionsLimit;
        private IPAddress _netInterface;
        private bool _useSparse;
        private bool _autoSelectPort;
        private bool _measureUploadSourceQuality;

        /// <summary>
        /// Occurs when some setting is changed
        /// </summary>
        public event EventHandler<EngineSettingsEventArgs> Changed;

        private int _searchAlternativesInterval;
        private bool _backgroundSeedMode;
        private long _httpMemoryCacheSize;
        private int _httpQueueLimit;
        private int _httpConnectionsPerServer;
        private bool _asyncTransfers;

        protected void OnChanged(EngineSettingType st)
        {
            EventHandler<EngineSettingsEventArgs> handler = Changed;
            if (handler != null) handler(this, new EngineSettingsEventArgs(st));
        }

        /// <summary>
        /// Maximum amount of connections allowed, 0 - unlimited
        /// </summary>
        public int ConnectionsLimit
        {
            get { return _connectionsLimit; }
            set
            {
                if (_connectionsLimit != value)
                {
                    _connectionsLimit = value;
                    OnChanged(EngineSettingType.ConnectionsLimit);
                }
            }
        }

        /// <summary>
        /// Port used for incoming TCP connections
        /// </summary>
        public int TcpPort
        {
            get { return _tcpPort; }
            set
            {
                if (_tcpPort != value)
                {
                    _tcpPort = value;
                    OnChanged(EngineSettingType.TcpPort);
                }
            }
        }

        /// <summary>
        /// Port used for UDP connections
        /// </summary>
        public int UdpPort
        {
            get { return _udpPort; }
            set
            {
                if (_udpPort != value)
                {
                    _udpPort = value;
                    OnChanged(EngineSettingType.UdpPort);
                }
            }
        }

        /// <summary>
        /// Default download folder path
        /// </summary>
        public string PathDownload
        {
            get { return _pathDownload; }
            set
            {
                if (_pathDownload != value)
                {
                    _pathDownload = value;
                    OnChanged(EngineSettingType.PathDownload);
                }
            }
        }

        /// <summary>
        /// Path where filelists been stored
        /// </summary>
        public string PathFileLists
        {
            get { return _pathFileLists; }
            set
            {
                if (_pathFileLists != value)
                {
                    _pathFileLists = value;
                    OnChanged(EngineSettingType.PathFileLists);
                }
            }
        }

        /// <summary>
        /// Limit for maximum download threads
        /// </summary>
        public int MaxDownloadThreads
        {
            get { return _maxDownloadThreads; }
            set
            {
                if (_maxDownloadThreads != value)
                {
                    _maxDownloadThreads = value;
                    OnChanged(EngineSettingType.MaxDownloadThreads);
                }
            }
        }

        /// <summary>
        /// Limit for maximum upload threads (Slots), 0 means unlimited
        /// </summary>
        public int MaxUploadThreads
        {
            get { return _maxUploadThreads; }
            set
            {
                if (_maxUploadThreads != value)
                {
                    _maxUploadThreads = value;
                    OnChanged(EngineSettingType.MaxUploadThreads);
                }
            }
        }

        /// <summary>
        /// Seconds between hub reconnect attempts, 0 - don't reconnect
        /// </summary>
        public int ReconnectTimeout
        {
            get { return _reconnectTimeout; }
            set
            {
                if (_reconnectTimeout != value)
                {
                    _reconnectTimeout = value;
                    OnChanged(EngineSettingType.ReconnectTimeout);
                }
            }
        }

        /// <summary>
        /// Mode used for hubs connections
        /// </summary>
        public bool ActiveMode
        {
            get { return _activeMode; }
            set
            {
                if (_activeMode != value)
                {
                    _activeMode = value;
                    OnChanged(EngineSettingType.ActiveMode);
                }
            }
        }

        /// <summary>
        /// Maximum simultaneous downloads
        /// </summary>
        public int MaxFiles
        {
            get { return _maxFiles; }
            set
            {
                if (_maxFiles != value)
                {
                    _maxFiles = value;
                    OnChanged(EngineSettingType.MaxFiles);
                }
            }
        }

        /// <summary>
        /// Determines whether files should be verified
        /// </summary>
        public bool VerifyFiles
        {
            get { return _verifyFiles; }
            set
            {
                if (_verifyFiles != value)
                {
                    _verifyFiles = value;
                    OnChanged(EngineSettingType.VerifyFiles);
                }
            }
        }

        /// <summary>
        /// Gets or sets value indicating if the engine should allocate a file immediately 
        /// </summary>
        public bool InstantAllocate
        {
            get { return _instantAllocate; }
            set
            {
                if (_instantAllocate != value)
                {
                    _instantAllocate = value;
                    OnChanged(EngineSettingType.InstantAllocate);
                }
            }
        }

        /// <summary>
        /// Gets or sets value indicating if the hubs should request a user list
        /// </summary>
        public bool GetUsersList
        {
            get { return _getUsersList; }
            set
            {
                if (_getUsersList != value)
                {
                    _getUsersList = value;
                    OnChanged(EngineSettingType.GetUsersList);
                }
            }
        }

        /// <summary>
        /// Gets or sets value indicating if the messages from hub should be dumped
        /// </summary>
        public bool DumpHubProtocolMessages
        {
            get { return _dumpHubMessages; }
            set
            {
                if (_dumpHubMessages != value)
                {
                    _dumpHubMessages = value;
                    OnChanged(EngineSettingType.DumpHubProtocolMessages);
                }
            }
        }

        /// <summary>
        /// Gets or sets value indicating if the messages from transfers should be dumped
        /// </summary>
        public bool DumpTransferProtocolMessages
        {
            get { return _dumpTransferMessages; }
            set
            {
                if (_dumpTransferMessages != value)
                {
                    _dumpTransferMessages = value;
                    OnChanged(EngineSettingType.DumpTransferProtocolMessages);
                }
            }
        }

        /// <summary>
        /// Gets or sets local ip-address. This filed should contain external(router) ip to work in active mode.
        /// </summary>
        public string LocalAddress
        {
            get { return _localAddress; }
            set
            {
                if (_localAddress != value)
                {
                    _localAddress = value;
                    OnChanged(EngineSettingType.LocalAddress);
                }
            }
        }

        /// <summary>
        /// Gets or sets interval between search for alternative sources in minutes
        /// </summary>
        public int SearchAlternativesInterval
        {
            get { return _searchAlternativesInterval; }
            set
            {
                if (_searchAlternativesInterval != value)
                {
                    _searchAlternativesInterval = value;
                    OnChanged(EngineSettingType.SearchAlternativesInterval);
                }
            }
        }

        /// <summary>
        /// Gets or sets interval between search for alternative sources in minutes
        /// </summary>
        public int TcpBacklog
        {
            get { return _tcpBackLog; }
            set
            {
                if (_tcpBackLog != value)
                {
                    _tcpBackLog = value;
                    OnChanged(EngineSettingType.TcpBacklog);
                }
            }
        }

        /// <summary>
        /// Gets or sets the default size of the tcp connection buffer used to read data.
        /// Concrete buffer size can be changed on each connection
        /// </summary>
        public int TcpReceiveBufferSize
        {
            get { return _tcpReceiveBufferSize; }
            set
            {
                if (_tcpReceiveBufferSize != value)
                {
                    _tcpReceiveBufferSize = value;
                    OnChanged(EngineSettingType.TcpReceiveBufferSize);
                }
            }
        }

        /// <summary>
        /// Gets or sets the default size of the FileStream buffer used to read data for uploads.
        /// </summary>
        public int FileReadBufferSize
        {
            get { return _fileReadBufferSize; }
            set
            {
                if (_fileReadBufferSize != value)
                {
                    _fileReadBufferSize = value;
                    OnChanged(EngineSettingType.FileReadBufferSize);
                }
            }
        }

        /// <summary>
        /// Gets or sets network interface to use
        /// </summary>
        public IPAddress NetworkInterface
        {
            get { return _netInterface; }
            set
            {
                if (!Equals(_netInterface, value))
                {
                    _netInterface = value;
                    OnChanged(EngineSettingType.NetworkInterface);
                }
            }
        }

        /// <summary>
        /// Uses sparse files if possible (only works in Windows)
        /// Usefull for video on demand services, allows to quickly write at the end of huge and empty file
        /// Read more at http://en.wikipedia.org/wiki/Sparse_file
        /// </summary>
        public bool UseSparseFiles
        {
            get { return _useSparse; }
            set
            {
                if (_useSparse != value)
                {
                    _useSparse = value;
                    OnChanged(EngineSettingType.UseSparseFiles);
                }
            }
        }

        /// <summary>
        /// Engine will check for the TCP and UDP ports and change them if they are busy
        /// If false will throw an exception
        /// </summary>
        public bool AutoSelectPort
        {
            get { return _autoSelectPort; }
            set
            {
                if (_autoSelectPort != value)
                {
                    _autoSelectPort = value;
                    OnChanged(EngineSettingType.AutoSelectPort);
                }
            }
        }

        /// <summary>
        /// If true all upload threads will work in background mode consuming less resources
        /// Affected only Windows Vista or later
        /// </summary>
        public bool BackgroundSeedMode
        {
            get { return _backgroundSeedMode; }
            set {
                if (_backgroundSeedMode != value)
                {
                    _backgroundSeedMode = value;
                    OnChanged(EngineSettingType.BackgroundSeedMode);
                }
            }
        }
        
        
        /// <summary>
        /// If enabled, all upload sources will be measured for errors and better sources will be selected for new uploads
        /// Reasonable only for servers
        /// </summary>
        public bool UploadSourceQualityEnabled
        {
            get { return _measureUploadSourceQuality; }
            set {
                if (_measureUploadSourceQuality != value)
                {
                    _measureUploadSourceQuality = value;
                    OnChanged(EngineSettingType.UploadSourceQuality);
                }
            }
        }

        /// <summary>
        /// Gets or sets the maximum size (in bytes) of memory cache for http upload items (server usage only)
        /// Default 0 means no cache
        /// </summary>
        public long HttpMemoryCacheSize
        {
            get { return _httpMemoryCacheSize; }
            set
            {
                if (_httpMemoryCacheSize != value)
                {
                    _httpMemoryCacheSize = value;
                    OnChanged(EngineSettingType.HttpMemoryCacheSize);
                }
            }
        }

        /// <summary>
        /// Gets or sets the maximum number of segments could be in the waiting queue for http upload items (server usage only)
        /// Default 0
        /// </summary>
        public int HttpQueueLimit
        {
            get { return _httpQueueLimit; }
            set
            {
                if (_httpQueueLimit != value)
                {
                    _httpQueueLimit = value;
                    OnChanged(EngineSettingType.HttpQueueLimit);
                }
            }
        }

        /// <summary>
        /// Gets or sets the maximum number of simultaneous connectons to each server for http upload items (server usage only)
        /// Default 0
        /// </summary>
        public int HttpConnectionsPerServer
        {
            get { return _httpConnectionsPerServer; }
            set
            {
                if (_httpConnectionsPerServer != value)
                {
                    _httpConnectionsPerServer = value;
                    OnChanged(EngineSettingType.HttpConnectionsPerServer);
                }
            }
        }

        /// <summary>
        /// Indicates if the transfers use async operations or create a thread for each connection
        /// </summary>
        public bool AsyncTransfers
        {
            get { return _asyncTransfers; }
            set {
                if (_asyncTransfers != value)
                {
                    _asyncTransfers = value;
                    OnChanged(EngineSettingType.AsyncTransfers);
                }
            }
        }


        /// <summary>
        /// Gets default settings
        /// </summary>
        public static EngineSettings Default
        {
            get
            {
                return new EngineSettings
                           {
                               _pathDownload = string.IsNullOrEmpty(AppDomain.CurrentDomain.BaseDirectory) ? "Downloads" : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads"),
                               _pathFileLists = string.IsNullOrEmpty(AppDomain.CurrentDomain.BaseDirectory) ? "FileLists" : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FileLists"),
                               _tcpPort = 10853,
                               _udpPort = 6308,
                               _maxDownloadThreads = 20,
                               _maxUploadThreads = 0,
                               _reconnectTimeout = 10,
                               _maxFiles = 2,
                               _activeMode = true,
                               _instantAllocate = false,
                               _getUsersList = true,
                               _dumpHubMessages = false,
                               _searchAlternativesInterval = 5,
                               _tcpBackLog = 10,
                               _tcpReceiveBufferSize = 64 * 1024,
                               _fileReadBufferSize = 64 * 1024,
                               _netInterface = null,
                               _useSparse = false,
                               _backgroundSeedMode = true,
                               _measureUploadSourceQuality = false,
                               _httpQueueLimit = 0,
                               _httpConnectionsPerServer = 0,
                               _asyncTransfers = true
                           };
            }
        }
    }
}