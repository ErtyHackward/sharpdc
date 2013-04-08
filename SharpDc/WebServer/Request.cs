// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;

namespace SharpDc.WebServer
{
    /// <summary>
    /// WebServer Request 
    /// </summary>
    public class Request
    {
        public string RequestString { get; set; }
        public Dictionary<string, string> Headers { get; set; }
        public string Method { get; set; }
        public string URL { get; set; }
        public string Protocol { get; set; }

        public Request(NetworkStream ns)
        {
            try
            {
                Headers = new Dictionary<string, string>();
                var sr = new StreamReader(ns);

                RequestString = sr.ReadLine();
                var args = RequestString.Split(' ');
                Method = args[0];
                URL = Uri.UnescapeDataString(args[1]);
                Protocol = args[2];

                string line;
                while (!string.IsNullOrEmpty(line = sr.ReadLine()))
                {
                    int j = line.IndexOf(":");
                    if (j != -1)
                    {
                        string name = line.Substring(0, j);
                        if (!Headers.ContainsKey(name))
                            Headers.Add(name, line.Substring(j + 2));
                    }
                }
            }
            catch
            {
            }
        }
    }
}