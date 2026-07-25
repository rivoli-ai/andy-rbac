using System.Threading.Channels;
using Andy.Rbac.Models;

namespace Andy.Rbac.Api.Services;

/// <summary>
/// Configuration for the buffered audit writer.
/// Section name: <c>Audit</c>.
/// </summary>
public sealed class RbacAuditOptions
{
    public const string SectionName = "Audit";

    /// <summary>
    /// Maximum buffered audit records. Once full, the oldest queued record is
    /// dropped rather than blocking the request — an authorization decision
    /// must never wait on audit capacity.
    /// </summary>
    public int Capacity { get; set; } = 10_000;

    /// <summary>Maximum records written per database round trip.</summary>
    public int BatchSize { get; set; } = 200;

    /// <summary>
    /// How long the writer waits for a batch to fill before flushing what it
    /// has. Bounds worst-case visibility lag for a low-traffic service.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Accepts audit records without touching the database on the calling thread.
///
/// Issue #124: <c>PermissionEvaluator</c> used to insert a row and call
/// <c>SaveChangesAsync</c> on every permission check, allowed or denied — a
/// write plus its own transaction on the hottest path in the service, and
/// <c>CheckAnyPermission</c> multiplied it by the number of permissions tried.
/// It also meant the check path could never be served from a read replica.
///
/// The trade is explicit: records buffered at the moment of a crash are lost,
/// and a sustained burst past <see cref="RbacAuditOptions.Capacity"/> drops
/// records rather than slowing requests down. Drops are counted and logged so
/// the loss is visible rather than silent.
/// </summary>
public interface IRbacAuditSink
{
    /// <summary>
    /// Queues a record. Returns false if it was dropped. Never blocks, never
    /// throws — auditing must not change an authorization outcome.
    /// </summary>
    bool TryWrite(RbacAuditLog entry);
}

/// <inheritdoc />
public sealed class ChannelRbacAuditSink : IRbacAuditSink
{
    private readonly Channel<RbacAuditLog> _channel;
    private readonly ILogger<ChannelRbacAuditSink> _logger;
    private long _dropped;

    public ChannelRbacAuditSink(RbacAuditOptions options, ILogger<ChannelRbacAuditSink> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<RbacAuditLog>(new BoundedChannelOptions(options.Capacity)
        {
            // Drop the oldest rather than the newest: recent decisions are the
            // ones an operator is likely investigating.
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>Records dropped since startup, for tests and diagnostics.</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    public ChannelReader<RbacAuditLog> Reader => _channel.Reader;

    public bool TryWrite(RbacAuditLog entry)
    {
        if (_channel.Writer.TryWrite(entry))
            return true;

        // DropOldest means TryWrite effectively always succeeds while the
        // channel is open, so reaching here means it has been completed —
        // shutdown in progress.
        var dropped = Interlocked.Increment(ref _dropped);
        if (dropped == 1 || dropped % 1000 == 0)
        {
            _logger.LogWarning(
                "Dropped {Dropped} RBAC audit record(s); the audit buffer is closed or saturated.",
                dropped);
        }

        return false;
    }

    /// <summary>Signals that no further records will be queued.</summary>
    public void Complete() => _channel.Writer.TryComplete();
}

/// <summary>
/// Discards audit records. Used when no sink is wired — direct unit
/// construction of <see cref="PermissionEvaluator"/> — so that auditing is
/// never the reason a permission check behaves differently.
/// </summary>
public sealed class NullRbacAuditSink : IRbacAuditSink
{
    public static readonly NullRbacAuditSink Instance = new();

    public bool TryWrite(RbacAuditLog entry) => false;
}
