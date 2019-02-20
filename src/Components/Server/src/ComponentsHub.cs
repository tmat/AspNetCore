// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Components.Server
{
    /// <summary>
    /// A SignalR hub that accepts connections to an ASP.NET Core Components application.
    /// </summary>
    public sealed class ComponentsHub : Hub
    {
        private static readonly TimeSpan LockAcquisitionTimeout = TimeSpan.FromSeconds(10);
        private static readonly SemaphoreSlim CircuitRegistryLock = new SemaphoreSlim(1);
        private static readonly object CircuitKey = new object();
        private readonly CircuitFactory _circuitFactory;
        private readonly CircuitRegistry _circuitRegistry;
        private readonly ILogger _logger;

        /// <summary>
        /// Intended for framework use only. Applications should not instantiate
        /// this class directly.
        /// </summary>
        public ComponentsHub(IServiceProvider services, ILogger<ComponentsHub> logger)
        {
            _circuitFactory = services.GetRequiredService<CircuitFactory>();
            _circuitRegistry = services.GetRequiredService<CircuitRegistry>();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the default endpoint path for incoming connections.
        /// </summary>
        public static PathString DefaultPath { get; } = "/_blazor";

        /// <summary>
        /// For unit testing only.
        /// </summary>
        internal CircuitHost CircuitHost
        {
            get => (CircuitHost)Context.Items[CircuitKey];
            set => Context.Items[CircuitKey] = value;
        }

        // For unit testing
        internal Task OnBeforeDeactivate { get; set; } = Task.CompletedTask;
        internal Task OnBeforeActivate { get; set; } = Task.CompletedTask;

        /// <summary>
        /// Intended for framework use only. Applications should not call this method directly.
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var circuitHost = CircuitHost;
            if (circuitHost == null)
            {
                return;
            }

            CircuitHost = null;
            try
            {
                if (!await AcquireCircuitLock())
                {
                    // If we're unable to enter the critical section, clear this instance and throw.
                    await circuitHost.DisposeAsync();

                    throw new InvalidOperationException("Failed to acqiure a lock to de-activate the CircuitHost instance");
                }

                await OnBeforeDeactivate;

                _circuitRegistry.Deactivate(circuitHost, Context.ConnectionId);
            }
            finally
            {
                CircuitRegistryLock.Release();
            }

            await circuitHost.OnConnectionDownAsync();
        }

        /// <summary>
        /// Intended for framework use only. Applications should not call this method directly.
        /// </summary>
        public async Task<string> StartCircuit(string uriAbsolute, string baseUriAbsolute)
        {
            var circuitHost = _circuitFactory.CreateCircuitHost(Context, Clients.Caller);
            circuitHost.UnhandledException += CircuitHost_UnhandledException;
            circuitHost.RemoteUriHelper.Initialize(uriAbsolute, baseUriAbsolute);

            // If initialization fails, this will throw. The caller will fail if they try to call into any interop API.
            await circuitHost.InitializeAsync(Context.ConnectionAborted);

            _circuitRegistry.Register(circuitHost);

            CircuitHost = circuitHost;

            return circuitHost.CircuitId;
        }

        /// <summary>
        /// Intended for framework use only. Applications should not call this method directly.
        /// </summary>
        public async Task<bool> ConnectCircuit(string circuitId)
        {
            CircuitHost circuitHost;
            try
            {
                if (!await AcquireCircuitLock())
                {
                    return false;
                }

                await OnBeforeActivate;

                if (!_circuitRegistry.TryActivate(circuitId, Clients.Caller, Context.ConnectionId, out circuitHost))
                {
                    return false;
                }

                CircuitHost = circuitHost;
            }
            finally
            {
                CircuitRegistryLock.Release();
            }

            await circuitHost.OnConnectionUpAsync(Context.ConnectionAborted);
            // Dispatch buffered renders, but don't wait for it to finish.
            _ = circuitHost.Renderer.DispatchBufferedRenderAsync();
            return true;
        }

        /// <summary>
        /// Intended for framework use only. Applications should not call this method directly.
        /// </summary>
        public void BeginInvokeDotNetFromJS(string callId, string assemblyName, string methodIdentifier, long dotNetObjectId, string argsJson)
        {
            EnsureCircuitHost().BeginInvokeDotNetFromJS(callId, assemblyName, methodIdentifier, dotNetObjectId, argsJson);
        }

        /// <summary>
        /// Intended for framework use only. Applications should not call this method directly.
        /// </summary>
        public void OnRenderCompleted(long renderId, string errorMessageOrNull)
        {
            EnsureCircuitHost().Renderer.OnRenderCompleted(renderId, errorMessageOrNull);
        }

        private async void CircuitHost_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var circuitHost = (CircuitHost)sender;
            try
            {
                _logger.LogWarning((Exception)e.ExceptionObject, "Unhandled Server-Side exception");
                await circuitHost.CircuitClient.Client.SendAsync("JS.Error", e.ExceptionObject);

                // We generally can't abort the connection here since this is an async
                // callback. The Hub has already been torn down. We'll rely on the
                // client to abort the connection if we successfully transmit an error.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to transmit exception to client");
            }
        }

        private CircuitHost EnsureCircuitHost()
        {
            var circuitHost = CircuitHost;
            if (circuitHost == null)
            {
                var message = "The circuit state is invalid. This is due to an exception thrown during initialization or due to a timeout when reconnecting to it.";
                throw new InvalidOperationException(message);
            }

            return circuitHost;
        }

        private static Task<bool> AcquireCircuitLock()
        {
            var timeout =
#if DEBUG
                Debugger.IsAttached ? TimeSpan.FromMilliseconds(-1) : LockAcquisitionTimeout;
#else
                LockAcquisitionTimeout;
#endif
            return CircuitRegistryLock.WaitAsync(timeout);
        }
    }
}
