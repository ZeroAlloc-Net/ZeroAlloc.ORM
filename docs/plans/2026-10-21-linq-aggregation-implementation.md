# ZeroAlloc.ORM.Linq aggregation and joins — Implementation Plan (LINQ plan 2 of 4)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Translate `GroupBy` with `Sum`/`Count`/`Min`/`Max`/`Average`, `.Date` on timestamps, explicit inner `Join`s, and scalar aggregates, so Daedalus' `CostAnalyticsService` queries and its `Max`/`Average` lookups run as `[LinqQuery]` methods on PostgreSQL.

**Architecture:** Every step extends LINQ plan 1's translator in `src/ZeroAlloc.ORM.Generator/Linq/`:
- `LinqQueryReader` chain walk: new operator cases.
- `SqlAst`: new records.
- `PostgresSqlRenderer`, `LinqQueryBinder` and `SqlParameterReferences`: handle the new records.

The authoring surface in `src/ZeroAlloc.ORM.Linq` gains `GroupBy`, `IGroup<TKey,T>` with group aggregates, `Join`, and scalar aggregate terminals. Every body throws, as in plan 1. Results still materialise through the `[Query]` path, into positional records or scalars.

**Tech Stack:** .NET 10 SDK, the Roslyn incremental generator (`netstandard2.0`), Npgsql 10.0.3, xunit.v3 with AwesomeAssertions, Testcontainers PostgreSQL and GeneratorSnapshot.

**Spec:** `docs/design/2026-10-17-linq-design.md` §3 Aggregation, plus the Daedalus query shapes below. Plan 1 (`docs/plans/2026-10-21-linq-foundation-implementation.md`) and `docs/cookbook/linq.md` describe the translator this builds on.

**Daedalus shapes this plan must translate.** These are from `src/Daedalus.Infrastructure/Services/CostAnalyticsService.cs`, rewritten from anonymous `Select` results into positional records:
- **Q1** (:79): `Executions.GroupBy(e => new { e.ModelId, Day = e.ExecutedAt.Date }).Select(g => new R(g.Key.ModelId, g.Key.Day, g.Sum(e => (long)e.InputTokens), g.Sum(e => (long)e.OutputTokens), g.Count()))`.
- **Q2** (:143): the same shape over `AgentSessions`, summing `long` columns.
- **Q3** (:193): `Executions.Join(Tasks, e => e.TaskId, t => t.Id, (e, t) => new { Execution = e, Task = t }).Join(Projects, et => et.Task.ProjectId, p => p.Id, (et, p) => new { et.Execution, et.Task, Project = p }).GroupBy(x => new { x.Project.Id, x.Project.ProjectName, x.Execution.ModelId }).Select(g => new R(g.Key.Id, g.Key.ProjectName, g.Key.ModelId, g.Sum(x => (long)x.Execution.InputTokens), g.Sum(x => (long)x.Execution.OutputTokens), g.Count()))`.
- **Q4** (:234): `Executions.Join(Tasks.Where(t => t.ProjectId == projectId), e => e.TaskId, t => t.Id, (e, t) => new { Execution = e, Task = t }).GroupBy(…).Select(…)`.
- **Q5** (:272): `Executions.Where(e => e.OutputTokens > 0).Average(e => (double?)e.OutputTokens)`.
- **Q6** (`PostgresAgentSessionStore.cs:130`): `Messages.Where(m => m.SessionId == id).Max(m => (int?)m.Sequence)`.

## Global Constraints

- Ships on the repo's single release train as an additive minor. Public API changes go into `PublicAPI.Unshipped.txt`. New operators are extension methods. `IGroup<TKey,T>` gets only its `Key` member; everything else is an extension method. No optional parameters on shipped signatures.
- Every generator snapshot that exists on `main` stays byte-identical. That includes plan 1's LINQ snapshots: a query without joins renders exactly as it did in 2.5.0, with no table aliases.
- No run-time expression trees, `Expression.Compile`, reflection, client evaluation or interceptors. Every `[LinqQuery]` is fully translated at build time or is a build error.
- C# semantics win, as decided for plan 1:
  - results equal what LINQ to Objects returns for the same data;
  - null comparisons keep plan 1's compensation;
  - where SQL and C# differ, compensate in SQL, or make the query a build error.
