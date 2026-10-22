# LINQ queries with `[LinqQuery]`

`ZeroAlloc.ORM.Linq` lets you write a query method's SQL as a LINQ chain instead of a SQL string.
The generator reads the chain at build time, translates it to parameterised PostgreSQL and implements
the method the same way it implements a `[Query]`. Nothing builds, compiles or reflects over an
expression at run time, so the result is as AOT-clean as hand-written `[Query]` SQL. A chain the
generator cannot translate is a build error, never a silent fallback to running it in memory.

It is a second way to write the SQL, not an entity-graph ORM: there are no navigations, change
tracking or entity writes. In place of EF Core's `Include`, one method loads a parent and its
children as a tuple in one round trip, see [Parents with children](#parents-with-children).

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
- Every public readable instance property that holds a value is a column, whatever its setter's
  accessibility: one with a setter, `init` included, or a get-only auto-property. A computed
  property such as `public string Label => First + Last;` is not a column, and a query that uses it
  reports ZAO105.
- An entity from another assembly, such as a separate Domain project, is read from metadata, where
  a private setter is not visible. There a property without a visible setter is a column only when
  it is an auto-property, which the compiler marks `[CompilerGenerated]`. A hand-written getter
  backed by a private setter, `Note { get => _note; private set => _note = value; }`, looks computed
  from outside and is left out. So does a C# 14 `field`-keyword getter with logic, such as
  `{ get => field ?? ""; private set; }`: it is hand-written too, so it needs a `[ColumnMap]`.
- A `[ColumnMap(nameof(E.P))]` always makes `P` a column, so it brings in a property the rules above
  leave out. `[ColumnMap(nameof(E.P), Ignore = true)]` always leaves `P` out. `Ignore` cannot be
  combined with `Column`, `EnumAsString` or `DateTimeStorage`. Either way a wrong guess fails the
  build with ZAO105, and one attribute fixes it:

  ```csharp
  [TableMap(typeof(Domain.Note), "Notes")]
  [ColumnMap(nameof(Domain.Note.Body))]                  // hand-written getter, stored
  [ColumnMap(nameof(Domain.Note.Cached), Ignore = true)] // auto-property, not stored
  internal static class NoteMap;
  ```
- Every identifier is quoted, so `"Order"`, `"Tasks"` and mixed-case names are safe.
- An enum type marked `[StoreAsString]` is stored by its member name everywhere. For an enum
  without the attribute, `[ColumnMap(nameof(E.P), EnumAsString = true)]` stores this one column by
  name; elsewhere the enum keeps its integer storage.
- A `DateTime` column may be a `timestamp` or a `timestamptz`, and the entity does not say which.
  Declare it so `.Date` is exact, see [GroupBy](#groupby): for every `DateTime` column in the
  assembly with `[assembly: EntityMapDefaults(DateTimeStorage = DateTimeStorage.TimestampTz)]`, or
  for one with `[ColumnMap(nameof(E.P), DateTimeStorage = DateTimeStorage.TimestampTz)]`. A
  column's own setting other than `Default` wins over the assembly default, so
  `DateTimeStorage.Timestamp` marks one `timestamp` column under a `TimestampTz` default.
  A `DateTimeStorage` of `Timestamp` or `TimestampTz` on a property that is not a `DateTime` or
  `DateTime?` reports ZAO106.

  ```csharp
  // EF Core's Npgsql default
  [assembly: EntityMapDefaults(DateTimeStorage = DateTimeStorage.TimestampTz)]

  [TableMap(typeof(Execution), "Executions")]
  [ColumnMap(nameof(Execution.LocalDay), DateTimeStorage = DateTimeStorage.Timestamp)]
  internal static class ExecutionMap;
  ```

### Owned types

An EF Core owned type, `OwnsOne`, has no table of its own: its properties are columns of the
owner's table. `[OwnedMap(nameof(E.P))]` declares one, and each public readable instance property
of the owned type that holds a value becomes a column, by the same rules as the entity's own
properties. The column name is EF Core's default, `{Owner}_{Property}`, such as `Location_StartLine`.
A `[ColumnMap]` with a dotted path, `Owner.Property`, overrides it and sets `EnumAsString`,
`DateTimeStorage` or `Ignore` for the member as for any column:

```csharp
public sealed class CodeAnalysisRequest
{
    public Guid Id { get; private set; }
    public RepositoryReference Repository { get; private set; } = null!; // Url, Branch, Platform
    public CodeLocation Location { get; private set; } = null!;          // FilePath, StartLine
    public AnalysisOutcome? Outcome { get; private set; }                // optional
}

// Url, Branch and FilePath carry Daedalus' bare column names; every other owned member keeps
// EF's {Owner}_{Property}, such as "Repository_Platform".
[TableMap(typeof(CodeAnalysisRequest), "AnalysisRequests")]
[OwnedMap(nameof(CodeAnalysisRequest.Repository))]
[OwnedMap(nameof(CodeAnalysisRequest.Location))]
[OwnedMap(nameof(CodeAnalysisRequest.Outcome))]
[ColumnMap("Repository.Url", Column = "RepositoryUrl")]
[ColumnMap("Repository.Branch", Column = "RepositoryBranch")]
[ColumnMap("Location.FilePath", Column = "FilePath")]
internal static class CodeAnalysisRequestMap;
```

A query reads a member of an owned object, `r.Repository.Url`, as its column wherever it reads a
column: in `Where`, `Select`, `OrderBy`, `GroupBy` keys, aggregates and join keys.

- The owned object itself is no column. Projecting it whole, `new Row(r.Id, r.Repository)`,
  reports ZAO107; project its members instead. A null test on it, `r.Outcome == null`, tests that
  every one of its columns is NULL, and `!= null` is its negation. That holds for a required owner
  too, so `r.Repository == null` is translated rather than taken as false.
- A member of an object the map does not declare owned is not mapped, and reports ZAO105.
- A dotted path has exactly two segments, `Owner.Property`; a deeper one reports ZAO106. An owned
  type nested in an owned type is not supported: a member of it, such as
  `r.Repository.Origin.FilePath`, reports ZAO102 in `Where` or an ordering and ZAO107 in `Select`,
  and a null test on the nested object, `r.Repository.Origin == null`, reports ZAO105.
- An optional owned object, a property annotated `?` such as `AnalysisOutcome? Outcome`, that is
  absent has every one of its columns NULL. Its members read NULL then, where C# would throw on
  the null owner, so each member of an optional owned object counts as nullable: a `bool` member
  as `bool?`, a `string` as `string?`. The rules of [Nulls compare as in C#](#nulls-compare-as-in-c)
  apply to it as to a nullable column: `r.Outcome!.HasFailedValidation == true` does not match a
  request without an outcome, while `!r.Outcome!.HasFailedValidation` and `!= true` do. Projecting
  such a member needs a parameter that takes the NULL, such as `bool?`; a `bool` parameter reports
  ZAO107. This differs on purpose from the `Value` of a nullable value object, which is rejected:
  without the lift a member of an optional owned object could not be used at all. Without nullable
  annotations, an owned object counts as present, as everywhere.
- An owned type is a class or struct whose properties are the columns. `[OwnedMap]` on a property
  that binds as one value, such as a `string`, an enum, a value object with a `Value` or a
  single-argument record, or on a nullable value type or a collection, reports ZAO106.

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
  from code, or any use of it as a method group, reports [ZAO109](../diagnostics/ZAO109.md). Call the
  `[LinqQuery]` method instead. Keep companions `private`.
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
| `Task<int>`, `Task<long>`, `Task<double>`, `Task<decimal>`, a nullable one, `DateTime?`, `DateTimeOffset?` or `string?`, or the same in `ValueTask`, after `Sum`, `Min`, `Max` or `Average` | see [Scalar aggregates](#scalar-aggregates) |

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

## Parents with children

A companion can return a tuple of 2 to 7 `IQuery<T>`, one per result set, with a tuple literal of
complete chains as its body. The method returns the matching tuple. Each element becomes its own
statement, and the statements run as one `IAsyncDbBatch` round trip, through the same multi-result
path as a [`[Query]` head and lines method](multi-result-set.md):

```csharp
[LinqQuery(nameof(WithExecutionsQuery))]
public partial Task<(TaskHead? Head, List<ExecutionLine> Executions)> WithExecutions(Guid id, CancellationToken ct);

private static (IQuery<TaskHead>, IQuery<ExecutionLine>) WithExecutionsQuery(Guid id) =>
    (Sql.Table<TaskEntity>().Where(t => t.Id == id).Select(t => new TaskHead(t.Id, t.Title)),
     Sql.Table<ExecutionEntity>()
        .Where(e => e.TaskId == id)
        .OrderBy(e => e.ExecutedAt)
        .Select(e => new ExecutionLine(e.Id, e.InputTokens)));
```

```sql
SELECT "Id", "Title" FROM "Tasks" WHERE "Id" = @id LIMIT 1;
SELECT "Id", "InputTokens" FROM "Executions" WHERE "TaskId" = @id ORDER BY "ExecutedAt"
```

- Each element takes a shape from the table above: a list, `T?` for the first row, or the value of
  a terminal such as `Count()`. A stream is not an element shape.
- A missing parent reads as a `null` head and an empty child list. With a nullable tuple,
  `Task<(TaskHead? Head, List<ExecutionLine> Executions)?>`, the whole tuple is `null` when the first
  result set is empty, as for `[Query]`.
- Each batch command binds only the parameters its own statement uses.
- The caller assembles the object graph from the tuple. An element that does not fit its chain, or a
  tuple of another length, reports [ZAO112](../diagnostics/ZAO112.md), naming the element.

### Children of a paged parent set

For a page of parents, the child chain filters with `IQuery<T>.Contains`: a subquery that repeats the
parent chain's order and page, and selects its id. It renders as an `IN` subquery, with the
`ORDER BY`, `LIMIT` and `OFFSET` inside it, so it selects the same page of parents as the first
statement:

```csharp
[LinqQuery(nameof(PageQuery))]
public partial Task<(List<TaskHead> Tasks, List<ExecutionOfTask> Executions)> Page(int skip, int take, CancellationToken ct);

private static (IQuery<TaskHead>, IQuery<ExecutionOfTask>) PageQuery(int skip, int take) =>
    (Sql.Table<TaskEntity>()
        .OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id)
        .Skip(skip).Take(take)
        .Select(t => new TaskHead(t.Id, t.Title)),
     Sql.Table<ExecutionEntity>()
        .Where(e => Sql.Table<TaskEntity>()
            .OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id)
            .Skip(skip).Take(take)
            .Select(t => t.Id)
            .Contains(e.TaskId))
        .OrderBy(e => e.ExecutedAt).ThenBy(e => e.Id)
        .Select(e => new ExecutionOfTask(e.Id, e.TaskId, e.InputTokens)));
```

`ExecutionOfTask(Guid Id, Guid TaskId, int InputTokens)` carries the parent id, so the caller can
group the children by task:

```sql
SELECT "Id", "Title" FROM "Tasks" ORDER BY "CreatedAt" DESC, "Id" LIMIT GREATEST(@take, 0) OFFSET GREATEST(@skip, 0);
SELECT "Id", "TaskId", "InputTokens" FROM "Executions"
WHERE "TaskId" IN (SELECT "Id" FROM "Tasks" ORDER BY "CreatedAt" DESC, "Id" LIMIT GREATEST(@take, 0) OFFSET GREATEST(@skip, 0))
ORDER BY "ExecutedAt", "Id"
```

- The children are exactly those of the returned parents when the data does not change between
  the two statements. Under PostgreSQL's default READ COMMITTED each statement of the batch reads
  its own snapshot, so a row inserted or reordered between them can shift the page. For a strict
  guarantee, call the method inside a REPEATABLE READ transaction, where both statements share one
  snapshot. Either way, group the children by their parent id, so a stray child drops out.
- End both orderings with a unique key, such as `ThenBy(t => t.Id)`. Rows that tie on every key
  have no defined order, so without one the page and the subquery could pick different rows.
  Paging without any `OrderBy` warns [ZAO110](../diagnostics/ZAO110.md), inside the subquery too.
- The subquery is a full `Sql.Table<T>()` chain, with `Where`, ordering, `Skip`, `Take` and joins,
  ending in a `Select` of exactly one mapped column. It may use companion parameters, which bind on
  the statement that contains it, but not the outer row: a correlated subquery reports
  [ZAO102](../diagnostics/ZAO102.md).
- `!query.Contains(x)` renders `NOT IN` when `x` cannot be null.
- `Contains` follows C#, which never errs on a null. `NOT IN` is NULL for every row once the
  subquery selects one NULL, so the selected column must be non-nullable, or the query reports
  ZAO102. A nullable column becomes eligible with a `!= null` filter in the subquery:

  ```csharp
  .Where(e => !Sql.Table<TaskEntity>()
      .Where(t => t.ParentId != null)
      .Select(t => t.ParentId)
      .Contains(e.TaskId))
  ```

- The outer value may be null, such as a nullable foreign key. Cast the selected column to the
  nullable type, so `Contains` takes the value; the column still selects no NULL. A null value is
  not contained, as in C#: `IN` is NULL for it, which the `WHERE` drops, and `!Contains` renders
  `(... IN (...)) IS NOT TRUE`, which keeps the row:

  ```csharp
  .Where(t => Sql.Table<TaskEntity>()
      .Where(p => p.Title == title)
      .Select(p => (Guid?)p.Id)
      .Contains(t.ParentTaskId))
  ```

- A subquery with a join aliases its sources `s0`, `s1`, ..., which never meet the outer query's
  `t0`, `t1`, .... A subquery without one is not aliased.

### A parent with owned columns

A parent's [owned types](#owned-types) are columns of its table, so the head reads them like any
other column. An optional owned object can get an element of its own, which is `null` when the
object is absent:

```csharp
[LinqQuery(nameof(WithIterationsQuery))]
public partial Task<(RequestHead? Request, OutcomeLine? Outcome, List<IterationLine> Iterations)> WithIterations(
    Guid id, CancellationToken ct);

private static (IQuery<RequestHead>, IQuery<OutcomeLine>, IQuery<IterationLine>) WithIterationsQuery(Guid id) =>
    (Sql.Table<CodeAnalysisRequest>()
        .Where(r => r.Id == id)
        .Select(r => new RequestHead(r.Id, r.Repository.Url, r.Repository.Branch, r.Location.FilePath)),
     Sql.Table<CodeAnalysisRequest>()
        .Where(r => r.Id == id && r.Outcome != null)
        .Select(r => new OutcomeLine(r.Outcome!.PullRequestUrl, r.Outcome!.HasFailedValidation)),
     Sql.Table<CodeAnalysisIteration>()
        .Where(i => i.RequestId == id)
        .OrderBy(i => i.Number)
        .Select(i => new IterationLine(i.Number, i.Summary)));
```

`OutcomeLine` takes `bool? HasFailedValidation`: a member of an optional owned object counts as
nullable, even under the `!= null` filter.

### Replacing EF Core's `Include`

Each of Daedalus' seven `Include` queries maps to one tuple companion:

| Site | Parents and children | `[LinqQuery]` pattern |
|---|---|---|
| `TaskRepository.cs:21` | one `Task` by id, its `Executions` | single tuple, see [Parents with children](#parents-with-children) |
| `TaskQueryService.cs:17` | a page of `Task`s by `CreatedAt` descending, their `Executions` | tuple with an IN subquery, see [Children of a paged parent set](#children-of-a-paged-parent-set) |
| `TaskQueryService.cs:36` | one `Task` by id, its `Executions` | single tuple, see [Parents with children](#parents-with-children) |
| `ProjectQueryService.cs:16` | a page of `Project`s by name, their `Tasks` | tuple with an IN subquery, see [Children of a paged parent set](#children-of-a-paged-parent-set) |
| `ProjectQueryService.cs:77` | one `Project` by id, its `Tasks` | single tuple, see [Parents with children](#parents-with-children) |
| `CodeAnalysisRepository.cs:22` | one `CodeAnalysisRequest` by id with owned `Repository`, `Location` and `Outcome`, its `Iterations` | single tuple over owned columns, see [A parent with owned columns](#a-parent-with-owned-columns) and [Owned types](#owned-types) |
| `BrainstormRepository.cs:37` | one `BrainstormSession` by id, its `Messages` by `CreatedAt` | single tuple with an ordered child chain, see [Parents with children](#parents-with-children) |

The paged sites order by a column that need not be unique, so add `ThenBy(x => x.Id)` to both the
page and its subquery. EF Core fills the navigation collections; here the caller groups the child
rows by their parent id.

## Projection

`Select` constructs a positional record through the constructor its rows are read with. Each argument is
a mapped column, a companion parameter or a literal; named arguments are put in constructor order.

```csharp
.Select(t => new TaskLine(t.Id, t.Title, t.Order))   // SELECT "Id", "Title", "Order"
```

A column stored by name reads back into its enum. Any other projection, such as an anonymous type, an
expression or a single column into `List<Guid>`, reports [ZAO107](../diagnostics/ZAO107.md). After a
`GroupBy` the arguments are group keys and aggregates instead, see [GroupBy](#groupby); after a `Join`
they read the joined sources, see [Joins](#joins).

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
| `Sum`, `Min`, `Max`, `Average` | see [Scalar aggregates](#scalar-aggregates) |
| `GroupBy(t => key)` | see [GroupBy](#groupby) |
| `Join(Sql.Table<U>(), outer, inner, result)` | see [Joins](#joins) |

The order is checked, and each break of it reports [ZAO102](../diagnostics/ZAO102.md):

- `Where` or an ordering after `Skip` or `Take`: LINQ would apply it to the page, but SQL filters and
  orders before it pages;
- a second `OrderBy`, since LINQ would make its key primary; use `ThenBy` to add a key;
- `Take` before `Skip`, and `Skip` or `Take` twice;
- an operator after `Select`, other than a terminal, and any operator after a terminal.

Ordering keys are mapped columns. An enum stored by name orders by its member names, as text. A
string orders by the database collation, where LINQ's `OrderBy` uses the current culture, so the two
can differ: under the `C` collation `"B"` sorts before `"a"`. This is a known difference.

Nulls sort as in LINQ: first in an ascending ordering, last in a descending one. PostgreSQL's own
default is the opposite, so a key that can be null, such as a nullable column, a nullable group key,
or a `Min`, `Max` or `Average` of a nullable selector, renders `NULLS FIRST` after `ORDER BY "A"` and
`NULLS LAST` after `"A" DESC`. A key that cannot be null renders without it. Releases up to 2.5.0
ordered a nullable key by PostgreSQL's default, nulls last ascending, so the row order of such a query
changes with this version. A default B-tree index is ascending with nulls last and serves neither
`ORDER BY "A" NULLS FIRST` nor `"A" DESC NULLS LAST`, so `OrderBy(t => t.A).Take(n)` on a nullable
column that used an index top-N scan now sorts. Index such a column as `("A" NULLS FIRST)`, or make
it non-nullable if it never holds NULL.

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
| `p.HasValue`, `!p.HasValue`, for a nullable parameter | `@p IS NOT NULL`, `@p IS NULL` |
| `a && b`, `a \|\| b`, `!a` | `a AND b`, `(a OR b)`, `NOT (a)`, or `(a) IS NOT TRUE` when `a` can be null |
| `t.IsActive` | `"IsActive"` |
| `t.Status == WorkStatus.Done` | `"Status" = 1`, or `'Done'` when stored by name |
| `string.Equals(t.Title, title)`, with or without `StringComparison.Ordinal` | `"Title" = @title` |
| `t.Title.Contains(text)` | `"Title" LIKE '%' \|\| <escaped @text> \|\| '%' ESCAPE '\'` |
| `t.Title.StartsWith(text)`, `EndsWith(text)` | the same `LIKE`, with `%` on one side |
| `Sql.ILike(t.Title, pattern)` | `"Title" ILIKE @pattern` |
| `ids.Contains(t.Id)` | `"Id" = ANY(@ids)` |
| `Sql.Table<P>().Select(p => p.Id).Contains(t.PId)`, or `Select(p => (Guid?)p.Id)` for a nullable `t.PId` | `"PId" IN (SELECT "Id" FROM "P")`; see [children of a paged parent set](#children-of-a-paged-parent-set) |
| `Sql.Row(t.CreatedAt, t.Id) > Sql.Row(at, id)` | `("created_at", "Id") > (@at, @id)` |

- `Contains`, `StartsWith` and `EndsWith` match literally: `%`, `_` and `\` in the value are escaped,
  so `"50%"` finds the text `50%`. They also accept `StringComparison.Ordinal`. `Sql.ILike` is
  case-insensitive and passes the pattern through, wildcards included.
- `==` on strings works too. `string.Equals` and the `StringComparison.Ordinal` overloads exist for
  codebases whose analyzers reject `==` or a comparison without a `StringComparison`. Any other
  `StringComparison` reports ZAO102.
- `= ANY` takes an array parameter: `T[]`, `List<T>` or `IReadOnlyList<T>` of `string`, `int`, `long`,
  `float`, `double` or `Guid`. Array parameters need Npgsql, and a slim data source needs `EnableArrays()`.
- An enum parameter compared with a column stored by name binds by its member name. Compared with
  columns stored both ways it reports [ZAO111](../diagnostics/ZAO111.md).
- Typed ids and value objects compare by their underlying value. A composite such as
  `Money(decimal Amount, string Currency)`, which spans several columns or parameters, reports ZAO102,
  as a companion parameter or as an entity member.
- A `float` constant is written `0.1::real`, so it compares with a `real` column at the column's
  precision; a bare `0.1` would compare as double precision and miss the stored value.
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
instead: `@ids::uuid[] IS NULL`. A nullable one projected by `Select` is cast there too:
`SELECT "Id", @at::timestamptz`. A nullable parameter of a type with no such cast, such as `byte[]?`,
reports ZAO102 in a null test or a projection. A `[Param(DbType = ...)]` you write on the method's
parameter wins over the generated type, and ZAO015 checks it against the dialect as for `[Query]`.

A parameter the rendered SQL does not use, for example one that only a projection dropped by `Count()`
read, stays in the signature but is not bound.

## Scalar aggregates

`Sum`, `Min`, `Max` and `Average` end a chain like `Count` does, and the method returns the value:

```csharp
[LinqQuery(nameof(TokensQuery))]
public partial Task<long> Tokens(Guid taskId, CancellationToken ct);

[LinqQuery(nameof(MeanTokensQuery))]
public partial Task<double> MeanTokens(Guid taskId, CancellationToken ct);

private static IQuery<long> TokensQuery(Guid taskId) =>
    Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Sum(e => (long)e.InputTokens);

private static IQuery<double> MeanTokensQuery(Guid taskId) =>
    Sql.Table<ExecutionEntity>().Where(e => e.TaskId == taskId).Average(e => e.InputTokens);
```

| C# | SQL |
|---|---|
| `Sum(e => e.Tokens)` | `COALESCE(sum("Tokens"), 0)::<type>`, an exact cast back to the selector's type: `int`, `bigint`, `numeric` or `double precision`. A `decimal` sum over a `decimal` column gets no cast: `COALESCE(sum("Price"), 0)` |
| `Sum(e => e.Weight)`, a `float` widened to `double` | `COALESCE(sum("Weight"::double precision), 0)::double precision`: the sum adds doubles, as C# does |
| `Average(e => e.Tokens)`, `int` or `long` | `avg("Tokens")::double precision` |
| `Average(e => e.Price)`, `decimal` | `avg("Price")` |
| `Min(e => e.At)`, `Max(e => e.At)` | `min("At")`, `max("At")` |

The result follows LINQ to Objects, with these rules:

- `Sum` is `0` over no rows, as in LINQ, through `COALESCE`. A nullable selector, `Sum(e => e.Bonus)`
  with `int? Bonus`, still sums to `0` when every value is null.
- `Average` is a `double`, for `int` and `long` selectors too, so `Average(e => e.Tokens)` keeps its
  fraction. A `decimal` average agrees with LINQ only to about 16 significant digits, because
  PostgreSQL's `avg(numeric)` has its own scale. This is a known difference.
- `Average`, `Min` and `Max` over no rows return `null` for a nullable result: `Task<double?>`,
  `Task<DateTime?>`. For a non-null result such as `Task<double>` they throw
  `InvalidOperationException` with LINQ's message, "Sequence contains no elements", when no row
  matches, as LINQ does. Ask for the nullable result, or check with `Any()`, when the set can be empty.
- `Min` and `Max` over a `bool`, a `Guid` or an enum stored by name report ZAO102, because
  PostgreSQL has no such aggregate or would compare names. A method that returns an enum from a
  terminal reports [ZAO112](../diagnostics/ZAO112.md).
- `Min` and `Max` over a string compare by the database collation, where LINQ uses the current
  culture, so they can pick a different string: under the `C` collation `"B"` is less than `"a"`.
  This is a known difference.
- Over a page, `Skip` and `Take` run in a subquery:
  `SELECT COALESCE(sum("Tokens"), 0)::bigint FROM (SELECT "Tokens" FROM ... ORDER BY ... LIMIT ...) AS q`.
  The `ORDER BY` stays inside it, because which rows form the page decides the result; without one
  the chain reports [ZAO110](../diagnostics/ZAO110.md). A paged `Count` or `Any` needs no ordering.

## GroupBy

`GroupBy` makes `IQuery<IGroup<TKey, T>>`. A group has `g.Key` and the aggregates `Count`,
`LongCount`, `Sum`, `Min`, `Max` and `Average`, and the chain ends with a `Select` that builds a
positional record from them:

```csharp
private static IQuery<ModelDayTokens> TokensPerModelAndDayQuery(DateTime since) =>
    Sql.Table<ExecutionEntity>()
        .Where(e => e.ExecutedAt >= since)
        .GroupBy(e => new { e.ModelId, Day = e.ExecutedAt.Date })
        .OrderBy(g => g.Key.Day)
        .ThenByDescending(g => g.Sum(e => (long)e.InputTokens))
        .Select(g => new ModelDayTokens(
            g.Key.ModelId, g.Key.Day, g.Sum(e => (long)e.InputTokens), g.Count()));
```

```sql
SELECT "ModelId", date_trunc('day', "ExecutedAt"), sum("InputTokens")::bigint, count(*)::int
FROM "Executions" WHERE "ExecutedAt" >= @since
GROUP BY "ModelId", date_trunc('day', "ExecutedAt")
ORDER BY date_trunc('day', "ExecutedAt"), sum("InputTokens")::bigint DESC
```

A key is one of:

- a mapped column, `GroupBy(e => e.ModelId)`;
- an anonymous type of columns and `.Date` members, `new { e.ModelId, Day = e.ExecutedAt.Date }`;
- `.Date` alone, `GroupBy(e => e.ExecutedAt.Date)`.

Any other key reports ZAO102.

`.Date` is the calendar day, as in C#:

| Column | SQL | Day |
|---|---|---|
| `DateTime` declared `TimestampTz` | `date_trunc('day', "col", 'UTC')` | the UTC day whatever the session's `TimeZone`, as a `DateTime` of `Kind` `Utc`, as C#'s `.Date` is on the UTC value Npgsql reads. Needs PostgreSQL 12 or later |
| `DateTime` declared `Timestamp` | `date_trunc('day', "col")` | the day of the stored value, exact |
| `DateTime` not declared | `date_trunc('day', "col")` | for a `timestamptz` column, cut in the session's `TimeZone`. Declare the column `TimestampTz`, see [Map the entity](#map-the-entity), or connect with `Timezone=UTC` |
| `DateTimeOffset` | `date_trunc('day', "col" AT TIME ZONE 'UTC')` | the UTC day, as a `DateTime` of `Kind` `Unspecified`, as `DateTimeOffset.UtcDateTime.Date` is |

The group aggregates render like the [scalar ones](#scalar-aggregates), with the same casts and
rules: `Average` is a `double` and a `float` sum adds doubles. A group is never empty, so the
empty-input differences do not arise, and `Sum` is `COALESCE`d only over a nullable selector, whose
values can all be null: `COALESCE(sum("Bonus"), 0)::int`. `Count()` is `count(*)::int` and
`LongCount()` is `count(*)`.

`OrderBy` and `ThenBy` after `GroupBy` take keys, key members and aggregates, as in the example. The
`Select` may use only `g.Key`, its members, group aggregates, companion parameters and literals. Any
other use of the group, such as `g.Count() + 1` or a column that is not a key, reports ZAO102, and a
`GroupBy` without a final `Select` reports [ZAO107](../diagnostics/ZAO107.md).

Nulls order as LINQ orders them, see [Operators and their SQL](#operators-and-their-sql). A nullable
key or a nullable aggregate such as `Max` of a nullable column renders `NULLS FIRST` ascending and
`NULLS LAST` descending.

## Joins

`Join` is an inner join. The inner side is `Sql.Table<T>()`, optionally narrowed by `Where`, which
renders in the `ON` clause:

```csharp
private static IQuery<TaskCost> CostsByTaskQuery(ProjectId projectId) =>
    Sql.Table<ExecutionEntity>()
        .Join(
            Sql.Table<TaskEntity>().Where(t => t.ProjectId == projectId),
            e => e.TaskId,
            t => t.Id,
            (e, t) => new { Execution = e, Task = t })
        .GroupBy(x => new { x.Task.Id, x.Task.Title })
        .OrderBy(g => g.Key.Title)
        .Select(g => new TaskCost(g.Key.Id, g.Key.Title, g.Sum(x => (long)x.Execution.InputTokens), g.Count()));
```

```sql
SELECT t1."Id", t1."Title", sum(t0."InputTokens")::bigint, count(*)::int
FROM "Executions" AS t0
INNER JOIN "app"."Tasks" AS t1 ON t0."TaskId" = t1."Id" AND t1."ProjectId" = @projectId
GROUP BY t1."Id", t1."Title" ORDER BY t1."Title"
```

The rules:

- Each key is one mapped column, on either side. The outer key comes first in the `ON`.
- `.Value` of a value-object column, such as a typed id, reads as the column, in a join key and
  anywhere else in a chain: `t.ProjectId.Value` is the `"ProjectId"` column. `.Value` of a
  `Nullable<TValueObject>` is rejected, and so is `.Value` of a reference-type value object
  annotated `?`, such as `ProjectRef? Parent`: C# would throw on a null where SQL drops or nulls the
  row. Code without nullable annotations cannot be checked, so there such a member counts as not null.
- The result selector is an anonymous type of the two range variables, `new { Execution = e, Task = t }`,
  or members carried over from an earlier join, `new { et.Execution, et.Task, Project = p }`. A
  chain may join more than once. A computed member reports ZAO102.
- Later operators read the sources through those names: `x.Task.Title`, `x.Execution.InputTokens`. The
  source tables are aliased `t0`, `t1`, and so on, in join order, only when the chain has a join. A
  chain without a join renders exactly as before, with no alias.
- A joined chain ends with `Select`, `GroupBy` and `Select`, or a terminal. `Where` before the `Join`
  filters the outer side.

Not translated, each reported as ZAO102: a composite key, a computed result member, an inner side
with an operator other than `Where`, and `Join` after `GroupBy`, `Select`, `Skip`, `Take` or
`OrderBy`. There is no left-join operator yet.

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
| [ZAO100](../diagnostics/ZAO100.md) | Error | The companion is missing, overloaded, not static, not one expression, or does not return `IQuery<T>` or a tuple literal of them. |
| [ZAO101](../diagnostics/ZAO101.md) | Error | The companion's parameters are not the method's value parameters, by name, type and order. |
| [ZAO102](../diagnostics/ZAO102.md) | Error | An operator, predicate, key, count or chain order the translator does not support. |
| [ZAO103](../diagnostics/ZAO103.md) | Error | The chain reads a field, local or other value that is not a companion parameter or constant. |
| [ZAO104](../diagnostics/ZAO104.md) | Error | The queried entity has no `[TableMap]`. |
| [ZAO105](../diagnostics/ZAO105.md) | Error | The chain uses a member that is not a mapped column. |
| [ZAO106](../diagnostics/ZAO106.md) | Error | A `[TableMap]`, `[OwnedMap]` or `[ColumnMap]` is invalid: a duplicate, an unknown property or path, a shared column, `EnumAsString` on a non-enum, `DateTimeStorage` on a non-`DateTime`, or an owned property whose type has no properties to map. |
| [ZAO107](../diagnostics/ZAO107.md) | Error | The projection is not a positional record of columns, parameters and literals. |
| [ZAO108](../diagnostics/ZAO108.md) | Error | The repository does not declare `[Dialect(SqlDialect.PostgreSql)]`. |
| [ZAO109](../diagnostics/ZAO109.md) | Error | Code calls a companion, which never runs. |
| [ZAO110](../diagnostics/ZAO110.md) | Warning | `Skip` or `Take` without an ordering returns rows in no defined order. |
| [ZAO111](../diagnostics/ZAO111.md) | Error | An enum parameter is compared with columns that store the enum differently. |
| [ZAO112](../diagnostics/ZAO112.md) | Error | The method's return type, or one of its tuple elements, does not fit the companion's `IQuery<T>`. |

## Not supported yet

The next planned work, in this order. Until then, write such a query as a raw `[Query]` or `[Command]`:

- Set-based `ExecuteUpdate` and `ExecuteDelete`, optimistic concurrency checks, and `FOR UPDATE`.

Also not supported today:

- `HAVING`: `Where` after `GroupBy` reports ZAO102. Filter the rows before grouping.
- Counting groups, such as `GroupBy(...).Count()` (ZAO102), and `GroupBy` without a final `Select`
  (ZAO107).
- Left joins: there is no left-join operator yet.
- Composite join keys, an inner side other than `Sql.Table<T>()` with `Where`, and `Join` after
  `GroupBy`, `Select`, `Skip`, `Take` or `OrderBy` (ZAO102).
- Navigations and object graphs: a tuple companion returns flat result sets, and the caller
  assembles the graph.
- Correlated subqueries, such as `EXISTS` or a subquery that reads the outer row (ZAO102). Join,
  or filter the subquery on companion parameters.
- An owned type nested in an owned type: a deeper `[ColumnMap]` path reports ZAO106, and a read
  of the nested object's members ZAO102, ZAO105 or ZAO107, see [Owned types](#owned-types).
- Owned collections, `OwnsMany`: `[OwnedMap]` on a collection reports ZAO106.
- A stream, `IAsyncEnumerable<T>`, as a tuple element (ZAO112), and a companion tuple of more than
  7 elements (ZAO100).
- Projecting a single column into a list of scalars, such as `Select(t => t.Id)` into `List<Guid>`
  (ZAO107). Project into a one-member record instead.
- `Skip` and `Take` after `Select` (ZAO102). Put them before it.
- Dialects other than PostgreSQL (ZAO108).
