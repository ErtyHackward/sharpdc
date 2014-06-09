// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2014
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SharpDc.Helpers;

namespace SharpDc.Structs
{
    public class CachedItem
    {
        public Magnet Magnet { get; private set; }
        
        public int SegmentLength { get; private set; }
        /// <summary>
        /// Indicates if the file is completely cached
        /// </summary>
        public bool Complete { get; set; }

        public BitArray CachedSegments { get; private set; }

        public string CachePath { get; set; }

        public CachedItem(Magnet magnet, int segmentLength)
        {
            Magnet = magnet;
            SegmentLength = segmentLength;
            CachedSegments = new BitArray(DownloadItem.SegmentsCount(magnet.Size, segmentLength));
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
    }

    /// <summary>
    /// Contains statistics data about item usage
    /// </summary>
    public struct StatItem
    {
        public DateTime LastUsage { get; set; }

        public Magnet Magnet { get; set; }

        /// <summary>
        /// Gets how many times the file was uploaded completely
        /// </summary>
        public double Rate
        {
            get { return (double)TotalUploaded / Magnet.Size; }
        }

        public long TotalUploaded { get; set; }
    }
}