- PostgreSQL only. Identifiers are always quoted.
- No new diagnostic IDs. Use ZAO102 (unsupported construct), ZAO105 (unmapped member), ZAO107 (unsupported projection) and ZAO112 (shape mismatch), with messages that name the construct.
- Every new AST record is added to `PostgresSqlRenderer`, `SqlParameterReferences`, `EnumParameterStorage.Visit` and `LinqQueryBinder`, and to the reflection exhaustiveness test `SqlAstExhaustivenessTests`.
- AOT-clean, with no `#pragma`, `SuppressMessage` or `UnconditionalSuppressMessage`. Models stay equatable and symbol-free.
- Commits are conventional. Body lines are ≤ 100 chars, with no nested parentheses. They end with the session's `Co-Authored-By` line and contain no session URLs.
- Agents VM: one test project or publish at a time.

## Review Focus

1. **`Sum` over an empty set, or a nullable selector whose values are all null**, returns `0` as LINQ does, not NULL or a cast error. Test: Task 3 `Sum_of_no_rows_is_zero`.
2. **`Average` over `int`/`long` columns** returns a `double` with its fractional part. PostgreSQL's `avg` returns `numeric`, so the result is read through a cast. Test: Task 3 `Average_keeps_the_fraction`, and Task 4's Q1 case.
3. **A group key with a nullable member**, such as `ModelId = null`, forms its own group, as in LINQ. Test: Task 4 `Null_key_member_forms_one_group`.
4. **Inner join semantics:** an outer row with no inner match disappears, and duplicate inner keys multiply rows. Test: Task 5 `Join_drops_unmatched_and_multiplies_duplicates`.
5. **The same column name in two joined tables** (`Id`, `Name`) is qualified and read into the right record member. Test: Task 5 `Same_column_name_in_both_tables_is_qualified`.

---

### Task 1: Authoring surface — `GroupBy`, `IGroup`, aggregates, `Join`, scalar aggregates

**Files:**
- Modify: `src/ZeroAlloc.ORM.Linq/QueryOperators.cs` and `PublicAPI.Unshipped.txt`.
- Create: `src/ZeroAlloc.ORM.Linq/IGroup.cs` and `src/ZeroAlloc.ORM.Linq/GroupOperators.cs`.
- Test: `tests/ZeroAlloc.ORM.Linq.Tests/AggregationSurfaceTests.cs`.

**Interfaces:**
- Produces, namespace `ZeroAlloc.ORM.Linq`:
  - `public interface IGroup<out TKey, out T> { TKey Key { get; } }`.
  - `QueryOperators.GroupBy<T,TKey>(this IQuery<T>, Func<T,TKey> key) → IQuery<IGroup<TKey,T>>`.
  - `QueryOperators.Join<TOuter,TInner,TKey,TResult>(this IQuery<TOuter> outer, IQuery<TInner> inner, Func<TOuter,TKey> outerKey, Func<TInner,TKey> innerKey, Func<TOuter,TInner,TResult> result) → IQuery<TResult>`.
  - The aggregate overload set, named `S`:
    - `Sum`: one overload per selector type `int`, `long`, `decimal`, `double`, `int?`, `long?`, `decimal?` and `double?`. Each returns its selector type.
    - `Min<TValue>` and `Max<TValue>`: return `TValue`.
    - `Average`: `int` and `long` give `double`; `double` gives `double`; `decimal` gives `decimal`; `int?`, `long?` and `double?` give `double?`; `decimal?` gives `decimal?`.
  - `public static class GroupOperators` (extension methods on `IGroup<TKey,T>`): `Count() → int` and `LongCount() → long`, plus the set `S` with selector `Func<T, …>` returning plain values.
  - `QueryOperators`: scalar terminals with the same set `S`, on `IQuery<T>`. They return `IQuery<TResult>`. For example `Max<T,TValue>(this IQuery<T>, Func<T,TValue>) → IQuery<TValue>`, and `Average(this IQuery<T>, Func<T,int?>) → IQuery<double?>`.
  - Every body throws the existing guidance exception.

