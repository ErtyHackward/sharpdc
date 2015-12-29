using System.Diagnostics;
using SharpDc.Managers;

namespace SharpDc.Connections
{
    public struct HyperServerTask
    {
        public int Token;
        public string Path;
        public long Offset;
        /// <summary>
        /// Length of bytes to request, -1 means file length request (check of existance)
        /// </summary>
        public int Length;
        public ReusableObject<byte[]> Buffer;
        public HyperServerSession Session;
        public long Created;
        public string TTH;
        public long FileLength;

        public bool IsFileCheck => Length == -1;
        public bool IsSegmentRequest => Length > 0;

        public void Done()
        {
            Session.EnqueueSend(this);
        }

        public HyperServerTask(HyperRequestMessage msg)
        {
            Path = msg.Path;

            if (Path != null && Path.StartsWith("\\"))
                Path = Path.Remove(0, 1);

            Offset = msg.Offset;
            Token = msg.Token;
            Length = msg.Length;
            Buffer = new ReusableObject<byte[]>();
            Session = null;
            Created = Stopwatch.GetTimestamp();
            TTH = null;
            FileLength = 0;
        }
    }
}