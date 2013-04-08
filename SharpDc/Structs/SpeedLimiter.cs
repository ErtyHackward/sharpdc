// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Threading;

namespace SharpDc.Structs
{
    /// <summary>
    /// Allows to limit speed of stream operations
    /// </summary>
    public class SpeedLimiter
    {
        private readonly object _synRoot = new object();
        private DateTime _timeMarker;
        private int _bytesProcessed;
        private int _speedLimit;

        /// <summary>
        /// Gets or sets speed limit in bytes per second, 0 = disabled
        /// </summary>
        public int SpeedLimit
        {
            get { return _speedLimit; }
            set
            {
                _speedLimit = value;
                _bytesProcessed = 0;
            }
        }

        /// <summary>
        /// Calculates amount of bytes passed, wait required amount of time if needed
        /// </summary>
        /// <param name="bytesCount"></param>
        public void Update(int bytesCount)
        {
            lock (_synRoot)
            {
                _bytesProcessed += bytesCount;

                while (SpeedLimit > 0 && _bytesProcessed >= SpeedLimit)
                {
                    if (_timeMarker.AddSeconds(1) < DateTime.Now)
                    {
                        _timeMarker = DateTime.Now;
                        _bytesProcessed = 0;
                    }
                    var waitFactor = (float)_bytesProcessed / SpeedLimit;
                    if (waitFactor > 1)
                    {
                        var sleep = (_timeMarker.AddSeconds(waitFactor) - DateTime.Now).Milliseconds;
                        Thread.Sleep(sleep);
                    }
                }
            }
        }
    }
}