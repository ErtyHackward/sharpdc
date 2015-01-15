// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2014
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections;
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

        public DateTime Created { get; set; }
        
        public string BitFileldFilePath
        {
            get { return CachePath + ".bitfield"; }
        }

        public CachedItem(Magnet magnet, int segmentLength) : 
            this(magnet, segmentLength, new BitArray(DownloadItem.SegmentsCount(magnet.Size, segmentLength)))
        {

        }

        public CachedItem(Magnet magnet, int segmentLength, BitArray segments)
        {
            Magnet = magnet;
            SegmentLength = segmentLength;
            CachedSegments = segments;
            Created = DateTime.Now;
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
}
