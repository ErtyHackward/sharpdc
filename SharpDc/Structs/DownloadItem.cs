//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using SharpDc.Collections;
using SharpDc.Events;
using SharpDc.Interfaces;
using SharpDc.Logging;

namespace SharpDc.Structs
{
    public enum DownloadPriority
    {
        Pause = 0,
        Lowest = 1,
        Low = 2,
        Normal = 3,
        High = 4,
        Highest = 5
    }

    public class DownloadItem
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        public static int SegmentSize = 1024 * 1024;

        public static int GetSegmentIndex(long filePosition)
        {
            return (int)(filePosition / SegmentSize);
        }

        private BitArray _activeSegments;
        private BitArray _downloadedSegments;
        private int _doneSegmentsCount;
        private int _activeSegmentsCount;
        private int _totalSegmentsCount;
        private SourceList _sources;
        private readonly List<int> _highPrioritySegments = new List<int>();
        private readonly object _syncRoot = new object();

        private Magnet _magnet;

        public List<int> HighPrioritySegments
        {
            get { return _highPrioritySegments; }
        }

        public BitArray DoneSegments
        {
            get { return _downloadedSegments; }
        }

        public object SyncRoot
        {
            get { return _syncRoot; }
        }

        public Magnet Magnet
        {
            get { return _magnet; }
            set { _magnet = value; }
        }

        public int DoneSegmentsCount
        {
            get { return _doneSegmentsCount; }
        }

        public int ActiveSegmentsCount
        {
            get { return _activeSegmentsCount; }
        }

        public int TotalSegmentsCount
        {
            get {
                if (_totalSegmentsCount == 0)
                {
                    // initiate the buffers
                    lock (_syncRoot)
                    {
                        if (_totalSegmentsCount != 0)
                            return _totalSegmentsCount;

                        var ttl = (int)(_magnet.Size / SegmentSize);
                        if (_magnet.Size % SegmentSize != 0) ttl++;
                        _doneSegmentsCount = 0;
                        _activeSegmentsCount = 0;

                        _activeSegments = new BitArray(ttl);
                        _downloadedSegments = new BitArray(ttl);
                        _totalSegmentsCount = ttl;
                    }
                    
                }
                return _totalSegmentsCount; 
            }
        }

        public DownloadPriority Priority { get; set; }

        public string FolderUnitPath { get; set; }

        public DownloadItemsGroup FolderUnit { get; set; }

        public IStorageContainer StorageContainer { get; set; }

        /// <summary>
        /// Gets a list of absolute paths where the file should be copied to when download is complete, can be null
        /// </summary>
        public List<string> SaveTargets { get; set; }

        /// <summary>
        /// Gets list of all available sources for this DownloadItem
        /// </summary>
        public SourceList Sources
        {
            get { return _sources; }
            set
            {
                _sources = value;
                if (_sources != null)
                    _sources.DownloadItem = this;
            }
        }

        public List<Source> ActiveSources { get; set; }

        /// <summary>
        /// Gets or sets last sources search time
        /// </summary>
        public DateTime LastSearch { get; set; }

        /// <summary>
        /// Tells if download is finished
        /// </summary>
        public bool Downloaded
        {
            get { return TotalSegmentsCount == DoneSegmentsCount; }
        }

        /// <summary>
        /// Whether or not to log segment take/done/cancel events
        /// </summary>
        public bool LogSegmentEvents { get; set; }

        #region Events

        public event EventHandler<SegmentEventArgs> SegmentTaken;

        private void OnSegmentTaken(SegmentEventArgs e)
        {
            var handler = SegmentTaken;
            if (handler != null) handler(this, e);

            if (LogSegmentEvents)
                Logger.Info("SEG TAKEN ID:{0} POS:{1} {2}", e.SegmentInfo.Index, e.SegmentInfo.StartPosition, e.Source);
        }

        public event EventHandler<SegmentEventArgs> SegmentCancelled;

        private void OnSegmentCancelled(SegmentEventArgs e)
        {
            var handler = SegmentCancelled;
            if (handler != null) handler(this, e);

            if (LogSegmentEvents)
                Logger.Info("SEG CANCELLED ID:{0} {1}", e.SegmentInfo.Index, e.Source);
        }

        public event EventHandler<SegmentEventArgs> SegmentFinished;

        private void OnSegmentFinished(SegmentEventArgs e)
        {
            var handler = SegmentFinished;
            if (handler != null) handler(this, e);

            if (LogSegmentEvents)
                Logger.Info("SEG FINISHED ID:{0} {1} ", e.SegmentInfo.Index, e.Source);
        }

        public event EventHandler DownloadFinished;

        private void OnDownloadFinished()
        {
            var handler = DownloadFinished;
            if (handler != null) handler(this, EventArgs.Empty);

            Logger.Info("{0} download finished", Magnet.FileName);
        }

        #endregion

        public DownloadItem()
        {
            Priority = DownloadPriority.Normal;
            _sources = new SourceList { DownloadItem = this };
            ActiveSources = new List<Source>();
        }

