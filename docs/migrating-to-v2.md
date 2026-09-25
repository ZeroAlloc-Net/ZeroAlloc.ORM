# Migrating to ZeroAlloc.ORM v2

v2 contains two breaking changes in the `ZeroAlloc.ORM.Migrations` namespace and one in the
code the generator emits: parameter names no longer carry an `@` prefix. Everything else in v1.x
carries forward unchanged.

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

## Generated parameter names no longer carry an `@` prefix

### Why

[#219](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/issues/219): the generator hardcoded `@`
into every `DbParameter.ParameterName` it emitted. That only works for providers whose
placeholders start with `@`. Oracle's start with `:`, so a `[Query]` written with `:id` still got
`ParameterName = "@id"` and failed to bind.

v2 moves the sigil out of the generated code. You write the placeholder the provider expects in
the SQL; the generator emits the bare name, and each provider matches it to the placeholder. This
removes the blocker for Oracle support. Oracle itself, with its dialect and type mapping, is not
part of v2.0.

| | v1.x | v2 |
|---|---|---|
| SQL you write | `WHERE Id = @id` | `WHERE Id = @id`, unchanged |
| Emitted for `int id` | `ParameterName = "@id"` | `ParameterName = "id"` |
| Emitted for a `Money total` composite | `"@total_Amount"`, `"@total_Currency"` | `"total_Amount"`, `"total_Currency"` |
| Emitted for a `BulkInsert` row value | `"@CustomerId_0"`, `"@CustomerId_1"` | `"CustomerId_0"`, `"CustomerId_1"` |
| `[Param(Name = "@orderId")]` | `ParameterName = "@orderId"` | `ParameterName = "orderId"` |
| `MigrationRunner` history insert | `"@version"`, `"@name"`, `"@applied_at"` | `"version"`, `"name"`, `"applied_at"` |

### Who is affected

**SQL written with `@` placeholders needs no change.** Microsoft.Data.Sqlite, Npgsql and
Microsoft.Data.SqlClient all bind a bare parameter name to an `@name` placeholder, including
stored-procedure input and output parameters. The integration suite runs against all three.

You are affected only if your code reads the generated `ParameterName` back:

- **Command interceptors, logging or tracing** that look parameters up by name, compare names
  against `"@id"`, or print them expecting the `@`. Expect `"id"` instead, or strip the sigil
  before comparing.
- **Code that indexes the parameter collection by an `@`-prefixed name**, such as
  `command.Parameters["@id"]`. Npgsql and SqlClient normalise the prefix on lookup;
  Microsoft.Data.Sqlite's `SqliteParameterCollection` does not, so use `"id"`.
- **A custom `IMigrationDialect`** whose `InsertAppliedVersionSql` uses placeholders other than
  `@version`, `@name` and `@applied_at` with a provider that does not accept a bare name. Every
  shipped dialect already works.

`[Param(Name = ...)]` overrides need no change: one leading `@`, `:` or `$` is dropped, so
`"@orderId"` and `"orderId"` emit the same code. New code should use the bare form.

### Before (v1.x)

```csharp
[Query("SELECT Total FROM Orders WHERE Id = @orderId")]
public partial Task<decimal> GetTotalAsync([Param(Name = "@orderId")] int id, CancellationToken ct);

// generated
__p_id.ParameterName = "@orderId";
```

### After (v2)

```csharp
[Query("SELECT Total FROM Orders WHERE Id = @orderId")]
public partial Task<decimal> GetTotalAsync([Param(Name = "orderId")] int id, CancellationToken ct);

// generated
__p_id.ParameterName = "orderId";
```

See the cookbook's [Parameter prefixes](cookbook/provider-quirks.md#parameter-prefixes) section
for how each provider binds the bare name.
