// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SharpDc.Helpers
{
    public static class FileHelper
    {
        private static string _defaultPath;

        private static readonly string[] ReservedFileNames = new[]
                                                                 {
                                                                     "CON", "AUX", "COM1", "COM2", "COM3", "COM4",
                                                                     "LPT1",
                                                                     "LPT2", "LPT3", "PRN", "NUL"
                                                                 };

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
#if MONO
            return File.Exists(path);
#else
            // 10 times faster than System.IO.File.Exists()
            var s = new STAT();
            return _stat64(path, ref s) == 0;
#endif
        }

        public static bool FileExists(string path, out long size,  out DateTime lastWriteTime)
        {
#if MONO
            var fi = new FileInfo(path);
            lastWriteTime = fi.LastWriteTime;
            size = fi.Length;
            return fi.Exists
#else
            // 10 times faster than System.IO.File.Exists()
            var s = new STAT();

            var result = _stat64(path, ref s);
            lastWriteTime = UnixTimeStampToDateTime(s.st_mtime);
            size = s.st_size;
            return result == 0;
#endif
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

#if !MONO
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

#endif

        /// <summary>
        /// Get the free diskspace of a drive
        /// by the drive' name in bytes
        /// </summary>
        /// <param name="driveName">Drive's name like C:</param>
        /// <returns>Free Space in bytes as long</returns>
        /// <exception cref="ArgumentException">
        /// Thrown if the name of the drive can't be evaluated
        /// </exception>
        public static long GetFreeDiskSpace(string driveName)
        {
            if (!Directory.Exists(driveName))
                throw new ArgumentException("Invalid Drive " + driveName);

#if MONO
            var di = new DriveInfo(driveName);
            return di.AvailableFreeSpace;
#else
            long totalBytes, freeBytes, freeBytesAvail;
            GetDiskFreeSpaceEx(driveName,
                               out freeBytesAvail,
                               out totalBytes,
                               out freeBytes);
            return freeBytesAvail;
#endif
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