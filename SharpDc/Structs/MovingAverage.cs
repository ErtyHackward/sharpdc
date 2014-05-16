// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2014
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SharpDc.Structs
{
    public class MovingAverage
    {
        private readonly TimeSpan _interval;
        private readonly Queue<KeyValuePair<long, int>> _queue = new Queue<KeyValuePair<long, int>>();
        private readonly object _syncRoot = new object();

        public MovingAverage(TimeSpan interval)
        {
            _interval = interval;
        }

        public void Update(int value)
        {
            lock (_syncRoot)
            {
                RemoveOldValues();
                _queue.Enqueue(new KeyValuePair<long, int>(Stopwatch.GetTimestamp(),value));
            }
        }

        public double GetAverage()
        {
            lock (_syncRoot)
            {
                RemoveOldValues();
                return _queue.Count == 0 ? 0 : _queue.Average(p => p.Value);
            }
        }

        private void RemoveOldValues()
        {
            while (true)
            {
                if (_queue.Count == 0 || (double)(Stopwatch.GetTimestamp() - _queue.Peek().Key) / Stopwatch.Frequency < _interval.TotalSeconds)
                    break;
                _queue.Dequeue();
            }
        }
    }
}