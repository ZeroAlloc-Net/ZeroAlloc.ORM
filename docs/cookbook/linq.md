# LINQ queries with `[LinqQuery]`

`ZeroAlloc.ORM.Linq` lets you write a query method's SQL as a LINQ chain instead of a SQL string.
The generator reads the chain at build time, translates it to parameterised PostgreSQL and implements
the method the same way it implements a `[Query]`. Nothing builds, compiles or reflects over an
expression at run time, so the result is as AOT-clean as hand-written `[Query]` SQL. A chain the
generator cannot translate is a build error, never a silent fallback to running it in memory.

It is a second way to write the SQL, not an entity-graph ORM: there are no navigations, `Include`,
change tracking or entity writes.

## Install

```bash
dotnet add package ZeroAlloc.ORM
dotnet add package ZeroAlloc.ORM.Generator
dotnet add package ZeroAlloc.ORM.Linq
dotnet add package Npgsql
```

The translator renders PostgreSQL only, so the repository, or its assembly, declares
`[Dialect(SqlDialect.PostgreSql)]`. Without it the method reports [ZAO108](../diagnostics/ZAO108.md).

## Map the entity

The chain queries an entity type, such as a Domain class. The generator needs only its table and
column names, which a map class declares outside the entity, so the entity itself is not touched:

```csharp
using ZeroAlloc.ORM;

public enum WorkStatus { Open, Done }

public enum WorkKind { Bug, Feature }

public sealed class TaskEntity
{
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Title { get; private set; } = "";
    public WorkStatus Status { get; private set; }
    public WorkKind Kind { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int Order { get; private set; }
}

// "app"."Tasks": every column is named after its property, apart from CreatedAt,
// and Kind is stored by its member name.
[TableMap(typeof(TaskEntity), "Tasks", Schema = "app")]
[ColumnMap(nameof(TaskEntity.CreatedAt), Column = "created_at")]
[ColumnMap(nameof(TaskEntity.Kind), EnumAsString = true)]
internal static class TaskMap;
```

- `[TableMap(typeof(E), "Table", Schema = ...)]` names the table. `Schema` is optional.
- EF Core's naming conventions are the default: a column has its property's name, so a map lists only
  the overrides. That reads a schema EF Core created as it is.
- `[ColumnMap(nameof(E.P), Column = ...)]` renames one column.
- Every public readable instance property is a column, whatever its setter's accessibility.
- Every identifier is quoted, so `"Order"`, `"Tasks"` and mixed-case names are safe.
- An enum type marked `[StoreAsString]` is stored by its member name everywhere. For an enum
  without the attribute, `[ColumnMap(nameof(E.P), EnumAsString = true)]` stores this one column by
  name; elsewhere the enum keeps its integer storage.

An entity without a map reports [ZAO104](../diagnostics/ZAO104.md), a member that is not a mapped
column [ZAO105](../diagnostics/ZAO105.md), and a map that contradicts itself
[ZAO106](../diagnostics/ZAO106.md).

## The method and its companion

Each query is a `partial` method plus a companion method that holds the chain:

```csharp
using System.Data.Async;
using ZeroAlloc.ORM;
using ZeroAlloc.ORM.Linq;

public sealed record TaskSummary(Guid Id, string Title);

[Dialect(SqlDialect.PostgreSql)]
public sealed partial class TaskStore(IAsyncDbConnection connection)
{
    [LinqQuery(nameof(OpenTasksQuery))]
    public partial Task<List<TaskSummary>> OpenTasks(Guid projectId, WorkKind? kind, CancellationToken ct);

    private static IQuery<TaskSummary> OpenTasksQuery(Guid projectId, WorkKind? kind) =>
        Sql.Table<TaskEntity>()
            .Where(t => t.ProjectId == projectId && t.Status == WorkStatus.Open)
            .Where(t => kind == null || t.Kind == kind)
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Select(t => new TaskSummary(t.Id, t.Title));
}
```

The generated `OpenTasks` holds this SQL as a `const string`, under a `// LINQ: TaskStore.OpenTasksQuery`
comment:

