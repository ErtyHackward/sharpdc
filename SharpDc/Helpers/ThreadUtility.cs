﻿// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2014
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Threading;

namespace SharpDc.Helpers
{
    public static class ThreadUtility
    {
        private static bool _isLinux;

        static ThreadUtility()
        {
            var p = (int)Environment.OSVersion.Platform;
            _isLinux = (p == 4) || (p == 6) || (p == 128);
        }

        /// <summary>
        /// Runs the action in the background processing mode (low io and cpu priority)
        /// </summary>
        /// <param name="action"></param>
        public static void RunInBackground(Action action)
        {
            DcEngine.ThreadPool.QueueWorkItem(delegate
            {
                using (EnterBackgroundProcessingMode())
                {
                    action();
                }
            });
        }

        /// <summary>
        /// Puts the current thread into background processing mode.
        /// </summary>
        /// <returns>A Scope that must be disposed to leave background processing mode.</returns>
        [SecurityPermission(SecurityAction.Demand, Flags = SecurityPermissionFlag.ControlThread)]
        public static Scope EnterBackgroundProcessingMode()
        {
            if (_isLinux)
                return Scope.Empty;

            Thread.BeginThreadAffinity();
            IntPtr hThread = NativeMethods.GetCurrentThread();
            ThreadPriority previous = NativeMethods.GetThreadPriority(hThread);

            if (IsWindowsVista() && NativeMethods.SetThreadPriority(hThread,
                ThreadPriority.THREAD_PRIORITY_IDLE | ThreadPriority.THREAD_MODE_BACKGROUND_BEGIN))
            {

                // OS supports background processing; return Scope that exits this mode
                return Scope.Create(() =>
                {
                    NativeMethods.SetThreadPriority(hThread, previous | ThreadPriority.THREAD_MODE_BACKGROUND_END);
                    Thread.EndThreadAffinity();
                });
            }


            // try to set idle cpu priority at least
            if (NativeMethods.SetThreadPriority(hThread, ThreadPriority.THREAD_PRIORITY_IDLE))
            {
                return Scope.Create(() =>
                    {
                        NativeMethods.SetThreadPriority(hThread, previous);
                        Thread.EndThreadAffinity();
                    });
            }

            // OS doesn't support background processing mode (or setting it failed)
            Thread.EndThreadAffinity();
            return Scope.Empty;
        }

        public static bool InBackgorundMode()
        {
            Thread.BeginThreadAffinity();
            IntPtr hThread = NativeMethods.GetCurrentThread();
            var background = (NativeMethods.GetThreadPriority(hThread) & ThreadPriority.THREAD_MODE_BACKGROUND_BEGIN) == ThreadPriority.THREAD_MODE_BACKGROUND_BEGIN;

            Thread.EndThreadAffinity();
            return background;
        }

        public static Scope EnterBackgroundProcessingMode(this Thread thread)
        {
            return EnterBackgroundProcessingMode();
        }

        public static bool BeginBackgroundProcessing()
        {
            if (_isLinux)
                return false;

            Thread.BeginThreadAffinity();
            IntPtr hThread = NativeMethods.GetCurrentThread();
            return IsWindowsVista() && NativeMethods.SetThreadPriority(hThread, ThreadPriority.THREAD_MODE_BACKGROUND_BEGIN);
        }

        public static bool EndBackgroundProcessing()
        {
            if (!IsWindowsVista())
                return false;

            IntPtr hThread = NativeMethods.GetCurrentThread();
            Thread.EndThreadAffinity();
            return NativeMethods.SetThreadPriority(hThread, ThreadPriority.THREAD_MODE_BACKGROUND_END);
        }

        // Returns true if the current OS is Windows Vista (or Server 2008) or higher.
        private static bool IsWindowsVista()
        {
            var os = Environment.OSVersion;
            return os.Platform == PlatformID.Win32NT && os.Version >= new Version(6, 0);
        }
    }

    internal enum ThreadPriority
    {
        THREAD_MODE_BACKGROUND_BEGIN = 0x00010000,
        THREAD_MODE_BACKGROUND_END = 0x00020000,
        THREAD_PRIORITY_ABOVE_NORMAL = 1,
        THREAD_PRIORITY_BELOW_NORMAL = -1,
        THREAD_PRIORITY_HIGHEST = 2,
        THREAD_PRIORITY_IDLE = -15,
        THREAD_PRIORITY_LOWEST = -2,
        THREAD_PRIORITY_NORMAL = 0,
        THREAD_PRIORITY_TIME_CRITICAL = 15
    }


    internal static class NativeMethods
    {
        [DllImport("Kernel32.dll", ExactSpelling = true)]
        public static extern IntPtr GetCurrentThread();

        [DllImport("Kernel32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetThreadPriority(IntPtr hThread, ThreadPriority nPriority);

        [DllImport("kernel32.dll")]
        public static extern ThreadPriority GetThreadPriority(IntPtr hThread);

    }
}