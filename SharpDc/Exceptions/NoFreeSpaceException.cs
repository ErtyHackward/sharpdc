// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;

namespace SharpDc.Exceptions
{
    public class NoFreeSpaceException : Exception
    {
        public string DriveName { get; set; }

        public NoFreeSpaceException(string driveName)
            : base(string.Format("Unable to add download because no free space on {0} drive.", driveName))
        {
            DriveName = driveName;
        }
    }
}