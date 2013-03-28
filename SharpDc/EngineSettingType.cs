//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
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
        TcpReceiveBufferSize,
        FileReadBufferSize,
        ConnectionsLimit,
        NetworkInterface
    }
}