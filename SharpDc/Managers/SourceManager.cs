//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using SharpDc.Structs;

namespace SharpDc.Managers
{
    /// <summary>
    /// Provides download source quality checks
    /// </summary>
    public class SourceManager
    {
        private readonly Dictionary<Source, SourceQuality> _sources = new Dictionary<Source, SourceQuality>();
        private readonly object _syncRoot = new object();

        public void Error(Source s)
        {
            lock (_syncRoot)
            {
                SourceQuality q;
                var add = !_sources.TryGetValue(s, out q);

                q.ErrorsCount++;

                if (add) _sources.Add(s, q);
                else _sources[s] = q;
            }
        }

        public void UpdateReceived(Source s)
        {
            lock (_syncRoot)
            {
                SourceQuality q;
                var add = !_sources.TryGetValue(s, out q);

                q.SegmentsReceived ++;

                if (add) _sources.Add(s, q);
                else _sources[s] = q;
            }
        }

        public void UpdateRequests(Source s, int requests)
        {
            lock (_syncRoot)
            {
                SourceQuality q;
                var add = !_sources.TryGetValue(s, out q);

                q.UnasnweredRequests += requests;

                if (add) _sources.Add(s, q);
                else _sources[s] = q;
            }
        }

        public List<Source> SortByQualityDesc(IEnumerable<Source> sources)
        {
            var pairs = new List<KeyValuePair<Source, SourceQuality>>();

            foreach (var source in sources)
            {
                SourceQuality q;
                _sources.TryGetValue(source, out q);
                pairs.Add(new KeyValuePair<Source, SourceQuality>(source, q));
            }

            pairs.Sort((one, two) => two.Value.Total.CompareTo(one.Value.Total));

            return pairs.Select(keyValuePair => keyValuePair.Key).ToList();
        }
    }


    public struct SourceQuality
    {
        public int ErrorsCount;
        public int SegmentsReceived;
        public int UnasnweredRequests;

        public int Total
        {
            get {
                return SegmentsReceived - ErrorsCount - UnasnweredRequests;
            }
        }
    }
}
