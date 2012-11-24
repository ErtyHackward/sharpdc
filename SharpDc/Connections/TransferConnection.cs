//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.Net.Sockets;
using System.Text;
using SharpDc.Events;
using SharpDc.Logging;
using SharpDc.Managers;
using SharpDc.Messages;
using SharpDc.Structs;

namespace SharpDc.Connections
{
    /// <summary>
    /// Represents a DC transfer
    /// </summary>
    public class TransferConnection : TcpConnection
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private int _segmentPosition;
        private Source _source;
        private SegmentInfo _segmentInfo;
        private string _tail;
        private Encoding _encoding = Encoding.Default;
        private bool _binaryMode;
        private DirectionMessage _userDirection;
        private int _ourNumer;
        private bool _disposed;
        private byte[] _readBuffer;

        private readonly object _disposeSync = new object();

        private TransferDirection _direction;

        public TransferDirection Direction
        {
            get { return _direction; }
        }

        public DownloadItem DownloadItem { get; set; }

        public UploadItem UploadItem { get; set; }

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

        #region Events
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

        public event EventHandler<UploadItemNeededEventArgs> UploadItemNeeded;

        private void OnUploadItemNeeded(UploadItemNeededEventArgs e)
        {
            var handler = UploadItemNeeded;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<MessageEventArgs> IncomingMessage;

        private void OnIncomingMessage(MessageEventArgs e)
        {
            var handler = IncomingMessage;
            if (handler != null) handler(this, e);
        }

        public event EventHandler<MessageEventArgs> OutgoingMessage;

        private void OnOutgoingMessage(MessageEventArgs e)
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

            foreach (var message in FirstMessages)
            {
                SendMessage(message);
            }
            
        }

        private void ParseBinary(byte[] buffer, int length)
        {
            if (_tail != null)
            {
                var tailBytes = _encoding.GetBytes(_tail);
                var newBuffer = new byte[tailBytes.Length + length];

                Buffer.BlockCopy(tailBytes, 0, newBuffer, 0, tailBytes.Length);
                Buffer.BlockCopy(buffer, 0, newBuffer, tailBytes.Length, length);

                length = length + tailBytes.Length;
                buffer = newBuffer;
                _tail = null;
            }

            if (DownloadItem.StorageContainer.WriteData(_segmentInfo, _segmentPosition, buffer, length))
            {
                _segmentPosition += length;

                // segment finished
                if (_segmentPosition >= _segmentInfo.Length)
                {
                    _binaryMode = false;
                    // tell that it is finished
                    DownloadItem.FinishSegment(_segmentInfo.Index, Source);

                    if (SegmentCompleted != null)
                    {
                        var ea = new TransferSegmentCompletedEventArgs();
                        OnSegmentCompleted(ea);

                        if (ea.Pause)
                            return;
                    }

                    // request another segment
                    if (DownloadItem.TakeFreeSegment(_source, out _segmentInfo))
                    {
                        _segmentPosition = 0;
                    }
                    else
                    {
                        //request another download item
                        if (!GetNewDownloadItem())
                        {
                            // nothing to download more 
                            Dispose();
                            return;
                        }
                        if (DownloadItem.TakeFreeSegment(_source, out _segmentInfo))
                        {
                            _segmentPosition = 0;
                        }
                        else
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
                SendMessage(new ErrorMessage { Error = "Sorry I can't write your data" }.Raw);
                Dispose();
            }
        }

        private void ReleaseSegment()
        {
            if (_segmentInfo.Index != -1)
            {
                DownloadItem.CancelSegment(_segmentInfo.Index, Source);
                _segmentInfo.Index = -1;
                _segmentInfo.Length = -1;
                _segmentInfo.StartPosition = -1;
            }
        }

        public bool GetNewDownloadItem()
        {
            var ea = new DownloadItemNeededEventArgs();
            OnDownloadItemNeeded(ea);
            DownloadItem = ea.DownloadItem;
            return DownloadItem != null;
        }

        public void RequestSegment()
        {
            if (_segmentInfo.Index == -1)
                throw new InvalidOperationException();

            SendMessage(new ADCGETMessage { 
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
            if (DownloadItem.TakeFreeSegment(_source, out _segmentInfo))
            {
                _segmentPosition = 0;
                RequestSegment();
            }
        }

        private void SendMessage(string msg)
        {
            if (OutgoingMessage != null)
            {
                var ea = new MessageEventArgs{ Message = msg };
                OnOutgoingMessage(ea);
            }
            SendAsync(msg + "|");
        }

        protected override void ParseRaw(byte[] buffer, int length)
        {
            if (_disposed) return;

            if (_binaryMode)
            {
                ParseBinary(buffer, length);
                return;
            }

            var received = _encoding.GetString(buffer, 0, length);

            if (!string.IsNullOrEmpty(_tail))
            {
                received = _tail + received;
                _tail = null;
            }

            int currentIndex = 0;
            string command;
            while (currentIndex < received.Length)
            {
                //Debug.WriteLine("RCV:" + received);
                var cmdEnd = received.IndexOf('|', currentIndex);

                if (cmdEnd == -1)
                {
                    _tail = received.Substring(currentIndex);

                    //Trace.WriteLine(string.Format("length: {0} current: {1} strlen: {2}", length, currentIndex, received.Length));
                    //Trace.WriteLine("TAILED:" + received);
                    //Trace.WriteLine("TAILEDUTF8:" + Encoding.UTF8.GetString(buffer,0, length));
                    //Trace.WriteLine("TAIL:" + _tail);
                    return;
                }
                else
                {
                    command = received.Substring(currentIndex, cmdEnd - currentIndex);
                    currentIndex = cmdEnd + 1;
                }

                if (IncomingMessage != null)
                {
                    OnIncomingMessage(new MessageEventArgs { Message = command });
                }

                if (command[0] == '$')
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
                                    if(currentIndex != length)
                                        _tail = received.Substring(currentIndex);

                                    _binaryMode = true;
                                    return;
                                }

                                Dispose();
                            }
                            break;
                        case "$ADCGET":
                            {
                                var arg = ADCGETMessage.Parse(command);
                                OnMessageAdcget(ref arg);
                            }
                            break;
                    }
                }
            }
        }

