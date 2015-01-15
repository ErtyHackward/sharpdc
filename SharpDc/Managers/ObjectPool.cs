using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace SharpDc.Managers
{
    public class ObjectPool<T> : IEnumerable<T>
    {
        private readonly ConcurrentBag<T> _objects;
        private readonly Func<T> _objectGenerator;
        private int _totallyItemsCreated;

        /// <summary>
        /// Returns count of items in the pool right now
        /// </summary>
        public int Count
        {
            get { return _objects.Count; }
        }

        /// <summary>
        /// Gets amount of items were totally created by this pool
        /// </summary>
        public int TotallyItemsCreated
        {
            get { return _totallyItemsCreated; }
            private set { _totallyItemsCreated = value; }
        }

        public ObjectPool(Func<T> objectGenerator)
        {
            if (objectGenerator == null) throw new ArgumentNullException("objectGenerator");
            _objects = new ConcurrentBag<T>();
            _objectGenerator = objectGenerator;
        }

        public T GetObject()
        {
            T item;
            
            if (_objects.TryTake(out item)) 
                return item;

            var newItem = _objectGenerator();
            Interlocked.Increment(ref _totallyItemsCreated);
            return newItem;
        }

        public void PutObject(T item)
        {
            _objects.Add(item);
        }
        
        public IEnumerator<T> GetEnumerator()
        {
            return _objects.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}