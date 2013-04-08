// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;

namespace SharpDc.Collections
{
    /// <summary>
    /// Represents List with events
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [Serializable]
    public class ObservableList<T> : IList<T>
    {
        protected List<T> BaseList;

        #region Events

        /// <summary>
        /// Occurs when item was added
        /// </summary>
        public event EventHandler<ObservableListEventArgs<T>> ItemAdded;

        protected virtual void OnItemAdded(ObservableListEventArgs<T> e)
        {
            var handler = ItemAdded;
            if (handler != null) handler(this, e);
        }

        /// <summary>
        /// Occurs when item was removed
        /// </summary>
        public event EventHandler<ObservableListEventArgs<T>> ItemRemoved;

        protected virtual void OnItemRemoved(ObservableListEventArgs<T> e)
        {
            var handler = ItemRemoved;
            if (handler != null) handler(this, e);
        }

        #endregion

        private readonly object _syncRoot = new object();

        public object SyncRoot
        {
            get { return _syncRoot; }
        }

        public ObservableList()
        {
            BaseList = new List<T>();
        }

        public ObservableList(IEnumerable<T> copyFrom)
        {
            BaseList = new List<T>(copyFrom);
        }

        public ObservableList(int capacity)
        {
            BaseList = new List<T>(capacity);
        }

        public int BinarySearch(T item, IComparer<T> comparer)
        {
            lock (_syncRoot)
                return BaseList.BinarySearch(item, comparer);
        }

        #region Члены IList<T>

        public int IndexOf(T item)
        {
            lock (SyncRoot)
                return BaseList.IndexOf(item);
        }

        public void Insert(int index, T item)
        {
            lock (SyncRoot)
                BaseList.Insert(index, item);
            OnItemAdded(new ObservableListEventArgs<T> { Index = index, Item = item });
        }

        public void RemoveAt(int index)
        {
            if (ItemRemoved != null)
            {
                T item = BaseList[index];
                lock (SyncRoot)
                    BaseList.RemoveAt(index);
                OnItemRemoved(new ObservableListEventArgs<T> { Item = item, Index = index });
                return;
            }
            lock (SyncRoot)
                BaseList.RemoveAt(index);
        }

        #endregion

        public bool Contains(T item)
        {
            lock (SyncRoot)
                return BaseList.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (SyncRoot)
                BaseList.CopyTo(array, arrayIndex);
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        /// <summary>
        /// Add object into a list
        /// </summary>
        /// <param name="p"></param>
        public virtual void Add(T p)
        {
            lock (SyncRoot)
            {
                BaseList.Add(p);
            }
            OnItemAdded(new ObservableListEventArgs<T> { Item = p, Index = BaseList.Count - 1 });
        }

        public virtual void Add(IEnumerable<T> items)
        {
            foreach (var p in items)
            {
                Add(p);
            }
        }

        public virtual void AddRange(IEnumerable<T> items)
        {
            Add(items);
        }

        /// <summary>
        /// Removes object
        /// </summary>
        /// <param name="p"></param>
        public bool Remove(T p)
        {
            int index;
            lock (SyncRoot)
            {
                index = BaseList.IndexOf(p);
                if (index != -1)
                    BaseList.RemoveAt(index);
            }
            if (index != -1)
            {
                OnItemRemoved(new ObservableListEventArgs<T> { Item = p, Index = index });
                return true;
            }
            return false;
        }

        public void Clear()
        {
            lock (SyncRoot)
            {
                BaseList.Clear();
            }
        }

        public void Sort(IComparer<T> comparer)
        {
            lock (SyncRoot)
                BaseList.Sort(comparer);
        }

        public T this[int index]
        {
            get { return BaseList[index]; }
            set { BaseList[index] = value; }
        }

        public int Count
        {
            get { return BaseList.Count; }
        }

        public IEnumerator<T> GetEnumerator()
        {
            return BaseList.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return BaseList.GetEnumerator();
        }
    }

    public class ObservableListEventArgs<T> : EventArgs
    {
        public T Item { get; set; }
        public int Index { get; set; }
    }
}