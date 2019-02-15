// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Components.Server.Circuits
{
    public class CircuitRegistryTest
    {
        [Fact]
        public void Register_AddsCircuit()
        {
            // Arrange
            var registry = new CircuitRegistry(
                Options.Create(new ComponentsServerOptions()),
                NullLogger<CircuitRegistry>.Instance);
            var circuitHost = TestCircuitHost.Create();

            // Act
            registry.Register(circuitHost);

            // Assert
            var actual = Assert.Single(registry.ActiveCircuits.Values);
            Assert.Same(circuitHost, actual);
        }

        [Fact]
        public void TryActivate_TransfersClientOnActiveCircuit()
        {
            // Arrange
            var registry = new CircuitRegistry(
                Options.Create(new ComponentsServerOptions()),
                NullLogger<CircuitRegistry>.Instance);
            var circuitHost = TestCircuitHost.Create();
            registry.Register(circuitHost);

            var newClient = Mock.Of<IClientProxy>();
            var newConnectionId = "new-id";

            // Act
            var result = registry.TryActivate(circuitHost.CircuitId, newClient, newConnectionId, out var activated);

            // Assert
            Assert.True(result);
            Assert.Same(circuitHost, activated);
            Assert.Same(newClient, circuitHost.CircuitClient.Client);
            Assert.Same(newConnectionId, circuitHost.CircuitClient.ConnectionId);

            var actual = Assert.Single(registry.ActiveCircuits.Values);
            Assert.Same(circuitHost, actual);
        }

        [Fact]
        public void TryActivate_MakesInactiveCircuitActive()
        {
            // Arrange
            var registry = new CircuitRegistry(
                Options.Create(new ComponentsServerOptions()),
                NullLogger<CircuitRegistry>.Instance);
            var circuitHost = TestCircuitHost.Create();
            registry.InactiveCircuits.Set(circuitHost.CircuitId, circuitHost, new MemoryCacheEntryOptions { Size = 1 });

            var newClient = Mock.Of<IClientProxy>();
            var newConnectionId = "new-id";

            // Act
            var result = registry.TryActivate(circuitHost.CircuitId, newClient, newConnectionId, out var activated);

            // Assert
            Assert.True(result);
            Assert.Same(circuitHost, activated);
            Assert.Same(newClient, circuitHost.CircuitClient.Client);
            Assert.Same(newConnectionId, circuitHost.CircuitClient.ConnectionId);

            var actual = Assert.Single(registry.ActiveCircuits.Values);
            Assert.Same(circuitHost, actual);
            Assert.False(registry.InactiveCircuits.TryGetValue(circuitHost.CircuitId, out _));
        }

        [Fact]
        public void Deactivate_DoesNothing_IfCircuitIsInactive()
        {
            // Arrange
            var registry = new CircuitRegistry(
                Options.Create(new ComponentsServerOptions()),
                NullLogger<CircuitRegistry>.Instance);
            var circuitHost = TestCircuitHost.Create();
            registry.InactiveCircuits.Set(circuitHost.CircuitId, circuitHost, new MemoryCacheEntryOptions { Size = 1 });

            // Act
            registry.Deactivate(circuitHost, circuitHost.CircuitClient.ConnectionId);

            // Assert
            Assert.Empty(registry.ActiveCircuits.Values);
            Assert.True(registry.InactiveCircuits.TryGetValue(circuitHost.CircuitId, out _));
        }
    }
}