```sql
SELECT "Id", "Title" FROM "app"."Tasks"
WHERE "ProjectId" = @projectId AND "Status" = 0 AND (@kind IS NULL OR "Kind" = @kind)
ORDER BY "created_at", "Id"
```

The rules:

- The companion is a `static` method of the same type, not overloaded, that returns `IQuery<T>`. Its
  body is one expression, or one `return`, from `Sql.Table<T>()` to the last operator. A query built
  through locals and `if` branches reports [ZAO100](../diagnostics/ZAO100.md); write optional filters
  inline instead, as below.
- The companion's parameters are the method's value parameters, by name, type and order: every
  parameter except the `CancellationToken` and an `IAsyncDbTransaction`. They become the SQL
  parameters. A mismatch reports [ZAO101](../diagnostics/ZAO101.md).
- The chain may read its own parameters, constants, which are inlined as literals, and the entity's
  mapped columns. A field, a local or `this` reports [ZAO103](../diagnostics/ZAO103.md).
- The companion never runs. Its operators all throw `InvalidOperationException`, and any call to it
  from code reports [ZAO109](../diagnostics/ZAO109.md). Call the `[LinqQuery]` method instead. Keep
  companions `private`.
- Everything else is `[Query]`'s: parameter binding, typed ids and value objects, materialisation, and
  joining a caller's transaction through an `IAsyncDbTransaction` parameter on the method.

## Result shapes

The method's return type picks the shape. `T` is the companion's row type.

| Return type | SQL |
|---|---|
| `Task<List<T>>`, `Task<IReadOnlyList<T>>`, `Task<IList<T>>`, or the same in `ValueTask` | every row |
| `Task<T?>` or `ValueTask<T?>` | the first row, or `null`: `LIMIT 1` is added, or `LIMIT 0` with `Take(0)` |
| `IAsyncEnumerable<T>` | streamed rows; the token needs `[EnumeratorCancellation]` |
| `Task<int>` or `ValueTask<int>` after `.Count()` | `SELECT count(*)::int ...` |
| `Task<long>` or `ValueTask<long>` after `.LongCount()` | `SELECT count(*) ...` |
| `Task<bool>` or `ValueTask<bool>` after `.Any()` | `SELECT EXISTS (SELECT 1 ...)` |

Any other return type reports [ZAO112](../diagnostics/ZAO112.md).

```csharp
[LinqQuery(nameof(ByIdQuery))]
public partial ValueTask<TaskSummary?> ById(Guid id, CancellationToken ct);

[LinqQuery(nameof(AllQuery))]
public partial IAsyncEnumerable<TaskSummary> All([EnumeratorCancellation] CancellationToken ct);

[LinqQuery(nameof(CountOpenQuery))]
public partial Task<int> CountOpen(Guid projectId, CancellationToken ct);

private static IQuery<TaskSummary> ByIdQuery(Guid id) =>
    Sql.Table<TaskEntity>().Where(t => t.Id == id).Select(t => new TaskSummary(t.Id, t.Title));

private static IQuery<TaskSummary> AllQuery() =>
    Sql.Table<TaskEntity>().OrderBy(t => t.Id).Select(t => new TaskSummary(t.Id, t.Title));

private static IQuery<int> CountOpenQuery(Guid projectId) =>
    Sql.Table<TaskEntity>().Where(t => t.ProjectId == projectId && t.Status == WorkStatus.Open).Count();
```

`Count`, `LongCount` and `Any` drop the projection and the ordering, which change neither a count nor
an existence. Over a page they count the page:
`SELECT count(*)::int FROM (SELECT 1 FROM ... LIMIT ... OFFSET ...) AS q`.

## Projection

`Select` constructs a positional record through the constructor its rows are read with. Each argument is
a mapped column, a companion parameter or a literal; named arguments are put in constructor order.

```csharp
.Select(t => new TaskLine(t.Id, t.Title, t.Order))   // SELECT "Id", "Title", "Order"
```

