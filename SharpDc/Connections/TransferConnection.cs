// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using SharpDc.Events;
using SharpDc.Helpers;
using SharpDc.Logging;
using SharpDc.Managers;
using SharpDc.Messages;
using SharpDc.Structs;

namespace SharpDc.Connections
{
    /// <summary>
    /// Represents a DC transfer
    /// </summary>
    public class TransferConnection : TcpConnection, INotifyOnSend
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly object _syncRoot = new object();

        private Source _source;
        private SegmentInfo _segmentInfo;
        private byte[] _tail;
        private Encoding _encoding = Encoding.Default;
        private bool _binaryMode;
        private DirectionMessage _userDirection;
        private int _ourNumer;
        private bool _disposed;
        private bool _isResponding;

        private TransferDirection _direction;
        private UploadItem _uploadItem;
        private static SpeedAverage _requests = new SpeedAverage();
        private static SpeedAverage _fails = new SpeedAverage();

        public static MovingAverage ServiceTime = new MovingAverage(TimeSpan.FromSeconds(30));

        public static int AVGRequestsPerSecond
        {
            get { return (int)_requests.GetSpeed(); }
        }

        public static int AVGFailsPerSecond
        {
            get { return (int)_fails.GetSpeed(); }
        }

        internal bool UseBackgroundSeedMode { get; set; }

        /// <summary>
        /// Indicates that this connection uses one slot
        /// </summary>
        public bool SlotUsed { get; private set; }

        public TransferDirection Direction
        {
            get { return _direction; }
        }

        public DownloadItem DownloadItem { get; set; }

        public UploadItem UploadItem
        {
            get { return _uploadItem; }
            set { 
                if (_uploadItem == value)
                    return;

                var ea = new UploadItemChangedEventArgs
                {
                    UploadItem = value,
                    PreviousItem = _uploadItem
                };

                _uploadItem = value;
                OnUploadItemChanged(ea);
            }
        }

        public string[] FirstMessages { get; set; }

        public Encoding Encoding
        {
            get { return _encoding; }
            set { _encoding = value; }
        }

        public Source Source
        {
            get { return _source; }
            set { _source = value; }
        }

        public SegmentInfo SegmentInfo
        {
            get { return _segmentInfo; }
            set { _segmentInfo = value; }
        }

        /// <summary>
        /// Gets or sets if the connection should be established 
        /// without check in allow list
        /// </summary>
        public bool AllowedToConnect { get; set; }

        #region Events

        public event EventHandler<UploadItemChangedEventArgs> UploadItemChanged;