- [ ] **Step 1: Write the failing tests.**
  - `Group_and_join_operators_throw`: `GroupBy`, `Join`, and an `IGroup` `Sum` and `Count` all throw `InvalidOperationException` with the guidance text.
  - `Scalar_aggregates_throw`: `Sum`, `Min`, `Max` and `Average` on `Sql.Table<object>()` throw the same.
- [ ] **Step 2: Run them; expect a compile failure.** `dotnet test tests/ZeroAlloc.ORM.Linq.Tests`.
- [ ] **Step 3: Implement.** Add the PublicAPI entries. Build `src/ZeroAlloc.ORM.Linq` with zero warnings.
- [ ] **Step 4: Run the tests; expect PASS.**
- [ ] **Step 5: Commit** as `feat: add GroupBy, Join and aggregate operators to ZeroAlloc.ORM.Linq`.

### Task 2: `Task<double>` and `Task<decimal>` scalar results for `[Query]`

A non-nullable `Average` of `int`/`long`/`double`, and a `Sum` of `double`/`decimal`, return `double` or `decimal`. Today `[Query]` supports only `int`, `long` and `bool` as non-null scalars.

**Files:**
- Modify: `src/ZeroAlloc.ORM.Generator/OrmGenerator.cs`, at `ClassifyEmitShape` next to ScalarInt/ScalarBool/ScalarLong, with `EmitScalarConvert`.
- Test: `tests/ZeroAlloc.ORM.Generator.Tests/Emit/ScalarDoubleDecimalTests.cs`, and integration in `tests/ZeroAlloc.ORM.Integration.Tests/ScalarBoolLongTests.cs` (Sqlite) plus its Postgres counterpart.

**Interfaces:**
- Produces: `[Query]` mapping `Task<double>` / `ValueTask<double>` to `EmitShape.ScalarDouble` (`Convert.ToDouble`), and `Task<decimal>` / `ValueTask<decimal>` to `EmitShape.ScalarDecimal` (`Convert.ToDecimal`).
  - Both use invariant culture. Classify them after `ResolveScalarFactoryShape`, so a `[Materialize]` factory keeps its old path, as Task 2 of plan 1 did.
  - No row and NULL behave as for `Task<int>`.
- Check that `NullableScalar` already supports `double?`, `decimal?`, `int?` and `long?`. Add a snapshot for each; if one is missing, add it here.

- [ ] **Step 1: Write the failing tests.** Snapshots: `Query_returning_double_reads_scalar`, `Query_returning_decimal_reads_scalar`, and `Nullable_double_decimal_int_long_scalars`. Integration: `SELECT avg(x)::double precision` and `SELECT sum(d)` read back exactly, on Sqlite and Postgres.
- [ ] **Step 2: Run them; expect FAIL** (ZAO022).
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole generator project and both integration filters.** Old snapshots are unchanged.
- [ ] **Step 5: Commit** as `feat: support Task<double> and Task<decimal> scalar results`.

### Task 3: Scalar aggregate terminals

**Files:**
- Modify in `src/ZeroAlloc.ORM.Generator/Linq/`:
  - `LinqQueryReader.Chain.cs`: terminal dispatch;
  - `SqlAst.cs`;
  - `PostgresSqlRenderer.cs`, `SqlParameterReferences.cs`, `EnumParameterStorage.cs` and `LinqQueryBinder.cs`;
  - `LinqQueryReader.Shape.cs`.
- Create: `src/ZeroAlloc.ORM.Generator/Linq/LinqQueryReader.Aggregates.cs`.
- Test: `tests/ZeroAlloc.ORM.Generator.Tests/Linq/LinqScalarAggregateTests.cs`, and `tests/ZeroAlloc.ORM.Integration.Tests/Linq/PostgresLinqAggregateTests.cs`.
- Schema: add `"Executions"` to `tests/ZeroAlloc.ORM.Integration.Tests/Linq/LinqSchema.cs`, with columns `"Id" uuid PK`, `"TaskId" uuid`, `"ModelId" text NULL`, `"InputTokens" int`, `"OutputTokens" int`, `"ExecutedAt" timestamptz` and `"Sequence" int NULL`. Add an `ExecutionEntity` with `DateTime ExecutedAt`, and an `ExecutionMap`.

