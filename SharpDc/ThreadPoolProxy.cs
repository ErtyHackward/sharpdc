﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace SharpDc
{
    /// <summary>
    /// Provides default thread pool access
    /// </summary>
    public class ThreadPoolProxy : IThreadPoolProxy
    {
        public void QueueWorkItem(ThreadStart workItem)
        {
            workItem.BeginInvoke(null, null);
        }
    }
}