        protected virtual void OnUploadItemChanged(UploadItemChangedEventArgs e)
        {
            var handler = UploadItemChanged;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<TransferSegmentCompletedEventArgs> SegmentCompleted;

        private void OnSegmentCompleted(TransferSegmentCompletedEventArgs e)
        {
            var handler = SegmentCompleted;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<DownloadItemNeededEventArgs> DownloadItemNeeded;

        private void OnDownloadItemNeeded(DownloadItemNeededEventArgs e)
        {
            var handler = DownloadItemNeeded;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// Handle this event to change upload item creation
        /// </summary>
        public event EventHandler<UploadItemEventArgs> UploadItemNeeded;

        private void OnUploadItemNeeded(UploadItemEventArgs e)
        {
            e.Transfer = this;
            var handler = UploadItemNeeded;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// Handle this event to customize UploadItem disposing
        /// </summary>
        public event EventHandler<UploadItemEventArgs> UploadItemDispose;

        protected virtual void OnUploadItemDispose(UploadItemEventArgs e)
        {
            e.Transfer = this;
            var handler = UploadItemDispose;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<MessageEventArgs> IncomingMessage;

        private void OnIncomingMessage(MessageEventArgs e)
        {
            var handler = IncomingMessage;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<MessageEventArgs> OutgoingMessage;

        public bool NotificationsEnabled
        {
            get { return OutgoingMessage != null; }
        }

        public void OnOutgoingMessage(MessageEventArgs e)
        {
            var handler = OutgoingMessage;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<TransferAuthorizationEventArgs> Authorization;

        private void OnAuthorization(TransferAuthorizationEventArgs e)
        {
            var handler = Authorization;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<TransferDirectionChangedEventArgs> DirectionChanged;

        private void OnDirectionChanged(TransferDirectionChangedEventArgs e)
        {
            e.Previous = _direction;
            _direction = e.Download ? TransferDirection.Download : TransferDirection.Upload;
            var handler = DirectionChanged;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<TransferErrorEventArgs> Error;

        protected void OnError(TransferErrorEventArgs e)
        {
            var hander = Error;
            if (hander != null) hander(this, e);
        }

        protected bool OnError(TransferErrors errorType)
        {
            var e = new TransferErrorEventArgs { ErrorType = errorType };
            OnError(e);
            return e.Handled;
        }

        /// <summary>
        /// Occurs when transfer want to upload something to a user
        /// Allows to cancel upload
        /// </summary>
        public event EventHandler<CancelEventArgs> SlotRequest;

        protected virtual void OnSlotRequest(CancelEventArgs e)
        {
            var handler = SlotRequest;
            if (handler != null) handler(this, e);
        }

        #endregion

        public TransferConnection(string address)
            : base(address)
        {
            _segmentInfo = new SegmentInfo { Index = -1, Length = -1, StartPosition = -1 };
        }

        public TransferConnection(Socket socket) : base(socket)
        {
            _segmentInfo = new SegmentInfo { Index = -1, Length = -1, StartPosition = -1 };
        }

        protected override void SendFirstMessages()
        {
            if (FirstMessages == null || FirstMessages.Length == 0)
                return;

            using (var transaction = new SendTransaction(this))
            foreach (var message in FirstMessages)
            {
                transaction.Send(message);
            }
        }

        private void ParseBinary(byte[] buffer, int offset, int length)
        {
            if (_segmentInfo.Length < _segmentInfo.Position + length)
            {
                var writeLength = (int)(_segmentInfo.Length - _segmentInfo.Position);
                _tail = new byte[length - writeLength];
                Buffer.BlockCopy(buffer, offset + writeLength, _tail, 0, _tail.Length);
                var extra = length - writeLength;
                length = writeLength;
                Logger.Warn("Received extra data in parse binary len={0}", extra);
            }

            if (DownloadItem.StorageContainer.WriteData(_segmentInfo, _segmentInfo.Position, buffer, offset, length))
            {
                _segmentInfo.Position += length;

                // segment finished
                if (_segmentInfo.Position >= _segmentInfo.Length)
                {
                    _binaryMode = false;
                    
                    FinishSegment();

                    if (SegmentCompleted != null)
                    {
                        var ea = new TransferSegmentCompletedEventArgs();
                        OnSegmentCompleted(ea);

                        if (ea.Pause)
                            return;
                    }

                    // request another segment
                    if (!TakeSegment())
                    {
                        //request another download item
                        if (!GetNewDownloadItem())
                        {
                            // nothing to download more 
                            Dispose();
                            return;
                        }

                        if (!TakeSegment())
                        {
                            Dispose();
                            return;
                        }
                    }

                    RequestSegment();
                }
            }
            else
            {
                Dispose();
            }
        }

        private bool TakeSegment()
        {
            lock (_syncRoot)
            {
                if (_closingSocket || _disposed)
                    return false;

                if (_segmentInfo.Index != -1)
                    throw new InvalidOperationException();

                return DownloadItem.TakeFreeSegment(_source, out _segmentInfo);
            }
        }

        private void ReleaseSegment()
        {
            lock (_syncRoot)
            {
                if (_segmentInfo.Index != -1)
                {
                    DownloadItem.CancelSegment(_segmentInfo.Index, Source);
                    _segmentInfo.Index = -1;
                    _segmentInfo.Length = -1;
                    _segmentInfo.StartPosition = -1;
                }
            }
        }

        public void FinishSegment()
        {
            lock (_syncRoot)
            {
                DownloadItem.FinishSegment(_segmentInfo.Index, Source);
                _segmentInfo.Index = -1;
            }
        }

        public bool GetNewDownloadItem()
        {
            if (_closingSocket || _disposed)
            {
                return false;
            }

            var ea = new DownloadItemNeededEventArgs();
            OnDownloadItemNeeded(ea);
            DownloadItem = ea.DownloadItem;
            return DownloadItem != null;
        }

        public void RequestSegment()
        {
            if (_segmentInfo.Index == -1)
            {
                Logger.Error("No segment to request");
                throw new InvalidOperationException();
            }



            SendMessageAsync(new ADCGETMessage
                            {
                                Type = ADCGETType.File,
                                Start = _segmentInfo.StartPosition,
                                Length = _segmentInfo.Length,
                                Request = "TTH/" + DownloadItem.Magnet.TTH
                            }.Raw);
        }

        /// <summary>
        /// Allows to continue a paused download
        /// </summary>
        public void ResumeDownload()
        {
            // request a segment
            if (TakeSegment())
            {
                RequestSegment();
            }
        }

        private void SendMessageAsync(string msg)
        {
            if (OutgoingMessage != null)
            {
                var ea = new MessageEventArgs { Message = msg };
                OnOutgoingMessage(ea);
            }

            SendAsync(msg + "|").NoWarning();
        }
        
        protected override void ParseRaw(byte[] buffer, int offset, int length)
        {
            if (_disposed)
                return;

            if (_tail != null)
            {
                var newBuffer = new byte[_tail.Length + length];

                Buffer.BlockCopy(_tail, 0, newBuffer, 0, _tail.Length);
                Buffer.BlockCopy(buffer, offset, newBuffer, _tail.Length, length);

                length = length + _tail.Length;
                buffer = newBuffer;
                offset = 0;
                _tail = null;
            }

            if (_binaryMode)
            {
                ParseBinary(buffer, offset, length);
                return;
            }
            
            int cmdEndIndex = offset;

            while (true)
            {
                var prevPos = cmdEndIndex == offset ? offset : cmdEndIndex + 1;
                cmdEndIndex = Array.IndexOf(buffer, (byte)'|', prevPos, length - (cmdEndIndex - offset));

                if (cmdEndIndex == -1)
                {
                    if (prevPos < length)
                    {
                        _tail = new byte[length - prevPos];
                        Buffer.BlockCopy(buffer, prevPos, _tail, 0, _tail.Length);
                    }

                    break;
                }

                var command = _encoding.GetString(buffer, prevPos, cmdEndIndex - prevPos);

                if (IncomingMessage != null)
                {
                    OnIncomingMessage(new MessageEventArgs { Message = command });
                }

                if (command.Length > 0 && command[0] == '$')
                {
                    // command
                    var spaceIndex = command.IndexOf(' ');
                    var cmdName = spaceIndex == -1 ? command : command.Substring(0, spaceIndex);

                    switch (cmdName)
                    {
                        case "$MyNick":
                            {
                                var arg = MyNickMessage.Parse(command);
                                OnMessageMyNick(ref arg);
                            }
                            break;
                        case "$Supports":
                            {
                                var arg = SupportsMessage.Parse(command);
                                OnMessageSupports(ref arg);
                            }
                            break;
                        case "$Lock":
                            {
                                var arg = LockMessage.Parse(command);
                                OnMessageLock(ref arg);
                            }
                            break;
                        case "$Direction":
                            {
                                var arg = DirectionMessage.Parse(command);
                                OnMessageDirection(ref arg);
                            }
                            break;
                        case "$Error":
                            {
                                var arg = ErrorMessage.Parse(command);
                                OnMessageError(ref arg);
                            }
                            break;
                        case "$Key":
                            {
                                var arg = KeyMessage.Parse(command);
                                OnMessageKey(ref arg);
                            }
                            break;
                        case "$ADCSND":
                            {
                                var arg = ADCSNDMessage.Parse(command);
                                if (OnMessageAdcsnd(ref arg))
                                {
                                    prevPos = cmdEndIndex + 1;
                                    if (prevPos < length + offset)
                                    {
                                        _tail = new byte[length - (prevPos - offset)];
                                        Buffer.BlockCopy(buffer, prevPos, _tail, 0, _tail.Length);
                                    }

                                    _binaryMode = true;
                                    return;
                                }

                                Dispose();
                            }
                            break;
                        case "$ADCGET":
                            {
                                var arg = ADCGETMessage.Parse(command);
                                OnMessageAdcget(arg);
                            }
                            break;
                    }
                }
            }
        }

        private async void OnMessageAdcget(ADCGETMessage adcgetMessage)
        {
            var reqItem = new ContentItem();

            if (adcgetMessage.Type == ADCGETType.Tthl)
            {
                SendMessageAsync(new ErrorMessage { Error = "File Not Available" }.Raw);
                return;
            }

            if (adcgetMessage.Type == ADCGETType.File)
            {
                if (adcgetMessage.Request.StartsWith("TTH/"))
                {
                    reqItem.Magnet = new Magnet { TTH = adcgetMessage.Request.Remove(0, 4) };
                }
                else
                {
                    reqItem.Magnet = new Magnet { FileName = adcgetMessage.Request };
                }
            }

            _requests.Update(1);
            _isResponding = true;

            if (!SlotUsed)
            {
                var ea = new CancelEventArgs();
                OnSlotRequest(ea);

                if (ea.Cancel)
                {
                    Logger.Info("Can't start upload to {0}, no slots available", Source);
                    SendMessageAsync(new MaxedOutMessage().Raw);
                    return;
                }

                SlotUsed = true;
            }

            if (UploadItem == null || UploadItem.Content.Magnet.TTH != reqItem.Magnet.TTH)
            {
                var ea = new UploadItemEventArgs { Transfer = this, Content = reqItem };
                OnUploadItemNeeded(ea);

                if (UploadItem != null)
                {
                    var uea = new UploadItemEventArgs();
                    OnUploadItemDispose(uea);
                    if (!uea.Handled)
                    {
                        UploadItem.Dispose();
                    }
                }

                UploadItem = ea.UploadItem;
                if (ea.UploadItem == null)
                {
                    SendMessageAsync(new ErrorMessage { Error = "File Not Available" }.Raw);
                    return;
                }
            }
            
            if (adcgetMessage.Start >= UploadItem.Content.Magnet.Size)
            {
                SendMessageAsync(new ErrorMessage { Error = "File Not Available" }.Raw);
                return;
            }

            if (adcgetMessage.Start + adcgetMessage.Length > UploadItem.Content.Magnet.Size)
            {
                Logger.Warn("Trim ADCGET length to file actual length {0}/{1}",
                            adcgetMessage.Start + adcgetMessage.Length, UploadItem.Content.Magnet.Size);
                adcgetMessage.Length = UploadItem.Content.Magnet.Size - adcgetMessage.Start;
            }


            var uploadItem = UploadItem;

            if (_disposed || uploadItem == null)
                return;

            var sw = PerfTimer.StartNew();

            await SendAsync(new ADCSNDMessage
            {
                Type = ADCGETType.File,
                Request = adcgetMessage.Request,
                Start = adcgetMessage.Start,
                Length = adcgetMessage.Length
            }.Raw + "|").ConfigureAwait(false);
            
            try
            {
                if (_disposed)
                    return;

                await uploadItem.SendChunkAsync(this, adcgetMessage.Start, adcgetMessage.Length).ConfigureAwait(false);
                Stream.Flush();
                sw.Stop();
                _isResponding = false;
                ServiceTime.Update((int)sw.ElapsedMilliseconds);
            }
            catch (Exception x)
            {
                Logger.Error("Upload read error {0} (L:{1}) {2} {3} ms",
                    x.Message,
                    adcgetMessage.Length,
                    uploadItem.Content.SystemPath,
                    sw.ElapsedMilliseconds);
                Dispose();
            }
        }
        
        private void OnMessageKey(ref KeyMessage keyMessage)
        {
            if (DownloadItem != null)
            {
                if (_userDirection.Download)
                {
                    // conflict
                    if (_userDirection.Number == _ourNumer)
                    {
                        // the same value, disconnecting
                        Logger.Error("Disconnecting because of the same value");
                        Dispose();
                        return;
                    }
                    if (_userDirection.Number > _ourNumer)
                    {
                        // we loose, allow user to download
                        DownloadItem = null;
                        OnDirectionChanged(new TransferDirectionChangedEventArgs { Download = false });
                        return;
                    }
                }

                OnDirectionChanged(new TransferDirectionChangedEventArgs { Download = true });

                if (_segmentInfo.Index != -1)
                    RequestSegment();
                else if (TakeSegment())
                {
                    // we won, request a segment
                    RequestSegment();
                }
                else
                {
                    Logger.Warn("Can't take the segment to download");
                }
            }
            else
            {
                OnDirectionChanged(new TransferDirectionChangedEventArgs { Download = false });
            }
        }

        private void OnMessageError(ref ErrorMessage msg)
        {
            TransferErrorEventArgs e;
            switch (msg.Error)
            {
                case "File Not Available":
                    e = new TransferErrorEventArgs { ErrorType = TransferErrors.FileNotAvailable };
                    break;
                default:
                    e = new TransferErrorEventArgs
                            {
                                ErrorType = TransferErrors.Unknown,
                                Exception = new Exception(msg.Error)
                            };
                    break;
            }

            //if (requestingLeaves && e.ErrorType == TransferErrors.FileNotAvailable)
            //{
            //    requestingLeaves = false;
            //    // we should try to get file without tthl
            //    Send(new ADCGET(trans, "file", trans.Content.Get(ContentInfo.REQUEST), trans.CurrentSegment.Start, trans.CurrentSegment.Length, compressedZLib));
            //}
            //else
            //{
            OnError(e);
            if (!e.Handled)
            {
                Dispose();
            }
            //}
            //Dispose();
        }

        private bool OnMessageAdcsnd(ref ADCSNDMessage adcsndMessage)
        {
            if (adcsndMessage.Type == ADCGETType.File &&
                adcsndMessage.Start == _segmentInfo.StartPosition &&
                adcsndMessage.Length == _segmentInfo.Length &&
                adcsndMessage.Request == "TTH/" + DownloadItem.Magnet.TTH)
            {
                return true;
            }
            return false;
        }

        private void OnMessageDirection(ref DirectionMessage directionMessage)
        {
            _userDirection = directionMessage;

            if (!_userDirection.Download && DownloadItem == null)
            {
                Logger.Warn("User {0} want to upload and we have no DownloadItem for him. Disconnecting.", _source.UserNickname);
                Dispose();
            }
        }

        private void OnMessageLock(ref LockMessage lockMessage)
        {
            using (var transaction = new SendTransaction(this))
            {
                if (lockMessage.ExtendedProtocol)
                {
                    transaction.Send(new SupportsMessage { ADCGet = true, TTHF = true, TTHL = true }.Raw);
                }

                var r = new Random();
                _ourNumer = r.Next(0, 32768);
                transaction.Send(new DirectionMessage { Download = GetNewDownloadItem(), Number = _ourNumer }.Raw);
                transaction.Send(lockMessage.CreateKey().Raw);
            }
        }

        private void OnMessageSupports(ref SupportsMessage supportsMessage)
        {
            //throw new NotImplementedException();
        }

        private void OnMessageMyNick(ref MyNickMessage arg)
        {
            var ea = new TransferAuthorizationEventArgs { UserNickname = arg.Nickname };

            OnAuthorization(ea);

            if (!ea.Allowed || string.IsNullOrEmpty(ea.OwnNickname))
            {
                Logger.Info(RemoteAddress + " connection is not allowed");
                Dispose();
                return;
            }

            Source = new Source { UserNickname = arg.Nickname, HubAddress = ea.HubAddress };

            if (FirstMessages == null)
            {
                using (var transaction = new SendTransaction(this))
                {
                    transaction.Send(new MyNickMessage { Nickname = ea.OwnNickname }.Raw);
                    transaction.Send(new LockMessage().Raw);
                }
            }
        }

        public override void Dispose()
        {
            if (_disposed)
                return;

            if (_isResponding)
                _fails.Update(1);

            ReleaseSegment();
            DownloadItem = null;
            if (UploadItem != null)
            {
                var ea = new UploadItemEventArgs();

                try
                {
                    OnUploadItemDispose(ea);
                }
                catch (Exception x)
                {
                    Logger.Error("Exception when disposing transfer {0} {1}", x.Message, x.StackTrace);
                }

                if (!ea.Handled)
                {
                    UploadItem.Dispose();
                    UploadItem = null;
                }
            }

            DisconnectAsync();
            _disposed = true;
        }
    }

    public class UploadItemChangedEventArgs : UploadItemEventArgs
    {
        public UploadItem Item { get; set; }

        public UploadItem PreviousItem { get; set; }
    }

    public enum TransferDirection
    {
        NotSet,
        Download,
        Upload
    }

    public class UploadItemEventArgs : BaseEventArgs
    {
        public TransferConnection Transfer { get; set; }
        public ContentItem Content { get; set; }
        public UploadItem UploadItem { get; set; }
        public Exception Exception { get; set; }
    }

    public class TransferErrorEventArgs : BaseEventArgs
    {
        public TransferErrors ErrorType { get; set; }
        public Exception Exception { get; set; }
    }

    public enum TransferErrors
    {
        Unknown = 0,
        Inactivity = 1,
        NoFreeSlots = 2,
        FileNotAvailable = 4,
        UseridMismatch = 8,
        WrongTthl = 16,
        NoMatchForRequest = 32,
        InvalidFileName,
        FileError
    }

    public class TransferDirectionChangedEventArgs : EventArgs
    {
        public TransferDirection Previous { get; set; }
        public bool Download { get; set; }
    }
}