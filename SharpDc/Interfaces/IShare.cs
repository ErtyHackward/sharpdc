// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections.Generic;
using SharpDc.Managers;
using SharpDc.Messages;

namespace SharpDc.Interfaces
{
    public interface IShare
    {
        event EventHandler TotalSharedChanged;

        /// <summary>
        /// Gets total amount of bytes in the share
        /// </summary>
        long TotalShared { get; }

        /// <summary>
        /// Gets total amount of files in the share
        /// </summary>
        int TotalFiles { get; }

        /// <summary>
        /// Adds file into the share
        /// </summary>
        /// <param name="item"></param>
        void AddFile(ContentItem item);

        /// <summary>
        /// Removes file from share
        /// </summary>
        /// <param name="tth"></param>
        void RemoveFile(string tth);

        /// <summary>
        /// Performs share search
        /// </summary>
        /// <param name="query">query string</param>
        /// <param name="limit">results limit, leave 0 for unlimited</param>
        /// <returns></returns>
        List<ContentItem> Search(SearchQuery query, int limit = 0);
        
        /// <summary>
        /// Checks all content to be in the system
        /// </summary>
        void Reload();

        /// <summary>
        /// Erases all files from the share
        /// </summary>
        void Clear();

        /// <summary>
        /// Allows to enumerate all items in the share
        /// </summary>
        /// <returns></returns>
        IEnumerable<ContentItem> Items();
    }

    public static class ShareExtensions
    {
        public static ContentItem? SearchByTth(this IShare share, string tth)
        {
            var results = share.Search(new SearchQuery { Query = tth, SearchType = SearchType.TTH });

            if (results.Count > 0)
                return results[0];
            return null;
        }
    }

}