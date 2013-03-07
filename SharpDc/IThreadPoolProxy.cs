using System.Threading;

namespace SharpDc
{
    public interface IThreadPoolProxy
    {
        void QueueWorkItem(ThreadStart workItem);
    }
}