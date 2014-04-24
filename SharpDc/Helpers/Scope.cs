// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2014
// licensed under the LGPL
// -------------------------------------------------------------

using System;

namespace SharpDc.Helpers
{
    public sealed class Scope : IDisposable
    {
        public static readonly Scope Empty = new Scope(null);

        public static Scope Create(Action actionOnDispose)
        {
            return new Scope(actionOnDispose);
        }

        Action _disposeAction;

        private Scope(Action actionOnDispose)
        {
            _disposeAction = actionOnDispose;
        }

        public void Dispose()
        {
            if (_disposeAction == null) 
                return;

            _disposeAction();
            _disposeAction = null;
        }
    }
}