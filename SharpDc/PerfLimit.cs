// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
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

    /// <summary>
    /// Allows to measure performance time of a section
    /// </summary>
    public class PerfCounter
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly string _name;
        private readonly long _sw;
        private readonly List<KeyValuePair<string, long>> _tasks = new List<KeyValuePair<string, long>>();

        private long _last = 0;

        public PerfCounter(string name)
        {
            _name = name;
            _sw = Stopwatch.GetTimestamp();
            _last = _sw;
        }

        public void Stage(string name)
        {
            _tasks.Add(new KeyValuePair<string, long>(name, Stopwatch.GetTimestamp() - _last));
            _last = Stopwatch.GetTimestamp();
        }

        public void LastStage(string name)
        {
            Stage(name);

            var sb = new StringBuilder();

            sb.AppendFormat(" [C] {0}; {1}", ToMs(_last - _sw),
                string.Join(", ", _tasks.Select(p => string.Format("{0} +{1}ms", p.Key, ToMs(p.Value)))));

            Logger.Info(sb.ToString());
        }

        private long ToMs(long ts)
        {
            return (long)TimeSpan.FromSeconds((double)(ts) / Stopwatch.Frequency).TotalMilliseconds;
        }
    }
}