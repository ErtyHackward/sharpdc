using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SharpDc.Events;
using SharpDc.Logging;
using SharpDc.Managers;
using SharpDc.Structs;

namespace SharpDc.Connections
{
    /// <summary>
    /// Provides high throughput network support (10Gbit+)
    /// 
    /// Two types of connection are:
    /// 1. Control - used to transfer requests
    /// 2. Transfer - user to transfer data
    /// 
    /// There are could be more than one connections of each type
    /// But must be at least one of each type
    /// </summary>
    public abstract class HyperConnection : TcpConnection
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private byte[] _tail;
        
        private ReusableObject<byte[]> _tempBuffer;
        private int _tempBufferOffset;
        private int _tempToken;
        private int _tempMessageLength;
        
        private int _flushingRequestQueue;
        private int _flushingResponseQueue;

        private IHyperRequestsProvider _requests;
        private IHyperResponseProvider _responses;

        public void SetRequestRovider(IHyperRequestsProvider hyperRequestsProvider)
        {
            _requests = hyperRequestsProvider;
        }

        public void SetResponsesRovider(IHyperResponseProvider hyperResponseProvider)
        {
            _responses = hyperResponseProvider;
        }

        /// <summary>
        /// Indicates if the connections is a control connection
        /// </summary>
        public bool IsControl { get; set; }

        public long SessionToken { get; set; }

        public MovingAverage SegemntSendDelay { get; private set; }

        public MovingAverage ParseRawTime { get; private set; }

        public MovingAverage ParseRawBufferLength { get; private set; }


        private void Initialize()
        {
            SegemntSendDelay = new MovingAverage(TimeSpan.FromSeconds(10));
            ParseRawTime = new MovingAverage(TimeSpan.FromSeconds(10));
            ParseRawBufferLength = new MovingAverage(TimeSpan.FromSeconds(10));
            ConnectionStatusChanged += HyperConnection_ConnectionStatusChanged;   
        }

        protected HyperConnection()
        {
            Initialize();
        }

        protected HyperConnection(Socket socket) : base(socket)
        {
            Initialize();
        }

        void HyperConnection_ConnectionStatusChanged(object sender, ConnectionStatusEventArgs e)
        {
            if (e.Status == ConnectionStatus.Disconnected)
            {
                _tail = null;
                if (_tempBuffer.Object != null)
                {
                    _tempBuffer.Dispose();
                    _tempBuffer = new ReusableObject<byte[]>();
                }
            }
        }

        protected virtual ReusableObject<byte[]> GetBuffer()
        {
            return new ReusableObject<byte[]>();
        }

        private bool CheckSegmentReceiveReady()
        {
            if (_tempBufferOffset == _tempBuffer.Object.Length)
            {
                HyperSegmentDataMessage msg;
                msg.Token = _tempToken;
                msg.Buffer = _tempBuffer;

                OnMessageSegmentData(msg);

                _tempBuffer = new ReusableObject<byte[]>();
                _tempBufferOffset = 0;
                _tempToken = 0;

                return true;
            }

            return false;
        }

        protected override void ParseRaw(byte[] buffer, int offset, int length)
        {
            ParseRawBufferLength.Update(length);

            var sw = Stopwatch.StartNew();

            using (new PerfLimit("Parse raw", 1000))
            {
                ParseRawInternal(buffer, offset, length);
            }
            
            ParseRawTime.Update((int)sw.ElapsedMilliseconds);
        }

