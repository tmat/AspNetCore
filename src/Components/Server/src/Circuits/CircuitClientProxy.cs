// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace Microsoft.AspNetCore.Components.Server.Circuits
{
    internal class CircuitClientProxy : IClientProxy
    {
        private CancellationTokenSource _clientCancellationTokenSource;

        public CircuitClientProxy(IClientProxy clientProxy, string connectionId)
        {
            Client = clientProxy;
            ConnectionId = connectionId;
            Connected = true;

            _clientCancellationTokenSource = new CancellationTokenSource();
        }

        public bool Connected { get; set; }

        public string ConnectionId { get; private set; }

        public IClientProxy Client { get; private set; }

        public void Transfer(IClientProxy clientProxy, string connectionId)
        {
            var oldTokenSource = Interlocked.Exchange(ref _clientCancellationTokenSource, new CancellationTokenSource());
            oldTokenSource.Cancel();

            Client = clientProxy;
            ConnectionId = connectionId;
            Connected = true;
        }

        public Task SendCoreAsync(string method, object[] args, CancellationToken cancellationToken = default)
        {
            var tokenSource = _clientCancellationTokenSource;
            var combinedToken = tokenSource.Token;
            if (cancellationToken.CanBeCanceled)
            {
                combinedToken = CancellationTokenSource.CreateLinkedTokenSource(
                    tokenSource.Token,
                    cancellationToken).Token;
            }

            return Client.SendCoreAsync(method, args, combinedToken);
        }
    }
}
