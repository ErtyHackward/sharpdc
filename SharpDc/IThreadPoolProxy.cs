// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2014
// licensed under the LGPL
// -------------------------------------------------------------

using System.Threading;

namespace SharpDc
{
    public interface IThreadPoolProxy
    {
        void QueueWorkItem(ThreadStart workItem);

        string GetStatus();

        int MaxThreads { get; set; }

        int ActiveThreads { get; }
    }
}