// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

namespace SharpDc
{
    public enum EngineSettingType
    {
        TcpPort,
        UdpPort,
        PathDownload,
        PathFileLists,
        MaxDownloadThreads,
        MaxUploadThreads,
        ReconnectTimeout,
        KeepAliveTimeout,
        ActiveMode,
        MaxFiles,
        VerifyFiles,
        InstantAllocate,
        GetUsersList,
        DumpHubProtocolMessages,
        DumpTransferProtocolMessages,
        LocalAddress,
        SearchAlternativesInterval,
        TcpBacklog,
        FileReadBufferSize,
        ConnectionsLimit,
        NetworkInterface,
        UseSparseFiles,
        AutoSelectPort,
        BackgroundSeedMode,
        UploadSourceQuality,
        HttpQueueLimit,
        HttpConnectionsPerServer
    }
}