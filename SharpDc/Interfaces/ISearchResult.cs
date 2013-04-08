// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;

namespace SharpDc.Interfaces
{
    public interface ISearchResult : ICloneable
    {
        string Name { get; }
        long Size { get; }
    }
}