// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;

namespace SharpDc.WebServer
{
    public class WebServerRequestEventArgs : EventArgs
    {
        public Request Request { get; set; }
        public Response Response { get; set; }

        public WebServerRequestEventArgs(Request req, Response resp)
        {
            Request = req;
            Response = resp;
        }
    }
}