**Interfaces:**
- Consumes: Task 1's terminals and Task 2's shapes.
- Produces:
  - `internal sealed record SqlAggregateCallExpression(SqlAggregateFunction Function, SqlExpression? Argument, string? ResultCast, bool CoalesceToZero)`, with `internal enum SqlAggregateFunction { Count, LongCount, Sum, Min, Max, Average }`.
  - A scalar aggregate statement renders `SELECT <call> FROM … WHERE …`, through a new statement record or by extending `SqlAggregateStatement` additively. Paging composes as plan 1's paged `Count` does.
- Rendering rules, chosen so results equal LINQ to Objects:

  | Aggregate | SQL |
  |---|---|
  | `Sum` over `int` | `COALESCE(sum(x), 0)::int` |
  | `Sum` over `long` | `COALESCE(sum(x), 0)::bigint` |
  | `Sum` over `decimal` | `COALESCE(sum(x), 0)` |
  | `Sum` over `double` | `COALESCE(sum(x), 0)::double precision` |
  | `Sum` over a nullable selector | the same COALESCE form, because LINQ's nullable Sum is 0 for no values |
  | `Average` of `int`/`long` | `avg(x)::double precision` |
  | `Average` of `double` | `avg(x)::double precision` |
  | `Average` of `decimal` | `avg(x)` |
  | `Min` / `Max` | `min(x)` / `max(x)` |

  - **Empty-set rule:** non-nullable `Average`/`Min`/`Max` over no rows throw at run time, as LINQ does. The existing NULL-read behaviour of a non-null scalar shape provides this; assert the exception type in the integration test. Nullable forms return `null`.
- The selector's cast to a wider type, `(long)e.InputTokens` or `(double?)e.OutputTokens`, picks the overload and the result cast. The column is not cast.
- A result type that `[Query]` can't return as a scalar is ZAO112.

- [ ] **Step 1: Write the failing snapshot tests.**
  - `Sum_int_long_decimal_double_and_nullable`
  - `Average_int_long_double_decimal_and_nullable`
  - `Min_max_value_and_nullable`
  - `Scalar_aggregate_with_filter_and_paging`
  - `Unsupported_aggregate_result_reports_ZAO112`
- [ ] **Step 2: Write the failing Postgres tests.**
  - `Sum_of_no_rows_is_zero`: `Sum` over an empty filter, and `Sum(int?)` with all values null, both give 0.
  - `Average_keeps_the_fraction`: rows with 1, 2 give `1.5`.
  - `Daedalus_q5_average_and_q6_max`: `Average(e => (double?)e.OutputTokens)` over `OutputTokens > 0`, plus `Max(e => (int?)e.Sequence)` filtered by a parameter. Each is checked on data, and on no rows, which gives null.
  - `Non_nullable_average_of_no_rows_throws`.
- [ ] **Step 3: Run both; expect FAIL.**
- [ ] **Step 4: Implement.**
- [ ] **Step 5: Run the whole generator project, then the Linq integration filter.** Old snapshots are unchanged.
- [ ] **Step 6: Commit** as `feat: translate LINQ scalar aggregates`.

### Task 4: `GroupBy`, group aggregates and `.Date`

**Files:**
- Modify in `src/ZeroAlloc.ORM.Generator/Linq/`: `LinqQueryReader.Chain.cs`, `LinqQueryReader.Projection.cs`, `LinqQueryReader.Ordering.cs`, `LinqQueryReader.Values.cs` and `LinqQueryReader.Aggregates.cs`, plus `SqlAst.cs`, the renderer, the binder, `SqlParameterReferences` and `EnumParameterStorage`.
- Test: `tests/ZeroAlloc.ORM.Generator.Tests/Linq/LinqGroupByTests.cs` and `tests/ZeroAlloc.ORM.Integration.Tests/Linq/PostgresLinqGroupByTests.cs`.
- Schema: add `"Sessions"` (`"Id"`, `"OwnerId" text`, `"CreatedAt" timestamptz`, `"TotalInputTokens" bigint`, `"TotalOutputTokens" bigint`) with a `SessionEntity` and `SessionMap`, for Q2.

