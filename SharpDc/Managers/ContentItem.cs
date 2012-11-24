//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using SharpDc.Structs;

namespace SharpDc.Managers
{
    /// <summary>
    /// Represents a single file in a share
    /// </summary>
    public struct ContentItem
    {

        public Magnet Magnet { get; set; }

        /// <summary>
        /// File system location
        /// </summary>
        public string SystemPath { get; set; }

        /// <summary>
        /// Share virtual path
        /// </summary>
        public string VirtualPath { get; set; }
    }
}