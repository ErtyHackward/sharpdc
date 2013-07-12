// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SharpDc.Structs
{
    /// <summary>
    /// Structure that represents magnet link
    /// https://en.wikipedia.org/wiki/Magnet_URI_scheme
    /// </summary>
    [Serializable]
    public struct Magnet
    {
        private long _size;
        private bool _preview;
        private string _filename;
        private string _tth;
        private string _sha1;
        private string _btih;
        private string _md5;
        private string _ed2K;
        private string[] _p2pSources;
        private string[] _trackers;
        private string[] _webSources;

        /// <summary>
        /// File size (xl tag)
        /// </summary>
        public long Size
        {
            get { return _size; }
            set { _size = value; }
        }

        /// <summary>
        /// Tiger Tree Hash of the file (base32 encoded) (xt tag)
        /// null if not presented
        /// </summary>
        public string TTH
        {
            get { return _tth; }
            set
            {
                _tth = value;
                
                if (_tth != null)
                {
                    if (_tth.Length != 39)
                        throw new FormatException("Invalid tiger tree hash length 39 expected");

                    for (int i = 0; i < _tth.Length; i++)
                    {
                        if (!Char.IsLetterOrDigit(_tth[i]))
                        {
                            throw new FormatException("Invalid hash value, expected base32 encoded hash");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// SHA1 HEX/base32 hash (xt tag)
        /// null if not presented
        /// </summary>
        public string SHA1
        {
            get { return _sha1; }
            set { _sha1 = value; }
        }

        /// <summary>
        /// BitTorrent Info Hash HEX/base32 hash (xt tag)
        /// null if not presented
        /// </summary>
        public string BTIH
        {
            get { return _btih; }
            set { _btih = value; }
        }

        /// <summary>
        /// MD5 HEX hash (xt tag)
        /// null if not presented
        /// </summary>
        public string MD5
        {
            get { return _md5; }
            set { _md5 = value; }
        }

        /// <summary>
        /// ED2K base32 hash (xt tag)
        /// null if not presented
        /// </summary>
        public string ED2K
        {
            get { return _ed2K; }
            set { _ed2K = value; }
        }

        /// <summary>
        /// File name (dn tag)
        /// </summary>
        public string FileName
        {
            get { return _filename; }
            set { _filename = value; }
        }

        /// <summary>
        /// Indicates whether the file play should be started before file was downloaded
        /// </summary>
        public bool Preview
        {
            get { return _preview; }
            set { _preview = value; }
        }

        /// <summary>
        /// Contains links to p2p specific sources (xs tag)
        /// This includes dc hubs
        /// null if not present
        /// </summary>
        public string[] P2PSources
        {
            get { return _p2pSources; }
            set { _p2pSources = value; }
        }

        /// <summary>
        /// Contains BitTorrent announce urls (tr tag)
        /// null if not present
        /// </summary>
        public string[] Trackers
        {
            get { return _trackers; }
            set { _trackers = value; }
        }

        /// <summary>
        /// Web mirrors of the file (as tag)
        /// null if not present
        /// </summary>
        public string[] WebSources
        {
            get { return _webSources; }
            set { _webSources = value; }
        }

        /// <summary>
        /// Creates Magnet object from string representation
        /// </summary>
        /// <param name="magnet">Link text</param>
        public Magnet(string magnet)
        {
            _size = 0;
            _filename = null;
            _preview = false;

            _tth = null;
            _sha1 = null;
            _btih = null;
            _md5 = null;
            _ed2K = null;
            _p2pSources = null;
            _trackers = null;
            _webSources = null;

            ParseInternal(magnet);
        }

        private void ParseInternal(string magnet)
        {
            if (!magnet.StartsWith("magnet:", StringComparison.CurrentCultureIgnoreCase))
                throw new ApplicationException("Unable to parse magnet link, the link should start with 'magnet:'");

            List<string> webSources = null;
            List<string> p2pSources = null;
            List<string> trackers = null;
            
            string[] pars = magnet.Substring(8).Split('&');
            foreach (string param in pars)
            {
                string[] p = param.Split('=');
                if (p.GetUpperBound(0) == 1)
                {
                    switch (p[0])
                    {
                        case "xt":
                            if (p[1].StartsWith("urn:tree:tiger:", StringComparison.InvariantCultureIgnoreCase))
                            {
                                TTH = p[1].Substring(15);
                            }
                            // IXE2K3JMCPUZWTW3YQZZOIB5XD6KZIEQ len = 32
                            if (p[1].StartsWith("urn:sha1:", StringComparison.InvariantCultureIgnoreCase))
                            {
                                _sha1 = p[1].Substring(9);

                                if (_sha1.Length != 32)
                                    throw new FormatException("Invalid tiger tree hash length");
                            }

                            // e111b5b5f803ee4bc29237178a10dd567315748b len = 40
                            if (p[1].StartsWith("urn:btih:", StringComparison.InvariantCultureIgnoreCase))
                            {
                                _btih = p[1].Substring(9);

                                if (_btih.Length != 40)
                                    throw new FormatException("Invalid tiger tree hash length");
                            }

                            // d41d8cd98f00b204e9800998ecf8427e len = 32
                            if (p[1].StartsWith("urn:md5:", StringComparison.InvariantCultureIgnoreCase))
                            {
                                _md5 = p[1].Substring(9);

                                if (_md5.Length != 32)
                                    throw new FormatException("Invalid MD5 hash length");
                            }

                            // 354B15E68FB8F36D7CD88FF94116CDC1 len = 32
                            if (p[1].StartsWith("urn:ed2k:", StringComparison.InvariantCultureIgnoreCase))
                            {
                                _md5 = p[1].Substring(9);

                                if (_md5.Length != 32)
                                    throw new FormatException("Invalid ED2K hash length");
                            }
                            break;
                        case "xl":
                            _size = long.Parse(p[1]);
                            break;
                        case "dn":
                            try
                            {
                                if (IsInIEFormat(p[1]))
                                    _filename = ExplorerUnescape(p[1]);
                                else
                                    _filename = Uri.UnescapeDataString(p[1]);

                                if (InUtf8(_filename))
                                    _filename = Utf8Unescape(_filename);
                            }
                            catch (FormatException)
                            {
                                _filename = ChitUnescape(p[1]);
                            }
                            _filename = _filename.Replace('+', ' ');
                            if (_filename.EndsWith("."))
                                _filename = _filename.TrimEnd('.');
                            break;
                        case "video":
                            _preview = p[1] == "1";
                            break;
                        case "as":
                            if (webSources == null)
                                webSources = new List<string>();

                            webSources.Add(Uri.UnescapeDataString(p[1]));
                            break;
                        case "xs":
                            if (p2pSources == null)
                                p2pSources = new List<string>();

                            p2pSources.Add(Uri.UnescapeDataString(p[1]));
                            break;
                        case "tr":
                            if (trackers == null)
                                trackers = new List<string>();

                            trackers.Add(Uri.UnescapeDataString(p[1]));
                            break;
                        default:
                            break;
                    }
                }
            }

            if (p2pSources != null)
                _p2pSources = p2pSources.ToArray();

            if (trackers != null)
                _trackers = trackers.ToArray();

            if (webSources != null)
                _webSources = webSources.ToArray();
        }

        public override string ToString()
        {
            var arguments = new List<string>();
            
            // add hashes
            if (!string.IsNullOrEmpty(TTH))
            {
                arguments.Add("xt=urn:tree:tiger:" + TTH);
            }

            if (!string.IsNullOrEmpty(SHA1))
            {
                arguments.Add("xt=urn:sha1:" + SHA1);
            }

            if (!string.IsNullOrEmpty(ED2K))
            {
                arguments.Add("xt=urn:ed2k:" + ED2K);
            }

            if (!string.IsNullOrEmpty(BTIH))
            {
                arguments.Add("xt=urn:btih:" + BTIH);
            }

            if (!string.IsNullOrEmpty(MD5))
            {
                arguments.Add("xt=urn:md5:" + MD5);
            }

            if (arguments.Count == 0)
                throw new InvalidProgramException("Impossible to create magnet link without any hash data");

            arguments.Add("xl=" + _size);
            arguments.Add("dn=" + Uri.EscapeDataString(_filename));

            if (_webSources != null)
            {
                arguments.AddRange(_webSources.Select(item => "as=" + Uri.EscapeDataString(item)));
            }

            if (_p2pSources != null)
            {
                arguments.AddRange(_p2pSources.Select(item => "xs=" + Uri.EscapeDataString(item)));
            }

            if (_trackers != null)
            {
                arguments.AddRange(_trackers.Select(item => "tr=" + Uri.EscapeDataString(item)));
            }

            if (Preview) 
                arguments.Add("video=1");

            return "magnet:?" + string.Join("&", arguments);

            return string.Format("magnet:?xt=urn:tree:tiger:{0}&xl={1}&dn={2}{3}", _tth, _size,
                                 Uri.EscapeDataString(_filename), Preview ? "&video=1" : "");
        }

        public string ToWebLink()
        {
            return string.Format("[magnet=magnet:?xt=urn:tree:tiger:{0}&xl={1}&dn={2}]{3}[/magnet]", _tth, _size,
                                 Uri.EscapeDataString(_filename), _filename);
        }

        /// <summary>
        /// Trying to convert from wrong encoded magnet link (For cp1251)
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        internal static string ChitUnescape(string input)
        {
            // trying to convert manually
            var sb = new StringBuilder(input);
            for (int i = 0; i < sb.Length; i++)
            {
                if (sb[i] == '%')
                {
                    if (i + 2 <= sb.Length - 1)
                    {
                        if (sb[i + 1] == '%') continue;
                        string s = new string(new char[] { sb[i], sb[i + 1], sb[i + 2] });
                        string s1 = new string(new char[] { sb[i + 1], sb[i + 2] });
                        sb.Replace(s, ((char)Convert.ToInt32(s1, 16)).ToString());
                    }
                }
            }
            byte[] bytes = Encoding.GetEncoding(1252).GetBytes(sb.ToString());
            return Encoding.GetEncoding(1251).GetString(bytes, 0, bytes.Length);
        }

        internal static string ExplorerUnescape(string input)
        {
            return Encoding.UTF8.GetString(Encoding.GetEncoding(28591).GetBytes(input));
        }

        internal static bool IsInIEFormat(string input)
        {
            byte[] bytes = Encoding.GetEncoding(28591).GetBytes(input);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == 208)
                    return true;
            }
            return false;
        }

        internal static bool InUtf8(string input)
        {
            //byte[] bytes = Encoding.Default.GetBytes( input);
            string n = Encoding.UTF8.GetString(Encoding.Default.GetBytes(input));
            for (int i = 0; i < n.Length; i++)
            {
                if (n[i] == 65533)
                    return false;
            }
            return true;
        }

        internal static string Utf8Unescape(string inputInUtf8)
        {
            return Encoding.UTF8.GetString(Encoding.Default.GetBytes(inputInUtf8));
        }

        /// <summary>
        /// Tries to parse text as a magnet
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static Magnet Parse(string text)
        {
            return new Magnet(text);
        }
    }

    /// <summary>
    /// Enumerates common magnet hash types
    /// </summary>
    [Flags]
    public enum MagnetHashType
    {
        None = 0x0,
        /// <summary>
        /// Tiger Tree Hash
        /// </summary>
        TTH = 0x1,
        SHA1 = 0x2,
        BitPrint = 0x4,
        /// <summary>
        /// eDonkey 200 hash
        /// </summary>
        ED2K = 0x8,
        /// <summary>
        /// Bit Torrent Info Hash
        /// </summary>
        BTIH = 0x10,
        MD5 = 0x20
    }
}