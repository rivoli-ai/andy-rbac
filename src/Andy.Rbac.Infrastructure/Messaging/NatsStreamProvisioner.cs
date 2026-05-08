// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.JetStream.Models;

namespace Andy.Rbac.Infrastructure.Messaging;

// Ensures the JetStream streams exist before any BackgroundService
// (OutboxDispatcher, GoalCreatedHandler) starts publishing or
// subscribing. IHostedService.StartAsync runs before BackgroundService
// .ExecuteAsync, so the ordering guarantee is built into the host.
// CreateOrUpdateStreamAsync is idempotent — safe on every boot.
public sealed class NatsStreamProvisioner(
    NatsMessageBus bus,
    IOptions<NatsOptions> options,
    ILogger<NatsStreamProvisioner> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await bus.ConnectAsync(ct);

        var opts = options.Value;
        if (opts.Streams.Length == 0)
        {
            throw new InvalidOperationException(
                "Messaging:Nats:Streams is empty. At least one stream must be configured.");
        }

        foreach (var spec in opts.Streams)
        {
            var config = new StreamConfig(spec.Name, spec.Subjects)
            {
                MaxAge = spec.MaxAge
            };

            await bus.JetStream.CreateOrUpdateStreamAsync(config, ct);

            // AK5: log retention so an operator inspecting boot logs can
            // confirm the active window without querying NATS directly.
            logger.LogInformation(
                "NATS JetStream stream {Stream} provisioned with subjects [{Subjects}], retention {Retention}",
                spec.Name, string.Join(", ", spec.Subjects), spec.MaxAge);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
