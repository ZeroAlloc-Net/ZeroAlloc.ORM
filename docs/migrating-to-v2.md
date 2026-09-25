# Migrating to ZeroAlloc.ORM v2

v2 contains one breaking change, in the `ZeroAlloc.ORM.Migrations` namespace. Everything
else in v1.x carries forward unchanged.

## `IMigrationDialect.SelectAppliedVersionsSql` must return `version, name`

### Why

[#230](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/issues/230): `MigrationRunner` used to
filter pending migrations by `Version` alone, so a discovered migration whose version collided
with an already-applied one — but under a different name — was silently dropped instead of
applied. The fix requires `MigrationRunner` to compare both `Version` and `Name` against the
history table, so it now reads both columns from `SelectAppliedVersionsSql`.

The history table itself does not change: every shipped dialect (Sqlite, Postgres, SqlServer)
already writes `name` via `InsertAppliedVersionSql` and already has a `name NOT NULL` column.
This is a read-side change only — no schema migration of your own history table is needed.

### Who is affected

Only adopters with a **custom `IMigrationDialect` implementation** (the
[cookbook's "Custom dialects" recipe](cookbook/migrations.md#custom-dialects-advanced)) for a
provider ZeroAlloc.ORM doesn't ship out of the box. If you use the shipped
`SqliteMigrationDialect`, `PostgresMigrationDialect`, or `SqlServerMigrationDialect` as-is,
nothing changes for you.

### Before (v1.x)

```csharp
public sealed class MyCustomDialect : IMigrationDialect
{
    public string SelectAppliedVersionsSql =>
        "SELECT version FROM __zaorm_migrations ORDER BY version";

    // ...
}
```

### After (v2)

```csharp
public sealed class MyCustomDialect : IMigrationDialect
{
    public string SelectAppliedVersionsSql =>
        "SELECT version, name FROM __zaorm_migrations ORDER BY version";

    // ...
}
```

`MigrationRunner` reads the two columns positionally (`reader.GetInt32(0)` for `version`,
`reader.GetString(1)` for `name`), so the column order matters — `version` first, `name`
second — but the aliases and any additional `ORDER BY`/`WHERE` clauses are yours to keep.

## Two discovered migrations sharing a version are now rejected

`IMigrationSource.GetMigrations()` returning two `Migration` entries with the same `Version` in
one call used to be permitted, applied in source order. `MigrationRunner` now throws
`ZeroAllocOrmMigrationConflictException` before attempting to apply anything, whether or not
either migration has already been applied. If your `IMigrationSource` implementation ever
produced duplicate versions on purpose, give each migration a distinct version instead.

See the cookbook's [Version collisions](cookbook/migrations.md#version-collisions) section for
the full set of cases `MigrationRunner` now distinguishes.
