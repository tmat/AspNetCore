// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Components.Server.Circuits
{
    internal class CircuitRegistry
    {
        private readonly ComponentsServerOptions _options;
        private readonly ILogger _logger;
        private readonly PostEvictionCallbackRegistration _postEvictionCallback;

        public CircuitRegistry(
            IOptions<ComponentsServerOptions> options,
            ILogger<CircuitRegistry> logger)
        {
            _options = options.Value;
            _logger = logger;

            ActiveCircuits = new ConcurrentDictionary<string, CircuitHost>(StringComparer.Ordinal);

            InactiveCircuits = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = _options.MaxRetainedDisconnectedCircuits,
            });

            _postEvictionCallback = new PostEvictionCallbackRegistration
            {
                EvictionCallback = OnEntryEvicted,
            };
        }

        internal ConcurrentDictionary<string, CircuitHost> ActiveCircuits { get; }

        internal MemoryCache InactiveCircuits { get; }

        public void Register(CircuitHost circuitHost)
        {
            ActiveCircuits.TryAdd(circuitHost.CircuitId, circuitHost);
        }

        public void Deactivate(CircuitHost circuitHost, string connectionId)
        {
            if (!ActiveCircuits.TryGetValue(circuitHost.CircuitId, out circuitHost))
            {
                // The circuit might already have been marked as inactive.
                return;
            }

            if (!string.Equals(circuitHost.CircuitClient.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                // The circuit is associated with a different connection. One way this could happen is when
                // the client reconnects with a new connection before the OnDisconnect for the older
                // connection is executed. Do nothing
                return;
            }

            ActiveCircuits.TryRemove(circuitHost.CircuitId, out circuitHost);

            circuitHost.CircuitClient.Connected = false;
            var entryOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = DateTimeOffset.UtcNow.Add(_options.DisconnectedCircuitRetentionPeriod),
                Size = 1,
                PostEvictionCallbacks = { _postEvictionCallback },
            };

            InactiveCircuits.Set(circuitHost.CircuitId, circuitHost, entryOptions);
        }

        public bool TryActivate(string circuitId, IClientProxy clientProxy, string connectionId, out CircuitHost host)
        {
            if (ActiveCircuits.TryGetValue(circuitId, out host))
            {
                if (!string.Equals(host.CircuitClient.ConnectionId, connectionId, StringComparison.Ordinal))
                {
                    // The host is still active i.e. the server hasn't detected the client disconnect.
                    // However the client reconnected establishing a new connection.
                    host.CircuitClient.Transfer(clientProxy, connectionId);
                }

                return true;
            }

            if (InactiveCircuits.TryGetValue(circuitId, out host))
            {
                ActiveCircuits.TryAdd(circuitId, host);
                InactiveCircuits.Remove(circuitId);

                // Inactive connections always require transfering the connection
                host.CircuitClient.Transfer(clientProxy, connectionId);

                return true;
            }

            host = null;
            return false;
        }

        private void OnEntryEvicted(object key, object value, EvictionReason reason, object state)
        {
            switch (reason)
            {
                case EvictionReason.Expired:
                case EvictionReason.Capacity:
                    // Kick off the dispose in the background, but don't wait for it to finish.
                    _ = DisposeCircuitHost((CircuitHost)value);
                    break;

                case EvictionReason.Removed:
                    // The entry was explicitly removed as part of TryGetInactiveCircuit. Nothing to do here.
                    return;

                default:
                    Debug.Fail($"Unexpected {nameof(EvictionReason)} {reason}");
                    break;
            }
        }

        private async Task DisposeCircuitHost(CircuitHost circuitHost)
        {
            try
            {
                await circuitHost.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.UnhandledExceptionDisposingCircuitHost(ex);
            }
        }
    }
}
