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
    /// <see cref="Migration.Version"/> ascending. Equal-version entries are
    /// permitted but discouraged — the runner applies them in source order.
    /// </summary>
    IReadOnlyList<Migration> GetMigrations();
}
