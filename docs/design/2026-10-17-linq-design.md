# ZeroAlloc.ORM.Linq — LINQ as a compile-time SQL source

**Status:** design, awaiting approval · **Date:** 2026-10-17
**Origin:** Daedalus handover `Daedalus.NET/docs/plans/2026-10-17-zeroalloc-linq-handover.md` (milestone 3, Native AOT)

## 1. Intent

Daedalus must ship as Native AOT. EF Core can't: its query pipeline builds and compiles expression trees at run time.
ZeroAlloc.ORM is AOT-clean but raw-SQL only, so moving Daedalus today means rewriting about 32 files of EF queries
as hand-written SQL, losing typed predicates, rename safety and compile errors when a column or property changes.

ZeroAlloc.ORM.Linq lets a query method's SQL be written as **LINQ instead of a SQL string**. The generator reads the
LINQ chain at build time, translates it to parameterised, readable SQL, and implements the method exactly as it
implements a `[Query]` today. Nothing builds, compiles or reflects over an expression at run time. A chain the
generator can't translate is a **build error**, never a silent fallback.

**The library's direction does not change.** ZeroAlloc.ORM stays "typed SQL behind `partial` methods that return
rows". It does not become an entity-graph ORM: no navigations, no `Include`, no entity writes, no change tracking.
LINQ is only a second way to write the SQL. Those EF-like features may be added later as an additive step; this
design leaves the door open but doesn't walk through it.

Daedalus is the first consumer; Thalos' `Workflow.Orm` is a possible second. It must work for any ZeroAlloc.ORM
consumer.

## 2. Decisions made with the owner

1. **Placement.** Everything lives in the **ZeroAlloc.ORM repo**. The LINQ translation ships as a new package,
   `ZeroAlloc.ORM.Linq` (with its generator), in the same release train. The minimal table/column mapping (§2.6) goes
   into ZeroAlloc.ORM's abstractions so `[Query]` can use it too. No `ZeroAlloc.Linq` repo: zero-allocation in-memory
   LINQ is already covered by ZLinq.
2. **Direction: LINQ as SQL source only.** See §1. Explicitly rejected for now: the full entity model with
   navigations, `Include` object graphs and `Insert`/`Update`/`Delete(entity)`.
3. **Nothing existing breaks.** All of this is additive and opt-in:
   - `[Query]` / `[Command]` / `[StoredProcedure]` behave exactly as today. Existing generator snapshot tests must stay
     byte-identical.
   - Mapping is optional; unmapped types keep today's positional / column-name materialisation.
   - Not referencing `ZeroAlloc.ORM.Linq` means no extra generated code.
   - `api-compat` must classify the ZeroAlloc.ORM changes as additive (minor bump).
4. **Queries are defined in methods.** A query is a `partial` method the generator implements, paired with a companion
   method whose body is the LINQ chain. The companion is **never executed**: the generator reads its syntax and
   semantic model at build time. The companion's parameters become SQL parameters. No interceptors, no closures or
   delegates at run time, no expression trees.

   ```csharp
   public sealed partial class TaskStore(IAsyncDbConnection db)
   {
       [LinqQuery(nameof(OpenTasksQuery))]
       public partial Task<List<TaskSummary>> OpenTasks(ProjectId p, TaskStatus? status, CancellationToken ct);

       private static IQuery<TaskSummary> OpenTasksQuery(ProjectId p, TaskStatus? status) =>
           Sql.Table<TaskEntity>()
              .Where(t => t.ProjectId == p && (status == null || t.Status == status))
              .OrderBy(t => t.CreatedAt)
              .Select(t => new TaskSummary(t.Id, t.Title));
   }
   ```

   The generated `OpenTasks` holds the SQL as a `const string`, binds `p` and `status` as `[Query]` does, and
   materialises through the ORM's existing emitters. Every return shape `[Query]` supports is available: scalar,
   single row, list, `IAsyncEnumerable`, and caller transactions via an `IAsyncDbTransaction` parameter.

   Why methods and not inline LINQ: in `db.Table<T>().Where(t => t.X == p)`, `p` lives in a closure the compiler
   generates, and generated code can't read it without reflection or compiler-internal names. ZLinq was checked and
   doesn't apply: it runs its lambdas in memory, while here the lambda must become SQL before any row exists.
5. **One chain per companion.** The body is a single expression from `Sql.Table<T>()` to the last operator. Optional
   filters are written inline (`status == null || t.Status == status`) or with a `WhereIf` operator and become
   `(@p IS NULL OR …)`. Building a query through locals and `if` branches is a build error whose diagnostic shows the
   inline form.
