// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.AspNetCore.Components.Browser;
using Microsoft.AspNetCore.Components.Browser.Rendering;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Components.Server.Circuits
{
    internal class DefaultCircuitFactory : CircuitFactory
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DefaultCircuitFactoryOptions _options;
        private readonly ILoggerFactory _loggerFactory;

        public DefaultCircuitFactory(
            IServiceScopeFactory scopeFactory,
            IOptions<DefaultCircuitFactoryOptions> options,
            ILoggerFactory loggerFactory)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _options = options.Value;
            _loggerFactory = loggerFactory;
        }

        public override CircuitHost CreateCircuitHost(HubCallerContext hubContext, IClientProxy client)
        {
            var httpContext = hubContext.GetHttpContext();
            if (!_options.StartupActions.TryGetValue(httpContext.Request.Path, out var config))
            {
                var message = $"Could not find an ASP.NET Core Components startup action for request path '{httpContext.Request.Path}'.";
                throw new InvalidOperationException(message);
            }

            var circuitClient = new CircuitClientProxy(client, hubContext.ConnectionId);
            var scope = _scopeFactory.CreateScope();
            var jsRuntime = new RemoteJSRuntime(circuitClient);
            var uriHelper = new RemoteUriHelper(jsRuntime);
            var rendererRegistry = new RendererRegistry();
            var dispatcher = Renderer.CreateDefaultDispatcher();
            var renderer = new RemoteRenderer(
                scope.ServiceProvider,
                rendererRegistry,
                jsRuntime,
                circuitClient,
                dispatcher,
                _loggerFactory.CreateLogger<RemoteRenderer>());

            var circuitHandlers = scope.ServiceProvider.GetServices<CircuitHandler>()
                .OrderBy(h => h.Order)
                .ToArray();


            var circuitHost = new CircuitHost(
                scope,
                circuitClient,
                rendererRegistry,
                renderer,
                jsRuntime,
                uriHelper,
                config,
                circuitHandlers,
                _loggerFactory.CreateLogger<CircuitHost>());

            // Initialize per-circuit data that services need
            (scope.ServiceProvider.GetRequiredService<ICircuitAccessor>() as DefaultCircuitAccessor).Circuit = circuitHost.Circuit;
            (scope.ServiceProvider.GetRequiredService<IJSRuntimeAccessor>() as DefaultJSRuntimeAccessor).JSRuntime = jsRuntime;
            scope.ServiceProvider.GetRequiredService<UriHelperAccessor>().UriHelper = uriHelper;

            return circuitHost;
        }
    }
}