**Interfaces:**
- Consumes: Task 1's surface and Task 3's `SqlAggregateCallExpression`.
- Produces:
  - Group keys on the select statement: an `EquatableArray<SqlExpression>` rendered as `GROUP BY`. Add them additively; a trailing default member on the internal record is acceptable. Update every walker.
  - `internal sealed record SqlDateTruncExpression(SqlExpression Operand)`, rendered `date_trunc('day', <operand>)`.
- Key selector forms:
  - a single column;
  - an anonymous type whose members are columns or `<column>.Date`;
  - `<column>.Date` alone.

  Anything else is ZAO102. `.Date` is supported on `DateTime` and `DateTimeOffset` columns, matching EF Core's Npgsql translation. **Document** that the day boundary of a `timestamptz` column follows the session `TimeZone`, so `Timezone=UTC` in the connection string matches C# `.Date` on UTC values.
- After `GroupBy`:
  - `OrderBy`, `ThenBy` and their descending forms over `g.Key`, `g.Key.Member` or a group aggregate are allowed.
  - `Where` (HAVING) is ZAO102 "not supported yet".
  - `Count`, `LongCount` and `Any` over groups are ZAO102.
  - `Skip` and `Take` follow plan 1's rules.
- `Select` over `IGroup`: a positional record whose arguments are `g.Key`, `g.Key.Member`, a group aggregate (`g.Count()`, `g.LongCount()`, `g.Sum(…)`, `g.Min(…)`, `g.Max(…)`, `g.Average(…)`), a parameter or a literal. Aggregate rendering follows Task 3's table, but without COALESCE: a group always has rows. A nullable-selector `Sum` keeps COALESCE, because its values may all be null.
- A `GroupBy` with no `Select` is ZAO107.

- [ ] **Step 1: Write the failing snapshot tests.**
  - `Group_by_single_column_with_count`
  - `Group_by_anonymous_key_with_date`, the Q1 shape
  - `Group_by_with_order_by_key_and_aggregate`
  - `Having_and_group_count_report_ZAO102`
  - `Group_by_without_select_reports_ZAO107`
- [ ] **Step 2: Write the failing Postgres tests.**
  - `Daedalus_q1_tokens_per_model_and_day`: rows over two days and two models, including a `ModelId` of null. The record holds the key, the day, both long sums and the count; assert the exact rows.
  - `Daedalus_q2_session_tokens_per_owner_and_day`
  - `Null_key_member_forms_one_group`
  - `Average_per_group_keeps_the_fraction`
- [ ] **Step 3: Run both; expect FAIL.**
- [ ] **Step 4: Implement.**
- [ ] **Step 5: Run the whole generator project, then the Linq integration filter.**
- [ ] **Step 6: Commit** as `feat: translate LINQ GroupBy, group aggregates and Date`.

### Task 5: Inner joins

**Files:**
- Create: `src/ZeroAlloc.ORM.Generator/Linq/LinqQueryReader.Joins.cs`.
- Modify: the reader files, `SqlAst.cs`, the renderer, `LinqQueryBinder.cs` (multi-source name scopes), `SqlParameterReferences` and `EnumParameterStorage`.
- Test: `tests/ZeroAlloc.ORM.Generator.Tests/Linq/LinqJoinTests.cs` and `tests/ZeroAlloc.ORM.Integration.Tests/Linq/PostgresLinqJoinTests.cs`.

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces:
  - `internal sealed record SqlJoin(SqlTableSource Source, string Alias, SqlExpression On, EquatableArray<SqlExpression> SourcePredicates)`.
  - The FROM source gets an alias.
  - `SqlMemberExpression` gains an optional source alias, additively with a default of null. A null alias renders exactly as in 2.5.0.
  - Aliases are `t0`, `t1`, … in join order. They are used **only** when the query has a join, so plan 1's output is unchanged.
