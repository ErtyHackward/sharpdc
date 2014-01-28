// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.ComponentModel;
using SharpDc.Structs;

namespace SharpDc.Managers
{
    /// <summary>
    /// Represents a single file in a share
    /// </summary>
    [Serializable]
    public struct ContentItem
    {
        private Magnet _magnet;
        private string[] _systemPaths;
        private string _virtualPath;
        private DateTime _createDate;
        private ulong _uploadedBytes;
        private DateTime _fileLastWrite;
        private DateTime _lastAccess;

        /// <summary>
        /// Gets or sets content file magnet
        /// </summary>
        public Magnet Magnet
        {
            get { return _magnet; }
            set { _magnet = value; }
        }

        /// <summary>
        /// Gets first file location or null
        /// </summary>
        public string SystemPath
        {
            get { return SystemPaths == null ? null : SystemPaths[0]; }
        }

        /// <summary>
        /// Gets or sets array of available file locations
        /// </summary>
        public string[] SystemPaths
        {
            get { return _systemPaths; }
            set { _systemPaths = value; }
        }

        /// <summary>
        /// Share virtual path
        /// </summary>
        public string VirtualPath
        {
            get { return _virtualPath; }
            set { _virtualPath = value; }
        }

        /// <summary>
        /// Gets time when this item was created
        /// </summary>
        [DefaultValue(typeof(DateTime), "0001-01-01T00:00:00")]
        public DateTime CreateDate
        {
            get { return _createDate; }
            set { _createDate = value; }
        }

        /// <summary>
        /// Total uploaded bytes of this item
        /// </summary>
        [DefaultValue(typeof(ulong), "0")]
        public ulong UploadedBytes
        {
            get { return _uploadedBytes; }
            set { _uploadedBytes = value; }
        }

        [DefaultValue(typeof(DateTime), "0001-01-01T00:00:00")]
        public DateTime FileLastWrite
        {
            get { return _fileLastWrite; }
            set { _fileLastWrite = value; }
        }

        [DefaultValue(typeof(DateTime), "0001-01-01T00:00:00")]
        public DateTime LastAccess
        {
            get { return _lastAccess; }
            set { _lastAccess = value; }
        }

        public ContentItem(DownloadItem item)
        {
            _magnet = item.Magnet;
            _systemPaths = item.SaveTargets.ToArray();
            _virtualPath = "Downloads\\" + _magnet.FileName;
            _createDate = DateTime.Now;
            _uploadedBytes = 0;
            _fileLastWrite = DateTime.MinValue;
            _lastAccess = new DateTime();
        }

        public void AddSystemPath(string path)
        {
            if (SystemPaths == null)
            {
                SystemPaths = new[] { path };
            }
            else
            {
                var array = SystemPaths;
                Array.Resize(ref array, SystemPaths.Length + 1);
                array[array.Length - 1] = path;
                SystemPaths = array;
            }
        }
    }
}