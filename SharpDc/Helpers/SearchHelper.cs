// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2014
// licensed under the LGPL
// -------------------------------------------------------------

using System.IO;
using SharpDc.Messages;

namespace SharpDc.Helpers
{
    public static class SearchHelper
    {
        private static string[] musicExt = { ".mp3", ".aac", ".flac", ".a3c", ".ogg", ".ape", ".mid", ".rm", ".au", ".wav", ".sm", ".kar" };
        private static string[] videoExt = { ".avi", ".mpg", ".mpeg", ".mkv", ".asf", ".mov", ".ts" };
        private static string[] archiveExt = { ".zip", ".rar", ".arj", ".lzh", ".gz", ".z", ".arc", ".pak", ".7z" };
        private static string[] documentExt = { ".doc", ".docx", ".txt", ".wri", ".pdf", ".ps", ".tex", ".xls", ".xlsx", ".gdoc", ".gsheet", ".gslides" };
        private static string[] pictureExt = { ".png", ".gif", ".jpg", ".jpeg", ".bmp", ".pcx", ".wmf", ".psd" };
        private static string[] exeExt = { ".exe" };
        private static string[] imageExt = { ".iso", ".nrg", ".vcd", ".img", ".dmg", ".mds", ".mdf", ".daa", ".ccd", ".pqi" };


        public static string[][] Extensions =
        { 
            /*0*/ new string[0],
            /*1*/ new string[0],
            /*2*/ musicExt,
            /*3*/ archiveExt,
            /*4*/ documentExt,
            /*5*/ exeExt,
            /*6*/ pictureExt,
            /*7*/ videoExt,
            /*8*/ new string[0],
            /*9*/ new string[0],
            /*10*/imageExt
        };

        public static bool IsFileInCategory(string fileName, SearchType category)
        {
            if (category == SearchType.Any || category == SearchType.Folder || category == SearchType.TTH) 
                return true;

            var value = (byte)category;
            if (value < 1 || value > 10)
                return false;

            if (string.IsNullOrEmpty(fileName)) 
                return false;

            var ext = Path.GetExtension(fileName).ToLower();

            for (var i = 0; i < Extensions[value].Length; i++)
            {
                if (ext.Equals(Extensions[value][i]))
                    return true;
            }
            return false;
        }
    }
}
