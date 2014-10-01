using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpDc.Helpers
{
    public sealed class TransferSocketAwaitable : INotifyCompletion
    {
        private readonly static Action SENTINEL = () => { };

        internal bool m_wasCompleted;
        internal Action m_continuation;
        internal SocketAsyncEventArgs m_eventArgs;

        public TransferSocketAwaitable(SocketAsyncEventArgs eventArgs)
        {
            if (eventArgs == null) throw new ArgumentNullException("eventArgs");
            m_eventArgs = eventArgs;
            eventArgs.Completed += delegate
            {
                var prev = m_continuation ?? Interlocked.CompareExchange(
                    ref m_continuation, SENTINEL, null);
                if (prev != null) prev();
            };
        }

        internal void Reset()
        {
            m_wasCompleted = false;
            m_continuation = null;
        }

        public TransferSocketAwaitable GetAwaiter() { return this; }

        public bool IsCompleted { get { return m_wasCompleted; } }

        public void OnCompleted(Action continuation)
        {
            if (m_continuation == SENTINEL ||
                Interlocked.CompareExchange(
                    ref m_continuation, continuation, null) == SENTINEL)
            {
                Task.Run(continuation);
            }
        }

        public int GetResult()
        {
            if (m_eventArgs.SocketError != SocketError.Success)
                throw new SocketException((int)m_eventArgs.SocketError);

            return m_eventArgs.BytesTransferred;
        }
    }

    public sealed class VoidSocketAwaitable : INotifyCompletion
    {
        private readonly static Action SENTINEL = () => { };

        internal bool m_wasCompleted;
        internal Action m_continuation;
        internal SocketAsyncEventArgs m_eventArgs;

        public VoidSocketAwaitable(SocketAsyncEventArgs eventArgs)
        {
            if (eventArgs == null) throw new ArgumentNullException("eventArgs");
            m_eventArgs = eventArgs;
            eventArgs.Completed += delegate
            {
                var prev = m_continuation ?? Interlocked.CompareExchange(
                    ref m_continuation, SENTINEL, null);
                if (prev != null) prev();
            };
        }

        internal void Reset()
        {
            m_wasCompleted = false;
            m_continuation = null;
        }

        public VoidSocketAwaitable GetAwaiter() { return this; }

        public bool IsCompleted { get { return m_wasCompleted; } }

        public void OnCompleted(Action continuation)
        {
            if (m_continuation == SENTINEL ||
                Interlocked.CompareExchange(
                    ref m_continuation, continuation, null) == SENTINEL)
            {
                Task.Run(continuation);
            }
        }

        public void GetResult()
        {
            if (m_eventArgs.SocketError != SocketError.Success)
                throw new SocketException((int)m_eventArgs.SocketError);
        }
    }

    public static class SocketExtensions
    {
        public static TransferSocketAwaitable ReceiveAsync(this Socket socket,
            TransferSocketAwaitable awaitable)
        {
            awaitable.Reset();
            if (!socket.ReceiveAsync(awaitable.m_eventArgs))
                awaitable.m_wasCompleted = true;
            return awaitable;
        }

        public static TransferSocketAwaitable SendAsync(this Socket socket,
            TransferSocketAwaitable awaitable)
        {
            awaitable.Reset();
            if (!socket.SendAsync(awaitable.m_eventArgs))
                awaitable.m_wasCompleted = true;
            return awaitable;
        }

        public static VoidSocketAwaitable ConnectAsync(this Socket socket, VoidSocketAwaitable awaitable)
        {
            awaitable.Reset();
            if (!socket.ConnectAsync(awaitable.m_eventArgs))
                awaitable.m_wasCompleted = true;
            return awaitable;
        }

        public static VoidSocketAwaitable DisconnectAsync(this Socket socket, VoidSocketAwaitable awaitable)
        {
            awaitable.Reset();
            if (!socket.DisconnectAsync(awaitable.m_eventArgs))
                awaitable.m_wasCompleted = true;
            return awaitable;
        }
    }
}
