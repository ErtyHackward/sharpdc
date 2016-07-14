// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using SharpDc.Logging;

namespace SharpDc.Helpers
{
    public static class FileHelper
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        public static readonly bool IsLinux;
        private static string _defaultPath;

        static FileHelper()
        {
            var p = (int)Environment.OSVersion.Platform;
            IsLinux = (p == 4) || (p == 6) || (p == 128);
        }

        private static readonly string[] ReservedFileNames = new[]
                                                                 {
                                                                     "CON", "AUX", "COM1", "COM2", "COM3", "COM4",
                                                                     "LPT1",
                                                                     "LPT2", "LPT3", "PRN", "NUL"
                                                                 };

        /// <summary>
        /// Returns program start folder path
        /// </summary>
        public static string DefaultPath
        {
            get
            {
                if (string.IsNullOrEmpty(_defaultPath))
                {
                    _defaultPath = AppDomain.CurrentDomain.BaseDirectory;
                }
                return _defaultPath;
            }
            set { _defaultPath = value; }
        }

        public static bool IsValidFilePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            var forbiddenChars = Path.GetInvalidPathChars();
            for (int i = 0; i < forbiddenChars.Length; i++)
            {
                if (path.Contains(forbiddenChars[i].ToString()))
                {
                    // path is containing invalid chars
                    return false;
                }
            }

            foreach (var dir in path.Split(Path.DirectorySeparatorChar))
                if (IsReservedName(dir)) return false;

