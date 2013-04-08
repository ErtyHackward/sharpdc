// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2013-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpDc.Messages
{
    public struct UserIPMessage : IStringMessage
    {
        public List<KeyValuePair<string, string>> UsersIp;

        public string Raw
        {
            get { throw new NotImplementedException(); }
        }

        public static UserIPMessage Parse(string raw)
        {
            //$UserIP marusya 10.103.11.181$$telepuzeg 10.108.7.137$$vinogradov_a 10.108.12.167$$

            UserIPMessage msg;

            msg.UsersIp = new List<KeyValuePair<string, string>>();

            string[] tmp = raw.Substring(8).Split(new[] { "$$" }, System.StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < tmp.Length; i++)
            {
                if (!string.IsNullOrEmpty(tmp[i]))
                {
                    string[] vals = tmp[i].Split(' ');
                    if (vals.Length == 2)
                        msg.UsersIp.Add(new KeyValuePair<string, string>(vals[0], vals[1]));
                }
            }

            return msg;
        }
    }
}