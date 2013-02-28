//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.IO;
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
        private string _pathIncomplete;
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

        /// <summary>
        /// Occurs when some setting is changed
        /// </summary>
        public event EventHandler<EngineSettingsEventArgs> Changed;
        private int _searchAlternativesInterval;

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
        /// Seconds between reconnect attempts, 0 - don't reconnect
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
        /// Gets or sets value indicating if the hubs should request a user list
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
        /// Gets default settings
        /// </summary>
        public static EngineSettings Default
        {
            get
            {
                return new EngineSettings
                           {
                               _pathDownload = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads"),
                               _pathFileLists = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FileLists"),
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
                               _fileReadBufferSize = 64 * 1024
                           };
            }
        }


    }
}