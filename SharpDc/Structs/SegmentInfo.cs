// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

namespace SharpDc.Structs
{
    public struct SegmentInfo
    {

        public int Index;

        /// <summary>
        /// File position where the segment starts
        /// </summary>
        public long StartPosition;

        /// <summary>
        /// Length of the segment
        /// </summary>
        public long Length;

        /// <summary>
        /// Internal segment position
        /// </summary>
        public int Position;

        public override string ToString()
        {
            return string.Format("SegInfo[I:{0};S:{1};P:{3};L:{2}]", Index, StartPosition, Length, Position);
        }
    }
}