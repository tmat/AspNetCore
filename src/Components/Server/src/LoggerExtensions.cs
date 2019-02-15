// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Components.Server
{
    internal static class LoggerExtensions
    {
        private static readonly Action<ILogger, string, Exception> _unhandledExceptionRenderingComponent;
        private static readonly Action<ILogger, string, Exception> _disposingCircuit;
        private static readonly Action<ILogger, string, Exception> _unhandledExceptionDisposingCircuitHost;
        private static readonly Action<ILogger, string, Exception> _unhandledExceptionInvokingCircuitHandler;

        static LoggerExtensions()
        {
            _unhandledExceptionRenderingComponent = LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(1, "ExceptionRenderingComponent"),
                "Unhandled exception rendering component: {Message}");

            _disposingCircuit = LoggerMessage.Define<string>(
                LogLevel.Trace,
                new EventId(2, "DisposingCircuit"),
                "Disposing circuit with identifier {CircuitId}");

            _unhandledExceptionDisposingCircuitHost = LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(3, "ExceptionDisposingCircuitHost"),
                "Unhandled exception disposing circuit host: {Message}");

            _unhandledExceptionInvokingCircuitHandler = LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(4, "ExceptionInvokingCircuitHandler"),
                "Unhandled exception invoking circuit handler: {Message}");
        }

        public static void UnhandledExceptionRenderingComponent(this ILogger logger, Exception exception)
        {
            _unhandledExceptionRenderingComponent(
                logger,
                exception.Message,
                exception);
        }

        public static void DisposingCircuit(this ILogger logger, string circuitId) => _disposingCircuit(logger, circuitId, null);

        public static void UnhandledExceptionDisposingCircuitHost(this ILogger logger, Exception exception)
        {
            _unhandledExceptionDisposingCircuitHost(
                logger,
                exception.Message,
                exception);
        }

        public static void UnhandledExceptionInvokingCircuitHandler(this ILogger logger, Exception exception)
        {
            _unhandledExceptionDisposingCircuitHost(
                logger,
                exception.Message,
                exception);
        }
    }
}
