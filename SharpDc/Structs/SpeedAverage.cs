//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpDc.Structs
{
    /// <summary>
    /// General class for speed calculations
    /// </summary>
    public class SpeedAverage
    {
        private readonly int _period;
        private long _accumulated;
        private readonly object _syncRoot = new object();
        private DateTime _lastSecond;
        private readonly Queue<KeyValuePair<DateTime, long>> _buffer = new Queue<KeyValuePair<DateTime, long>>();

        private const int SizeLimit = 100;

        private long _total;

        /// <summary>
        /// Gets total amount of bytes processed
        /// </summary>
        public long Total
        {
            get { return _total; }
        }

        public SpeedAverage(int period)
        {
            _period = period;
        }

        public void Update(int bytesTransmitted)
        {
            lock (_syncRoot)
            {
                _total += bytesTransmitted;
                if ((DateTime.Now - _lastSecond).TotalSeconds < 1)
                {
                    _accumulated += bytesTransmitted;
                    return;
                }
            }

            if (_buffer.Count >= SizeLimit)
            {
                RemoveOldValues();
                if (_buffer.Count >= SizeLimit)
                {
                    return;
                }
            }

            var pair = new KeyValuePair<DateTime, long>(DateTime.Now, _accumulated + bytesTransmitted);
            _buffer.Enqueue(pair);
            _accumulated = 0;
            _lastSecond = DateTime.Now;
        }

        public double GetSpeed()
        {
            lock (_syncRoot)
            {
                RemoveOldValues();

                if (_buffer.Count == 0)
                    return 0;

                var result = _buffer.Sum(pair => pair.Value);

                result += _accumulated;

                var elapsed = (DateTime.Now - _buffer.Peek().Key).TotalSeconds;

                if (elapsed < 1) elapsed = 1;

                return result/elapsed;
            }
        }


        private void RemoveOldValues()
        {
            while (_buffer.Count > 0)
            {
                var pair = _buffer.Peek();

                if ((DateTime.Now - pair.Key).TotalSeconds > _period)
                    _buffer.Dequeue();
                else
                    break;
            }
        }

    }
}
