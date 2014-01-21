using System;

namespace SharpDc.Structs
{
    public class UploadSourceQuality
    {
        /// <summary>
        /// Source id (location)
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Total requests in the period
        /// </summary>
        public SpeedAverage Requests { get; set; }

        /// <summary>
        /// Errors count in the period
        /// </summary>
        public SpeedAverage Errors { get; set; }

        /// <summary>
        /// Gets errors percent in the period [0; 1]
        /// </summary>
        public double ErrorsPercent
        {
            get
            {
                var reqs = Requests.GetSpeed();
                if (reqs == 0)
                    return 0;
                return Errors.GetSpeed() / reqs;
            }
        }

        public UploadSourceQuality(string id, TimeSpan period, TimeSpan window)
        {
            Id = id;
            Requests = new SpeedAverage(period, window);
            Errors = new SpeedAverage(period, window);
        }
    }
}