        public bool TakeFreeSegment(Source src, out SegmentInfo segment)
        {
            segment.Index = -1;
            segment.Length = 0;
            segment.StartPosition = -1;


            if (StorageContainer == null)
            {
                Logger.Error("Unable to take the segment, no storage container set");
                return false;
            }

            if (StorageContainer.FreeSegments <= 0)
            {
                return false;
            }

            bool result = false;

            lock (_syncRoot)
            {
                if (TotalSegmentsCount == _activeSegmentsCount + _doneSegmentsCount)
                    return false;

                for (var i = 0; i < _highPrioritySegments.Count; i++)
                {
                    var segIndex = _highPrioritySegments[i];
                    if (!_downloadedSegments[segIndex] && !_activeSegments[segIndex])
                    {
                        segment.Index = segIndex;
                        break;
                    }
                }

                if (segment.Index == -1)
                {
                    for (var i = 0; i < _totalSegmentsCount; i++)
                    {
                        if (!_downloadedSegments[i] && !_activeSegments[i])
                        {
                            segment.Index = i;
                            break;
                        }
                    }
                }

                if (segment.Index != -1)
                {
                    segment.StartPosition = (long)segment.Index * SegmentSize;
                    segment.Length = segment.Index == _totalSegmentsCount - 1
                                            ? (int)(_magnet.Size % SegmentSize)
                                            : SegmentSize;

                    _activeSegmentsCount++;
                    _activeSegments[segment.Index] = true;

                    ActiveSources.Add(src);
                    result = true;
                }
            }

            if (result)
                OnSegmentTaken(new SegmentEventArgs { SegmentInfo = segment, Source = src, DownloadItem = this });

            return result;
        }

        public void CancelSegment(int index, Source src)
        {
            lock (_syncRoot)
            {
                _activeSegmentsCount--;
                _activeSegments[index] = false;
                ActiveSources.Remove(src);
            }

            OnSegmentCancelled(new SegmentEventArgs { SegmentInfo = new SegmentInfo { Index = index }, DownloadItem = this, Source = src });
        }

        public void FinishSegment(int index, Source src)
        {
            bool downloadFinished;

            lock (_syncRoot)
            {
                _activeSegmentsCount--;
                _activeSegments[index] = false;
                _doneSegmentsCount++;
                _downloadedSegments[index] = true;
                downloadFinished = _doneSegmentsCount == _totalSegmentsCount;
                ActiveSources.Remove(src);
            }

            OnSegmentFinished(new SegmentEventArgs { SegmentInfo = new SegmentInfo { Index = index }, DownloadItem = this, Source = src });

            if (downloadFinished)
            {
                OnDownloadFinished();
            }
        }

        /// <summary>
        /// Determines was all the segments from range been downloaded
        /// </summary>
        /// <param name="startPos">File position from wich bytes should be read</param>
        /// <param name="count"></param>
        /// <param name="makeRequest">If true the not-finished segments will be marked as High-Priority segments</param>
        /// <returns></returns>
        protected bool CanRead(long startPos, int count, bool makeRequest = true)
        {
            var startIndex = GetSegmentIndex(startPos);
            var endIndex   = GetSegmentIndex(startPos + count);
            var result     = true;

            lock (SyncRoot)
            {
                for (int i = startIndex; i <= endIndex; i++)
                {
                    lock (_syncRoot)
                    {
                        if (!StorageContainer.CanReadSegment(i))
                        {
                            result = false;

                            if (makeRequest)
                            {
                                if (!HighPrioritySegments.Contains(i))
                                {
                                    Logger.Info("Add high priority segment {0}", i);
                                    HighPrioritySegments.Add(i);
                                }
                            }
                            else return false;
                        }
                    }
                }
                return result;
            }
        }

        public bool Read(byte[] buffer, long filePosition, int count)
        {
            if (!CanRead(filePosition, count))
                return false;

            var startIndex = GetSegmentIndex(filePosition);
            var endIndex   = GetSegmentIndex(filePosition + count);
            
            lock (_syncRoot)
            {
                if (startIndex == endIndex)
                {
                    var startOffset = (int)(filePosition % SegmentSize);
                    return StorageContainer.Read(startIndex, startOffset, buffer, 0, count) == count;
                }
                else
                {
                    int position = 0;
                    for (var i = startIndex; i <= endIndex; i++)
                    {
                        if (i == startIndex)
                        {
                            var startOffset = (int)(filePosition % SegmentSize);
                            var length = SegmentSize - startOffset;
                            if (StorageContainer.Read(i, startOffset, buffer, 0, length) != length)
                                return false;
                            position += length;
                        }
                        else if (i == endIndex)
                        {
                            var length = (int)((filePosition + count) % SegmentSize);
                            if (StorageContainer.Read(i, 0, buffer, position, length) != length)
                                return false;
                        }
                        else
                        {
                            // copy whole segment
                            if (StorageContainer.Read(i, 0, buffer, position, SegmentSize) != SegmentSize)
                                return false;
                            position += SegmentSize;
                        }
                    }
                }
            }

            return true;
        }
    }
}
