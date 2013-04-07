//  -------------------------------------------------------------
//  LiveDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
//  licensed under the LGPL
//  -------------------------------------------------------------
using System.Threading;

namespace SharpDc
{
    public interface IThreadPoolProxy
    {
        void QueueWorkItem(ThreadStart workItem);
    }
}