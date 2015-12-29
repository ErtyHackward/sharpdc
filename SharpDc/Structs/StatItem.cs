using System;

namespace SharpDc.Structs
{
    /// <summary>
    /// Contains statistics data about item usage
    /// </summary>
    [Serializable]
    public struct StatItem
    {
        public DateTime LastUsage { get; set; }

        public Magnet Magnet { get; set; }

        /// <summary>
        /// Total bytes uploaded from this file
        /// </summary>
        public long TotalUploaded { get; set; }

        /// <summary>
        /// Gets how many times the file was uploaded completely
        /// </summary>
        public double Rate => (double)TotalUploaded / Magnet.Size;

        /// <summary>
        /// Returns the cache effectivity calculated as (in Gb) TotalUploaded ^ 2 / FileSize
        /// </summary>
        public double CacheEffectivity
        {
            get
            {
                var uploadedGb = (double)TotalUploaded / Utils.GiB;
                var sizeGb = (double)Magnet.Size / Utils.GiB;

                return uploadedGb * uploadedGb / sizeGb; 
            }
        }

        public static StatItem operator +(StatItem one, StatItem two)
        {
            if (one.Magnet.TTH != two.Magnet.TTH)
                throw new InvalidOperationException("Can't sum StatItems with different magnets");

            var res = new StatItem
            {
                Magnet = one.Magnet,
                LastUsage = one.LastUsage > two.LastUsage ? two.LastUsage : one.LastUsage, // takign the newest time
                TotalUploaded = one.TotalUploaded + two.TotalUploaded
            };
            
            return res;
        }
    }
}