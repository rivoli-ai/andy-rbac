// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using Andy.Rbac.Infrastructure.Data;
using Andy.Rbac.Messaging;
using Andy.Rbac.Models;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Andy.Rbac.Api.Tests;

/// <summary>
/// Tests for <see cref="SqliteSchemaBootstrapper"/> — the additive
/// schema heal that runs after <c>EnsureCreatedAsync</c> on the SQLite
/// path. EnsureCreated is a no-op on an existing database, so model
/// changes (new columns, new tables) never reach a DB created by an
/// older binary. The production incident: migration AddOutboxRetryState
/// added <c>outbox.DeadLetteredAt</c> / <c>outbox.NextAttemptAt</c> and
/// every existing embedded install crashed with
/// <c>SQLite Error 1: 'no such column: o.DeadLetteredAt'</c> in
/// OutboxDispatcher. These tests simulate that exact drift.
/// </summary>
public class SqliteSchemaBootstrapperTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CapturingLogger _logger = new();

    public SqliteSchemaBootstrapperTests()
    {
        // Shared in-memory DB: lives as long as this connection is open,
        // and every context created over it sees the same schema/data.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private RbacDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<RbacDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new RbacDbContext(options);
    }

    private void Execute(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private HashSet<string> TableColumns(string table)
    {
        var columns = new HashSet<string>(StringComparer.Ordinal);
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) columns.Add(reader.GetString(1));
        return columns;
    }

    private HashSet<string> IndexNames()
    {
        var indexes = new HashSet<string>(StringComparer.Ordinal);
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%';";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) indexes.Add(reader.GetString(0));
        return indexes;
    }

    // ── The production drift: outbox retry-state columns missing ──────

    [Fact]
    public async Task Heal_RestoresDroppedOutboxRetryColumns_AndDispatcherQueryRuns()
    {
        // Arrange: schema created by "an older binary" — EnsureCreated,
        // then strip the AddOutboxRetryState additions exactly as an old
        // DB lacks them (the index first: SQLite refuses to drop an
        // indexed column, and the old DB had neither).
        using (var db = CreateContext())
        {
            await db.Database.EnsureCreatedAsync();
            db.Outbox.Add(new OutboxEntry
            {
                Id = Guid.NewGuid(),
                Subject = "andy.rbac.events.role.test.updated",
                PayloadJson = "{}",
                CorrelationId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
        }

        Execute("DROP INDEX \"IX_outbox_PublishedAt_DeadLetteredAt_NextAttemptAt\";");
        Execute("ALTER TABLE outbox DROP COLUMN \"DeadLetteredAt\";");
        Execute("ALTER TABLE outbox DROP COLUMN \"NextAttemptAt\";");

        TableColumns("outbox").Should().NotContain(["DeadLetteredAt", "NextAttemptAt"]);

        // Sanity: without the heal (the old code path) the dispatcher's
        // hot query crashes — this is the exact production failure.
        using (var db = CreateContext())
        {
            var now = DateTimeOffset.UtcNow;
            var act = () => db.Outbox
                .Where(e => e.PublishedAt == null && e.DeadLetteredAt == null)
                .Where(e => e.NextAttemptAt == null || e.NextAttemptAt <= now)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync();
            (await act.Should().ThrowAsync<SqliteException>())
                .WithMessage("*no such column*DeadLetteredAt*");
        }

        // Act
        int healed;
        using (var db = CreateContext())
        {
            healed = await SqliteSchemaBootstrapper.HealAsync(db, _logger);
        }

        // Assert: both columns are back, the covering index is back, and
        // the OutboxDispatcher query executes and sees the pending row.
        healed.Should().BeGreaterThanOrEqualTo(3, "two columns and one index were missing");
        TableColumns("outbox").Should().Contain(["DeadLetteredAt", "NextAttemptAt"]);
        IndexNames().Should().Contain("IX_outbox_PublishedAt_DeadLetteredAt_NextAttemptAt");

        using (var db = CreateContext())
        {
            var now = DateTimeOffset.UtcNow;
            var pending = await db.Outbox
                .Where(e => e.PublishedAt == null && e.DeadLetteredAt == null)
                .Where(e => e.NextAttemptAt == null || e.NextAttemptAt <= now)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync();
            pending.Should().HaveCount(1);
        }
    }

    // ── Whole-missing-table heal ───────────────────────────────────────

    [Fact]
    public async Task Heal_RecreatesDroppedTable_WithAllModelColumns()
    {
        using (var db = CreateContext())
        {
            await db.Database.EnsureCreatedAsync();
        }

        // rbac_audit_logs is a leaf table — nothing references it.
        Execute("DROP TABLE \"rbac_audit_logs\";");
        TableColumns("rbac_audit_logs").Should().BeEmpty();

        int healed;
        using (var db = CreateContext())
        {
            healed = await SqliteSchemaBootstrapper.HealAsync(db, _logger);
        }

        healed.Should().BeGreaterThanOrEqualTo(1);
        var columns = TableColumns("rbac_audit_logs");
        columns.Should().NotBeEmpty();

        // Every column the EF model maps for RbacAuditLog must exist.
        using (var db = CreateContext())
        {
            var entityType = db.Model.FindEntityType(typeof(RbacAuditLog))!;
            var storeObject = Microsoft.EntityFrameworkCore.Metadata
                .StoreObjectIdentifier.Table("rbac_audit_logs", entityType.GetSchema());
            foreach (var property in entityType.GetProperties())
            {
                var columnName = property.GetColumnName(storeObject);
                columns.Should().Contain(columnName!);
            }

            // And the table is usable through EF again.
            db.AuditLogs.Add(new RbacAuditLog
            {
                Id = Guid.NewGuid(),
                EventType = "permission.check",
                Result = "Allow"
            });
            await db.SaveChangesAsync();
        }

        using (var db = CreateContext())
        {
            (await db.AuditLogs.CountAsync()).Should().Be(1);
        }
    }

    // ── No-op guards ───────────────────────────────────────────────────

    [Fact]
    public async Task Heal_OnFreshEmptyDatabase_DoesNothing()
    {
        // No EnsureCreated — zero user tables. Materialising the schema
        // is EnsureCreated's job; the heal must stay out of the way.
        using var db = CreateContext();
        var healed = await SqliteSchemaBootstrapper.HealAsync(db, _logger);

        healed.Should().Be(0);
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
        Convert.ToInt32(cmd.ExecuteScalar()).Should().Be(0);
    }

    [Fact]
    public async Task Heal_OnNonSqliteProvider_DoesNothing()
    {
        var options = new DbContextOptionsBuilder<RbacDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new RbacDbContext(options);

        var healed = await SqliteSchemaBootstrapper.HealAsync(db, _logger);

        healed.Should().Be(0);
        _logger.Messages.Should().BeEmpty();
    }

    // ── Refusal path ───────────────────────────────────────────────────

    [Fact]
    public async Task Heal_RefusesNonNullableColumnWithoutDefault()
    {
        using (var db = CreateContext())
        {
            await db.Database.EnsureCreatedAsync();
        }

        // outbox.Subject is a required string with no default and no
        // value converter — adding it to a populated table can't be done
        // safely, so the heal must warn and leave it alone.
        Execute("ALTER TABLE outbox DROP COLUMN \"Subject\";");

        int healed;
        using (var db = CreateContext())
        {
            healed = await SqliteSchemaBootstrapper.HealAsync(db, _logger);
        }

        healed.Should().Be(0);
        TableColumns("outbox").Should().NotContain("Subject");
        _logger.Messages.Should().Contain(m =>
            m.Contains("refusing to add non-nullable column") &&
            m.Contains("outbox") &&
            m.Contains("Subject"));
    }

    private sealed class CapturingLogger : ILogger
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Enqueue(formatter(state, exception));
    }
}
