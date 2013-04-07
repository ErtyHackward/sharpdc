//  -------------------------------------------------------------
//  LiveDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.Text;

namespace SharpDc.Structs
{
    /// <summary>
    /// Structure that represents magnet link
    /// </summary>
    public struct Magnet
    {
        private long _size;
        private string _tth;
        private string _filename;
        private bool _preview;

        /// <summary>
        /// File size
        /// </summary>
        public long Size
        {
            get
            {
                return _size;
            }
            set
            {
                _size = value;
            }
        }
        
        /// <summary>
        /// Tiger Tree Hash of the file
        /// </summary>
        public string TTH
        {
            get
            {
                return _tth;
            }
            set
            {
                _tth = value;
            }
        }
        
        /// <summary>
        /// File name
        /// </summary>
        public string FileName
        {
            get
            {
                return _filename;
            }
            set
            {
                _filename = value;
            }
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
        /// Gets validity of magnet
        /// </summary>
        public bool IsCorrect
        {
            get
            {
                return !string.IsNullOrEmpty(_tth) && _size > 0 && !string.IsNullOrEmpty(_filename);
            }
        }

        public Magnet(string tth, long size, string fileName)
        {
            _tth = tth;
            _size = size;
            _filename = fileName;
            _preview = false;
        }

        /// <summary>
        /// Creates Magnet object from string representation
        /// </summary>
        /// <param name="magnet">Link text</param>
        public Magnet(string magnet)
        {
            _size = 0;
            _tth = "";
            _filename = "";
            _preview = false;
            ParseInternal(magnet);
        }

        private void ParseInternal(string magnet)
        {
            if (!magnet.StartsWith("magnet:", StringComparison.CurrentCultureIgnoreCase))
                throw new ApplicationException("Unable to parse magnet link, format is incorrect");

            _size = 0;
            _tth = "";
            _filename = "";
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
                                _tth = p[1].Substring(15);

                                for (int i = 0; i < _tth.Length; i++)
                                {
                                    if (!Char.IsLetterOrDigit(_tth[i]))
                                    {
                                        // incorrect tth string
                                        _tth = string.Empty;
                                        break;
                                    }
                                }
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
                            catch (System.FormatException)
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
                        default:
                            break;
                    }
                }
            }
        }

        public override string ToString()
        {
            return string.Format("magnet:?xt=urn:tree:tiger:{0}&xl={1}&dn={2}{3}", _tth, _size, Uri.EscapeDataString(_filename), Preview ? "&video=1" : "");
        }

        public string ToWebLink()
        {
            return string.Format("[magnet=magnet:?xt=urn:tree:tiger:{0}&xl={1}&dn={2}]{3}[/magnet]", _tth, _size, Uri.EscapeDataString(_filename), _filename);
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
}