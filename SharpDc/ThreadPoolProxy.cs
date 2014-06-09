// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2014
// licensed under the LGPL
// -------------------------------------------------------------

using System.Threading;
using SharpDc.Structs;

namespace SharpDc
{
    /// <summary>
    /// Provides default thread pool access
    /// </summary>
    public class ThreadPoolProxy : IThreadPoolProxy
    {
        SpeedAverage _threadCalls = new SpeedAverage();

        public void QueueWorkItem(ThreadStart workItem)
        {
            workItem.BeginInvoke(null, null);
            _threadCalls.Update(1);
        }

        public string GetStatus()
        {
            int minWt, minIo, maxWt, maxIo, avWt, avIo;

            ThreadPool.GetMinThreads(out minWt, out minIo);
            ThreadPool.GetMaxThreads(out maxWt, out maxIo);
            ThreadPool.GetAvailableThreads(out avWt, out avIo);

            return string.Format("Work {0}/{1}/{2} Io {3}/{4}/{5} (min, used, max) tps: {6}", minWt, maxWt - avWt, maxWt, minIo, maxIo - avIo, maxIo, _threadCalls.GetSpeed());
        }

        public int MaxThreads
        {
            get {
                int maxWt, maxIo;
                ThreadPool.GetMaxThreads(out maxWt, out maxIo);
                return maxWt;
            }
            set {
                int maxWt, maxIo;
                ThreadPool.GetMaxThreads(out maxWt, out maxIo);
                ThreadPool.SetMaxThreads(value, maxIo);
            }
        }

        public int ActiveThreads
        {
            get {
                int maxWt, maxIo;
                int avWt, avIo;
                ThreadPool.GetAvailableThreads(out avWt, out avIo);
                ThreadPool.GetMaxThreads(out maxWt, out maxIo);
                return maxWt - avWt;
            }
        }
    }
}