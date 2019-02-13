// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.AspNetCore.Components.Server.Circuits
{
    public class DisconnectedCircuitRegistryTest
    {
        [Fact]
        public void AddInactiveCircuit_AddsCacheEntry()
        {
            // Arrange
            var registry = new DisconnectedCircuitRegistry(
                Options.Create(new ComponentsServerOptions()),
                NullLogger<DisconnectedCircuitRegistry>.Instance);
            var circuitHost = TestCircuitHost.Create();

            // Act
            registry.AddInactiveCircuit(circuitHost);

            // Assert
            Assert.Equal(1, registry.MemoryCache.Count);
            Assert.True(registry.MemoryCache.TryGetValue(circuitHost.CircuitId, out var value));
            Assert.Same(circuitHost, value);
        }
    }
}
