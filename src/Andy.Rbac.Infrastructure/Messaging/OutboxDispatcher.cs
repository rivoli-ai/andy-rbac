// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Andy.Rbac.Messaging;
using Andy.Rbac.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.Rbac.Infrastructure.Messaging;

// Background worker that drains the OutboxEntry table to the message bus.
// One instance per service. Polls at a configurable interval, batches
// pending rows, publishes each to its target subject, records success
// or failure. Rows are never deleted — the outbox doubles as an audit
// log. A separate retention policy may purge published rows older than
// N days.
//
// Retry semantics: on publish failure the row stays pending with
// AttemptCount incremented and LastError set. The dispatcher respects
// exponential backoff and dead-letters rows after MaxAttempts so a
// poison message cannot spin the worker indefinitely.
public sealed class OutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxDispatcher> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly int _batchSize;
    private readonly int _maxAttempts;
    private readonly TimeSpan _initialRetryDelay;
    private readonly TimeSpan _maxRetryDelay;

    public OutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxDispatcher> logger,
        IOptions<OutboxDispatcherOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _pollInterval = options.Value.PollInterval;
        _batchSize = options.Value.BatchSize;
        _maxAttempts = options.Value.MaxAttempts;
        _initialRetryDelay = options.Value.InitialRetryDelay;
        _maxRetryDelay = options.Value.MaxRetryDelay;

        if (_batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(options), "BatchSize must be positive");
        if (_maxAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(options), "MaxAttempts must be positive");
        if (_initialRetryDelay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options), "InitialRetryDelay must be positive");
        if (_maxRetryDelay < _initialRetryDelay) throw new ArgumentOutOfRangeException(nameof(options), "MaxRetryDelay must not be shorter than InitialRetryDelay");

        // AK2: ADR-0001 op invariant — PollInterval ≤ 2s across services.
        if (_pollInterval > OutboxDispatcherOptions.MaxRecommendedPollInterval)
        {
            _logger.LogWarning(
                "OutboxDispatcher Messaging:Outbox:PollInterval {Interval} exceeds the recommended maximum {Max}. " +
                "Outbound publish latency will be at least one poll interval; values above 2s violate ADR-0001 operational invariants.",
                _pollInterval, OutboxDispatcherOptions.MaxRecommendedPollInterval);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxDispatcher started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var drained = await DrainOnceAsync(stoppingToken);
                if (drained == 0)
                {
                    await Task.Delay(_pollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OutboxDispatcher tick failed; backing off");
                await Task.Delay(_pollInterval, stoppingToken);
            }
        }

        _logger.LogInformation("OutboxDispatcher stopped");
    }

    internal async Task<int> DrainOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RbacDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var now = DateTimeOffset.UtcNow;
        var pending = await db.Set<OutboxEntry>()
            .Where(e => e.PublishedAt == null && e.DeadLetteredAt == null)
            .Where(e => e.NextAttemptAt == null || e.NextAttemptAt <= now)
            .OrderBy(e => e.CreatedAt)
            .Take(_batchSize)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            return 0;
        }

        foreach (var entry in pending)
        {
            try
            {
                var headers = new MessageHeaders(
                    MsgId: entry.Id,
                    CorrelationId: entry.CorrelationId,
                    CausationId: entry.CausationId,
                    Generation: entry.Generation);

                // Payload is stored as JSON text, but IMessageBus.PublishAsync
                // expects an object (it re-serializes). For the stub we pass
                // a JsonDocument so the bus has something to serialize back.
                // The real implementation will take a raw-bytes overload to
                // avoid the round trip.
                using var doc = JsonDocument.Parse(entry.PayloadJson);
                await bus.PublishAsync(entry.Subject, doc.RootElement, headers, ct);

                entry.PublishedAt = DateTimeOffset.UtcNow;
                entry.NextAttemptAt = null;
                entry.LastError = null;
            }
            catch (Exception ex)
            {
                entry.AttemptCount++;
                entry.LastAttemptAt = DateTimeOffset.UtcNow;
                entry.LastError = ex.Message;
                if (entry.AttemptCount >= _maxAttempts)
                {
                    entry.DeadLetteredAt = entry.LastAttemptAt;
                    entry.NextAttemptAt = null;
                    _logger.LogError(ex,
                        "Outbox entry {EntryId} exhausted {AttemptCount} attempts and was dead-lettered",
                        entry.Id, entry.AttemptCount);
                }
                else
                {
                    var exponent = Math.Min(entry.AttemptCount - 1, 30);
                    var delayTicks = Math.Min(
                        _initialRetryDelay.Ticks * Math.Pow(2, exponent),
                        _maxRetryDelay.Ticks);
                    entry.NextAttemptAt = entry.LastAttemptAt.Value.AddTicks((long)delayTicks);
                    _logger.LogWarning(ex,
                        "Outbox entry {EntryId} publish failed (attempt {Attempt}); next attempt at {NextAttemptAt}",
                        entry.Id, entry.AttemptCount, entry.NextAttemptAt);
                }
            }
        }

        await db.SaveChangesAsync(ct);
        return pending.Count;
    }
}
