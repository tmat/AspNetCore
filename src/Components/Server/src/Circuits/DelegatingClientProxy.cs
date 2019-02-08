// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace Microsoft.AspNetCore.Components.Server.Circuits
{
    internal class DelegatingClientProxy : IClientProxy
    {
        public IClientProxy Client { get; set; }

        public Task SendCoreAsync(string method, object[] args, CancellationToken cancellationToken = default)
            => Client.SendCoreAsync(method, args, cancellationToken);
    }
}
