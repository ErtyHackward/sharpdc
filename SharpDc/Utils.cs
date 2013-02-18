//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;

namespace SharpDc
{
    public static class Utils
    {
        public static Stopwatch Measure(Action action)
        {
            Stopwatch sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            return sw;
        }

        /// <summary>
        /// Allows to format bytes to human-readable string
        /// </summary>
        public class FileSizeFormatProvider : IFormatProvider, ICustomFormatter
        {
            private static FileSizeFormatProvider _instance = null;

            public static FileSizeFormatProvider Instance
            {
                get
                {
                    if (_instance == null) _instance = new FileSizeFormatProvider();
                    return _instance;
                }
            }

            public object GetFormat(Type formatType)
            {
                if (typeof(ICustomFormatter).IsAssignableFrom(formatType))
                    return this;
                return null;
            }

            private const string fileSizeFormat = "fs";

            private static readonly string[] letters = new[] { " B", " KiB", " MiB", " GiB", " TiB", " PiB" };

            public string Format(string format, object arg, IFormatProvider formatProvider)
            {
                if (format == null || !format.StartsWith(fileSizeFormat))
                {
                    return defaultFormat(format, arg, formatProvider);
                }

                Decimal size;
                try
                {
                    size = Convert.ToDecimal(arg);
                }
                catch (InvalidCastException)
                {
                    return defaultFormat(format, arg, formatProvider);
                }

                byte i = 0;
                while ((size >= 1024) && (i < letters.Length - 1))
                {
                    i++;
                    size /= 1024;
                }

                string precision = format.Substring(2);
                if (String.IsNullOrEmpty(precision)) precision = "2";

                return String.Format("{0:N" + precision + "}{1}", size, letters[i]);

            }

            private static string defaultFormat(string format, object arg, IFormatProvider formatProvider)
            {
                IFormattable formattableArg = arg as IFormattable;
                if (formattableArg != null)
                {
                    return formattableArg.ToString(format, formatProvider);
                }

                return arg.ToString();
            }
        }

        /// <summary>
        /// Create human readable size representation
        /// </summary>
        /// <param name="value">Amount of bytes</param>
        /// <returns></returns>
        public static string FormatBytes(long value)
        {
            return string.Format(FileSizeFormatProvider.Instance, "{0:fs}", value);
        }

        /// <summary>
        /// Create human readable size representation
        /// </summary>
        /// <param name="value">Amount of bytes</param>
        /// <returns></returns>
        public static string FormatBytes(double value)
        {
            return string.Format(FileSizeFormatProvider.Instance, "{0:fs}", value);
        }

        /// <summary>
        /// Parses string into a IPEndPoint
        /// http://stackoverflow.com/questions/2727609/best-way-to-create-ipendpoint-from-string
        /// </summary>
        /// <param name="endPoint"></param>
        /// <returns></returns>
        public static IPEndPoint CreateIPEndPoint(string endPoint)
        {
            string[] ep = endPoint.Split(':');
            if (ep.Length < 2) throw new FormatException("Invalid endpoint format");
            IPAddress ip;
            if (ep.Length > 2)
            {
                if (!IPAddress.TryParse(string.Join(":", ep, 0, ep.Length - 1), out ip))
                {
                    throw new FormatException("Invalid ip-adress");
                }
            }
            else
            {
                if (!IPAddress.TryParse(ep[0], out ip))
                {
                    throw new FormatException("Invalid ip-adress");
                }
            }
            int port;
            if (!int.TryParse(ep[ep.Length - 1], NumberStyles.None, NumberFormatInfo.CurrentInfo, out port))
            {
                throw new FormatException("Invalid port");
            }
            return new IPEndPoint(ip, port);
        }
    }
}
