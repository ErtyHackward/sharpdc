//  -------------------------------------------------------------
//  LiveDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
//  licensed under the LGPL
//  -------------------------------------------------------------
namespace SharpDc.Structs
{
    public struct SegmentInfo
    {
        public int Index;
        public long StartPosition;
        public long Length;

        public override string ToString()
        {
            return string.Format("SegInfo[I:{0};S:{1};L:{2}]", Index, StartPosition, Length);
        }
    }
}