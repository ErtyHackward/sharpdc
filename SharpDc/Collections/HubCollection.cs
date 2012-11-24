//  -------------------------------------------------------------
//  SharpDc project 
//  written by Vladislav Pozdnyakov (hackward@gmail.com) 2012
//  licensed under the LGPL
//  -------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using SharpDc.Connections;
using SharpDc.Events;

namespace SharpDc.Collections
{
    /// <summary>
    /// Allows to manage hubs
    /// </summary>
    public class HubCollection : ICollection<HubConnection>
    {
        private readonly object _syncRoot = new object();
        private readonly List<HubConnection> _hubList = new List<HubConnection>();

        /// <summary>
        /// Gets/Sets hub by index
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public HubConnection this[int index]
        {
            get
            {
                return _hubList[index];
            }
            set
            {
                _hubList[index] = value;
            }
        }

        /// <summary>
        /// Gets/Sets hub by name
        /// </summary>
        /// <param name="hubName"></param>
        /// <returns></returns>
        public HubConnection this[string hubName]
        {
            get { return _hubList.FirstOrDefault(item => item.Settings.HubName == hubName); }
        }

        public IEnumerable<HubConnection> All()
        {
            lock (_syncRoot)
            {
                foreach (var hubConnection in _hubList)
                {
                    yield return hubConnection;
                }
            }
        }

        /// <summary>
        /// Runs specified operation for all hubs
        /// </summary>
        /// <param name="action"></param>
        public void ForEach(Action<HubConnection> action)
        {
            lock (_syncRoot) 
                _hubList.ForEach(action);
        }

        public bool Exists(Predicate<HubConnection> predicate)
        {
            lock (_syncRoot)
                return _hubList.Exists(predicate);
        }

        public HubConnection Find(Predicate<HubConnection> predicate)
        {
            lock (_syncRoot)
                return _hubList.Find(predicate);
        }

        public object SyncRoot
        {
            get
            {
                return _syncRoot;
            }
        }

        #region Events


        public event EventHandler<HubsChangedEventArgs> HubAdded;

        protected void OnHubAdded(HubConnection hub)
        {
            var handler = HubAdded;
            if (handler != null) handler(this, new HubsChangedEventArgs(hub));
        }

        public event EventHandler<HubsChangedEventArgs> HubRemoved;

        protected void OnHubRemoved(HubConnection hub)
        {
            var handler = HubRemoved;
            if (handler != null) handler(this, new HubsChangedEventArgs(hub));
        }

        #endregion


        #region Collection

        /// <summary>
        /// Adds a hub with address specified and using nickname provided
        /// </summary>
        /// <param name="hubAddress"></param>
        /// <param name="nickname"></param>
        public void Add(string hubAddress, string nickname)
        {
            Add(new HubConnection(new HubSettings { 
                HubAddress = hubAddress, 
                HubName = hubAddress, 
                Nickname = nickname 
            }));
        }

        /// <summary>
        /// Adds a new hub into collection
        /// </summary>
        /// <param name="hub"></param>
        public void Add(HubConnection hub)
        {
            lock (_syncRoot)
            {
                _hubList.Add(hub);
            }
            OnHubAdded(hub);
        }

        public IEnumerator<HubConnection> GetEnumerator()
        {
            lock (_syncRoot)
                return _hubList.GetEnumerator();

        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            lock (_syncRoot)
                return _hubList.GetEnumerator();
        }

        /// <summary>
        /// Removes all hub from collection
        /// </summary>
        public void Clear()
        {
            // we send notify before delete... it is wrong style?
            foreach (HubConnection item in _hubList)
            {
                OnHubRemoved(item);
            }
            lock (_syncRoot)
                _hubList.Clear();
        }

        /// <summary>
        /// Detects if element is in this collection
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool Contains(HubConnection item)
        {
            lock (_syncRoot)
                return _hubList.Contains(item);
        }

        public void CopyTo(HubConnection[] array, int arrayIndex)
        {
            lock (_syncRoot)
                _hubList.CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Gets hubs count
        /// </summary>
        public int Count
        {
            get { return _hubList.Count; }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        /// <summary>
        /// Removes hub from collection
        /// </summary>
        /// <param name="hub"></param>
        /// <returns></returns>
        bool ICollection<HubConnection>.Remove(HubConnection hub)
        {
            bool result;
            lock (_syncRoot)
            {
                result = _hubList.Remove(hub);
            }
            OnHubRemoved(hub);
            return result;
        }

        public void RemoveAt(int index)
        {
            lock (_syncRoot)
            {
                HubConnection h = _hubList[index];
                _hubList.RemoveAt(index);
                if (h != null)
                {
                    OnHubRemoved(h);
                }
            }
        }

        #endregion


    }
}