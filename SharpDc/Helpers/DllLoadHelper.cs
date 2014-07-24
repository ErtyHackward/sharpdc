using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SharpDc.Helpers
{
    public static class DllLoadHelper
    {
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr LoadLibrary(string lpFileName);

        static public string AssemblyDirectory
        {
            get
            {
                string codeBase = Assembly.GetExecutingAssembly().CodeBase;
                var uri = new UriBuilder(codeBase);
                string path = Uri.UnescapeDataString(uri.Path);
                return Path.GetDirectoryName(path);
            }
        }

        /// <summary>
        /// Loads unmanaged library depending on the process arhitecture
        /// </summary>
        public static void LoadUmnanagedLibrary(string libaryName)
        {
            // don't try to call LoadLibrary on mono
            if (Type.GetType("Mono.Runtime") != null)
                return;

            var path = Path.Combine(AssemblyDirectory, Environment.Is64BitProcess ? "x64" : "x86", libaryName);

            if (LoadLibrary(path) == IntPtr.Zero)
            {
                throw new ApplicationException("Cannot load " + path);
            }
        }
    }
}
