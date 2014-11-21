// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Xml.Serialization;
using SharpDc.Structs;

namespace SharpDc.Interfaces
{
    /// <summary>
    /// Base interface for DownloadItem storage object
    /// </summary>
    public abstract class IStorageContainer : IDisposable
    {
        [XmlIgnore]
        public DownloadItem DownloadItem { get; set; }

        /// <summary>
        /// Writes data to the storage 
        /// </summary>
        /// <param name="segment">segment information</param>
        /// <param name="offset">segment space offset</param>
        /// <param name="buffer">data buffer to write</param>
        /// <param name="bufferOffset">source buffer offset</param>
        /// <param name="length">amount of bytes to write</param>
        /// <returns></returns>
        public abstract bool WriteData(SegmentInfo segment, int offset, byte[] buffer, int bufferOffset, int length);

        /// <summary>
        /// Reads data from the saved segment
        /// Returns amount of bytes read
        /// </summary>
        /// <param name="segmentIndex"></param>
        /// <param name="segmentOffset">segment offset to read from</param>
        /// <param name="buffer"></param>
        /// <param name="bufferOffset">buffer write offset</param>
        /// <param name="count"></param>
        /// <returns></returns>
        public abstract int Read(int segmentIndex, int segmentOffset, byte[] buffer, int bufferOffset, int count);

        /// <summary>
        /// Gets how much new segments the container can accept
        /// </summary>
        public abstract int FreeSegments { get; }

        /// <summary>
        /// Tells if the segment is available for reading
        /// i.e. is completely downloaded
        /// </summary>
        /// <param name="segmentIndex"></param>
        /// <returns></returns>
        public abstract bool CanReadSegment(int segmentIndex);

        /// <summary>
        /// Indicates if this storage is available for read and write operations
        /// </summary>
        public abstract bool Available { get; }

        public abstract void Dispose();
    }
}