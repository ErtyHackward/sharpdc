﻿// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Text;
using SharpDc.Events;
using SharpDc.Logging;
using SharpDc.Messages;
using SharpDc.Structs;

namespace SharpDc.Connections
{
    public class HubConnection : TcpConnection, INotifyOnSend
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private string _dataBuffer;
        private UserInfo _currentUser;
        private bool _active;
        private HubSettings _settings;
        private MyINFOMessage _prevMessage;
        private string _lastChatMessage;
        private Encoding _encoding = Encoding.Default;

        /// <summary>
        /// Contains public information of this client
        /// </summary>
        public TagInfo TagInfo { get; set; }

        /// <summary>
        /// Gets or sets information about current user
        /// </summary>
        public UserInfo CurrentUser
        {
            get { return _currentUser; }
            set { _currentUser = value; }
        }

        public HubSettings Settings
        {
            get { return _settings; }
            set
            {
                _settings = value;
                _currentUser.Nickname = _settings.Nickname;
            }
        }

        /// <summary>
        /// Indicates if this hub is connected and able to work
        /// </summary>
        public bool Active
        {
            get { return _active; }
            private set
            {
                if (_active != value)
                {
                    _active = value;
                    OnActiveStatusChanged();
                }
            }
        }

        public string RemoteAddressString { get; private set; }

        public ConcurrentDictionary<string, UserInfo> Users { get; private set; }

        public Encoding Encoding
        {
            get { return _encoding; }
            set { _encoding = value; }
        }

        #region Events

        public event EventHandler ActiveStatusChanged;

