// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.Rbac.Api.Services;

// SM.2.11 — background worker that performs the server-side grant expiry
// sweep. Polls at a configurable interval (default: 60 s). On each tick it
// delegates to GrantService.SweepExpiredGrantsAsync, which:
//   1. Removes InstancePermission rows whose ExpiresAt ≤ now.
//   2. Stages a grant.expired outbox row inside the same transaction.
//   3. The OutboxDispatcher publishes the row to NATS → Conductor receives
//      the push and reconciles the PermissionGrant aggregate without any
//      client involvement.
//
// This decouples expiry notification from client activity: a grant that
// expires while Conductor is idle is invalidated the next sweep tick —
// not on the next gate consultation. This closes the stale-grant
// disagreement class documented in conductor#1861 / SM.2.11.
public sealed class GrantExpiryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GrantExpiryWorker> _logger;
    private readonly TimeSpan _sweepInterval;

    public GrantExpiryWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<GrantExpiryWorker> logger,
        IOptions<GrantExpiryWorkerOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _sweepInterval = options.Value.SweepInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "GrantExpiryWorker started; sweep interval = {Interval}",
            _sweepInterval);

        // Stagger the first sweep slightly so startup database migrations
        // have time to complete before we hit the DB.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IGrantService>();
                var swept = await service.SweepExpiredGrantsAsync(stoppingToken);
                if (swept > 0)
                {
                    _logger.LogInformation(
                        "GrantExpiryWorker sweep complete: {Count} grant(s) expired and pushed",
                        swept);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GrantExpiryWorker sweep failed; will retry after {Interval}", _sweepInterval);
            }

            await Task.Delay(_sweepInterval, stoppingToken);
        }

        _logger.LogInformation("GrantExpiryWorker stopped");
    }
}

/// <summary>
/// Configuration options for <see cref="GrantExpiryWorker"/>.
/// Section name: <c>GrantExpiry</c>.
/// </summary>
public sealed class GrantExpiryWorkerOptions
{
    public const string SectionName = "GrantExpiry";

    /// <summary>
    /// How often the worker sweeps for expired grants.
    /// Default: 60 seconds. Must be ≥ 5 seconds.
    /// </summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(60);
}
