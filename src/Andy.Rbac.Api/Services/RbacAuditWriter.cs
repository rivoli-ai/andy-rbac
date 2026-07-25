using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Models;
using Microsoft.Extensions.Options;

namespace Andy.Rbac.Api.Services;

/// <summary>
/// Drains <see cref="ChannelRbacAuditSink"/> into the database in batches, off
/// the request path (#124).
///
/// Batching is what removes the write amplification: a burst of checks that
/// used to cost one transaction each now costs one per batch. On shutdown the
/// channel is completed and the remainder flushed, so an orderly stop loses
/// nothing.
/// </summary>
public sealed class RbacAuditWriter : BackgroundService
{
    private readonly ChannelRbacAuditSink _sink;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RbacAuditOptions _options;
    private readonly ILogger<RbacAuditWriter> _logger;

    public RbacAuditWriter(
        ChannelRbacAuditSink sink,
        IServiceScopeFactory scopeFactory,
        IOptions<RbacAuditOptions> options,
        ILogger<RbacAuditWriter> logger)
    {
        _sink = sink;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;

        if (_options.BatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Audit:BatchSize must be positive");
        if (_options.Capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Audit:Capacity must be positive");
        if (_options.FlushInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Audit:FlushInterval must be positive");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "RbacAuditWriter started; capacity={Capacity} batch={BatchSize} flush={FlushInterval}",
            _options.Capacity, _options.BatchSize, _options.FlushInterval);

        var batch = new List<RbacAuditLog>(_options.BatchSize);

        try
        {
            while (await _sink.Reader.WaitToReadAsync(stoppingToken))
            {
                batch.Clear();
                FillBatch(batch);

                if (batch.Count > 0)
                    await FlushAsync(batch);

                // A partially-filled batch means the queue has drained; pause
                // briefly so the next flush can coalesce rather than issuing a
                // round trip per record.
                if (batch.Count < _options.BatchSize)
                    await Task.Delay(_options.FlushInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down — fall through to the final drain.
        }

        await DrainRemainingAsync();
        _logger.LogInformation("RbacAuditWriter stopped");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // No further records; the loop above drains what is already queued.
        _sink.Complete();
        await base.StopAsync(cancellationToken);
    }

    private void FillBatch(List<RbacAuditLog> batch)
    {
        while (batch.Count < _options.BatchSize && _sink.Reader.TryRead(out var entry))
            batch.Add(entry);
    }

    private async Task DrainRemainingAsync()
    {
        var batch = new List<RbacAuditLog>(_options.BatchSize);
        while (true)
        {
            batch.Clear();
            FillBatch(batch);
            if (batch.Count == 0)
                return;

            await FlushAsync(batch);
        }
    }

    /// <summary>
    /// Persists a batch. Deliberately not cancellable: reading a record off the
    /// channel already removed it, so abandoning the write loses it outright.
    ///
    /// Passing the stopping token here meant that at shutdown the in-flight
    /// batch failed with TaskCanceledException, and because the records were no
    /// longer in the channel the final drain had nothing left to retry — an
    /// orderly stop silently discarded up to one batch. The write is a single
    /// short insert; letting it finish is cheaper than losing the records.
    /// </summary>
    private async Task FlushAsync(IReadOnlyList<RbacAuditLog> batch)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RbacDbContext>();
            db.AuditLogs.AddRange(batch);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Losing audit records is bad; failing the writer loop is worse,
            // since it would lose every subsequent record too.
            _logger.LogError(ex, "Failed to persist {Count} RBAC audit record(s)", batch.Count);
        }
    }
}
