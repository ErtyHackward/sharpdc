//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
namespace SharpDc.Structs
{
    /// <summary>
    /// Contains hub-specific settings
    /// </summary>
    public struct HubSettings
    {
        public string HubAddress;

        /// <summary>
        /// Indicates if hub should request users list
        /// It is maybe reasonable to not request the list on huge hubs
        /// </summary>
        public bool GetUsersList;

        public string HubName;
        public string Nickname;

        /// <summary>
        /// Optional user password
        /// </summary>
        public string Password;

        /// <summary>
        /// Mode of the hub
        /// </summary>
        public bool PassiveMode;

        /// <summary>
        /// Address and port to connect other users to
        /// </summary>
        public string LocalEndPoint;

        /// <summary>
        /// Allows to use fake share amount
        /// Don't use it
        /// </summary>
        public long FakeShare;
    }
}