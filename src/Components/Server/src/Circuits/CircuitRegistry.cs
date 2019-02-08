// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Microsoft.AspNetCore.Components.Server.Circuits
{
    internal class CircuitRegistry
    {
        private readonly MemoryCache _circuitHostRegistry;

        public CircuitRegistry()
        {
            _circuitHostRegistry = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = 100,
            });
        }

        public void AddInactiveCircuit(CircuitHost circuitHost)
        {
            var tokenSource = new CancellationTokenSource();
            var entryOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(20),
                Size = 1,
                ExpirationTokens =
                {
                    new CancellationChangeToken(tokenSource.Token),
                },
                PostEvictionCallbacks =
                {
                    new PostEvictionCallbackRegistration
                    {
                        EvictionCallback = OnEntryEvicted,
                    },
                },
            };

            _circuitHostRegistry.Set(circuitHost.CircuitId, new CacheEntry(circuitHost, tokenSource), entryOptions);
        }

        public bool TryGetInactiveCircuit(string circuitId, out CircuitHost host)
        {
            if (_circuitHostRegistry.TryGetValue(circuitId, out CacheEntry entry))
            {
                // Mark the entry as invalid.
                entry.TokenSource.Cancel();
                host = entry.Host;
                return true;
            }

            host = null;
            return false;
        }

        private void OnEntryEvicted(object key, object value, EvictionReason reason, object state)
        {
            if (reason == EvictionReason.Removed || reason == EvictionReason.Replaced || reason == EvictionReason.TokenExpired)
            {
                // If we were responsible for invalidating the entry, ignore.
                return;
            }

            // For every thing else, notify the user.
            var entry = (CacheEntry)value;

            // Fire off a dispose. We don't need to wait for it (?)
            _ = entry.Host.DisposeAsync();
        }

        private class CacheEntry
        {
            public CacheEntry(CircuitHost host, CancellationTokenSource tokenSource)
            {
                Host = host;
                TokenSource = tokenSource;
            }

            public CircuitHost Host { get; }
            public CancellationTokenSource TokenSource { get; }
        }
    }
}