A column stored by name reads back into its enum. Any other projection, such as an anonymous type, an
expression or a single column into `List<Guid>`, reports [ZAO107](../diagnostics/ZAO107.md).

## Operators and their SQL

The chain filters, orders, pages, then projects, in that order:
`Where`/`WhereIf` and the orderings, then `Skip`, then `Take`, then `Select`, then a terminal.

| Operator | SQL |
|---|---|
| `Where(t => a && b)` | `WHERE a AND b`; several `Where` calls join with `AND` |
| `WhereIf(cond, t => p)` | `((cond) IS NOT TRUE OR (p))`: a null `cond`, as `n > 0` is for a null `n`, skips the filter, as in C# |
| `OrderBy(t => t.A)`, `OrderByDescending` | `ORDER BY "A"`, `ORDER BY "A" DESC` |
| `ThenBy(t => t.B)`, `ThenByDescending` | `, "B"`, `, "B" DESC` |
| `Skip(skip)` | `OFFSET GREATEST(@skip, 0)`, or `OFFSET 5` for a literal |
| `Take(take)` | `LIMIT GREATEST(@take, 0)`, or `LIMIT 10` for a literal |
| `Select(t => new Row(...))` | the `SELECT` list |
| `Count()`, `LongCount()`, `Any()` | see [Result shapes](#result-shapes) |

The order is checked, and each break of it reports [ZAO102](../diagnostics/ZAO102.md):

- `Where` or an ordering after `Skip` or `Take`: LINQ would apply it to the page, but SQL filters and
  orders before it pages;
- a second `OrderBy`, since LINQ would make its key primary; use `ThenBy` to add a key;
- `Take` before `Skip`, and `Skip` or `Take` twice;
- an operator after `Select`, other than a terminal, and any operator after a terminal.

Ordering keys are mapped columns. An enum stored by name orders by its member names, as text.

`Skip` and `Take` take a companion parameter or an `int` literal. A negative parameter counts as 0, as
in LINQ, through `GREATEST`. A negative literal is a build error. On the single-row shape a `Take`
caps at 1: `Take(5)` gives `LIMIT 1`, `Take(0)` gives `LIMIT 0`, and a parameter gives
`LIMIT LEAST(GREATEST(@take, 0), 1)`. Paging a list or a stream without an ordering warns with
[ZAO110](../diagnostics/ZAO110.md).

### Predicates

| C# | SQL |
|---|---|
| `t.Order == n`, `!=`, `<`, `<=`, `>`, `>=` | `"Order" = @n`, `<>`, `<`, `<=`, `>`, `>=` |
| `t.Note == null`, `t.Note != null` | `"Note" IS NULL`, `"Note" IS NOT NULL` |
| `a && b`, `a \|\| b`, `!a` | `a AND b`, `(a OR b)`, `NOT (a)`, or `(a) IS NOT TRUE` when `a` can be null |
| `t.IsActive` | `"IsActive"` |
| `t.Status == WorkStatus.Done` | `"Status" = 1`, or `'Done'` when stored by name |
| `string.Equals(t.Title, title)`, with or without `StringComparison.Ordinal` | `"Title" = @title` |
| `t.Title.Contains(text)` | `"Title" LIKE '%' \|\| <escaped @text> \|\| '%' ESCAPE '\'` |
| `t.Title.StartsWith(text)`, `EndsWith(text)` | the same `LIKE`, with `%` on one side |
| `Sql.ILike(t.Title, pattern)` | `"Title" ILIKE @pattern` |
| `ids.Contains(t.Id)` | `"Id" = ANY(@ids)` |
| `Sql.Row(t.CreatedAt, t.Id) > Sql.Row(at, id)` | `("created_at", "Id") > (@at, @id)` |

- `Contains`, `StartsWith` and `EndsWith` match literally: `%`, `_` and `\` in the value are escaped,
  so `"50%"` finds the text `50%`. They also accept `StringComparison.Ordinal`. `Sql.ILike` is
  case-insensitive and passes the pattern through, wildcards included.
- `==` on strings works too. `string.Equals` and the `StringComparison.Ordinal` overloads exist for
  codebases whose analyzers reject `==` or a comparison without a `StringComparison`. Any other
  `StringComparison` reports ZAO102.
- `= ANY` takes an array parameter: `T[]`, `List<T>` or `IReadOnlyList<T>` of `string`, `int`, `long`
  or `Guid`. Array parameters need Npgsql, and a slim data source needs `EnableArrays()`.
- An enum parameter compared with a column stored by name binds by its member name. Compared with
  columns stored both ways it reports [ZAO111](../diagnostics/ZAO111.md).
- Typed ids and value objects compare by their underlying value.
- Arithmetic, other methods, and `<`/`>` on an enum stored by name report ZAO102.

#### Nulls compare as in C#

Comparisons mean what they mean in C#, not SQL's three-valued logic: `null == null` is true,
`null == 5` and `null > 5` are false, so `!(null > 5)` is true. The translator knows which operands
can be null from the entity property's or the companion parameter's declared type, nullable reference
annotations included, and adds the `IS NULL` terms EF adds:

| C# | SQL |
|---|---|
| `t.DueAt == due`, both nullable | `("DueAt" = @due OR ("DueAt" IS NULL AND @due IS NULL))` |
| `t.DueAt != due`, both nullable | `(("DueAt" <> @due OR ("DueAt" IS NULL AND @due IS NOT NULL)) OR ("DueAt" IS NOT NULL AND @due IS NULL))` |
| `t.Priority != n`, only the column nullable | `("Priority" <> @n OR "Priority" IS NULL)` |
| `t.Priority == n`, one side nullable | `"Priority" = @n`: a NULL is dropped, as C#'s false is |
| `!(t.Priority > n)`, `t.Priority` nullable | `("Priority" > @n) IS NOT TRUE`, which keeps the null rows |
| `WhereIf(n > 0, ...)`, `n` nullable | a null `n` skips the filter |

So `Where(t => t.DueAt == due)` with a null `due` returns the rows without a due date, and
`t.Kind != WorkKind.Feature` on a nullable column keeps the rows where it is null. In code without
nullable annotations a reference type counts as not null. A `Sql.Row` item that can be null reports
ZAO102: a NULL makes the whole row comparison NULL.

### Optional filters

A filter that applies only when its parameter is set is written inline. A `null` parameter then matches
every row:

```csharp
private static IQuery<TaskSummary> FindQuery(WorkStatus? status, Guid? projectId, bool activeOnly) =>
    Sql.Table<TaskEntity>()
        .Where(t => status == null || t.Status == status)          // (@status IS NULL OR "Status" = @status)
        .WhereIf(projectId != null, t => t.ProjectId == projectId) // ((@projectId IS NOT NULL) IS NOT TRUE OR ("ProjectId" = @projectId))
        .WhereIf(activeOnly, t => t.Status == WorkStatus.Open)
        .Select(t => new TaskSummary(t.Id, t.Title));
```

PostgreSQL has to know a parameter's type even where it only meets `IS NULL`. So each value parameter
gets a `DbType` derived from its type, such as `String`, `Int32`, `Int64` or `Guid`. A parameter
without one, such as an array, `DateTime`, `DateOnly` or `TimeSpan`, gets a cast on its null test
instead: `@ids::uuid[] IS NULL`. A `[Param(DbType = ...)]` you write on the method's parameter wins
over the generated type, and ZAO015 checks it against the dialect as for `[Query]`.

A parameter the rendered SQL does not use, for example one that only a projection dropped by `Count()`
read, stays in the signature but is not bound.

## Keyset paging

Deep `OFFSET` pages read and throw away every skipped row. Keyset paging continues from the last row
seen instead. `Sql.Row` builds a row value of two or three items, compared with `<`, `<=`, `>` or `>=`:

```csharp
[LinqQuery(nameof(FirstPageQuery))]
public partial Task<List<TaskPage>> FirstPage(int take, CancellationToken ct);

[LinqQuery(nameof(NextPageQuery))]
public partial Task<List<TaskPage>> NextPage(DateTimeOffset afterAt, Guid afterId, int take, CancellationToken ct);

private static IQuery<TaskPage> FirstPageQuery(int take) =>
    Sql.Table<TaskEntity>()
        .OrderBy(t => t.CreatedAt)
        .ThenBy(t => t.Id)
        .Take(take)
        .Select(t => new TaskPage(t.Id, t.CreatedAt));

private static IQuery<TaskPage> NextPageQuery(DateTimeOffset afterAt, Guid afterId, int take) =>
    Sql.Table<TaskEntity>()
        .Where(t => Sql.Row(t.CreatedAt, t.Id) > Sql.Row(afterAt, afterId))
        .OrderBy(t => t.CreatedAt)
        .ThenBy(t => t.Id)
        .Take(take)
        .Select(t => new TaskPage(t.Id, t.CreatedAt));
```

```sql
SELECT "Id", "created_at" FROM "app"."Tasks"
WHERE ("created_at", "Id") > (@afterAt, @afterId)
ORDER BY "created_at", "Id" LIMIT GREATEST(@take, 0)
```

Order by the same columns, in the same direction, and end with a unique key such as the primary key,
so that rows with equal timestamps are neither skipped nor repeated. Each pair is a column and a
parameter or a non-null constant, in either order. An item whose type can be null cannot be part of
a row, because a NULL makes the whole comparison NULL; nor can an enum stored by name, because it would
compare by name.

## Diagnostics

| Code | Severity | Reports |
|---|---|---|
| [ZAO100](../diagnostics/ZAO100.md) | Error | The companion is missing, overloaded, not static, not one expression, or does not return `IQuery<T>`. |
| [ZAO101](../diagnostics/ZAO101.md) | Error | The companion's parameters are not the method's value parameters, by name, type and order. |
| [ZAO102](../diagnostics/ZAO102.md) | Error | An operator, predicate, key, count or chain order the translator does not support. |
| [ZAO103](../diagnostics/ZAO103.md) | Error | The chain reads a field, local or other value that is not a companion parameter or constant. |
| [ZAO104](../diagnostics/ZAO104.md) | Error | The queried entity has no `[TableMap]`. |
| [ZAO105](../diagnostics/ZAO105.md) | Error | The chain uses a member that is not a mapped column. |
| [ZAO106](../diagnostics/ZAO106.md) | Error | A `[TableMap]` or `[ColumnMap]` is invalid: a duplicate, an unknown property, a shared column, or `EnumAsString` on a non-enum. |
| [ZAO107](../diagnostics/ZAO107.md) | Error | The projection is not a positional record of columns, parameters and literals. |
| [ZAO108](../diagnostics/ZAO108.md) | Error | The repository does not declare `[Dialect(SqlDialect.PostgreSql)]`. |
| [ZAO109](../diagnostics/ZAO109.md) | Error | Code calls a companion, which never runs. |
| [ZAO110](../diagnostics/ZAO110.md) | Warning | `Skip` or `Take` without an ordering returns rows in no defined order. |
| [ZAO111](../diagnostics/ZAO111.md) | Error | An enum parameter is compared with columns that store the enum differently. |
| [ZAO112](../diagnostics/ZAO112.md) | Error | The method's return type does not fit the companion's `IQuery<T>`. |

## Not supported yet

These are planned, in this order. Until then, write such a query as a raw `[Query]` or `[Command]`:

- `GroupBy` with aggregates such as `Sum`, and explicit joins.
- Loading a parent with its children: a multi-result head and lines method with a companion per
  result set.
- Set-based `ExecuteUpdate` and `ExecuteDelete`, optimistic concurrency checks, and `FOR UPDATE`.

Also not supported today:

- Projecting a single column into a list of scalars, such as `Select(t => t.Id)` into `List<Guid>`
  (ZAO107). Project into a one-member record instead.
- `Skip` and `Take` after `Select` (ZAO102). Put them before it.
- Dialects other than PostgreSQL (ZAO108).
