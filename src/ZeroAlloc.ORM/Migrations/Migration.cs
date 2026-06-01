using System;

namespace ZeroAlloc.ORM.Migrations;

/// <summary>
/// v1.1 — A single migration unit discovered by an <see cref="IMigrationSource"/>
/// and applied by <see cref="MigrationRunner"/>. The 3-tuple (Version, Name, Sql)
/// is the discovery shape; <see cref="AppliedAt"/> is populated by the runner
/// on the return value (UTC timestamp at the moment the migration committed).
/// Sources MUST leave <c>AppliedAt</c> as <c>null</c>.
/// </summary>
/// <param name="Version">
/// Monotonically-comparable version number parsed from the leading NNN of the
/// resource file name. Versions need not be consecutive; <see cref="MigrationRunner"/>
/// applies all versions strictly greater than the highest applied version it
/// finds in the history table, ordered ascending.
/// </param>
/// <param name="Name">
/// Human-readable identifier parsed from the segment between the NNN_ prefix
/// and the .sql suffix (e.g. <c>create_users</c> for <c>001_create_users.sql</c>).
/// Stored verbatim in the migration history table for audit / debug.
/// </param>
/// <param name="Sql">
/// Raw SQL body of the migration. The body may contain multiple statements; the
/// underlying provider's <c>ExecuteNonQueryAsync</c> is responsible for handling
/// the script (Microsoft.Data.Sqlite + Npgsql both accept multi-statement bodies).
/// </param>
/// <param name="AppliedAt">
/// <c>null</c> at discovery time; populated by <see cref="MigrationRunner"/> with
/// the UTC instant the row was inserted into the migration history table.
/// </param>
public sealed record Migration(int Version, string Name, string Sql, DateTime? AppliedAt = null);
