//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.Text;
using SharpDc.Events;
using SharpDc.Logging;
using SharpDc.Messages;
using SharpDc.Structs;

namespace SharpDc.Connections
{
    public class HubConnection : TcpConnection
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private string _dataBuffer;
        private UserInfo _currentUser;
        private bool _active;

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

        public HubSettings Settings { get; set; }
        
        /// <summary>
        /// Indicates if this hub is connected and able to work
        /// </summary>
        public bool Active
        {
            get { return _active; }
            private set {
                if (_active != value)
                {
                    _active = value;
                    OnActiveStatusChanged();
                }
            }
        }

        public string RemoteAddress { get; private set; }

        #region Events

        public event EventHandler ActiveStatusChanged;

        private void OnActiveStatusChanged()
        {
            var handler = ActiveStatusChanged;
            if (handler != null) handler(this, EventArgs.Empty);
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

        public event EventHandler<SearchRequestEventArgs> SearchRequest;

        private void OnSearchRequest(SearchRequestEventArgs e)
        {
            var handler = SearchRequest;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// Occurs when some users in passive asks our address to establish connection
        /// We need to respond with ConnectToMe message
        /// </summary>
        public event EventHandler<IncomingConnectionRequestEventArgs> IncomingConnectionRequest;

        private void OnIncomingConnectionRequest(IncomingConnectionRequestEventArgs e)
        {
            var handler = IncomingConnectionRequest;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// Occurs when some users ask to connect to him
        /// We need to establish connection with address specified
        /// </summary>
        public event EventHandler<OutgoingConnectionRequestEventArgs> OutgoingConnectionRequest;

        private void OnOutgoingConnectionRequest(OutgoingConnectionRequestEventArgs e)
        {
            var handler = OutgoingConnectionRequest;
            if (handler != null) handler(this, e);
        }

        #endregion

        public HubConnection(HubSettings settings) : base(settings.HubAddress){
            Settings = settings;
            _currentUser.Nickname = settings.Nickname;
            ConnectionStatusChanged += HubConnectionConnectionStatusChanged;
        }

        void HubConnectionConnectionStatusChanged(object sender, ConnectionStatusEventArgs e)
        {
            if (e.Status == ConnectionStatus.Connected)
                RemoteAddress = RemoteEndPoint.ToString();

            if (e.Status == ConnectionStatus.Disconnected)
                Active = false;
        }

        protected override void ParseRaw(byte[] buffer, int length)
        {
            var received = Encoding.Default.GetString(buffer, 0, length);

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

                if(cmd[0] == '$')
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
                                    var ea = new OutgoingConnectionRequestEventArgs{ Message = arg };

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
                                        SendMessage(new ConnectToMeMessage { Nickname = arg.SenderNickname , Address = ea.LocalAddress }.Raw);
                                    }

                                }
                                break;
                        }
                    }
                    catch (Exception x)
                    {
                        Logger.Error("Error when trying to parse a command: " + x.Message );
                    }
                }
                else
                {
                    // chat message
                }

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
            SendAsync(message+"|");
        }
        
        private void OnMessageLock(ref LockMessage lockMsg)
        {
            if (lockMsg.ExtendedProtocol)
            {
                SendMessage(new SupportsMessage { NoHello = true, NoGetINFO = true }.Raw);
            }

            SendMessage(lockMsg.CreateKey().Raw);
            SendMessage(new ValidateNickMessage { Nick = _currentUser.Nickname }.Raw);
        }

        private void OnMessageHubName(ref HubNameMessage arg)
        {
 	        
        }

        private void OnMessageHello(ref HelloMessage helloMessage)
        {
            var myInfo = new MyINFOMessage { 
                Nickname = _currentUser.Nickname,
                Tag = string.Format("<{0},M:{1},H:{2},S:{3}{4}>", TagInfo.Version, Settings.PassiveMode ? "P":"A", "0/0/0", "100", string.IsNullOrEmpty(TagInfo.City)?"":",C:"+TagInfo.City ), 
                Connection=TagInfo.Connection, 
                Flag = TagInfo.Flag
            };

            SendMessage(new VersionMessage().Raw);
            if(Settings.GetUsersList)
                SendMessage(new GetNickListMessage().Raw);
            SendMessage(myInfo.Raw);

            if (!Settings.GetUsersList)
            {
                // tell everybody that we are ready to work
                Active = true;
            }
        }
    }

    public class OutgoingConnectionRequestEventArgs : EventArgs
    {
        public ConnectToMeMessage Message { get; set; }
    }

    public class IncomingConnectionRequestEventArgs : EventArgs
    {
        public RevConnectToMeMessage Message { get; set; }

        /// <summary>
        /// Set this field to our local address, if null or empty connection will not be established
        /// </summary>
        public string LocalAddress { get; set; }
    }

    public class SearchRequestEventArgs : EventArgs
    {
        public SearchMessage Message { get; set; }
    }

    public struct HubSettings
    {
        public string HubAddress;
        public bool GetUsersList;
        public string HubName;
        public string Nickname;

        /// <summary>
        /// Mode of the hub
        /// </summary>
        public bool PassiveMode;

        /// <summary>
        /// Address and port to connect other users to
        /// </summary>
        public string LocalEndPoint;
    }
}
