﻿// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace SharpDc.Structs
{
    /// <summary>
    /// General class for speed calculations
    /// Uses moving average method
    /// </summary>
    public class SpeedAverage
    {
        private readonly TimeSpan _period;
        private readonly TimeSpan _window;

        private long _accumulated;
        private readonly object _syncRoot = new object();
        private long _windowStartedAt;
        private readonly Queue<KeyValuePair<long, long>> _buffer = new Queue<KeyValuePair<long, long>>();

        private long _total;

        /// <summary>
        /// Gets moving average period of time
        /// </summary>
        public TimeSpan Period
        {
            get { return _period; }
        }

        /// <summary>
        /// Gets total amount of bytes processed
        /// </summary>
        public long Total
        {
            get { return _total; }
        }

        /// <summary>
        /// Creates speed calculator with 10 seconds period and 1 second buffer
        /// </summary>
        public SpeedAverage()
        {
            _period = TimeSpan.FromSeconds(10);
            _window = TimeSpan.FromSeconds(1);
        }

        /// <summary>
        /// Creates speed calculator with 1 second buffer
        /// </summary>
        /// <param name="period">Moving average period</param>
        public SpeedAverage(TimeSpan period)
        {
            _period = period;
            _window = TimeSpan.FromSeconds(1);
        }

        /// <summary>
        /// Creates speed calculator
        /// </summary>
        /// <param name="period">Moving average period interval</param>
        /// <param name="window">Buffer length to accumulate values</param>
        public SpeedAverage(TimeSpan period, TimeSpan window)
        {
            _period = period;
            _window = window;
        }

        private double GetSecondsPassedFrom(long timestamp)
        {
            return (double)(Stopwatch.GetTimestamp() - timestamp) / Stopwatch.Frequency;
        }

        private double GetSecondsDiff(long to, long from)
        {
            return (double)(to - from) / Stopwatch.Frequency;
        }

        public void Update(long bytesTransmitted)
        {
            lock (_syncRoot)
            {
                _total += bytesTransmitted;

                if (GetSecondsPassedFrom(_windowStartedAt) < _window.TotalSeconds)
                {
                    _accumulated += bytesTransmitted;
                    return;
                }

                if (_buffer.Count >= _period.TotalMilliseconds / _window.TotalMilliseconds + 1)
                {
                    RemoveOldValues();
                }

                // save prev window

                var pair = new KeyValuePair<long, long>(_windowStartedAt, _accumulated);
                _buffer.Enqueue(pair);

                // start a new window
                _accumulated = bytesTransmitted;
                _windowStartedAt = Stopwatch.GetTimestamp();
            }
        }

        public double GetSpeed()
        {
            lock (_syncRoot)
            {
                Update(0);
                RemoveOldValues();
                return (_buffer.Sum(pair => pair.Value) + _accumulated) / _period.TotalSeconds;
            }
        }

        public long GetSum()
        {
            lock (_syncRoot)
            {
                Update(0);
                RemoveOldValues();
                return _buffer.Sum(pair => pair.Value) + _accumulated;
            }
        }

        private void RemoveOldValues()
        {
            while (_buffer.Count > 0)
            {
                var pair = _buffer.Peek();

                if (GetSecondsPassedFrom(pair.Key) > _period.TotalSeconds + 1)
                    _buffer.Dequeue();
                else
                    break;
            }
        }
    }

    /// <summary>
    /// Allows to perform speed calculations in high-concurrent conditions (lock-free)
    /// Uses moving average method
    /// </summary>
    public class ConcurrentSpeedAverage
    {
        private readonly TimeSpan _period;
        private readonly TimeSpan _window;

        private long _accumulated;
        private long _windowStartedAt;
        private int _isUpdatingWindow;
        private readonly ConcurrentQueue<KeyValuePair<long, long>> _buffer = new ConcurrentQueue<KeyValuePair<long, long>>();

        private long _total;

        /// <summary>
        /// Gets moving average period of time
        /// </summary>
        public TimeSpan Period
        {
            get { return _period; }
        }

        /// <summary>
        /// Gets total amount of bytes processed
        /// </summary>
        public long Total
        {
            get { return _total; }
        }

        /// <summary>
        /// Creates speed calculator with 10 seconds period and 1 second buffer
        /// </summary>
        public ConcurrentSpeedAverage()
        {
            _period = TimeSpan.FromSeconds(10);
            _window = TimeSpan.FromSeconds(1);
        }

        /// <summary>
        /// Creates speed calculator with 1 second buffer
        /// </summary>
        /// <param name="period">Moving average period</param>
        public ConcurrentSpeedAverage(TimeSpan period)
        {
            _period = period;
            _window = TimeSpan.FromSeconds(1);
        }

        /// <summary>
        /// Creates speed calculator
        /// </summary>
        /// <param name="period">Moving average period interval</param>
        /// <param name="window">Buffer length to accumulate values</param>
        public ConcurrentSpeedAverage(TimeSpan period, TimeSpan window)
        {
            _period = period;
            _window = window;
        }

        private double GetSecondsPassedFrom(long timestamp)
        {
            return (double)(Stopwatch.GetTimestamp() - timestamp) / Stopwatch.Frequency;
        }

        private double GetSecondsDiff(long to, long from)
        {
            return (double)(to - from) / Stopwatch.Frequency;
        }

        public void Update(long bytesTransmitted)
        {
            Interlocked.Add(ref _total, bytesTransmitted);
            Interlocked.Add(ref _accumulated, bytesTransmitted);

            if (GetSecondsPassedFrom(_windowStartedAt) < _window.TotalSeconds)
                return;
            
            if (Interlocked.Exchange(ref _isUpdatingWindow, 1) == 0)
            {
                if (GetSecondsPassedFrom(_windowStartedAt) < _window.TotalSeconds)
                    return;

                var windowValue = Interlocked.Exchange(ref _accumulated, bytesTransmitted);

                if (_buffer.Count >= _period.TotalMilliseconds / _window.TotalMilliseconds + 1)
                {
                    RemoveOldValues();
                }

                // save prev window

                var pair = new KeyValuePair<long, long>(_windowStartedAt, windowValue - bytesTransmitted);
                _buffer.Enqueue(pair);

                // start a new window
                _windowStartedAt = Stopwatch.GetTimestamp();

                Interlocked.Exchange(ref _isUpdatingWindow, 0);
            }
        }

        public double GetSpeed()
        {
            Update(0);
            return (_buffer.Sum(pair => pair.Value) + _accumulated) / _period.TotalSeconds;
        }

        public long GetSum()
        {
            Update(0);
            return _buffer.Sum(pair => pair.Value) + _accumulated;
        }

        private void RemoveOldValues()
        {
            while (_buffer.Count > 0)
            {
                KeyValuePair<long, long> pair;
                if (!_buffer.TryPeek(out pair))
                    return;

                if (GetSecondsPassedFrom(pair.Key) > _period.TotalSeconds + 1)
                {
                    if(!_buffer.TryDequeue(out pair))
                        return;
                }
                else
                    break;
            }
        }
    }

}