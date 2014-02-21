// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Xml.Serialization;
using SharpDc.Collections;
using SharpDc.Events;
using SharpDc.Hash;
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

    /// <summary>
    /// Provides multisegment download of a single file
    /// </summary>
    [Serializable]
    public class DownloadItem
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        
        public static int GetSegmentIndex(long filePosition, int segmentSize)
        {
            return (int)(filePosition / segmentSize);
        }

        private int _segmentLength = 1024 * 1024;
        private BitArray _activeSegments;
        private BitArray _downloadedSegments;
        private BitArray _verifiedSegments;
        private int _doneSegmentsCount;
        private int _activeSegmentsCount;
        private int _totalSegmentsCount;
        private int _verifiedSegmentsCount;
        private SourceList _sources;
        private readonly List<int> _highPrioritySegments = new List<int>();
        private readonly object _syncRoot = new object();
        private readonly Magnet _magnet;

        private byte[][] _leavesHashes;

        private Source[] _segmentSources;

        public bool HasLeaves
        {
            get { return _leavesHashes != null; }
        }

        /// <summary>
        /// Depending on available leaves verify segment can be bigger than download segment
        /// </summary>
        public int VerifySegmentLength { get; private set; }

        [XmlIgnore]
        public BitArray VerifiedSegments 
        {
            get { return _verifiedSegments; }
        }

        [XmlIgnore]
        public List<int> HighPrioritySegments
        {
            get { return _highPrioritySegments; }
        }

        [XmlIgnore]
        public BitArray DoneSegments
        {
            get { return _downloadedSegments; }
        }

        [XmlIgnore]
        public object SyncRoot
        {
            get { return _syncRoot; }
        }

        /// <summary>
        /// Gets a magnet describing the file
        /// </summary>
        public Magnet Magnet
        {
            get { return _magnet; }
        }
        
        /// <summary>
        /// Gets length of the segment in bytes
        /// </summary>
        public int SegmentLength
        {
            get { return _segmentLength; }
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
            get
            {
                if (_totalSegmentsCount == 0)
                {
                    // initiate the buffers
                    lock (_syncRoot)
                    {
                        if (_totalSegmentsCount != 0)
                            return _totalSegmentsCount;

                        var ttl = (int)(_magnet.Size / _segmentLength);
                        if (_magnet.Size % _segmentLength != 0) 
                            ttl++;
                        _doneSegmentsCount = 0;
                        _activeSegmentsCount = 0;

                        _activeSegments = new BitArray(ttl);
                        _downloadedSegments = new BitArray(ttl);
                        _segmentSources = new Source[_totalSegmentsCount];
                        _totalSegmentsCount = ttl;
                    }
                }
                return _totalSegmentsCount;
            }
        }

        public DownloadPriority Priority { get; set; }

        public string FolderUnitPath { get; set; }

        [XmlIgnore]
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

        [XmlIgnore]
        public List<Source> ActiveSources { get; set; }

        /// <summary>
        /// Gets or sets last sources search time
        /// </summary>
        public DateTime LastSearch { get; set; }

        /// <summary>
        /// Tells if download is finished
        /// </summary>
        [XmlIgnore]
        public bool Downloaded
        {
            get { return TotalSegmentsCount == DoneSegmentsCount; }
        }

        /// <summary>
        /// Whether or not to log segment take/done/cancel events
        /// </summary>
        [XmlIgnore]
        public bool LogSegmentEvents { get; set; }

        [XmlArray("DoneSegments")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public int[] SerializeDoneSegments
        {
            get
            {
                return Utils.BitArraySerialize(_downloadedSegments);
            }
            set
            {
                _totalSegmentsCount = TotalSegmentsCount;
                _downloadedSegments = new BitArray(value);
                _downloadedSegments.Length = _totalSegmentsCount;

                foreach (bool bit in _downloadedSegments)
                {
                    if (bit)
                        _doneSegmentsCount++;    
                }
                
            }
        }

        [XmlArray("VerifiedSegments")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public int[] SerializeVerifiedSegments
        {
            get
            {
                return Utils.BitArraySerialize(_verifiedSegments);
            }
            set
            {
                _verifiedSegments = new BitArray(value);
                _verifiedSegmentsCount = 0;
                
                foreach (bool bit in _verifiedSegments)
                {
                    if (bit)
                        _verifiedSegmentsCount++;
                }
            }
        }

        #region Events

        public event EventHandler<SegmentEventArgs> SegmentTaken;

        private void OnSegmentTaken(SegmentEventArgs e)
        {
            var handler = SegmentTaken;
            if (handler != null) handler(this, e);

            if (LogSegmentEvents)
                Logger.Info("SEG TAKEN ID:{0} POS:{1} {2} A:{3}", e.SegmentInfo.Index, e.SegmentInfo.StartPosition, e.Source, _activeSegmentsCount);
        }

        public event EventHandler<SegmentEventArgs> SegmentCancelled;

        private void OnSegmentCancelled(SegmentEventArgs e)
        {
            var handler = SegmentCancelled;
            if (handler != null) handler(this, e);

            if (LogSegmentEvents)
                Logger.Info("SEG CANCELLED ID:{0} {1} A:{2}", e.SegmentInfo.Index, e.Source, _activeSegmentsCount);
        }

        public event EventHandler<SegmentEventArgs> SegmentFinished;

        private void OnSegmentFinished(SegmentEventArgs e)
        {
            var handler = SegmentFinished;
            if (handler != null) handler(this, e);

            if (LogSegmentEvents)
                Logger.Info("SEG FINISHED ID:{0} {1} A:{2}", e.SegmentInfo.Index, e.Source, _activeSegmentsCount);
        }

        public event EventHandler DownloadFinished;

        private void OnDownloadFinished()
        {
            var handler = DownloadFinished;
            if (handler != null) handler(this, EventArgs.Empty);

            Logger.Info("{0} download finished", Magnet.FileName);
        }

        /// <summary>
        /// Occurs when we have a segment that can be verified using hash leaves
        /// </summary>
        public event EventHandler<SegmentVerificationEventArgs> VerificationNeeded;

        protected virtual void OnVerificationNeeded(SegmentVerificationEventArgs e)
        {
            var handler = VerificationNeeded;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// Occurs when the verification of the segment is finished
        /// </summary>
        public event EventHandler<SegmentVerificationEventArgs> VerificationComplete;

        protected virtual void OnVerificationComplete(SegmentVerificationEventArgs e)
        {
            var handler = VerificationComplete;
            if (handler != null) handler(this, e);
        }

        #endregion

        public DownloadItem(Magnet magnet)
        {
            _magnet = magnet;
            Priority = DownloadPriority.Normal;
            _sources = new SourceList { DownloadItem = this };
            ActiveSources = new List<Source>();
        }

        public bool TakeFreeSegment(Source src, out SegmentInfo segment)
        {
            segment.Index = -1;
            segment.Length = 0;
            segment.Position = 0;
            segment.StartPosition = -1;

            if (Priority == DownloadPriority.Pause)
            {
                Logger.Warn("Attemt to take segment on paused download item");
                return false;
            }

            if (StorageContainer == null)
            {
                Logger.Error("Unable to take the segment, no storage container is set");
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
                    if (_highPrioritySegments.Count > 0)
                    {
                        // first try to download after the last high priority segment
                        for (var i = _highPrioritySegments[_highPrioritySegments.Count - 1]; i < _totalSegmentsCount; i++)
                        {
                            if (!_downloadedSegments[i] && !_activeSegments[i])
                            {
                                segment.Index = i;
                                break;
                            }
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
                }

                if (segment.Index != -1)
                {
                    segment.StartPosition = (long)segment.Index * _segmentLength;
                    segment.Length = segment.Index == _totalSegmentsCount - 1
                                         ? (int)(_magnet.Size % _segmentLength)
                                         : _segmentLength;

                    _activeSegmentsCount++;
                    _activeSegments[segment.Index] = true;
                    _segmentSources[segment.Index] = src;

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
            if (index < 0 || index >= _totalSegmentsCount)
                throw new InvalidOperationException();

            lock (_syncRoot)
            {
                if (!_activeSegments[index])
                {
                    return;
                }

                _activeSegmentsCount--;
                _activeSegments[index] = false;
                ActiveSources.Remove(src);
            }

            OnSegmentCancelled(new SegmentEventArgs
                                   {
                                       SegmentInfo = new SegmentInfo { Index = index },
                                       DownloadItem = this,
                                       Source = src
                                   });
        }

        public void FinishSegment(int index, Source src)
        {
            if (index < 0 || index >= _totalSegmentsCount)
                throw new InvalidOperationException();

            bool downloadFinished;

            lock (_syncRoot)
            {
                if (!_activeSegments[index] || _downloadedSegments[index])
                    return;

                _activeSegmentsCount--;
                _activeSegments[index] = false;
                _doneSegmentsCount++;
                _downloadedSegments[index] = true;
                downloadFinished = _doneSegmentsCount == _totalSegmentsCount;
                ActiveSources.Remove(src);
                HighPrioritySegments.Remove(index);
            }

            OnSegmentFinished(new SegmentEventArgs
                                  {
                                      SegmentInfo = new SegmentInfo { Index = index },
                                      DownloadItem = this,
                                      Source = src
                                  });

            if (downloadFinished)
            {
                OnDownloadFinished();
            }
        }

        /// <summary>
        /// Marks verify segment as correct
        /// </summary>
        /// <param name="verifySegmentIndex"></param>
        public void SetCorrect(int verifySegmentIndex)
        {
            var ea = new SegmentVerificationEventArgs 
                { 
                    IsCorrect = true, 
                    Index = verifySegmentIndex 
                };
            
            FillVerificationRange(ea);

            lock (_syncRoot)
            {
                _verifiedSegmentsCount++;
                _verifiedSegments.Set(verifySegmentIndex, true);
                ea.Sources = SegmentsVerifyToDownload(verifySegmentIndex).Select(i => _segmentSources[i]).ToList();
            }
            
            OnVerificationComplete(ea);

            if (LogSegmentEvents)
                Logger.Info("SEG VERIFIED ID:{0}", verifySegmentIndex);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="verifySegmentIndex"></param>
        public void SetIncorrect(int verifySegmentIndex)
        {
            var ea = new SegmentVerificationEventArgs
            {
                IsCorrect = false,
                Index = verifySegmentIndex
            };

            FillVerificationRange(ea);

            lock (_syncRoot)
            {
                foreach (var i in SegmentsVerifyToDownload(verifySegmentIndex))
                {
                    _downloadedSegments.Set(i, false);
                    _doneSegmentsCount--;
                }

                ea.Sources = SegmentsVerifyToDownload(verifySegmentIndex).Select(i => _segmentSources[i]).ToList();
            }

            OnVerificationComplete(ea);

            if (LogSegmentEvents)
                Logger.Info("SEG VERIFICATION FAILED ID:{0}", verifySegmentIndex);
        }

        private void FillVerificationRange(SegmentVerificationEventArgs ea)
        {
            long start;
            int length;
            VerifySegmentToFileRange(ea.Index, out start, out length);
            ea.Start = start;
            ea.Length = length;
        }

        private void VerifySegmentToFileRange(int verifySegment, out long start, out int length)
        {
            start = VerifySegmentLength * verifySegment;
            length = VerifySegmentLength;

            if (verifySegment == _verifiedSegments.Count - 1)
                length = (int)(Magnet.Size % VerifySegmentLength);
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
            if (!StorageContainer.Available)
                return false;
            
            var startIndex = GetSegmentIndex(startPos, SegmentLength);
            var endIndex   = GetSegmentIndex(startPos + count - 1, SegmentLength);

            lock (SyncRoot)
            {
                for (int i = startIndex; i <= endIndex; i++)
                {
                    if (!StorageContainer.CanReadSegment(i) || !_downloadedSegments[i])
                    {
                        if (makeRequest)
                        {
                            if (!HighPrioritySegments.Contains(i))
                            {
                                Logger.Info("Add high priority segment {0}", i);
                                HighPrioritySegments.Add(i);
                            }
                        }
                        return false;
                    }
                }
                return true;
            }
        }

        public bool Read(byte[] buffer, long filePosition, int count)
        {
            if (!CanRead(filePosition, count))
                return false;

            var startIndex = GetSegmentIndex(filePosition, SegmentLength);
            var endIndex   = GetSegmentIndex(filePosition + count - 1, SegmentLength);

            try
            {
                lock (_syncRoot)
                {
                    if (startIndex == endIndex)
                    {
                        var startOffset = (int)(filePosition % _segmentLength);
                        return StorageContainer.Read(startIndex, startOffset, buffer, 0, count) == count;
                    }

                    var position = 0;
                    for (var i = startIndex; i <= endIndex; i++)
                    {
                        if (i == startIndex)
                        {
                            var startOffset = (int)(filePosition % _segmentLength);
                            var length = _segmentLength - startOffset;
                            if (StorageContainer.Read(i, startOffset, buffer, 0, length) != length)
                                return false;
                            position += length;
                        }
                        else if (i == endIndex)
                        {
                            var length = (int)((filePosition + count) % _segmentLength);
                            if (StorageContainer.Read(i, 0, buffer, position, length) != length)
                                return false;
                        }
                        else
                        {
                            // copy whole segment
                            if (StorageContainer.Read(i, 0, buffer, position, _segmentLength) != _segmentLength)
                                return false;
                            position += _segmentLength;
                        }
                    }
                }
            }
            catch (Exception x)
            {
                Logger.Error("Exception when reading data: {0}", x);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Changes segment length for the download
        /// Could only be set before download starts
        /// </summary>
        /// <param name="length"></param>
        public void SetSegmentLength(int length)
        {
            lock (_syncRoot)
            {
                if (_doneSegmentsCount > 0 || _activeSegmentsCount > 0)
                    throw new InvalidOperationException("You can only change the length before the download begins");

                _totalSegmentsCount = 0;
            }

            if (length < 64 * 1024)
                throw new ArgumentOutOfRangeException("length", "Length could not be less than 64Kbyte, specified " + length);

            _segmentLength = length;
        }

        /// <summary>
        /// Tries to load leaves hashes
        /// </summary>
        /// <param name="leavesData"></param>
        public bool LoadLeaves(byte[] leavesData)
        {
            if (leavesData == null) 
                throw new ArgumentNullException("leavesData");

            lock (_syncRoot)
            {
                if (HasLeaves) 
                    return true; // we already have leaves, but we should continue download

                if (leavesData.Length % 24 != 0)
                {
                    Logger.Error("Invalid leaves data, array length should be multiple to 24");
                    return false;
                }

                var leavesCount = leavesData.Length / 24;

                // restoring data structures
                var hashes = new byte[leavesCount][];
                for (int i = 0; i < leavesCount; i++)
                {
                    hashes[i] = new byte[24];
                    Array.Copy(leavesData, i * 24, hashes[i], 0, 24);
                }

                if (HashHelper.VerifyLeaves(Base32Encoding.ToBytes(Magnet.TTH), hashes))
                {
                    _leavesHashes = hashes;
                    _verifiedSegmentsCount = 0;
                    _verifiedSegments = new BitArray(leavesCount);
                    VerifySegmentLength = (int)HashHelper.GetBytePerHash(leavesCount, Magnet.Size);
                    return true;
                }

                return false;

            }
        }

        /// <summary>
        /// Converts verify to download segment indices 
        /// </summary>
        /// <param name="i">Verify segment index</param>
        /// <returns></returns>
        internal IEnumerable<int> SegmentsVerifyToDownload(int i)
        {
            if (_downloadedSegments.Count == _verifiedSegments.Count)
            {
                yield return i;
            }
            else
            {
                var multiply = Math.Max(SegmentLength, VerifySegmentLength) / Math.Min(SegmentLength, VerifySegmentLength);
                if (_verifiedSegments.Count > _downloadedSegments.Count)
                {
                    yield return i / multiply;
                }
                else
                {
                    for (var j = i * multiply; j < i * multiply + multiply; j++)
                    {
                        if (j < _downloadedSegments.Count)
                            yield return j;
                    }
                }
            }
        }

        /// <summary>
        /// Converts download to verify segments indices
        /// </summary>
        /// <param name="i">download segment index</param>
        /// <returns></returns>
        internal IEnumerable<int> SegmentsDownloadToVerify(int i)
        {
            if (_downloadedSegments.Count == _verifiedSegments.Count)
            {
                yield return i;
            }
            else
            {
                var multiply = Math.Max(SegmentLength, VerifySegmentLength) /
                               Math.Min(SegmentLength, VerifySegmentLength);
                if (_downloadedSegments.Count < _verifiedSegments.Count)
                {
                    for (int j = i * multiply; j < i * multiply + multiply; j++)
                    {
                        if (j < _verifiedSegments.Count)
                            yield return j;
                    }
                }
                else
                {
                    yield return i / multiply;
                }
            }
        }
    }

    public class SegmentVerificationEventArgs : EventArgs
    {
        public int Index { get; set; }
        public long Start { get; set; }
        public long Length { get; set; }
        public bool IsCorrect { get; set; }
        public List<Source> Sources { get; set; }
    }
}