// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Browser;
using Microsoft.AspNetCore.Components.Browser.Rendering;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Components.Server
{
    public class ComponentsHubTest
    {
        [Fact]
        public async Task StartCircuit_RegistersCircuitInHub()
        {
            // Arrange
            var registry = new CircuitRegistry(Options.Create(new ComponentsServerOptions()), NullLogger<CircuitRegistry>.Instance);
            var hub = GetHub(registry);

            // Act
            await hub.StartCircuit("http://www.example.com/test/", "http://www.example.com/");

            // Assert
            var host = Assert.Single(registry.ActiveCircuits.Values);
            Assert.True(host.CircuitClient.Connected);
        }

        [Fact]
        public async Task OnDisconnected_DecactivatesCircuit()
        {
            // Arrange
            var registry = new CircuitRegistry(Options.Create(new ComponentsServerOptions()), NullLogger<CircuitRegistry>.Instance);
            var hub = GetHub(registry);

            // Act
            await hub.StartCircuit("http://www.example.com/test/", "http://www.example.com/");
            var host = Assert.Single(registry.ActiveCircuits.Values);

            await hub.OnDisconnectedAsync(null);

            // Assert
            Assert.Empty(registry.ActiveCircuits.Values);
            Assert.True(registry.InactiveCircuits.TryGetValue(host.CircuitId, out var actual));
            Assert.Same(host, actual);
            Assert.False(host.CircuitClient.Connected);
        }

        [Fact]
        public async Task ConnectCircuit_ActivatesCircuit()
        {
            // Arrange
            var registry = new CircuitRegistry(Options.Create(new ComponentsServerOptions()), NullLogger<CircuitRegistry>.Instance);
            var hub = GetHub(registry);
            var circuit = TestCircuitHost.Create(connectionId: hub.Context.ConnectionId);
            circuit.CircuitClient.Connected = false;
            registry.InactiveCircuits.Set(circuit.CircuitId, circuit, new MemoryCacheEntryOptions { Size = 1 });

            // Act
            var success = await hub.ConnectCircuit(circuit.CircuitId);

            // Assert
            Assert.True(success);
            var host = Assert.Single(registry.ActiveCircuits.Values);
            Assert.True(host.CircuitClient.Connected);
            Assert.False(registry.InactiveCircuits.TryGetValue(host.CircuitId, out _));
        }

        [Fact]
        public async Task ConnectCircuit_WritesBufferedRender()
        {
            // Arrange
            var registry = new CircuitRegistry(Options.Create(new ComponentsServerOptions()), NullLogger<CircuitRegistry>.Instance);
            var hub = GetHub(registry);
            var renderer = new Mock<RemoteRenderer>(
                Mock.Of<IServiceProvider>(),
                new RendererRegistry(),
                Mock.Of<IJSRuntime>(),
                new CircuitClientProxy(Mock.Of<IClientProxy>(), "con"),
                Mock.Of<IDispatcher>(),
                NullLogger.Instance);

            var circuit = TestCircuitHost.Create(remoteRenderer: renderer.Object, connectionId: hub.Context.ConnectionId);
            circuit.CircuitClient.Connected = false;
            registry.InactiveCircuits.Set(circuit.CircuitId, circuit, new MemoryCacheEntryOptions { Size = 1 });

            // Act
            var success = await hub.ConnectCircuit(circuit.CircuitId);

            // Assert
            Assert.True(success);
            renderer.Verify(r => r.DispatchBufferedRenderAsync(), Times.Once());
        }

        [Fact]
        public async Task DisconnectThatHappensAfterClientReconnect()
        {
            // Arrange
            var tcs = new TaskCompletionSource<int>();
            var registry = new CircuitRegistry(Options.Create(new ComponentsServerOptions()), NullLogger<CircuitRegistry>.Instance);

            var hub1 = GetHub(registry);
            var circuit = TestCircuitHost.Create(connectionId: "old-connection-id");
            hub1.CircuitHost = circuit;
            hub1.OnBeforeActivate = tcs.Task;

            var hub2 = GetHub(registry);
            hub2.CircuitHost = circuit;

            circuit.CircuitClient.Connected = false;
            registry.InactiveCircuits.Set(circuit.CircuitId, circuit, new MemoryCacheEntryOptions { Size = 1 });

            // Act
            var connectCircuit = hub1.ConnectCircuit(circuit.CircuitId);
            var disconnectCircuit = hub2.OnDisconnectedAsync(null);
            tcs.SetResult(0);
            await Task.WhenAll(connectCircuit, disconnectCircuit);
            var success = await connectCircuit;

            // Assert
            Assert.True(success);
            var host = Assert.Single(registry.ActiveCircuits.Values);
            Assert.True(host.CircuitClient.Connected);
            Assert.False(registry.InactiveCircuits.TryGetValue(host.CircuitId, out _));
        }

        [Fact]
        public async Task ConnectWhileDisconnectInProgress()
        {
            // Arrange
            var tcs = new TaskCompletionSource<int>();
            var circuit = TestCircuitHost.Create(connectionId: "old-connection-id");
            var registry = new CircuitRegistry(Options.Create(new ComponentsServerOptions()), NullLogger<CircuitRegistry>.Instance);
            registry.Register(circuit);

            var hub1 = GetHub(registry);
            hub1.CircuitHost = circuit;

            var hub2 = GetHub(registry);
            hub2.CircuitHost = circuit;
            hub2.OnBeforeDeactivate = tcs.Task;

            // Act
            var disconnectCircuit = hub2.OnDisconnectedAsync(null);
            var connectCircuit = hub1.ConnectCircuit(circuit.CircuitId);
            tcs.SetResult(0);
            await Task.WhenAll(connectCircuit, disconnectCircuit);
            var success = await connectCircuit;

            // Assert
            Assert.True(success);
            var host = Assert.Single(registry.ActiveCircuits.Values);
            Assert.True(host.CircuitClient.Connected);
            Assert.False(registry.InactiveCircuits.TryGetValue(host.CircuitId, out _));
        }

        [Fact]
        public async Task StartCircuit_ExecutesCircuitHandlerEvents()
        {
            // Arrange
            var handler = new Mock<CircuitHandler> { CallBase = true };
            var host = TestCircuitHost.Create(handlers: new[] { handler.Object });
            var circuitFactory = Mock.Of<CircuitFactory>(f => f.CreateCircuitHost(It.IsAny<HubCallerContext>(), It.IsAny<IClientProxy>()) == host);
            var hub = GetHub(circuitFactory: circuitFactory);

            // Act
            await hub.StartCircuit("http://www.example.com/test/", "http://www.example.com/");

            // Assert
            handler.Verify(v => v.OnCircuitOpenedAsync(It.IsAny<Circuit>(), It.IsAny<CancellationToken>()), Times.Once());
            handler.Verify(v => v.OnConnectionUpAsync(It.IsAny<Circuit>(), It.IsAny<CancellationToken>()), Times.Once());
            handler.Verify(v => v.OnConnectionDownAsync(It.IsAny<Circuit>(), It.IsAny<CancellationToken>()), Times.Never());
            handler.Verify(v => v.OnCircuitClosedAsync(It.IsAny<Circuit>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [Fact]
        public async Task ConnectCircuit_ExecutesCircuitHandlerEvents()
        {
            // Arrange
            var registry = new CircuitRegistry(Options.Create(new ComponentsServerOptions()), NullLogger<CircuitRegistry>.Instance);
            var handler = new Mock<CircuitHandler> { CallBase = true };
            var host = TestCircuitHost.Create(handlers: new[] { handler.Object });
            var hub = GetHub(registry);
            registry.Register(host);

            // Act
            await hub.ConnectCircuit(host.CircuitId);

            // Assert
            handler.Verify(v => v.OnCircuitOpenedAsync(It.IsAny<Circuit>(), It.IsAny<CancellationToken>()), Times.Never());
            handler.Verify(v => v.OnConnectionUpAsync(It.IsAny<Circuit>(), It.IsAny<CancellationToken>()), Times.Once());
            handler.Verify(v => v.OnConnectionDownAsync(It.IsAny<Circuit>(), It.IsAny<CancellationToken>()), Times.Never());
            handler.Verify(v => v.OnCircuitClosedAsync(It.IsAny<Circuit>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [Fact]
        public async Task OnDisconnectedAsync_ExecutesCircuitHandlerEvents()
        {
            // Arrange
            var handler = new Mock<CircuitHandler> { CallBase = true };
            var hub = GetHub();
            hub.CircuitHost = TestCircuitHost.Create(handlers: new[] { handler.Object });

            // Act
            await hub.OnDisconnectedAsync(null);

            // Assert
            handler.Verify(v => v.OnCircuitOpenedAsync(It.IsAny<Circuit>(), It.IsAny<CancellationToken>()), Times.Never());
            handler.Verify(v => v.OnConnectionUpAsync(It.IsAny<Circuit>(), It.IsAny<CancellationToken>()), Times.Never());
            handler.Verify(v => v.OnConnectionDownAsync(It.IsAny<Circuit>(), It.IsAny<CancellationToken>()), Times.Once());
            handler.Verify(v => v.OnCircuitClosedAsync(It.IsAny<Circuit>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        private static ComponentsHub GetHub(
            CircuitRegistry registry = null,
            CircuitFactory circuitFactory = null)
        {
            registry = registry ?? new CircuitRegistry(Options.Create(new ComponentsServerOptions()), NullLogger<CircuitRegistry>.Instance);
            circuitFactory = circuitFactory ?? new TestCircuitFactory();

            var serviceProvider = new ServiceCollection()
                .AddSingleton(registry)
                .AddSingleton(circuitFactory)
                .AddSingleton(new RemoteUriHelper(Mock.Of<IJSRuntime>()))
                .BuildServiceProvider();

            var hub = new ComponentsHub(serviceProvider, NullLogger<ComponentsHub>.Instance)
            {
                Context = new TestHubCallerContext(),
            };
            hub.Clients = new HubCallerClients(Mock.Of<IHubClients>(), hub.Context.ConnectionId);
            return hub;
        }

        private class TestCircuitFactory : CircuitFactory
        {
            public override CircuitHost CreateCircuitHost(HubCallerContext hubContext, IClientProxy client)
                => TestCircuitHost.Create(connectionId: hubContext.ConnectionId);
        }

        private class TestHubCallerContext : HubCallerContext
        {
            public override string ConnectionId { get; } = Guid.NewGuid().ToString();
            public override string UserIdentifier { get; }
            public override ClaimsPrincipal User { get; }
            public override IDictionary<object, object> Items { get; } = new Dictionary<object, object>();
            public override IFeatureCollection Features { get; } = new FeatureCollection();
            public override CancellationToken ConnectionAborted { get; }

            public override void Abort()
            {
                throw new NotImplementedException();
            }
        }
    }
}
