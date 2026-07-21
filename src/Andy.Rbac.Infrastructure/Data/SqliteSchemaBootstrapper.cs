// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace Andy.Rbac.Infrastructure.Data;

/// <summary>
/// Heals additive schema drift on the embedded SQLite database.
///
/// Background: andy-rbac's SQLite path uses
/// <see cref="DatabaseFacade.EnsureCreatedAsync"/>, which creates the
/// schema from the live model snapshot on a fresh DB but is a no-op
/// once any table exists. Migrations on the SQLite path are not
/// expected to run (they are written for Postgres), so a user whose DB
/// was created by an older binary is stuck on whatever schema
/// EnsureCreated produced back then. When a new entity property lands
/// (e.g. <c>OutboxEntry.DeadLetteredAt</c> /
/// <c>OutboxEntry.NextAttemptAt</c> from the AddOutboxRetryState
/// migration), every query that selects it crashes with
/// <c>SQLite Error 1: 'no such column: o.DeadLetteredAt'</c> — the
/// OutboxDispatcher hot loop died on every tick on existing embedded
/// installs.
///
/// This bootstrapper closes that gap. After
/// <see cref="DatabaseFacade.EnsureCreatedAsync"/> has had a chance to
/// run, it compares the live EF model against the actual SQLite schema
/// and heals ADDITIVE drift only:
///   - creates entire tables (+ their indexes) the model declares but
///     the live DB lacks, using EnsureCreated's own generated DDL;
///   - adds columns the model declares but the live table lacks
///     (nullable / defaulted / converter-backed only — anything else is
///     refused with a warning);
///   - recreates missing indexes on existing tables.
/// It never drops, renames, or alters — those risk data loss and the
/// migrations system is the right tool for them. On a completely empty
/// DB (zero user tables) it no-ops: that is EnsureCreated's job.
/// </summary>
public static class SqliteSchemaBootstrapper
{
    /// <summary>
    /// Heals additive schema drift: first creates any tables the EF model
    /// declares that the live SQLite DB lacks (using EnsureCreated's own
    /// generated DDL), then adds any missing columns on existing tables,
    /// then recreates any missing indexes. Returns the number of healed
    /// objects (tables + columns + indexes). No-ops on non-SQLite
    /// providers and on a completely empty database.
    /// </summary>
    public static async Task<int> HealAsync(
        RbacDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsSqlite()) return 0;

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken);
        }

        var existingTables = await ReadTableNamesAsync(conn, cancellationToken);
        if (existingTables.Count == 0)
        {
            // Fresh DB — EnsureCreated materialises the full schema; nothing to heal.
            return 0;
        }

        int healed = 0;
        healed += await HealMissingTablesAsync(db, conn, existingTables, logger, cancellationToken);
        healed += await HealMissingColumnsAsync(db, conn, logger, cancellationToken);
        healed += await HealMissingIndexesAsync(db, conn, logger, cancellationToken);

        if (healed > 0)
        {
            logger.LogWarning(
                "andy-rbac SQLite schema heal complete: {Healed} object(s) added.",
                healed);
        }
        return healed;
    }

    private static async Task<int> HealMissingTablesAsync(
        RbacDbContext db,
        System.Data.Common.DbConnection conn,
        HashSet<string> existingTables,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Which model tables are absent from the live DB?
        var missing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (string.IsNullOrEmpty(tableName)) continue;
            if (!IsSafeIdentifier(tableName)) continue;
            if (!existingTables.Contains(tableName)) missing.Add(tableName);
        }
        if (missing.Count == 0) return 0;

        // EnsureCreated's own DDL — statements separated by ";" at line end.
        var statements = SplitSqlStatements(db.Database.GenerateCreateScript());

        int created = 0;
        foreach (var table in missing)
        {
            // CREATE TABLE first, then its indexes.
            var createTable = statements.FirstOrDefault(s =>
                s.StartsWith($"CREATE TABLE \"{table}\"", StringComparison.Ordinal));
            if (createTable is null)
            {
                logger.LogWarning(
                    "andy-rbac SQLite schema heal: model table {Table} missing from DB but no CREATE TABLE found in generated script; skipping.",
                    table);
                continue;
            }

            logger.LogWarning(
                "andy-rbac SQLite schema heal: creating missing table {Table}.",
                table);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = createTable;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            created++;

            foreach (var index in statements.Where(s =>
                         s.StartsWith("CREATE INDEX", StringComparison.Ordinal) ||
                         s.StartsWith("CREATE UNIQUE INDEX", StringComparison.Ordinal)))
            {
                if (!index.Contains($" ON \"{table}\" ", StringComparison.Ordinal)) continue;
                using var cmd = conn.CreateCommand();
                cmd.CommandText = index;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        return created;
    }

    /// <summary>
    /// Inspects the SQLite database and adds any column declared by the
    /// EF model that doesn't yet exist in the live schema. Only additive,
    /// safe adds are performed: the column must be nullable OR have a
    /// default OR be backed by a value converter that tolerates missing
    /// data on read. Anything else is refused with a warning.
    /// </summary>
    private static async Task<int> HealMissingColumnsAsync(
        RbacDbContext db,
        System.Data.Common.DbConnection conn,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        int healed = 0;

        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (string.IsNullOrEmpty(tableName)) continue;
            if (!IsSafeIdentifier(tableName)) continue;

            var actualColumns = await ReadColumnsAsync(conn, tableName, cancellationToken);
            if (actualColumns.Count == 0)
            {
                // Table not created yet — nothing to heal (missing tables
                // are handled by HealMissingTablesAsync).
                continue;
            }

            var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());

            foreach (var property in entityType.GetProperties())
            {
                var columnName = property.GetColumnName(storeObject);
                if (string.IsNullOrEmpty(columnName)) continue;
                if (actualColumns.Contains(columnName)) continue;
                if (!IsSafeIdentifier(columnName)) continue;

                // Only auto-add columns that are safe to add to a
                // populated table: must be nullable OR have a default
                // value OR be backed by a value converter that
                // gracefully handles missing data on read. Anything
                // else needs human attention.
                var isNullable = property.IsColumnNullable();
                var defaultSql = property.GetDefaultValueSql(storeObject);
                var defaultValue = property.GetDefaultValue(storeObject);
                var hasValueConverter = property.GetValueConverter() is not null;
                if (!isNullable && defaultSql is null && defaultValue is null && !hasValueConverter)
                {
                    logger.LogWarning(
                        "andy-rbac SQLite schema heal: refusing to add non-nullable column {Table}.{Column} without default.",
                        tableName,
                        columnName);
                    continue;
                }

                var columnType = ResolveSqliteColumnType(property);
                // Converter-backed non-nullable properties get added as
                // nullable on the SQLite path — the converter handles
                // null → safe default at read time. Plain non-nullable
                // columns keep their NOT NULL + default.
                var addAsNullable = isNullable || (hasValueConverter && defaultSql is null && defaultValue is null);
                var nullClause = addAsNullable ? "NULL" : "NOT NULL";
                var defaultClause = defaultSql is not null
                    ? $" DEFAULT ({defaultSql})"
                    : (defaultValue is not null ? $" DEFAULT {FormatDefaultLiteral(defaultValue)}" : "");

                var alterSql = $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" {columnType} {nullClause}{defaultClause};";

                logger.LogWarning(
                    "andy-rbac SQLite schema heal: adding missing column {Table}.{Column} (\"{Sql}\").",
                    tableName,
                    columnName,
                    alterSql);

                using var cmd = conn.CreateCommand();
                cmd.CommandText = alterSql;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                healed++;
            }
        }

        return healed;
    }

    /// <summary>
    /// Recreates indexes the generated schema declares but the live DB
    /// lacks (e.g. an index added alongside new columns — the
    /// AddOutboxRetryState case). Runs AFTER the column heal so indexes
    /// over freshly added columns succeed. A failing CREATE INDEX (e.g.
    /// a UNIQUE index over pre-existing duplicate data, or an index over
    /// a column the column heal refused) is logged and skipped — index
    /// drift degrades query plans, it must not block startup.
    /// </summary>
    private static async Task<int> HealMissingIndexesAsync(
        RbacDbContext db,
        System.Data.Common.DbConnection conn,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var existingIndexes = await ReadIndexNamesAsync(conn, cancellationToken);
        var statements = SplitSqlStatements(db.Database.GenerateCreateScript());

        int created = 0;
        foreach (var statement in statements)
        {
            string? indexName = null;
            if (statement.StartsWith("CREATE INDEX \"", StringComparison.Ordinal))
            {
                indexName = ExtractQuotedIdentifier(statement, "CREATE INDEX \"".Length);
            }
            else if (statement.StartsWith("CREATE UNIQUE INDEX \"", StringComparison.Ordinal))
            {
                indexName = ExtractQuotedIdentifier(statement, "CREATE UNIQUE INDEX \"".Length);
            }

            if (indexName is null) continue;
            if (existingIndexes.Contains(indexName)) continue;

            try
            {
                logger.LogWarning(
                    "andy-rbac SQLite schema heal: creating missing index {Index}.",
                    indexName);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = statement;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                created++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex,
                    "andy-rbac SQLite schema heal: could not create missing index {Index}; skipping.",
                    indexName);
            }
        }
        return created;
    }

    private static async Task<HashSet<string>> ReadTableNamesAsync(
        System.Data.Common.DbConnection conn,
        CancellationToken cancellationToken)
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory';";
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString(0));
        }
        return tables;
    }

    private static async Task<HashSet<string>> ReadIndexNamesAsync(
        System.Data.Common.DbConnection conn,
        CancellationToken cancellationToken)
    {
        var indexes = new HashSet<string>(StringComparer.Ordinal);
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT name FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%';";
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            indexes.Add(reader.GetString(0));
        }
        return indexes;
    }

    private static async Task<HashSet<string>> ReadColumnsAsync(
        System.Data.Common.DbConnection conn,
        string tableName,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.Ordinal);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tableName}\");";
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // PRAGMA table_info columns: cid (0), name (1), type (2), ...
            columns.Add(reader.GetString(1));
        }
        return columns;
    }

    private static string ResolveSqliteColumnType(IProperty property)
    {
        // Prefer the explicit relational type mapping (what EF would
        // emit when creating the schema). Falls back to TEXT as the
        // last-resort SQLite-affinity-safe default.
        var typeMapping = property.GetRelationalTypeMapping();
        var storeType = typeMapping?.StoreType;
        if (!string.IsNullOrEmpty(storeType)) return storeType;

        var configured = property.GetColumnType();
        return !string.IsNullOrEmpty(configured) ? configured : "TEXT";
    }

    private static string FormatDefaultLiteral(object value)
    {
        return value switch
        {
            bool b => b ? "1" : "0",
            string s => $"'{s.Replace("'", "''")}'",
            null => "NULL",
            _ => value.ToString() ?? "NULL"
        };
    }

    private static string? ExtractQuotedIdentifier(string statement, int startIndex)
    {
        var end = statement.IndexOf('"', startIndex);
        if (end <= startIndex) return null;
        return statement[startIndex..end];
    }

    private static List<string> SplitSqlStatements(string script)
    {
        // EF's SQLite create script separates statements with ";" followed
        // by a newline. No procedural blocks exist on SQLite, so this split
        // is unambiguous.
        return script
            .Split([";\r\n", ";\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    private static bool IsSafeIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier)) return false;
        foreach (var c in identifier)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
        }
        return true;
    }
}
