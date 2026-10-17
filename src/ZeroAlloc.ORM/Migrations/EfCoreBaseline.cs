using System;
using System.Data.Async;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.ORM.Migrations;

/// <summary>
/// Baseline checks for databases whose schema was created by EF Core migrations. Use with
/// <see cref="MigrationBaseline"/> to adopt such a database in place.
/// </summary>
public static class EfCoreBaseline
{
    internal const string PostgresHistoryExistsSql =
        "SELECT to_regclass('\"__EFMigrationsHistory\"') IS NOT NULL";

    internal const string SqliteHistoryExistsSql =
        "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory'";

    internal const string SqlServerHistoryExistsSql =
        "SELECT CASE WHEN OBJECT_ID(N'[__EFMigrationsHistory]', N'U') IS NULL THEN 0 ELSE 1 END";

    internal const string PostgresOtherSchemaSql =
        "SELECT n.nspname FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace "
        + "WHERE c.relname = '__EFMigrationsHistory' AND c.relkind IN ('r', 'p') ORDER BY n.nspname LIMIT 1";

    internal const string SqlServerOtherSchemaSql =
        "SELECT TOP 1 SCHEMA_NAME(schema_id) FROM sys.tables WHERE name = '__EFMigrationsHistory' ORDER BY schema_id";

    internal const string LatestIdSql = "SELECT MAX(\"MigrationId\") FROM \"__EFMigrationsHistory\"";

    internal const string SqlServerLatestIdSql = "SELECT MAX([MigrationId]) FROM [__EFMigrationsHistory]";

    /// <summary>
    /// Creates a check that adopts an EF Core database whose latest applied migration is
    /// <paramref name="migrationId"/>. It returns <see cref="BaselineDecision.RunAll"/> when the
    /// database has no <c>__EFMigrationsHistory</c> table in the connection's default schema or
    /// search path and none in another schema, and
    /// <see cref="BaselineDecision.MarkApplied"/> when the latest recorded id equals
    /// <paramref name="migrationId"/> (ordinal comparison). Any other state throws
    /// <see cref="ZeroAllocOrmMigrationConflictException"/>, so no database is adopted that EF has
    /// not brought to the expected migration. This includes a history table that exists only in
    /// another schema than the connection's default schema or search path. The EF history table is left in place.
    /// </summary>
    /// <param name="migrationId">The EF migration id the database must be at, for example <c>20261014090300_AddIndex</c>.</param>
    /// <param name="dialect">The database provider. Supports PostgreSql, Sqlite and SqlServer.</param>
    /// <exception cref="ArgumentException"><paramref name="migrationId"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dialect"/> is not supported.</exception>
    public static IMigrationBaselineCheck LatestMigrationIs(string migrationId, SqlDialect dialect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationId);
        return dialect switch
        {
            SqlDialect.PostgreSql => new Check(migrationId, PostgresHistoryExistsSql, PostgresOtherSchemaSql, LatestIdSql),
            SqlDialect.Sqlite => new Check(migrationId, SqliteHistoryExistsSql, null, LatestIdSql),
            SqlDialect.SqlServer => new Check(migrationId, SqlServerHistoryExistsSql, SqlServerOtherSchemaSql, SqlServerLatestIdSql),
            _ => throw new ArgumentOutOfRangeException(
                nameof(dialect), dialect, "EfCoreBaseline supports PostgreSql, Sqlite and SqlServer."),
        };
    }

    private sealed class Check(string expectedId, string existsSql, string? otherSchemaSql, string latestSql) : IMigrationBaselineCheck
    {
        public async Task<BaselineDecision> DecideAsync(IAsyncDbConnection connection, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(connection);

            var exists = await ScalarAsync(connection, existsSql, ct).ConfigureAwait(false);
            if (!IsTrue(exists))
            {
                if (otherSchemaSql is not null
                    && await ScalarAsync(connection, otherSchemaSql, ct).ConfigureAwait(false)
                        is { } schema and not DBNull)
                {
                    throw new ZeroAllocOrmMigrationConflictException(
                        $"An __EFMigrationsHistory table exists in the schema '{Convert.ToString(schema, CultureInfo.InvariantCulture)}', "
                        + "but not in the connection's default schema or search path. Point the search_path "
                        + "or default schema of this connection at it, so the EF history can be checked.");
                }

                return BaselineDecision.RunAll;
            }

            var latest = await ScalarAsync(connection, latestSql, ct).ConfigureAwait(false);
            var found = latest is null or DBNull ? null : Convert.ToString(latest, CultureInfo.InvariantCulture);
            if (string.Equals(found, expectedId, StringComparison.Ordinal))
            {
                return BaselineDecision.MarkApplied;
            }

            throw new ZeroAllocOrmMigrationConflictException(
                $"The database has an EF Core migration history, but its latest migration is "
                + $"{(found is null ? "no rows" : $"'{found}'")} instead of the expected '{expectedId}'. "
                + "Bring the database to the expected migration with EF before switching to ZeroAlloc migrations.");
        }

        private static bool IsTrue(object? value) => value switch
        {
            null or DBNull => false,
            bool b => b,
            _ => Convert.ToInt64(value, CultureInfo.InvariantCulture) > 0,
        };

        private static async Task<object?> ScalarAsync(IAsyncDbConnection connection, string sql, CancellationToken ct)
        {
            var cmd = connection.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.CommandText = sql;
                return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            }
        }
    }
}
