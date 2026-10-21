# ZeroAlloc.ORM

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.ORM.svg)](https://www.nuget.org/packages/ZeroAlloc.ORM)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/ZeroAlloc-Net/ZeroAlloc.ORM/graph/badge.svg)](https://codecov.io/gh/ZeroAlloc-Net/ZeroAlloc.ORM)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

Source-generated raw-SQL data access for .NET: you write the SQL, the generator writes the ADO.NET,
with no runtime reflection and NativeAOT-ready.

## Why ZeroAlloc.ORM

- **Close to hand-written ADO.NET.** A 1,000-row read allocates 88,592 B, 0.4% more than hand-written
  ADO.NET's 88,200 B, where Dapper.AOT allocates 130,001 B in the same run.
- **Ahead of Dapper.AOT on single-row reads and multi-result sets.** A single-row read on SQLite
  takes 1.06× the time of hand-written ADO.NET; Dapper.AOT takes 1.21× in the same run.
- **Works under NativeAOT and trimming.** CI publishes and runs AOT consumers on every PR, including
  ones that share the build with other source generators, where EF Core's precompiled queries collide.
- **Compile-time diagnostics instead of runtime surprises.** A non-partial method, a tuple that does not
  match the SQL's statement count or a parameter with no binding is a build error, `ZAO001` to `ZAO082`,
  and a LINQ chain that cannot be translated is one too, `ZAO100` to `ZAO112`.
- **LINQ as a compile-time SQL source.** With `ZeroAlloc.ORM.Linq`, a query's SQL can be written as a
  LINQ chain that the generator translates to PostgreSQL at build time; the chain never runs.

## Install

```bash
dotnet add package ZeroAlloc.ORM
dotnet add package ZeroAlloc.ORM.Generator
```

Then add your ADO.NET provider, such as `Microsoft.Data.Sqlite`, `Npgsql` or `Microsoft.Data.SqlClient`.

## Quick start

```csharp
using Microsoft.Data.Sqlite;
using System.Data.Async;
using System.Data.Async.Adapters;
using ZeroAlloc.ORM;

await using var db = new SqliteConnection("Data Source=:memory:").AsAsync();
await db.OpenAsync();

var customers = new CustomerRepository(db);
await customers.CreateTableAsync();
int id = await customers.AddAsync("Ada");
Console.WriteLine(await customers.FindAsync(id)); // Customer { Id = 1, Name = Ada }

public sealed record Customer(int Id, string Name);

public sealed partial class CustomerRepository(IAsyncDbConnection connection)
{
    [Command("CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL)")]
    public partial Task<int> CreateTableAsync(CancellationToken ct = default);

    [Command("INSERT INTO Customers (Name) VALUES (@name) RETURNING Id", Kind = CommandKind.Identity)]
    public partial Task<int> AddAsync(string name, CancellationToken ct = default);

    [Query("SELECT Id, Name FROM Customers WHERE Id = @id")]
    public partial Task<Customer?> FindAsync(int id, CancellationToken ct = default);
}
```

The generator fills in each `partial` method with typed parameter binding and row materialization
against [AdoNet.Async](https://github.com/MarcelRoozekrans/AdoNet.Async)'s `IAsyncDbConnection`. There
is no runtime LINQ provider, no expression compilation and no runtime reflection.

## LINQ queries

On PostgreSQL, `ZeroAlloc.ORM.Linq` lets you write the SQL as LINQ instead. Each query is a `partial`
method plus a companion that holds the chain. The generator reads the companion at build time and emits
the SQL as a `const string`; the companion never runs, and its parameters become the SQL parameters.

```bash
dotnet add package ZeroAlloc.ORM.Linq
```

```csharp
using System.Data.Async;
using ZeroAlloc.ORM;
using ZeroAlloc.ORM.Linq;

[TableMap(typeof(TaskEntity), "Tasks")]
[ColumnMap(nameof(TaskEntity.Kind), EnumAsString = true)]
internal static class TaskMap;

[Dialect(SqlDialect.PostgreSql)]
public sealed partial class TaskStore(IAsyncDbConnection connection)
{
    [LinqQuery(nameof(OpenTasksQuery))]
    public partial Task<List<TaskSummary>> OpenTasks(Guid projectId, WorkKind? kind, CancellationToken ct);

    // SELECT "Id", "Title" FROM "Tasks"
    // WHERE "ProjectId" = @projectId AND (@kind IS NULL OR "Kind" = @kind) ORDER BY "CreatedAt", "Id"
    private static IQuery<TaskSummary> OpenTasksQuery(Guid projectId, WorkKind? kind) =>
        Sql.Table<TaskEntity>()
            .Where(t => t.ProjectId == projectId)
            .Where(t => kind == null || t.Kind == kind)
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Select(t => new TaskSummary(t.Id, t.Title));
}
```

`TaskEntity` is any class with the mapped properties, such as a Domain entity; columns default to the
property names, as in EF Core. Filters, ordering, `Skip`/`Take`, `Sql.Row` keyset paging, `Count`,
`LongCount` and `Any` translate today. See the
[LINQ recipe](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/cookbook/linq.md) for every
operator, result shape and diagnostic.

## What you get

- **Every common result shape:** scalars, single rows, lists, `IAsyncEnumerable<T>` streams and
  multi-result-set tuples such as `(Order Head, IReadOnlyList<OrderLine> Lines)`.
- **Commands:** rows affected, scalars, inserted identities and chunked multi-row bulk inserts.
- **Stored procedures** with output, input-output and return-value parameters.
- **Rich types:** enums as integers or names, value objects, multi-column composites such as `Money`, and
  custom row factories.
- **Transactions** by declaring an `IAsyncDbTransaction` parameter.
- **Provider-aware checks** for SQLite, PostgreSQL, SQL Server and MySQL.
- **Migrations:** `MigrationRunner` applies embedded `.sql` files, with locking for safe concurrent
  startup on PostgreSQL and SQL Server.
- Observability through [ZeroAlloc.Telemetry](https://github.com/ZeroAlloc-Net/ZeroAlloc.Telemetry)'s
  `[Instrument]`; see the [observability recipe](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/cookbook/observability.md).

Every shape, option and diagnostic is in the [reference](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/README.md).

## Performance

SQLite, BenchmarkDotNet, captured at 2.4.0. Time is the same-run ratio against hand-written ADO.NET;
allocations are exact bytes per call.

| Workload | ZA.ORM time | Dapper.AOT time | ZA.ORM alloc | Dapper.AOT alloc | Hand-written alloc |
|---|---:|---:|---:|---:|---:|
| Single-row read | 1.06× | 1.21× | 1,568 B | 1,800 B | 1,232 B |
| 1,000-row read | 1.06× | 0.94× | 88,592 B | 130,001 B | 88,200 B |
| Head + 10 lines | 1.09× | 1.27× | 3,400 B | 4,072 B | 2,680 B |

Full tables, the insert workload, PostgreSQL and methodology: [SQLite](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/benchmarks/v2.x-sqlite-results.md)
and [PostgreSQL](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/benchmarks/v2.x-postgres-results.md).

## Learn more

- Documentation site: [orm.zeroalloc.net](https://orm.zeroalloc.net)
- [Documentation source](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/tree/main/docs): the [reference](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/README.md), [cookbook](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/tree/main/docs/cookbook),
  [diagnostics](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/diagnostics/README.md) and [migrating to v2](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/migrating-to-v2.md).
- Related packages: [AdoNet.Async](https://github.com/MarcelRoozekrans/AdoNet.Async),
  [ZeroAlloc.ValueObjects](https://github.com/ZeroAlloc-Net/ZeroAlloc.ValueObjects),
  [ZeroAlloc.Telemetry](https://github.com/ZeroAlloc-Net/ZeroAlloc.Telemetry) and
  [ZeroAlloc.Templates](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates).
- Contributing: issues and pull requests are welcome on [GitHub](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/issues).
- [CHANGELOG](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/CHANGELOG.md).

## License

MIT. See [LICENSE](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/LICENSE).