            return true;
        }

        public static bool IsValidFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            var forbiddenChars = Path.GetInvalidFileNameChars();

            for (var i = 0; i < forbiddenChars.Length; i++)
            {
                if (name.Contains(forbiddenChars[i].ToString()))
                {
                    // filename contains invalid chars.
                    return false;
                }
            }

            return !IsReservedName(name);
        }

        public static bool IsReservedName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;

            fileName = fileName.Trim();

            var dotIndex = fileName.IndexOf('.');

            var fn = fileName.Substring(0, dotIndex == -1 ? fileName.Length : dotIndex).ToUpper();

            for (int i = 0; i < ReservedFileNames.Length; i++)
            {
                if (fn == ReservedFileNames[i])
                    return true;
            }
            return false;
        }

        public static string MakeValidFileName(string fileName)
        {
            var forbiddenChars = Path.GetInvalidFileNameChars();

            if (fileName.IndexOfAny(forbiddenChars) != -1)
            {
                foreach (var c in forbiddenChars)
                    fileName = fileName.Replace(c, '_');
            }

            if (IsReservedName(fileName))
                fileName = fileName.Insert(0, "_");

            return fileName;
        }

        public static string MakeValidDirName(string saveto)
        {
            var tokens = saveto.Split(Path.DirectorySeparatorChar);
            for (var i = 1; i < tokens.Length; i++)
            {
                tokens[i] = MakeValidFileName(tokens[i]);
            }
            return string.Join(Path.DirectorySeparatorChar.ToString(), tokens);
        }

        /// <summary>
        /// Tries to create a file with size specified
        /// </summary>
        /// <param name="target">File path</param>
        /// <param name="size">Required size</param>
        /// <returns>True is file creation success otherwise false</returns>
        public static bool AllocateFile(string target, long size)
        {
            Exception x;
            return AllocateFile(target, size, out x);
        }
        
        public static bool AllocateFile(string target, long size, out Exception x)
        {
            try
            {
                x = null;
                using (var fs = new FileStream(target, FileMode.OpenOrCreate))
                {
                    fs.SetLength(size);
                }
                return true;
            }
            catch (IOException ex)
            {
                x = ex;
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                x = ex;
                return false;
            }
        }
        
        public static bool PathExists(string target)
        {
            var sep = Path.DirectorySeparatorChar;
            int pos;
            if ((pos = target.LastIndexOf(sep)) > 0)
            {
                try
                {
                    string dir = target.Substring(0, pos);
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                }
                catch (IOException)
                {
                    return false;
                }
            }
            return File.Exists(target);
        }

        /// <summary>
        /// Gets if file exists or not (very fast on Windows)
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static bool FileExists(string path)
        {
            if (IsLinux)
                return File.Exists(path);
            
            // 10 times faster than System.IO.File.Exists()
            var s = new STAT();
            return _stat64(path, ref s) == 0;
        }

        public static bool FileExists(string path, out long size, out DateTime lastWriteTime)
        {
            if (IsLinux)
            {
                var fi = new FileInfo(path);
                lastWriteTime = fi.LastWriteTime;
                size = fi.Length;
                return fi.Exists;
            }

            // 10 times faster than System.IO.File.Exists()
            var s = new STAT();

            var result = _stat64(path, ref s);
            lastWriteTime = UnixTimeStampToDateTime(s.st_mtime);
            size = s.st_size;
            return result == 0;
        }

        public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            var dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0);
            dtDateTime = dtDateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dtDateTime;
        }

        public static double DateTimeToUnixTimestamp(DateTime dateTime)
        {
            return (dateTime - new DateTime(1970, 1, 1).ToLocalTime()).TotalSeconds;
        }

        /// <summary>
        /// Try to delete file
        /// </summary>
        /// <param name="path"></param>
        /// <returns>True in file was deleted or in not exists otherwise false</returns>
        public static bool TryDelete(string path)
        {
            try
            {
                if (FileExists(path))
                {
                    File.Delete(path);
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void DeleteAnyway(string path)
        {
            var ts = Stopwatch.StartNew();

            while (!TryDelete(path))
            {
                Thread.Sleep(100);

                if (ts.Elapsed.TotalSeconds > 30)
                {
                    Logger.Error($"Failed to delete file {path} in 30 sec. Skip");
                    return;
                }
            }
        }

        /// <summary>
        /// Gets total volume of the path provided
        /// </summary>
        /// <param name="directoryName"></param>
        /// <returns></returns>
        public static long GetTotalCapacity(string directoryName)
        {
            if (!Directory.Exists(directoryName))
                throw new ArgumentException("Invalid directory " + directoryName);
            
            var directory = new DirectoryInfo(directoryName);
            return new DriveInfo(directory.Root.FullName).TotalSize;
        }

        [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExA")]
        private static extern long GetDiskFreeSpaceEx(string lpDirectoryName,
                                                      out long lpFreeBytesAvailableToCaller,
                                                      out long lpTotalNumberOfBytes,
                                                      out long lpTotalNumberOfFreeBytes);

        [DllImport("msvcrt.dll", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
        private static extern int _stat64(string file, ref STAT buf);

        [StructLayout(LayoutKind.Sequential)]
        struct STAT
        {
            public uint st_dev;
            public ushort st_ino;
            public ushort st_mode;
            public short st_nlink;
            public short st_uid;
            public short st_gid;
            public uint st_rdev;
            public long st_size;
            public long st_atime;
            public long st_mtime;
            public long st_ctime;
        }

        /// <summary>
        /// Get the free directory space
        /// by the drive' name in bytes
        /// </summary>
        /// <param name="directoryName">Directory's name like C:\Downloads</param>
        /// <returns>Free Space in bytes as long</returns>
        public static long GetFreeSpace(string directoryName)
        {
            if (!Directory.Exists(directoryName))
                throw new ArgumentException("Invalid directory " + directoryName);

            if (IsLinux)
            {
                var directory = new DirectoryInfo(directoryName);
                return new DriveInfo(directory.Root.FullName).AvailableFreeSpace; ;
            }

            var driveName = new DirectoryInfo(directoryName).Root.FullName;
                
            long totalBytes, freeBytes, freeBytesAvail;
            GetDiskFreeSpaceEx(driveName,
                out freeBytesAvail,
                out totalBytes,
                out freeBytes);
            return freeBytesAvail;
        }

        /// <summary>
        /// Detects if file is open in some application
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns>Return True if file is open otherwise returns false</returns>
        public static bool IsOpenForWrite(string filePath)
        {
            try
            {
                using (new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    return false;
                }
            }
            catch
            {
                return true;
            }
        }
    }
}