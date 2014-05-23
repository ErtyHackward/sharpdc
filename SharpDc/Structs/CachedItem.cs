// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2014
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SharpDc.Helpers;
using SharpDc.Logging;

namespace SharpDc.Structs
{
    public class CachedItem
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public Magnet Magnet { get; private set; }
        
        public int SegmentLength { get; private set; }
        
        /// <summary>
        /// Indicates if the file is completely cached
        /// </summary>
        public bool Complete { get; set; }

        public BitArray CachedSegments { get; private set; }
        
        /// <summary>
        /// Gets or sets system path of the cache file
        /// </summary>
        public string CachePath { get; set; }

        public Dictionary<long, long> SegmentMapping { get; set; }

        public long CacheUse
        {
            get { return SegmentMapping == null ? Magnet.Size : SegmentMapping.Count * SegmentLength; }
        }

        public long _writePos;

        /// <summary>
        /// Creates new instance of cache item
        /// </summary>
        /// <param name="magnet"></param>
        /// <param name="segmentLength"></param>
        /// <param name="realOrder">Will we keep the segments in file in real order or use segment mapping. Second option allow to use disk space only for taken segments</param>
        public CachedItem(Magnet magnet, int segmentLength, bool realOrder = false)
        {
            Magnet = magnet;
            SegmentLength = segmentLength;
            CachedSegments = new BitArray(DownloadItem.SegmentsCount(magnet.Size, segmentLength));
            if (!realOrder)
                SegmentMapping = new Dictionary<long, long>();
        }

        /// <summary>
        /// Tells if the area is in cache
        /// </summary>
        /// <param name="start">start file position in bytes</param>
        /// <param name="length">area length in bytes</param>
        /// <returns></returns>
        public bool IsAreaCached(long start, long length)
        {
            if (Complete)
                return true;

            if (length < 0)
                throw new ArgumentException("length could not be less than zero");

            if (start < 0)
                throw new ArgumentException("start could not be less than zero");

            if (start + length > Magnet.Size)
            {
                throw new ArgumentException("The area is out of file space");
            }

            var ind   = DownloadItem.GetSegmentIndex(start, SegmentLength);
            var total = DownloadItem.SegmentsCount(length, SegmentLength);

            return CachedSegments.FirstFalse(ind, total) == -1;
        }

        public int Read(byte[] buffer, long position, int offset, int length)
        {
            if (length > SegmentLength)
                return 0;

            using (var fs = new FileStream(CachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 128))
            {
                if (SegmentMapping == null)
                {
                    fs.Position = position;
                    return fs.Read(buffer, offset, length);
                }
                else
                {
                    long filePos;
                    if (!SegmentMapping.TryGetValue(position, out filePos))
                        return 0;
                    fs.Position = filePos;
                    return fs.Read(buffer, offset, length);
                }
            }
        }

        public bool WriteSegment(byte[] buffer, long position, int length)
        {
            if (length != SegmentLength)
                throw new ArgumentException("Length of the segment should be equal to the cache segment");

            try
            {
                using (new PerfLimit("Cache write"))
                using (var fs = new FileStream(CachePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite, 1024 * 64))
                {
                    if (SegmentMapping == null)
                    {
                        fs.Position = position;
                        using (var ms = new MemoryStream(buffer, 0, length, false))
                        {
                            ms.CopyTo(fs);
                        }
                    }
                    else
                    {
                        long curPos;

                        lock (SegmentMapping)
                        {
                            if (SegmentMapping.ContainsKey(position))
                                return false;

                            curPos = _writePos;
                            _writePos += SegmentLength;
                            SegmentMapping.Add(position, curPos);
                        }

                        fs.Position = curPos;
                        using (var ms = new MemoryStream(buffer, 0, length, false))
                        {
                            ms.CopyTo(fs);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error("Unable to write cache segment {0}", e.Message);
                return false;
            }


            return true;
        }
    }

    /// <summary>
    /// Contains statistics data about item usage
    /// </summary>
    public struct StatItem
    {
        public DateTime LastUsage { get; set; }

        public Magnet Magnet { get; set; }
        public bool Expired {
            get { return (DateTime.Now - LastUsage).TotalHours > 3; }
        }

        /// <summary>
        /// Gets how many times the file was uploaded completely
        /// </summary>
        public double Rate;
    }
}
