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
        public int Count => _objects.Count;

        /// <summary>
        /// Gets amount of items were totally created by this pool
        /// </summary>
        public int TotallyItemsCreated => _totallyItemsCreated;

        public ObjectPool(Func<T> objectGenerator)
        {
            _objects = new ConcurrentBag<T>();
            _objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
        }

        public T GetObject()
        {
            if (_objects.TryTake(out var item))
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

        /// <summary>
        /// Helper method to use in using () construct
        /// Automatically returns the object when the ReusableObject is disposed
        /// </summary>
        /// <returns></returns>
        public ReusableObject<T> UseObject()
        {
            return new ReusableObject<T>(this, GetObject());
        }
    }

    public struct ReusableObject<T> : IDisposable
    {
        private readonly T _object;
        private readonly ObjectPool<T> _pool;
        private bool _isDisposed;
        public T Object => _object;

        public ReusableObject(ObjectPool<T> pool, T obj)
        {
            _object = obj;
            _pool = pool;
            _isDisposed = false;
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _pool?.PutObject(_object);
        }
    }
}