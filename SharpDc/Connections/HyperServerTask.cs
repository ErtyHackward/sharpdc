using System.Diagnostics;

namespace SharpDc.Connections
{
    public struct HyperServerTask
    {
        public int Token;
        public string Path;
        public long Offset;
        public int Length;
        public byte[] Buffer;
        public HyperServerSession Session;
        public long Created;

        public HyperServerTask(HyperRequestMessage msg)
        {
            Path = msg.Path;

            if (Path.StartsWith("\\"))
                Path = Path.Remove(0, 1);

            Offset = msg.Offset;
            Token = msg.Token;
            Length = msg.Length;
            Buffer = null;
            Session = null;
            Created = Stopwatch.GetTimestamp();
        }
    }
}