6. **Minimal mapping, declared in Infrastructure.** The generator needs table and column names, nothing more about
   relationships. Mapping lives in map classes outside the entity, so Daedalus' Domain project stays untouched. EF's
   naming conventions are the default; a map lists only overrides.
7. **Writes:** set-based `ExecuteUpdate` / `ExecuteDelete` chains, in the same method + companion pattern (a
   `[LinqCommand]`-style attribute), returning rows affected. Inserts and single-row writes stay `[Command]` SQL.
8. **Dialects:** **PostgreSQL only** in the first release, with SQL emission behind a dialect seam so Sqlite and
   SQL Server can follow.

## 3. Goals

Every shape below translates, runs on ZeroAlloc.ORM, and is covered by tests against a real PostgreSQL 16
(Testcontainers, like the existing integration suite). File references are Daedalus `main` `8173fd55`.

- **Filters:** `==`, `!=`, ranges, `null` checks, `&&`/`||`/`!`; `Contains` on a collection parameter
  (→ `= ANY(@p)`); case-insensitive match (`ILike`, `FailurePatternDatabase.cs:32`); enums stored as `int` or
  `string` (existing `[StoreAsString]`).
- **Ordering and paging:** `OrderBy`/`ThenBy`/`…Descending`, `Take`/`Skip`; **tuple keyset paging**
  `(CreatedAt, Id) > (@a, @b)` (`PostgresMemoryStore.cs:210`).
- **Projections and results:** `Select` into records/DTOs; `Count`, `Any`, `FirstOrDefault`, `SingleOrDefault`, lists
  and streaming, expressed through the partial method's return type and a terminal operator in the chain.
- **Aggregation** (`CostAnalyticsService.cs`): `GroupBy` on anonymous keys with `Sum`/`Count` (`:80`, `:144`);
  `.Date` truncation on the key; **explicit inner joins** across three tables followed by `GroupBy`
  (`:194-215`, `:239-252`). Joins name their keys (`Join(…, a => a.TaskId, b => b.Id, …)`); there are no navigations.
- **Loading a parent with its children** (replaces EF `Include`, `TaskRepository.cs:24`, `TaskQueryService.cs:19,38`,
  `ProjectQueryService.cs:23,79`, `CodeAnalysisRepository.cs:24`, filtered and ordered in `BrainstormRepository.cs:38`):
  the ORM's existing multi-result head + lines method, with a LINQ companion per result set, run as one
  `IAsyncDbBatch`. The caller (Daedalus) assembles the object graph.
- **Set-based writes:** `ExecuteUpdate(setters)` with computed setters and `ExecuteDelete()` with predicates (the six
  Postgres stores named in the handover).
- **Optimistic concurrency:** PostgreSQL `xmin` and `byte[]` row-version columns usable in predicates, so a guarded
  `ExecuteUpdate`/`ExecuteDelete` (`… && t.Xmin == expected`) works; an option on the command attribute turns
  "zero rows affected" into a concurrency-conflict exception.
- **Row locking:** a `.ForUpdate()` operator → `SELECT … FOR UPDATE` (`PostgresAgentSessionStore.cs:115-119`).
- **Column types:** typed ids (`ZeroAlloc.ValueObjects`), enums, `DateTimeOffset`, `Guid`, nullable primitives,
  whole-document **jsonb** and **`text[]`** columns read in projections, and `ANY(@array)` comparisons.
- **Existing schema as EF created it:** explicit table and column names, including EF's quoted PascalCase names. No
  table is renamed or reshaped.
- **Inspectable SQL:** each query's SQL is a `const string` in generated source with a comment naming the method;
  parameterised only, never value concatenation.
- **AOT-clean:** zero trim/AOT warnings in the AOT smoke gates, no suppressions of any kind.

## 4. Non-goals

- Navigations, `Include`, object graphs, `Insert`/`Update`/`Delete(entity)` (decision 2). Possible later, additively.
- Change tracking, unit of work, lazy loading.
- Querying inside jsonb documents.
- Model-generated migrations (`MigrationRunner` with hand-written SQL stays the model).
- EF API compatibility, a runtime `IQueryable` provider, or speeding up EF Core queries.
- Inline LINQ at arbitrary call sites (decision 4); composing queries across locals and branches (decision 5).
- Dialects other than PostgreSQL in the first release.

## 5. Anti-goals — failure even if it "works"

- Any run-time expression-tree construction, `Expression.Compile`, reflection, or client-side evaluation.
- A `[LinqQuery]` whose chain isn't fully translated at build time. Every one is translated or a build error.
- Any change in behaviour or generated output for existing raw-SQL consumers.
- ZeroAlloc.ORM quietly growing into an entity-graph ORM through this work.
- SQL a reviewer can't read, or that embeds values instead of parameters.
- Diagnostic suppressions or `UnconditionalSuppressMessage` to get the AOT gate green.