        private void ParseRawInternal(byte[] buffer, int offset, int length)
        {
            if (_tempBuffer.Object != null)
            {
                // continue to receive 

                var bytesToCopy = Math.Min(_tempMessageLength - _tempBufferOffset - 5, length);
                Buffer.BlockCopy(buffer, offset, _tempBuffer.Object, _tempBufferOffset, bytesToCopy);
                _tempBufferOffset += bytesToCopy;

                if (!CheckSegmentReceiveReady())
                    return;

                // parse following messages...
                offset += bytesToCopy;
                length -= bytesToCopy;

                if (length == 0)
                    return;
            }

            if (_tail != null)
            {
                var newBuffer = new byte[_tail.Length + length];
                Buffer.BlockCopy(_tail, 0, newBuffer, 0, _tail.Length);
                Buffer.BlockCopy(buffer, offset, newBuffer, _tail.Length, length);
                buffer = newBuffer;
                offset = 0;
                length += _tail.Length;
                _tail = null;
            }

            var memoryStream = new MemoryStream(buffer, offset, length, false);
            var reader = new BinaryReader(memoryStream);

            while (memoryStream.Position + 5 < memoryStream.Length) // 4 (message length) + 1 (messageId) = 5
            {
                var backupPosition = (int)memoryStream.Position;
                var msgLen = reader.ReadInt32();
                var msgId = (HyperMessage)reader.ReadByte();

                // we no need to use tailing for segment receiving
                if (msgId == HyperMessage.SegmentData)
                {
                    if (memoryStream.Length - memoryStream.Position < 4)
                    {
                        memoryStream.Position = backupPosition;
                        break;
                    }

                    _tempMessageLength = msgLen;
                    _tempBuffer = GetBuffer();
                    _tempBufferOffset = 0;
                    _tempToken = reader.ReadInt32();

                    var bytesToCopy = Math.Min(msgLen - 5, length - (int)memoryStream.Position);
                    Buffer.BlockCopy(buffer, offset + (int)memoryStream.Position, _tempBuffer.Object, _tempBufferOffset, bytesToCopy);
                    _tempBufferOffset += bytesToCopy;

                    if (!CheckSegmentReceiveReady())
                        return;

                    // following code actually should never happen to execute (receive whole segment in one message)
                    // but just to keep it complete we will support that behaviour

                    Debugger.Break();
                    memoryStream.Seek(bytesToCopy, SeekOrigin.Current);
                    continue;
                }

                var bytesLeftInMemory = memoryStream.Length - memoryStream.Position;
                var bytesNeeded = msgLen - 1; // 1 because we just read the msgId

                if (bytesLeftInMemory < bytesNeeded)
                {
                    if (msgLen > 4 * 1024)
                    {
                        Logger.Warn("Data corruption detected");
                        return;
                    }

                    //Logger.Warn("Backuping tail bytes: {2} pos: {0} len: {1} cutMsgLen={3} cutMsgType={4}", backupPosition, memoryStream.Length, memoryStream.Length - backupPosition, msgLen, msgId);
                    // we can't read whole message, backup it for the next operation
                    _tail = new byte[memoryStream.Length - backupPosition];
                    Buffer.BlockCopy(buffer, offset + backupPosition, _tail, 0, _tail.Length);
                    return;
                }

                switch (msgId)
                {
                    case HyperMessage.Handshake:
                        {
                            HyperHandshakeMessge msg;
                            msg.SessionToken = reader.ReadInt64();
                            msg.IsControl = reader.ReadBoolean();
                            OnMessageHandshake(msg);
                        }
                        break;
                    case HyperMessage.FileCheckResult:
                        {
                            HyperFileResultMessage msg;
                            msg.Token = reader.ReadInt32();
                            msg.Size = reader.ReadInt64();
                            OnMessageFileResult(msg);
                        }
                        break;
                    case HyperMessage.Request:
                        {
                            HyperRequestMessage msg;
                            msg.Token = reader.ReadInt32();
                            msg.Path = reader.ReadString();
                            msg.Offset = reader.ReadInt64();
                            msg.Length = reader.ReadInt32();
                            OnMessageSegmentRequest(msg);
                        }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            if (memoryStream.Position < memoryStream.Length)
            {
                //Logger.Warn("Backuping2 tail bytes: {2} pos: {0} len: {1}", memoryStream.Position, memoryStream.Length, memoryStream.Length - memoryStream.Position);
                // we can't even read the length, backup...
                _tail = new byte[memoryStream.Length - memoryStream.Position];
                Buffer.BlockCopy(buffer, offset + (int)memoryStream.Position, _tail, 0, _tail.Length);
            }
        }

        public async void FlushResponseQueueAsync()
        {
            if (Interlocked.Exchange(ref _flushingResponseQueue, 1) == 0)
            {
                try
                {
                    await FlushResponseQueue().ConfigureAwait(false);
                }
                catch (Exception x)
                {
                    Logger.Error("Exception when flushing response queue {0}", x.Message);
                }
                finally
                {
                    Interlocked.Exchange(ref _flushingResponseQueue, 0);
                }
            }
        }

        public async void FlushRequestQueueAsync()
        {
            if (Interlocked.Exchange(ref _flushingRequestQueue, 1) == 0)
            {
                try
                {
                    await FlushRequestQueue().ConfigureAwait(false);
                }
                catch (Exception x)
                {
                    Logger.Error("Exception when flushing request queue {0}", x.Message);
                }
                finally
                {
                    Interlocked.Exchange(ref _flushingRequestQueue, 0);
                }
            }
        }

        private async Task FlushRequestQueue()
        {
            do
            {
                var ms = new MemoryStream();
                var writer = new BinaryWriter(ms);
                HyperRequestMessage request;

                while (_requests.TryGetRequest(out request))
                {
                    writer.Write(0); // will replace that field to actual length

                    var start = (int)writer.BaseStream.Position;

                    writer.Write((byte)HyperMessage.Request);
                    writer.Write(request.Token);
                    writer.Write(request.Path);
                    writer.Write(request.Offset);
                    writer.Write(request.Length);

                    var length = (int)writer.BaseStream.Position - start;

                    ms.Seek(-length - 4, SeekOrigin.Current);
                    writer.Write(length);
                    ms.Seek(length, SeekOrigin.Current);
                }

                var bytes = ms.ToArray();
                await SendAsync(bytes, 0, bytes.Length).ConfigureAwait(false);

            } while (_requests.HasRequests());
        }

        private async Task FlushResponseQueue()
        {
            var ms = new MemoryStream();
            var writer = new BinaryWriter(ms);

            while (true)
            {
                HyperFileResultMessage fileCheckResult;
                var hasFileResult = _responses.TryGetFileCheckResponse(out fileCheckResult);
                if (hasFileResult)
                {
                    ms.Position = 0;
                    ms.SetLength(17);

                    writer.Write(13);
                    writer.Write((byte)HyperMessage.FileCheckResult);
                    writer.Write(fileCheckResult.Token);
                    writer.Write(fileCheckResult.Size);

                    var bytes = ms.ToArray();
                    await SendAsync(bytes, 0, bytes.Length).ConfigureAwait(false);

                    continue;
                }

                HyperSegmentDataMessage response;
                var hasSegmentResult = _responses.TryGetSegmentResponse(out response);
                if (hasSegmentResult)
                {
                    ms.Position = 0;
                    ms.SetLength(9);

                    writer.Write(5 + response.Buffer.Object.Length);
                    writer.Write((byte)HyperMessage.SegmentData);
                    writer.Write(response.Token);
                    
                    try
                    {
                        var bytes = ms.ToArray();

                        var startSend = Stopwatch.GetTimestamp();

                        await SendAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                        await SendAsync(response.Buffer.Object, 0, response.Buffer.Object.Length).ConfigureAwait(false);
                        
                        SegemntSendDelay.Update((int)((Stopwatch.GetTimestamp() - startSend) / (Stopwatch.Frequency / 1000)));
                    }
                    finally
                    {
                        response.Buffer.Dispose();
                    }
                }

                if (!hasSegmentResult)
                    break;
            }
        }

        protected virtual void OnMessageHandshake(HyperHandshakeMessge message)
        {

        }

        protected virtual void OnMessageSegmentRequest(HyperRequestMessage message)
        {

        }

        protected virtual void OnMessageFileResult(HyperFileResultMessage message)
        {

        }

        protected virtual void OnMessageSegmentData(HyperSegmentDataMessage message)
        {

        }
    }

    public interface IHyperResponseProvider
    {
        bool TryGetSegmentResponse(out HyperSegmentDataMessage response);
        bool TryGetFileCheckResponse(out HyperFileResultMessage response);
    }

    public interface IHyperRequestsProvider
    {
        bool TryGetRequest(out HyperRequestMessage request);

        bool HasRequests();
    }

    public struct HyperUrl
    {
        public string Server;
        public string Request;
        public int Port;

        public HyperUrl(string url)
        {
            // hyp://127.0.0.1:9100/1/test.avi

            if (!url.StartsWith("hyp://"))
                throw new ArgumentException();

            Server = url.Remove(0, 6);

            var ind = Server.IndexOf('/');

            if (ind == -1)
            {
                Request = "/";
            }
            else
            {
                Request = Server.Substring(ind);
                Server = Server.Remove(ind);
            }

            ind = Server.IndexOf(':');

            if (ind == -1)
            {
                Port = 9100;
            }
            else
            {
                Port = int.Parse(Server.Substring(ind + 1));
                Server = Server.Remove(ind);
            }
        }
    }
}