using System.Collections.Generic;

namespace ZeroAlloc.ORM.Migrations;

/// <summary>
/// An <see cref="IMigrationDialect"/> whose history table records which
/// <see cref="IMigrationSource"/> applied each migration, so versions are unique per source
/// rather than per database. The built-in SQLite, PostgreSQL and SQL Server dialects implement
/// it.
/// </summary>
/// <remarks>
/// <para>
/// For a dialect that implements this interface, <see cref="MigrationRunner"/> binds a
/// parameter named <c>source</c>, the source's <see cref="IMigrationSource.Name"/>, to
/// <see cref="IMigrationDialect.SelectAppliedVersionsSql"/> and
/// <see cref="IMigrationDialect.InsertAppliedVersionSql"/>, as well as the parameters those
/// members name. <see cref="IMigrationDialect.CreateHistoryTableSql"/> creates the table with
/// a <c>source</c> column in its primary key, together with <c>version</c>.
/// </para>
/// <para>
/// A history table created before scoping has no <c>source</c> column. On each run the
/// runner checks for that layout with <see cref="SelectUnscopedHistorySql"/> and, when it
/// finds it, runs <see cref="UpgradeUnscopedHistorySql"/> in one transaction. The upgrade
/// assigns every existing row to the source being run.
/// </para>
/// <para>
/// A dialect that implements only <see cref="IMigrationDialect"/> keeps one version sequence
/// per database, as before.
/// </para>
/// </remarks>
public interface IScopedMigrationDialect : IMigrationDialect
{
    /// <summary>
    /// A query returning one value that is non-zero when the history table has no
    /// <c>source</c> column, the layout from before scoping, and zero otherwise. It runs after
    /// <see cref="IMigrationDialect.CreateHistoryTableSql"/>, so the table exists.
    /// </summary>
    string SelectUnscopedHistorySql { get; }

    /// <summary>
    /// The statements that move an unscoped history table to the scoped layout in place,
    /// keeping its rows and assigning each to the source bound as the <c>source</c>
    /// parameter. The runner runs them in order in one transaction, binding <c>source</c> to
    /// each, and rolls the transaction back if any statement fails.
    /// </summary>
    IReadOnlyList<string> UpgradeUnscopedHistorySql { get; }
}
