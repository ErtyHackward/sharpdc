using System;
using System.Net.Sockets;
using SharpDc.Logging;

namespace SharpDc.Connections
{
    public class HyperServerConnection : HyperConnection
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        
        public event EventHandler Handshake;

        protected virtual void OnHandshake()
        {
            var handler = Handshake;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public event EventHandler<HyperSegmentRequestEventArgs> SegmentRequested;

        protected virtual void OnSegmentRequested(HyperSegmentRequestEventArgs e)
        {
            var handler = SegmentRequested;
            if (handler != null) handler(this, e);
        }
        
        public HyperServerConnection(Socket socket)
            : base(socket)
        {

        }

        protected override void PrepareSocket(Socket socket)
        {
            base.PrepareSocket(socket);
            
            socket.ReceiveBufferSize = 256 * 1024;
            socket.SendBufferSize = 2 * 1024 * 1024;
        }

        protected override void OnMessageHandshake(HyperHandshakeMessge message)
        {
            SessionToken = message.SessionToken;
            IsControl = message.IsControl;
            OnHandshake();
        }

        protected override void OnMessageSegmentRequest(HyperRequestMessage message)
        {
            OnSegmentRequested(new HyperSegmentRequestEventArgs { Task = new HyperServerTask(message)});
        }
    }

    public enum HyperMessage : byte
    {
        None,
        Handshake,
        FileCheckResult,
        Request,
        SegmentData
    }

    public struct HyperHandshakeMessge
    {
        public long SessionToken;
        public bool IsControl;
    }

    public struct HyperRequestMessage
    {
        public int Token;
        public long Offset;
        public int Length;
        public string Path;

        /// <summary>
        /// Indicates if this is a request to check if file exists
        /// </summary>
        public bool IsFileCheck {
            get { return Length == -1; }
            set { Length = value ? -1 : 0; }
        }
    }

    public struct HyperFileResultMessage
    {
        public int Token;
        public long Size;
    }

    public struct HyperSegmentDataMessage
    {
        public int Token;
        public byte[] Buffer;
    }
}