        private void OnActiveStatusChanged()
        {
            ActiveStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler<MessageEventArgs> IncomingMessage;

        private void OnIncomingMessage(MessageEventArgs e)
        {
            IncomingMessage?.Invoke(this, e);
        }

        public event EventHandler<MessageEventArgs> OutgoingMessage;

        public bool NotificationsEnabled
        {
            get { return OutgoingMessage != null; }
        }

        public void OnOutgoingMessage(MessageEventArgs e)
        {
            OutgoingMessage?.Invoke(this, e);
        }

        public event EventHandler<SearchRequestEventArgs> SearchRequest;

        private void OnSearchRequest(SearchRequestEventArgs e)
        {
            SearchRequest?.Invoke(this, e);
        }

        /// <summary>
        /// Occurs when some users in passive asks our address to establish connection
        /// We need to respond with ConnectToMe message
        /// </summary>
        public event EventHandler<IncomingConnectionRequestEventArgs> IncomingConnectionRequest;

        private void OnIncomingConnectionRequest(IncomingConnectionRequestEventArgs e)
        {
            IncomingConnectionRequest?.Invoke(this, e);
        }

        /// <summary>
        /// Occurs when some users ask to connect to him
        /// We need to establish connection with address specified
        /// </summary>
        public event EventHandler<OutgoingConnectionRequestEventArgs> OutgoingConnectionRequest;

        private void OnOutgoingConnectionRequest(OutgoingConnectionRequestEventArgs e)
        {
            OutgoingConnectionRequest?.Invoke(this, e);
        }

        /// <summary>
        /// Occurs when no password is set and it is requested by the hub
        /// </summary>
        public event EventHandler<PasswordRequiredEventArgs> PasswordRequired;

        private void OnPasswordRequired(PasswordRequiredEventArgs e)
        {
            PasswordRequired?.Invoke(this, e);
        }

        public event EventHandler<SearchResultEventArgs> PassiveSearchResult;

        protected void OnPassiveSearchResult(SearchResultEventArgs e)
        {
            PassiveSearchResult?.Invoke(this, e);
        }

        /// <summary>
        /// Occurs when the hub sends us our own external ip address
        /// Read it from CurrentUser.IP property
        /// </summary>
        public event EventHandler OwnIpReceived;

        protected void OnOwnIpReceived()
        {
            OwnIpReceived?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Occurs when new chat message is arrived
        /// </summary>
        public event EventHandler<ChatMessageEventArgs> ChatMessage;

        protected virtual void OnChatMessage(ChatMessageEventArgs e)
        {
            ChatMessage?.Invoke(this, e);
        }

        #endregion

        public HubConnection(HubSettings settings) : base(settings.HubAddress)
        {
            Users = new ConcurrentDictionary<string, UserInfo>();
            Settings = settings;
            ConnectionStatusChanged += HubConnectionConnectionStatusChanged;
        }

        private void HubConnectionConnectionStatusChanged(object sender, ConnectionStatusEventArgs e)
        {
            switch (e.Status)
            {
                case ConnectionStatus.Connecting:
                    _lastChatMessage = null;
                    break;
                case ConnectionStatus.Connected:
                    RemoteAddressString = RemoteEndPoint.ToString();
                    break;
                case ConnectionStatus.Disconnected:
                    _prevMessage = default(MyINFOMessage);
                    if (!string.IsNullOrEmpty(_lastChatMessage))
                        Logger.Info("Last hub chat message: {0}", _lastChatMessage);
                    Active = false;
                    break;
            }
        }

        protected override void ParseRaw(byte[] buffer, int offset, int length)
        {
            var received = _encoding.GetString(buffer, offset, length);

            if (!string.IsNullOrEmpty(_dataBuffer))
            {
                received = _dataBuffer + received;
                _dataBuffer = null;
            }

            var commands = received.Split('|');
            var processCount = commands.Length;

            if (!received.EndsWith("|"))
            {
                _dataBuffer = received.Substring(received.LastIndexOf('|') + 1);
                processCount--;
            }

            for (int i = 0; i < processCount; i++)
            {
                var cmd = commands[i];

                if (cmd.Length == 0) continue;

                if (IncomingMessage != null)
                {
                    OnIncomingMessage(new MessageEventArgs { Message = cmd });
                }

                if (cmd[0] == '$')
                {
                    // command
                    var spaceIndex = cmd.IndexOf(' ');
                    var cmdName = spaceIndex == -1 ? cmd : cmd.Substring(0, spaceIndex);

                    try
                    {
                        switch (cmdName)
                        {
                            case "$Hello":
                                {
                                    var arg = HelloMessage.Parse(cmd);
                                    OnMessageHello(ref arg);
                                }
                                break;
                            case "$HubName":
                                {
                                    var arg = HubNameMessage.Parse(cmd);
                                    OnMessageHubName(ref arg);
                                }
                                break;
                            case "$Lock":
                                {
                                    var arg = LockMessage.Parse(cmd);
                                    OnMessageLock(ref arg);
                                }
                                break;
                            case "$Search":
                                {
                                    var arg = SearchMessage.Parse(cmd);
                                    OnMessageSearch(ref arg);
                                }
                                break;
                            case "$ConnectToMe":
                                {
                                    var arg = ConnectToMeMessage.Parse(cmd);
                                    var ea = new OutgoingConnectionRequestEventArgs { Message = arg };

                                    OnOutgoingConnectionRequest(ea);
                                }
                                break;
                            case "$RevConnectToMe":
                                {
                                    var arg = RevConnectToMeMessage.Parse(cmd);
                                    var ea = new IncomingConnectionRequestEventArgs { Message = arg };
                                    OnIncomingConnectionRequest(ea);

                                    if (!string.IsNullOrEmpty(ea.LocalAddress))
                                    {
                                        SendMessage(
                                            new ConnectToMeMessage
                                                {
                                                    RecipientNickname = arg.SenderNickname,
                                                    SenderAddress = ea.LocalAddress
                                                }.Raw);
                                    }
                                }
                                break;
                            case "$GetPass":
                                {
                                    OnMessageGetPass();
                                }
                                break;
                            case "$MyINFO":
                                {
                                    var arg = MyINFOMessage.Parse(cmd);
                                    OnMessageMyINFO(arg);
                                }
                                break;
                            case "$Quit":
                                {
                                    var arg = QuitMessage.Parse(cmd);
                                    OnMessageQuit(arg);
                                }
                                break;
                            case "$SR":
                                {
                                    var arg = SRMessage.Parse(cmd);
                                    OnMessageSR(arg);
                                }
                                break;
                            case "$UserIP":
                                {
                                    var arg = UserIPMessage.Parse(cmd);
                                    OnUserIPMessage(arg);
                                }
                                break;
                        }
                    }
                    catch (Exception x)
                    {
                        Logger.Error("Error when trying to parse a command: " + x.Message);
                    }
                }
                else
                {
                    // chat message
                    _lastChatMessage = cmd;

                    OnChatMessage(new ChatMessageEventArgs { RawMessage = cmd });
                }
            }
        }

        private void OnUserIPMessage(UserIPMessage userIpMessage)
        {
            foreach (var pair in userIpMessage.UsersIp)
            {
                var ui = new UserInfo();
                ui.Nickname = pair.Key;
                var ip = pair.Value;

                Users.AddOrUpdate(pair.Key, ui, (key, prev) =>
                                                    {
                                                        var u = prev;
                                                        u.IP = ip;
                                                        return u;
                                                    });

                if (pair.Key == CurrentUser.Nickname)
                {
                    var userInfo = CurrentUser;
                    userInfo.IP = pair.Value;
                    CurrentUser = userInfo;
                    OnOwnIpReceived();
                }
            }
        }

        private void OnMessageSR(SRMessage srMessage)
        {
            OnPassiveSearchResult(new SearchResultEventArgs { Message = srMessage });
        }

        private void OnMessageQuit(QuitMessage quitMessage)
        {
            UserInfo user;
            if (Users.TryRemove(quitMessage.Nickname, out user))
            {
                // TODO: fire event here
            }
        }

        private void OnMessageMyINFO(MyINFOMessage arg)
        {
            var ui = new UserInfo(arg);
            Users.AddOrUpdate(arg.Nickname, ui, (key, prev) => ui);

            if (ui.Nickname == CurrentUser.Nickname)
                Active = true;
        }

        private void OnMessageGetPass()
        {
            if (string.IsNullOrEmpty(_settings.Password))
            {
                var ea = new PasswordRequiredEventArgs();
                OnPasswordRequired(ea);
                _settings.Password = ea.Password;
            }

            if (!string.IsNullOrEmpty(_settings.Password))
            {
                SendMessage(new MyPassMessage { Password = _settings.Password }.Raw);
                Logger.Info("Password sent...");
            }
            else
            {
                Logger.Error("No password is supplied. Unable to log in.");
            }
        }

        private void OnMessageSearch(ref SearchMessage searchMessage)
        {
            OnSearchRequest(new SearchRequestEventArgs { Message = searchMessage });
        }

        public void SendMessage(string message)
        {
            if (OutgoingMessage != null)
            {
                OnOutgoingMessage(new MessageEventArgs { Message = message });
            }
            SendQueued(message + "|");
        }

        private void OnMessageLock(ref LockMessage lockMsg)
        {
            using (var transaction = new SendTransaction(this))
            {
                if (lockMsg.ExtendedProtocol)
                {
                    transaction.Send(new SupportsMessage { NoHello = true, NoGetINFO = true, UserIP2 = true }.Raw);
                }

                transaction.Send(lockMsg.CreateKey().Raw);
                transaction.Send(new ValidateNickMessage { Nick = _currentUser.Nickname }.Raw);
            }
        }

        private void OnMessageHubName(ref HubNameMessage arg)
        {

        }

        private void OnMessageHello(ref HelloMessage helloMessage)
        {
            using (var transaction = new SendTransaction(this))
            {
                transaction.Send(new VersionMessage().Raw);
                if (Settings.GetUsersList)
                    transaction.Send(new GetNickListMessage().Raw);
                SendMyINFO(transaction);
            }

            if (!Settings.GetUsersList)
            {
                // tell everybody that we are ready to work
                Active = true;
            }
        }

        public void SendMyINFO(SendTransaction transaction = null)
        {
            var myInfo = new MyINFOMessage
            {
                Nickname = _currentUser.Nickname,
                Tag =
                    string.Format("<{0},M:{1},H:{2},S:{3}{4}>", TagInfo.Version,
                                  Settings.PassiveMode ? "P" : "A", "0/0/0", "100",
                                  string.IsNullOrEmpty(TagInfo.City) ? "" : ",C:" + TagInfo.City),
                Connection = TagInfo.Connection,
                Flag = TagInfo.Flag,
                Share = _settings.FakeShare == 0 ? _currentUser.Share : _settings.FakeShare
            };

            if (!myInfo.Equals(_prevMessage))
            {
                if (transaction == null)
                    SendMessage(myInfo.Raw);
                else
                    transaction.Send(myInfo.Raw);
            }

            _prevMessage = myInfo;
        }

        public void KeepAlive()
        {
            SendMessage("");
        }
    }

    public class ChatMessageEventArgs : EventArgs
    {
        public string RawMessage { get; set; }
    }
}