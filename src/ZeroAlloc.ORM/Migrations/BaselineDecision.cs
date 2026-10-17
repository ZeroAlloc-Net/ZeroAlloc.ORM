namespace ZeroAlloc.ORM.Migrations;

/// <summary>
/// What <see cref="MigrationRunner"/> does with a database on which the migration source
/// has no recorded history, as decided by an <see cref="IMigrationBaselineCheck"/>.
/// </summary>
public enum BaselineDecision
{
    /// <summary>The database is empty of this source's schema: run every migration.</summary>
    RunAll = 0,

    /// <summary>
    /// The schema already exists, created outside this runner: record the baseline's
    /// migrations as applied, without running them, then run any later ones.
    /// </summary>
    MarkApplied = 1,
}
