// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Threading;

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

    internal static class Win32
    {
        public const int THREAD_MODE_BACKGROUND_BEGIN = 0x00010000;
        public const int THREAD_MODE_BACKGROUND_END = 0x00020000;
    }

    internal static class NativeMethods
    {
        [DllImport("Kernel32.dll", ExactSpelling = true)]
        public static extern IntPtr GetCurrentThread();

        [DllImport("Kernel32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetThreadPriority(IntPtr hThread, int nPriority);

    }

    public static class ThreadUtility
    {
        /// <summary>
        /// Puts the current thread into background processing mode.
        /// </summary>
        /// <returns>A Scope that must be disposed to leave background processing mode.</returns>
        [SecurityPermission(SecurityAction.Demand, Flags = SecurityPermissionFlag.ControlThread)]
        public static Scope EnterBackgroundProcessingMode()
        {
            Thread.BeginThreadAffinity();
            IntPtr hThread = NativeMethods.GetCurrentThread();
            if (IsWindowsVista() && NativeMethods.SetThreadPriority(hThread,
                Win32.THREAD_MODE_BACKGROUND_BEGIN))
            {
                // OS supports background processing; return Scope that exits this mode
                return Scope.Create(() =>
                {
                    NativeMethods.SetThreadPriority(hThread, Win32.THREAD_MODE_BACKGROUND_END);
                    Thread.EndThreadAffinity();
                });
            }

            // OS doesn't support background processing mode (or setting it failed)
            Thread.EndThreadAffinity();
            return Scope.Empty;
        }

        public static Scope EnterBackgroundProcessingMode(this Thread thread)
        {
            return EnterBackgroundProcessingMode();
        }

        public static bool BeginBackgroundProcessing()
        {
            Thread.BeginThreadAffinity();
            IntPtr hThread = NativeMethods.GetCurrentThread();
            return IsWindowsVista() && NativeMethods.SetThreadPriority(hThread, Win32.THREAD_MODE_BACKGROUND_BEGIN);
        }

        public static bool EndBackgroundProcessing()
        {
            if (!IsWindowsVista())
                return false;

            IntPtr hThread = NativeMethods.GetCurrentThread();
            Thread.EndThreadAffinity();
            return NativeMethods.SetThreadPriority(hThread, Win32.THREAD_MODE_BACKGROUND_END);
        }

        // Returns true if the current OS is Windows Vista (or Server 2008) or higher.
        private static bool IsWindowsVista()
        {
            var os = Environment.OSVersion;
            return os.Platform == PlatformID.Win32NT && os.Version >= new Version(6, 0);
        }
    }

    public sealed class Scope : IDisposable
    {
        public static readonly Scope Empty = new Scope(null);

        public static Scope Create(Action fnDispose)
        {
            return new Scope(fnDispose);
        }

        Action m_fnDispose;

        private Scope(Action fnDispose)
        {
            m_fnDispose = fnDispose;
        }

        public void Dispose()
        {
            if (m_fnDispose != null)
            {
                m_fnDispose();
                m_fnDispose = null;
            }
        }

        

        
    }
}