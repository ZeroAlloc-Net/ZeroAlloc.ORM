using System.Collections.Generic;

namespace ZeroAlloc.ORM.Migrations;

/// <summary>
/// v1.1 — Producer of the discoverable set of <see cref="Migration"/> entries.
/// Implementations are responsible for discovery + content load only; ordering,
/// filtering against the history table, and execution are owned by
/// <see cref="MigrationRunner"/>.
/// </summary>
public interface IMigrationSource
{
    /// <summary>
    /// Returns every migration this source can produce, sorted by
    /// <see cref="Migration.Version"/> ascending. Two entries sharing the same
    /// <see cref="Migration.Version"/> are rejected by
    /// <see cref="MigrationRunner.RunAsync"/> with a
    /// <see cref="ZeroAllocOrmMigrationConflictException"/> — versions must be
    /// unique across the returned set.
    /// </summary>
    IReadOnlyList<Migration> GetMigrations();
}
