// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Diagnostics;
using SharpDc.Logging;

namespace SharpDc
{
    /// <summary>
    /// Allows to measure performance time of a section
    /// </summary>
    public class PerfLimit : IDisposable
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly string _message;
        private readonly int _limitMs;
        private readonly Stopwatch _sw;

        public PerfLimit(string message, int limitMs = 100)
        {
            _message = message;
            _limitMs = limitMs;
            _sw = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _sw.Stop();
            if (_sw.ElapsedMilliseconds > _limitMs)
            {
                Logger.Warn("{0} : {1}/{2} ms", _message, _sw.ElapsedMilliseconds, _limitMs);
            }
        }
    }
}