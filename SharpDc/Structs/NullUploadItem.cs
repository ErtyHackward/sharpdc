//  -------------------------------------------------------------
//  LiveDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
//  licensed under the LGPL
//  -------------------------------------------------------------
using SharpDc.Managers;

namespace SharpDc.Structs
{
    /// <summary>
    /// Don't read anything and always return success status on read operation
    /// Use for various (performance) testing
    /// </summary>
    public class NullUploadItem : UploadItem
    {
        public NullUploadItem(ContentItem item) : base (item)
        {
            
        }

        protected override int InternalRead(byte[] array, long start, int count)
        {
            return count;
        }
    }
}