        private void OnMessageAdcget(ref ADCGETMessage adcgetMessage)
        {
            var reqItem = new ContentItem();

            if (adcgetMessage.Type == ADCGETType.Tthl)
            {
                SendMessage(new ErrorMessage { Error = "File Not Available" }.Raw);
                return;
            }
            
            if (adcgetMessage.Type == ADCGETType.File)
            {
                if (adcgetMessage.Request.StartsWith("TTH/"))
                {
                    reqItem.Magnet = new Magnet { TTH = adcgetMessage.Request.Remove(0,4) };
                }
                else
                {
                    reqItem.Magnet = new Magnet { FileName = adcgetMessage.Request };
                }
            }

            if (UploadItem == null || UploadItem.Content.Magnet.TTH != reqItem.Magnet.TTH)
            {
                var ea = new UploadItemNeededEventArgs { Content = reqItem };
                OnUploadItemNeeded(ea);

                if (UploadItem != null)
                {
                    UploadItem.Dispose();
                }

                UploadItem = ea.UploadItem;
                if (ea.UploadItem == null)
                {
                    SendMessage(new ErrorMessage { Error = "File Not Available" }.Raw);
                    return;
                }
            }

            // TODO: check for slots

            if (_readBuffer == null)
            {
                _readBuffer = new byte[1024*100];
            }

            if (adcgetMessage.Start + adcgetMessage.Length > UploadItem.Content.Magnet.Size)
                adcgetMessage.Length = UploadItem.Content.Magnet.Size - adcgetMessage.Start;

            for (var position = adcgetMessage.Start; !_disposed && position< adcgetMessage.Start + adcgetMessage.Length; position += _readBuffer.Length)
            {
                if (position == adcgetMessage.Start)
                    SendMessage(new ADCSNDMessage{ Type = ADCGETType.File, Request = adcgetMessage.Request, Start = adcgetMessage.Start, Length = adcgetMessage.Length}.Raw);

                var length = _readBuffer.Length;

                if (adcgetMessage.Start + adcgetMessage.Length < position + length)
                    length = (int)(adcgetMessage.Start + adcgetMessage.Length - position);

                var read = UploadItem.Read(_readBuffer, position, length);

                if (read != length)
                {
                    Logger.Error("File read error ({0},{1}): {2}", read, length, UploadItem.Content.SystemPath);
                    Dispose();
                    return;
                }

                SendRaw(_readBuffer, read);
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

                if (DownloadItem.TakeFreeSegment(_source, out _segmentInfo))
                {
                    _segmentPosition = 0;
                    // we won, request a segment
                    RequestSegment();
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
                    e = new TransferErrorEventArgs { ErrorType = TransferErrors.Unknown, Exception = new Exception(msg.Error) };
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
        }

        private void OnMessageLock(ref LockMessage lockMessage)
        {
            if (lockMessage.ExtendedProtocol)
            {
                SendMessage(new SupportsMessage { ADCGet = true, TTHF = true, TTHL = true }.Raw);
            }

            var r = new Random();
            _ourNumer = r.Next(0,32768);
            SendMessage(new DirectionMessage { Download = GetNewDownloadItem(), Number = _ourNumer }.Raw);
            SendMessage(lockMessage.CreateKey().Raw);
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
                SendMessage(new MyNickMessage {Nickname = ea.OwnNickname}.Raw);
                SendMessage(new LockMessage().Raw);
            }

        }

        public override void Dispose()
        {
            _disposed = true;
            _readBuffer = null;
            ReleaseSegment();
            DownloadItem = null;
            if (UploadItem != null)
            {
                UploadItem.Dispose();
                UploadItem = null;
            }

            DisconnectAsync();
        }
    }

    public enum TransferDirection
    {
        NotSet,
        Download,
        Upload
    }

    public class UploadItemNeededEventArgs : EventArgs
    {
        public ContentItem Content { get; set; }
        public UploadItem UploadItem { get; set; }
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
