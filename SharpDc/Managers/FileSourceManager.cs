//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using SharpDc.Structs;

namespace SharpDc.Managers
{
    /// <summary>
    /// Allows to compare file sources for upload
    /// In case when the CententItem have more than one SystemPaths
    /// </summary>
    public class FileSourceManager
    {
        private readonly List<KeyValuePair<string, SpeedAverage>> _fileSources = new List<KeyValuePair<string, SpeedAverage>>();
        private readonly object _syncRoot = new object();

        private readonly Random _random = new Random();

        public TimeSpan Period { get; set; }

        public TimeSpan Window { get; set; }
        
        public FileSourceManager()
        {
            Period = TimeSpan.FromMinutes(5);
            Window = TimeSpan.FromSeconds(10);
        }
        
        public void RegisterSource(string fileSource)
        {
            lock (_syncRoot)
            {
                if (_fileSources.FindIndex(p => p.Key.StartsWith(fileSource)) != -1)
                    throw new InvalidOperationException("Alredy have this source or similar");
                
                _fileSources.Add(new KeyValuePair<string, SpeedAverage>(fileSource, new SpeedAverage(Period, Window)));
            }
        }

        public void RegisterError(string filePath)
        {
            lock (_syncRoot)
            {
                var index = _fileSources.FindIndex(p => filePath.StartsWith(p.Key));

                if (index != -1)
                {
                    _fileSources[index].Value.Update(1);
                }
            }
        }

        public string GetBestSource(string[] fileSources)
        {
            if (fileSources.Length == 1)
                return fileSources[0];

            lock (_syncRoot)
            {
                var list = fileSources.Select(s =>
                                                  {
                                                      var ind = _fileSources.FindIndex(p => s.StartsWith(p.Key));
                                                      var q = ind == -1 ? 1d : _fileSources[ind].Value.GetSpeed();
                                                      return new KeyValuePair<string, double>(s, q);
                                                  }).ToList();

                list.Sort((s1, s2) => s1.Value.CompareTo(s2.Value));

                // find equal soruces if any
                int i;
                var quality = list[0].Value;

                for (i = 1; i < list.Count; i++)
                {
                    if (list[i].Value > quality)
                    {
                        break;
                    }
                }

                if (i > 1)
                {
                    return list[_random.Next(0, i)].Key;
                }

                return list[0].Key;
            }
        }

        public IEnumerable<KeyValuePair<string, SpeedAverage>> Sources()
        {
            lock (_syncRoot)
            {
                foreach (var keyValuePair in _fileSources)
                {
                    yield return keyValuePair;
                }
            }
        }
    }
}