- The join reader:
  - **Inner side:** `Sql.Table<T>()`, optionally followed by `Where(…)` (Q4). Its predicates go into the `ON`, or into WHERE scoped to that alias; either is fine. Anything else on the inner side is ZAO102.
  - **Keys:** each key is a single member access on the respective side. A value-object key equals its underlying column: `t.ProjectId` against `p.Id`, or `t.ProjectId.Value`. A composite anonymous key is ZAO102.
  - **Result selector:** an anonymous type whose members are the two range variables, or members carried over from an earlier join's anonymous type (Q3's `new { et.Execution, et.Task, Project = p }`). Any computed member is ZAO102.
  - **Later lambdas** (`Where`, `GroupBy`, `OrderBy`, `Select`) read `x.Execution.InputTokens` as alias `t0`, column `"InputTokens"`. Each anonymous member path resolves to its source alias.
  - **Columns:** resolve through the right entity map, so schema and renamed columns work, e.g. `app."Projects"` with `Title` in column `"Name"`.
- Enum storage voting and null compensation apply per column, as in plan 1.

- [ ] **Step 1: Write the failing snapshot tests.**
  - `Single_join_projects_both_sides`
  - `Two_chained_joins_with_group_by`, the Q3 shape
  - `Filtered_inner_source`, the Q4 shape
  - `Value_object_key_against_underlying_column`
  - `Unsupported_join_forms_report_ZAO102`, covering a composite key, a computed result member, and `Select` or `OrderBy` on the inner side
  - `Query_without_join_renders_unaliased`, which pins plan 1's output
- [ ] **Step 2: Write the failing Postgres tests.**
  - `Daedalus_q3_costs_by_project`: Executions → Tasks → Projects (`app` schema, renamed column), grouped by project id, name and model.
  - `Daedalus_q4_costs_by_task_for_one_project`, with a parameter on the filtered inner source.
  - `Join_drops_unmatched_and_multiplies_duplicates`
  - `Same_column_name_in_both_tables_is_qualified`: project `Task.Id` and `Project.Id` together.
- [ ] **Step 3: Run both; expect FAIL.**
- [ ] **Step 4: Implement.**
- [ ] **Step 5: Run the whole generator project, then the Linq integration filter.** Every pre-branch snapshot is unchanged.
- [ ] **Step 6: Commit** as `feat: translate LINQ inner joins`.

### Task 6: AOT smoke and docs

**Files:**
- Modify: `tests/ZeroAlloc.ORM.AotSmoke/`, adding a grouped join and a scalar `Average` to the existing `ZAORM_AOT_PG` LINQ section.
- Modify: `docs/cookbook/linq.md`, `docs/README.md`, and the root `README.md` if its LINQ bullet lists features.
- Modify: the `docs/diagnostics/ZAO102.md`, `ZAO107.md` and `ZAO112.md` example lists, for the new rejections.

- [ ] **Step 1: Add the AOT smoke case.** Publish with the `aot-smoke.yml` command, alone, and expect zero warnings. Run the binary once against a local PostgreSQL with `ZAORM_AOT_PG` set.
- [ ] **Step 2: Write the cookbook sections.** Cover:
  - GroupBy keys, including `.Date` and its time-zone note;
  - group aggregates and their SQL, with the LINQ-equivalence rules: COALESCE for Sum, double for Average, and throw versus null on empty input;
  - scalar aggregates;
  - joins: inner only, the key rules, the result-selector rule, and aliases;
  - what is still unsupported: HAVING, counting groups, left joins, composite join keys, plans 3–4.

  Update the "not yet supported" list.
- [ ] **Step 3: Commit** as `docs: document LINQ aggregation and joins`.

## After merge

- Confirm the release-please PR lists the `feat:` entries, then verify the release on NuGet.
- Tell the Daedalus session that Q1–Q6 translate, and which rewrite each needs: a positional record instead of an anonymous `Select` result.
