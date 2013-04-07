//  -------------------------------------------------------------
//  LiveDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
//  licensed under the LGPL
//  -------------------------------------------------------------
using SharpDc.Messages;

namespace SharpDc.Managers
{
    public struct SearchQuery
    {
        public SearchType SearchType { get; set; }
        public string Query { get; set; }
    }
}