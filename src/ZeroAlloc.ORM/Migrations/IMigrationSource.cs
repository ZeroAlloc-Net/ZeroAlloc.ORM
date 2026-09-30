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
    /// The stable name that scopes this source's versions in the history table. Two sources
    /// with different names each number their migrations independently, so two libraries can
    /// both start at version 1 in one database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The name is stored with every migration the source applies, and the runner looks up a
    /// source's applied versions by it. Changing it makes the runner treat the source as new
    /// and apply its migrations again, so keep it fixed once the source has run.
    /// </para>
    /// <para>
    /// The default is the implementing type's name, from <see cref="object.ToString"/> on its
    /// <see cref="System.Type"/>, such as <c>MyApp.Data.AppMigrations</c>. That is stable while
    /// the type keeps its name and namespace. A library that ships a source should return a
    /// fixed string instead, so a later rename or move of its type cannot change the name.
    /// </para>
    /// <para>
    /// The name must not be empty or whitespace, and is at most
    /// <see cref="MigrationRunner.MaxSourceNameLength"/> characters.
    /// </para>
    /// </remarks>
    string Name => GetType().ToString();

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
