using System;
using System.IO;
using System.Net.Sockets;
using SharpDc.Helpers;
using SharpDc.Logging;

namespace SharpDc.Connections
{
    public class HyperClientConnection : HyperConnection
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        
        public event EventHandler<HyperSegmentEventArgs> SegmentReceived;

        protected virtual void OnSegmentReceived(HyperSegmentEventArgs e)
        {
            var handler = SegmentReceived;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<HyperFileCheckEventArgs> FileFound;

        protected virtual void OnFileFound(HyperFileCheckEventArgs e)
        {
            var handler = FileFound;
            if (handler != null) handler(this, e);
        }

        public HyperClientConnection(HyperUrl url, bool isControl, long sessionToken)
        {
            RemoteEndPoint = ParseAddress(url.Server, url.Port);
            IsControl = isControl;
            SessionToken = sessionToken;
        }

        protected override byte[] GetBuffer()
        {
            return HyperDownloadManager.SegmentsPool.GetObject();
        }

        protected override void OnMessageSegmentData(HyperSegmentDataMessage message)
        {
            OnSegmentReceived(new HyperSegmentEventArgs
            {
                Buffer = message.Buffer,
                Token = message.Token
            });
        }

        protected override void OnMessageFileResult(HyperFileResultMessage message)
        {
            OnFileFound(new HyperFileCheckEventArgs
            {
                Token = message.Token,
                FileSize = message.Size
            });
        }

        protected override void PrepareSocket(Socket socket)
        {
            base.PrepareSocket(socket);

            if (IsControl)
            {
                socket.SendBufferSize = 128 * 1024;
                socket.ReceiveBufferSize = 128 * 1024;
            }
            else
            {
                socket.SendBufferSize = 128 * 1024;
                socket.ReceiveBufferSize = 1024 * 1024;
            }
        }

        protected override void SendFirstMessages()
        {
            var memoryStream = new MemoryStream();
            var writer = new BinaryWriter(memoryStream);

            writer.Write(10); // message length
            writer.Write((byte)HyperMessage.Handshake);
            writer.Write(SessionToken);
            writer.Write(IsControl);

            var buffer = memoryStream.ToArray();

            SendAsync(buffer, 0, buffer.Length).NoWarning();

            base.SendFirstMessages();
        }
    }
}
