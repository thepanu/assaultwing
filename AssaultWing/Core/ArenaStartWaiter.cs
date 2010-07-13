﻿using System;
using System.Collections.Generic;
using System.Linq;
using AW2.Net;
using AW2.Net.Connections;
using AW2.Net.MessageHandling;
using AW2.Net.Messages;

namespace AW2.Core
{
    public class ArenaStartWaiter : IDisposable
    {
        private bool _disposed;
        private MultiConnection _connections;
        private List<int> _readyIDs;

        public bool IsEverybodyReady
        {
            get
            {
                CheckDisposed();
                return _connections.Connections.All(conn => _readyIDs.Contains(conn.ID));
            }
        }

        public ArenaStartWaiter(MultiConnection connections)
        {
            _connections = connections;
            _readyIDs = new List<int>();
        }

        public void BeginWait()
        {
            CheckDisposed();
            MessageHandlers.ActivateHandlers(MessageHandlers.GetServerArenaStartHandlers(_readyIDs.Add));
            _connections.Send(new ArenaStartRequest());
        }

        public void EndWait()
        {
            CheckDisposed();
            Dispose();
        }

        public void Dispose()
        {
            _disposed = true;
            MessageHandlers.DeactivateHandlers(MessageHandlers.GetServerArenaStartHandlers(null));
        }

        private void CheckDisposed()
        {
            if (_disposed) throw new InvalidOperationException("This object has been disposed");
        }
    }
}
