// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SharpDc.Helpers
{
    public static class Windows
    {
        private const int FSCTL_SET_SPARSE = 590020; // &H000900C4

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
        
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode,
                                                   IntPtr lpInBuffer, uint nInBufferSize,
                                                   IntPtr lpOutBuffer, uint nOutBufferSize,
                                                   out uint lpBytesReturned, IntPtr lpOverlapped);

        public static bool SetCurrentThreadIdlePriority()
        {
            try
            {
                foreach (ProcessThread th in Process.GetCurrentProcess().Threads)
                {
                    if (GetCurrentThreadId() == th.Id)
                    {
                        th.PriorityLevel = ThreadPriorityLevel.Idle;
                        return true;
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        /// <summary>
        /// In a sparse file, large ranges of zeroes may not require disk allocation. Space for nonzero data will be allocated as needed as the file is written
        /// </summary>
        /// <param name="fs"></param>
        /// <returns></returns>
        public static bool SetSparse(FileStream fs)
        {
            try
            {
                uint b = 0;
                return fs.SafeFileHandle != null && DeviceIoControl(fs.SafeFileHandle.DangerousGetHandle(), FSCTL_SET_SPARSE, IntPtr.Zero, 0, IntPtr.Zero, 0, out b, IntPtr.Zero);
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }
    }
}