## 6. Constraints

- Repo conventions apply: PublicAPI analyzers (additive `PublicAPI.Unshipped.txt`), `api-compat`, AOT smoke and
  collision smoke gates, a diagnostics page per code with the help-link test, release-please conventional commits.
- Generator: incremental, `netstandard2.0`, `Microsoft.CodeAnalysis.CSharp` 5.9.0 as today; consumers on the
  .NET 10 SDK.
- Two source generators can't see each other's output. If the LINQ generator is a separate generator assembly, the
  sharing of the mapping and materialisation emitters must be designed on purpose (shared source compiled into both,
  or one generator that switches on the Linq package's presence).
- PostgreSQL 16 with pgvector loaded is the Daedalus target.
- Agents-VM rule: build and test runs one at a time, peak below a few GB.

## 7. The three ORM gaps — separate design, built first

The handover also lists three ZeroAlloc.ORM gaps that block Daedalus regardless of LINQ: adopting a database whose
history lives in `__EFMigrationsHistory` (in place, no data loss), array parameters, and jsonb parameters as a real
type. They get their own design document and land **before** the LINQ work, because `text[]`, `ANY(@array)` and
jsonb in §3 depend on them.

## 8. Calls I made (not discussed with the owner — check these)

- **Map classes:** one generator-read map per queried entity in the consumer's Infrastructure project: attributes and
  constants only, no executable fluent code. It covers table name and schema, column-name overrides, enum storage,
  jsonb and array columns, and system columns such as `xmin`. Exact names and shape are the builder's call. Querying
  an unmapped type is a diagnostic.
- **Results are DTOs/records, not Domain entities.** A query returns what `[Query]` can already materialise: records,
  ctor classes, scalars. Returning a mapped Domain entity with private setters, such as Daedalus' `BrainstormSession`
  with its `_messages` field, is out of scope. Daedalus projects into row records and builds its entities from them.
  Filling private members through `UnsafeAccessor` belongs to the later entity-model step.
- **The query type is not `IQueryable<T>`.** `Sql.Table<T>()` returns a dedicated query type whose operators have empty
  bodies; they exist only so the companion compiles and type-checks. Calling a companion at run time throws an
  `InvalidOperationException` naming the cause, and a diagnostic flags any call to a companion from code.
- **What a companion may reference:** its own parameters, constants, columns of the queried tables and supported
  functions. Anything else, such as fields, other locals or unknown method calls, is a build error.
- **Case-insensitive match:** a `Sql.ILike(column, pattern)` marker function (Postgres), alongside
  `string.Contains/StartsWith/EndsWith`, which become `LIKE` with escaping.
- **Keyset paging:** a row-comparison helper such as `Sql.Row(t.CreatedAt, t.Id) > Sql.Row(a, b)`, since C# tuples
  have no `>`.
- **Diagnostics:** a block of `ZAO` codes reserved for LINQ and mapping (proposal: `ZAO100`–`ZAO199`), allocated up
  front.
- **Order of work:** the §7 gaps first; then mapping plus filters, ordering, paging and projection; then aggregation
  and joins; then multi-result parent + children; then set-based writes, concurrency and locking.
- **Releases:** each phase ships in the owner's normal batched release cycles, so Daedalus can migrate store by store.
  `ZeroAlloc.ORM.Linq` ships on the repo's single release train, so its first release is the next ZeroAlloc.ORM
  version, 2.5.0. Its API is tracked with PublicAPI files from the start and stays additive across the later plans,
  the same rule as the mapping additions to ZeroAlloc.ORM.
- **Definition of done:** the §3 shapes. When the Daedalus gap audit (`docs/plans/2026-10-17-zeroalloc-orm-gap-audit.md`)
  lands, any shape it lists that §3 doesn't cover is agreed with the Daedalus session: added to scope, or written as
  raw `[Query]`.

## 9. Left to the builder

- The query type's API surface and generic shape, and how anonymous types are handled inside a chain (a partial
  method can't return one, so they appear only inside it, for example as `GroupBy` keys).
- How a `[LinqQuery]` names its companion (explicit `nameof` as shown, or a naming convention), and how a multi-result
  method names one companion per result set.
- The internal query model and the SQL emitter structure behind the dialect seam.
- How the generators share mapping and materialisers (constraint in §6).
- `GroupBy` result shapes and the exact `.Date` → `date_trunc` / `::date` mapping.
- Diagnostic wording, IDs within the reserved block, and docs pages.
- Benchmarks against hand-written ADO.NET and EF Core (the allocation profile should match a hand-written `[Query]`).
- Test layout: generator snapshot tests per shape, Postgres integration tests per goal in §3, an AOT smoke consumer.
