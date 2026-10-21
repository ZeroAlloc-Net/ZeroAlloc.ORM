# ZeroAlloc.ORM.Linq foundation — Implementation Plan (LINQ plan 1 of 4)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a `[LinqQuery]` partial method take its SQL from a LINQ chain in a never-executed companion method. This plan covers mapping, filters, ordering, paging, projections and the result shapes `[Query]` already has, on PostgreSQL.

**Architecture:**
- A new package, `ZeroAlloc.ORM.Linq`, holds only the authoring surface: `[LinqQuery]`, `Sql.Table<T>()`, `IQuery<T>` and its operators, and marker functions. Every body throws.
- The existing ORM generator does the translation, in a new `Linq/` folder. It reads the companion through Roslyn's `IOperation` tree, builds a small SQL AST, and renders it with a PostgreSQL renderer behind a dialect interface.
- The rendered SQL and the per-parameter `DbType` facets then go through the same `TransformMethod` pipeline as `[Query]`, so binding and materialisation are not reimplemented.
- Table and column mapping attributes go into `ZeroAlloc.ORM.Abstractions`.
- Putting the translator in the ORM generator, instead of a second generator, removes the cross-generator visibility problem named in the design's §6.

**Tech Stack:** .NET 10 SDK, Roslyn incremental generator (`netstandard2.0`, Microsoft.CodeAnalysis.CSharp 5.9.0), AdoNet.Async, Npgsql 10.0.3 for tests, xunit.v3 with AwesomeAssertions, Testcontainers PostgreSQL 16, and GeneratorSnapshot.

**Spec:** `docs/design/2026-10-17-linq-design.md`

**Later plans, not in this one:**
- Plan 2: `GroupBy`, aggregates, `.Date`, joins.
- Plan 3: a parent with its children through multi-result methods.
- Plan 4: `ExecuteUpdate` / `ExecuteDelete`, `xmin` concurrency, `.ForUpdate()`.
- A projection to a single scalar column, such as `Select(t => t.Title)` into `List<string>`, is not in plan 1. It reports ZAO107.

## Global Constraints

- **Versioning (owner decision 2026-10-21, overrides the design's §8 "0.x"):** `ZeroAlloc.ORM.Linq` ships on the repo's single release train, so the next release is 2.5.0.
  - Its public API is tracked with `PublicAPI.Shipped/Unshipped.txt` from the first commit.
  - It must stay additive across plans 2–4. Operators are extension methods on interfaces, never instance members; no optional parameters on shipped signatures.
- Existing `[Query]` / `[Command]` / `[StoredProcedure]` output stays byte-identical: every pre-existing generator snapshot is unchanged.
- No run-time expression trees, `Expression.Compile`, reflection, client evaluation, interceptors or delegates that run. The companion never executes.
- Every `[LinqQuery]` is either fully translated at build time or a build error. Nothing is deferred to run time.
- SQL is parameterised. Values reach SQL only as companion parameters. Compile-time constants may be inlined as SQL literals: numbers, `true`/`false`, `null`, enum members, and string literals with `'` doubled.
- PostgreSQL only. Identifiers are always double-quoted, with `"` doubled.
- AOT-clean: zero trim/AOT warnings in `aot-smoke`, and no `#pragma`, `SuppressMessage` or `UnconditionalSuppressMessage`.
- Diagnostics use the reserved block `ZAO100`–`ZAO199`. Each new ID needs:
  - a descriptor;
  - a `LookupDescriptor` entry, or the diagnostic is silently dropped;
  - a row in `AnalyzerReleases.Unshipped.md`;
  - `docs/diagnostics/ZAOxxx.md`, plus a row in `docs/diagnostics/README.md`.
- Commits are conventional. Body lines are ≤ 100 chars, with no nested parentheses. They end with the `Co-Authored-By` line from the session's attribution, and no session URLs.
- Agents VM: one test project or publish at a time; never the whole solution in parallel.

## Review Focus

1. **An optional filter with a `null` parameter on a non-text column** (`int`, enum, `Guid`, `DateTimeOffset`, typed id). `(@p IS NULL OR "Col" = @p)` must not fail with PostgreSQL 42P18 or 42883, and must return all rows. Test: Task 6 `Null_optional_filters_return_all_rows`.
2. **One parameter used several times in the SQL**, for example in a `WhereIf` condition and in its predicate. It binds once and works. Test: Task 6 `Parameter_reused_in_condition_and_predicate`.
3. **`LIKE` input containing `%`, `_` or `\`** matches literally. Test: Task 6 `Contains_escapes_like_wildcards`.
4. **Identifiers that are reserved words or mixed case**: table `"Tasks"`, a column named `Order`, a `Schema`. They are quoted and work. Test: Task 5 `Reserved_and_mixed_case_identifiers_are_quoted`.
5. **A projection with named arguments out of order**, `new Dto(Title: t.Title, Id: t.Id)`. Each value reaches the right constructor parameter. Test: Task 5 `Named_projection_arguments_follow_ctor_order`.

---

### Task 1: Authoring surface — `ZeroAlloc.ORM.Linq` package and mapping attributes

**Files:**
- Create: `src/ZeroAlloc.ORM.Linq/ZeroAlloc.ORM.Linq.csproj`. Mirror Abstractions' csproj: net10.0, `IsAotCompatible`, `IsTrimmable`, PublicApiAnalyzers, its own Title/Description/PackageTags, and a `ProjectReference` to Abstractions.
- Create: `src/ZeroAlloc.ORM.Linq/{LinqQueryAttribute,Sql,IQuery,IOrderedQuery,QueryOperators,SqlRow}.cs` and `PublicAPI.{Shipped,Unshipped}.txt`.
- Create: `src/ZeroAlloc.ORM.Abstractions/{TableMapAttribute,ColumnMapAttribute}.cs`, and modify its `PublicAPI.Unshipped.txt`.
- Modify: `ZeroAlloc.ORM.slnx`, adding the src project and a new test project.
- Create: `tests/ZeroAlloc.ORM.Linq.Tests/`, an xunit.v3 project like `ZeroAlloc.ORM.Abstractions.Tests`.
- Modify: `tests/ZeroAlloc.ORM.Generator.Tests/*.csproj` and `GeneratorHarness.cs` force-load list (~:102-126), adding `typeof(ZeroAlloc.ORM.Linq.Sql).Assembly`.
- Modify: `docs/design/2026-10-17-linq-design.md` §8 "Releases", replacing the 0.x paragraph with the owner's same-train decision.

**Interfaces:**
- Produces, namespace `ZeroAlloc.ORM.Linq`:
  - `[AttributeUsage(Method)] public sealed class LinqQueryAttribute(string companion)`, with `string Companion { get; }`.
  - `public interface IQuery<out T>`, and `public interface IOrderedQuery<out T> : IQuery<T>`.
  - `public static class Sql`:
    - `IQuery<T> Table<T>()`
    - `bool ILike(string? value, string? pattern)`
    - `SqlRow<T1,T2> Row<T1,T2>(T1, T2)`
    - `SqlRow<T1,T2,T3> Row<T1,T2,T3>(T1, T2, T3)`
  - `public readonly struct SqlRow<T1,T2>` and `SqlRow<T1,T2,T3>`, with operators `<`, `<=`, `>`, `>=` returning `bool`.
  - `public static class QueryOperators`, extension methods:
    - `Where(this IQuery<T>, Func<T,bool>)`
    - `WhereIf(this IQuery<T>, bool condition, Func<T,bool>)`
    - `OrderBy` / `OrderByDescending(this IQuery<T>, Func<T,TKey>) → IOrderedQuery<T>`
    - `ThenBy` / `ThenByDescending(this IOrderedQuery<T>, Func<T,TKey>) → IOrderedQuery<T>`
    - `Skip(this IQuery<T>, int)` and `Take(this IQuery<T>, int)`
    - `Select<T,TResult>(this IQuery<T>, Func<T,TResult>) → IQuery<TResult>`
    - `Count(this IQuery<T>) → IQuery<int>`, `LongCount → IQuery<long>`, `Any → IQuery<bool>`
- Produces, namespace `ZeroAlloc.ORM`, in Abstractions:
  - `[AttributeUsage(Class)] public sealed class TableMapAttribute(Type entity, string table)`, with `Type Entity`, `string Table` and `string? Schema { get; init; }`.
  - `[AttributeUsage(Class, AllowMultiple = true)] public sealed class ColumnMapAttribute(string property)`, with `string Property`, `string? Column { get; init; }` and `bool EnumAsString { get; init; }`.
- Every method and operator body throws the same message: `InvalidOperationException("ZeroAlloc.ORM.Linq query methods are read at build time by the ZeroAlloc.ORM generator and never run. Call the [LinqQuery] method instead; if this ran, the generator is not referenced.")`. Keep the message in one internal constant.

- [ ] **Step 1: Write the failing tests** in `tests/ZeroAlloc.ORM.Linq.Tests/AuthoringSurfaceTests.cs`:
  - `Table_throws_with_guidance`: `Sql.Table<object>()` throws `InvalidOperationException` whose message contains `"read at build time"`.
  - `Operators_throw`: `Where`, `Select`, `Count` and `Row(1,2) > Row(1,2)` each throw the same.
  - `Map_attributes_expose_values`: reading `TableMapAttribute` and `ColumnMapAttribute` from a decorated test class returns the given values. This reflection is in test code only.
- [ ] **Step 2: Run them.** `dotnet test tests/ZeroAlloc.ORM.Linq.Tests`, expect a compile failure.
- [ ] **Step 3: Implement the types, the PublicAPI entries and the slnx entries.** Then build `src/ZeroAlloc.ORM.Linq` and `src/ZeroAlloc.ORM.Abstractions` with zero warnings.
- [ ] **Step 4: Run the tests; expect PASS.** Run `ZeroAlloc.ORM.Generator.Tests` once, all green and unchanged.
- [ ] **Step 5: Update the design doc §8 and commit** as `feat: add the ZeroAlloc.ORM.Linq authoring surface and mapping attributes`.

### Task 2: `Task<bool>` and `Task<long>` scalar results for `[Query]`

LINQ `Any()` and `LongCount()` need them, and `[Query]` itself gains them; today both report ZAO022.

**Files:**
- Modify: `src/ZeroAlloc.ORM.Generator/OrmGenerator.cs`, at `ClassifyEmitShape` (~:1485) and the `ScalarInt` branch (~:1766), plus the emitter.
- Test: `tests/ZeroAlloc.ORM.Generator.Tests/Emit/ScalarBoolLongTests.cs`, and `tests/ZeroAlloc.ORM.Integration.Tests/ScalarBoolLongTests.cs` running Sqlite and Postgres.

**Interfaces:**
- Produces: `[Query]` with `Task<bool>` maps to `EmitShape.ScalarBool`, and `Task<long>` to `EmitShape.ScalarLong`. Both use `ExecuteScalar` with `Convert.ToBoolean` / `Convert.ToInt64` and the culture-invariant overload. Model the emit on `ScalarInt`, including its NULL / no-row behaviour.

- [ ] **Step 1: Write the failing tests.**
  - Snapshots: `Query_returning_bool_reads_scalar` and `Query_returning_long_reads_scalar`.
  - Integration:
    - `SELECT EXISTS (SELECT 1 FROM t WHERE id = @id)` returns `true` and `false`.
    - `SELECT count(*) FROM t` returns a `long`.
    - Run on Sqlite and Postgres.
- [ ] **Step 2: Run them; expect FAIL** (ZAO022).
- [ ] **Step 3: Implement both shapes.**
- [ ] **Step 4: Run the whole generator project and both integration filters.** Old snapshots are unchanged.
- [ ] **Step 5: Commit** as `feat: support Task<bool> and Task<long> scalar results`. Add a docs line in `docs/README.md` return shapes.

### Task 3: Extract the SQL-to-model core and add the `LinqQuery` pipeline kind

This is a pure refactor plus wiring, and it changes no output.

**Files:**
- Modify: `src/ZeroAlloc.ORM.Generator/OrmGenerator.cs`, at `TransformMethod` (:335) and the union (:113-136).
- Modify: `src/ZeroAlloc.ORM.Generator/Model/AttributePipelineKind.cs`.

**Interfaces:**
- Produces: `TransformMethodCore(GeneratorAttributeSyntaxContext ctx, AttributePipelineKind kind, string sql, LinqOverrides? linq)`. `TransformMethod` reads the SQL from the attribute and calls it with `linq: null`.
- Produces: `internal sealed record LinqOverrides(EquatableArray<LinqParameterFacet> Facets, string SourceComment)`, with `internal sealed record LinqParameterFacet(string ParameterName, string DbTypeExpression, int DbTypeValue)`.
  - When `linq` is non-null, the core applies each facet to the matching `ParameterInfo.Facets`, adding a `DbTypeCheck` so ZAO015 still runs.
  - It also sets a new `QueryMethodModel.LinqSourceComment` (`string?`, default null).
- Produces: `AttributePipelineKind.LinqQuery`.
  - The ZAO005 multiple-attribute check (~:491-505) counts `ZeroAlloc.ORM.Linq.LinqQueryAttribute` too.
  - The emitter, for a non-null `LinqSourceComment` only, emits the SQL as `private const string __sql_<Method> = "...";` above the method, preceded by `// <comment>`, and uses that constant as `CommandText`.

- [ ] **Step 1: Write the failing test.** `Diagnostics/ZAO005Tests.cs` case: a method with both `[Query]` and `[LinqQuery]` reports ZAO005.
- [ ] **Step 2: Run it; expect FAIL.**
- [ ] **Step 3: Extract the core, add the kind, the ZAO005 FQN, `LinqOverrides` and the emitter branch.** Nothing produces a `LinqQuery` model yet.
- [ ] **Step 4: Run the whole generator project.** All pass, and `git diff --stat --diff-filter=MD -- tests/ZeroAlloc.ORM.Generator.Tests/Snapshots` is empty.
- [ ] **Step 5: Commit** as `refactor: extract the SQL-to-model core for LINQ queries`.

### Task 4: Entity map model and map diagnostics

**Files:**
- Create: `src/ZeroAlloc.ORM.Generator/Linq/EntityMapModel.cs` and `Linq/EntityMapReader.cs`.
- Modify: `OrmGenerator.cs`, adding an FAWMN on `ZeroAlloc.ORM.TableMapAttribute`, collected into an `EquatableArray<EntityMapModel>` for Task 5 to combine.
- Modify: diagnostics files; create `docs/diagnostics/ZAO106.md`.
- Test: `tests/ZeroAlloc.ORM.Generator.Tests/Linq/EntityMapTests.cs`.

**Interfaces:**
- Produces: `internal sealed record EntityMapModel(string EntityTypeFullName, string Table, string? Schema, EquatableArray<ColumnMapModel> Columns)`, with `internal sealed record ColumnMapModel(string Property, string Column, bool EnumAsString)`.
  - `Columns` lists every public readable instance property of the entity.
  - Default column = property name, which is EF's convention. Explicit `ColumnMap` entries override.
- Produces: `ZAO106` (Error), "Invalid entity map: {0}", in these cases:
  - two `TableMap`s for the same entity;
  - a `ColumnMap` naming a property the entity doesn't have;
  - the same property mapped twice;
  - `EnumAsString` on a non-enum property.
  It is reported at the offending attribute.

- [ ] **Step 1: Write the failing tests.** Each ZAO106 case above, plus `Valid_map_reports_nothing`.
- [ ] **Step 2: Run them; expect FAIL.**
- [ ] **Step 3: Implement the reader and the diagnostics.**
- [ ] **Step 4: Run the whole generator project; expect PASS.**
- [ ] **Step 5: Commit** as `feat: read entity maps for LINQ queries`.

### Task 5: LINQ front end, projection and first end-to-end query

**Files:**
- Create in `src/ZeroAlloc.ORM.Generator/Linq/`:
  - `LinqQueryReader.cs`: companion resolution and validation, chain walk over `IOperation`.
  - `SqlAst.cs`: equatable records for select, from, predicate, order, paging and parameter.
  - `ISqlRenderer.cs` and `PostgresSqlRenderer.cs`.
  - `LinqTranslation.cs`: the result, holding SQL, facets, diagnostics and the resolved shape.
- Modify: `OrmGenerator.cs`, adding an FAWMN on `ZeroAlloc.ORM.Linq.LinqQueryAttribute` combined with the Task 4 maps. It calls the reader, then `TransformMethodCore(..., AttributePipelineKind.LinqQuery, sql, overrides)`, and joins the existing union.
- Modify: diagnostics; create `docs/diagnostics/ZAO100.md`, `ZAO101`, `ZAO102`, `ZAO103`, `ZAO104`, `ZAO105`, `ZAO107`, `ZAO108` and `ZAO112`.
- Test: `tests/ZeroAlloc.ORM.Generator.Tests/Linq/LinqProjectionTests.cs` with snapshots, and `Linq/LinqFrontEndDiagnosticsTests.cs`.
- Test: `tests/ZeroAlloc.ORM.Integration.Tests/Linq/`:
  - `LinqSchema.cs`: Postgres DDL shaped like Daedalus. Table `"Projects"`, and table `"Tasks"` with these columns:
    - `"Id" uuid`
    - `"ProjectId" uuid`, a typed id
    - `"Title" text`
    - `"Status" int`
    - `"Kind" text`, an enum stored as a string
    - `"CreatedAt" timestamptz`
    - `"DueAt" timestamptz NULL`
    - `"Tags" text[]`
    - `"Order" int`
  - `LinqEntities.cs`: entity classes with private setters, plus a `TaskMap` with `[TableMap]` / `[ColumnMap]`.
  - `LinqRepo.cs`.
  - `PostgresLinqProjectionTests.cs`.

**Interfaces:**
- Consumes: Task 1's surface, Task 3's `TransformMethodCore` and `LinqOverrides`, and Task 4's `EntityMapModel`.
- Produces: `LinqQueryReader.Read(...) → LinqTranslation`. It is the single entry the later tasks extend. `SqlAst` is extended by adding records, not by changing existing ones.
- Produces these diagnostics, all Errors:

  | ID | When |
  |---|---|
  | ZAO100 | The companion is invalid: not found in the same type, not `static`, not a single expression or a single `return`, or not returning `IQuery<T>`. |
  | ZAO101 | The companion's parameters don't match the partial method's parameters, minus the `CancellationToken` and the transaction, by name, type and order. |
  | ZAO102 | An unsupported LINQ construct. The message names it, and it is reported at that node. |
  | ZAO103 | The chain captures something other than a companion parameter, such as a field, a local or a static member. |
  | ZAO104 | The entity in `Sql.Table<T>()` has no `TableMap`. |
  | ZAO105 | A member that isn't a mapped column. |
  | ZAO107 | An unsupported projection. Supported is a constructor call on a positional record, with each argument a mapped column, a companion parameter or a literal. |
  | ZAO108 | The repository's dialect is not `SqlDialect.PostgreSql`. Read the `[Dialect]` on the containing type, else on the assembly; a missing dialect is also ZAO108. |
  | ZAO112 | The partial method's return type doesn't fit the companion's `IQuery<T>`. |

- Shapes, mapped from the companion's `IQuery<TRow>` (a projected record) to the partial method's return type:
  - `Task<List|IReadOnlyList|IList<TRow>>` → list.
  - `Task<TRow?>` → single row. Append `LIMIT 1` unless the chain has a `Take`.
  - `IAsyncEnumerable<TRow>` → streaming.
- Rendering: `SELECT "Col1", "Col2" FROM "schema"."Table"`. Columns go in the record constructor's parameter order, because FlatRow reads by position. Named arguments are placed by the parameter they bind.
- The source comment is `LINQ: <Type>.<Companion>`.

- [ ] **Step 1: Write the failing snapshot tests.**
  - `Select_into_record_list`
  - `Select_into_nullable_single_appends_limit_1`
  - `Select_streaming`
  - `Named_projection_arguments_follow_ctor_order`
  - `Reserved_and_mixed_case_identifiers_are_quoted`: table with `Schema = "app"`, a column named `Order`.
  - One diagnostics test per ZAO100–105, 107, 108 and 112, each asserting a single diagnostic and its location.
- [ ] **Step 2: Write the failing Postgres tests.** `Projection_round_trips_typed_id_enum_array_and_nullable` reads a record with `ProjectId`, `Status`, `Tags` and `DueAt` from seeded rows. Add the Review Focus 4 and 5 cases against the real database.
- [ ] **Step 3: Run both; expect FAIL.**
- [ ] **Step 4: Implement** the reader, AST, renderer, translation and pipeline wiring, plus the diagnostics and docs pages. Reading `"Kind"` (`EnumAsString`) into an enum record member uses the existing EnumAsString read convention for that column.
- [ ] **Step 5: Run the whole generator project, then `--filter "FullyQualifiedName~Linq"` on Integration.Tests.** Old snapshots are unchanged.
- [ ] **Step 6: Commit** as `feat: translate LINQ projections into [LinqQuery] methods`.

### Task 6: `Where`, parameters and their types

**Files:**
- Modify: `src/ZeroAlloc.ORM.Generator/Linq/{LinqQueryReader,SqlAst,PostgresSqlRenderer}.cs`.
- Create: `docs/diagnostics/ZAO111.md`.
- Test: `tests/ZeroAlloc.ORM.Generator.Tests/Linq/LinqWhereTests.cs` and `tests/ZeroAlloc.ORM.Integration.Tests/Linq/PostgresLinqWhereTests.cs`.

**Interfaces:**
- Consumes: Task 5's reader and AST.
- Translates:

  | C# | SQL |
  |---|---|
  | `==`, `!=` | `=`, `<>` |
  | `<`, `<=`, `>`, `>=` | the same |
  | `x == null`, `x != null` | `IS NULL`, `IS NOT NULL` |
  | `&&`, `\|\|`, `!` | `AND`, `OR`, `NOT`, parenthesised |
  | a `bool` column alone | the column |
  | a parameter | `@name` |
  | a constant | an inline literal, per Global Constraints |
  | `coll.Contains(t.Col)`, where `coll` is an array-typed parameter | `"Col" = ANY(@coll)` |
  | `t.Col.Contains(p)` | `"Col" LIKE '%' \|\| <esc>(@p) \|\| '%' ESCAPE '\'` |
  | `t.Col.StartsWith(p)` | the same `LIKE` with only a trailing `%` |
  | `t.Col.EndsWith(p)` | the same `LIKE` with only a leading `%` |
  | `Sql.ILike(t.Col, p)` | `"Col" ILIKE @p` |
  | `WhereIf(cond, pred)` | `(NOT (cond) OR (pred))` |
  | several `Where`s | `AND` |

  - `<esc>(x)` is `replace(replace(replace(x,'\','\\'),'%','\%'),'_','\_')`.
  - An array-typed parameter uses the 2.4.0 array binding.
  - The `WhereIf` condition is translated over parameters, e.g. `status != null` becomes `@status IS NOT NULL`.
- Parameter types: every companion parameter gets a `LinqParameterFacet` from its CLR type, so a `null` is typed.

  | CLR type | DbType |
  |---|---|
  | `int` | `Int32` |
  | `long` | `Int64` |
  | `short` | `Int16` |
  | `bool` | `Boolean` |
  | `Guid` | `Guid` |
  | `string` | `String` |
  | `decimal` | `Decimal` |
  | `double` | `Double` |
  | `float` | `Single` |
  | `DateTimeOffset` | `DateTimeOffset` |
  | enum stored as int | `Int32` |
  | enum stored as string | `String` |
  | value object | its underlying type's DbType |

  - Arrays and `DateTime` get no facet: Npgsql infers them from the value and `Kind`.
- Enum storage: an enum parameter compared with an `EnumAsString` column binds with the existing EnumAsString convention. If the same parameter is also compared with an int-stored column, report `ZAO111` (Error), "Parameter '{0}' is compared with columns that store its enum differently".
- Unsupported calls or operators → ZAO102. Captures that aren't parameters → ZAO103.

- [ ] **Step 1: Write the failing snapshot tests.** One per row of the translation table, plus `Parameters_get_dbtype_facets` and the ZAO111 test.
- [ ] **Step 2: Write the failing Postgres tests.**
  - `Equality_range_and_null_checks`
  - `Typed_id_and_enum_int_and_string_filters`
  - `Contains_on_guid_array_parameter`
  - `Contains_escapes_like_wildcards`: seed `a%b`, `a_b`, `axb`, `a\b`, filter on `%`, `_` and `\`, and assert literal matches only.
  - `ILike_is_case_insensitive`
  - `Null_optional_filters_return_all_rows`: `int?`, enum?, `Guid?`, `DateTimeOffset?` and typed id `?`, each `null` and each set.
  - `Parameter_reused_in_condition_and_predicate`
- [ ] **Step 3: Run both; expect FAIL.**
- [ ] **Step 4: Implement.**
- [ ] **Step 5: Run the whole generator project, then the Linq integration filter.**
- [ ] **Step 6: Commit** as `feat: translate LINQ Where clauses with typed parameters`.

### Task 7: Ordering, paging, keyset rows and scalar terminals

**Files:**
- Modify: the same `Linq/` files.
- Create: `docs/diagnostics/ZAO110.md`.
- Test: `Linq/LinqOrderPagingTests.cs` with snapshots, and `Linq/PostgresLinqOrderPagingTests.cs`.

**Interfaces:**
- Translates:
  - `OrderBy`, `ThenBy` and their `…Descending` forms → `ORDER BY "A", "B" DESC`.
  - `Skip(n)` / `Take(n)` → `OFFSET` / `LIMIT`, with `n` a parameter or an int literal. `Take` composes with Task 5's `LIMIT 1` by using the `Take` value.
  - `Sql.Row(t.A, t.B) > Sql.Row(p, q)`, and `>=`, `<`, `<=`, with 2 or 3 columns → `("A", "B") > (@p, @q)`.
  - `Count()` → `SELECT count(*)::int …`, as `Task<int>`. `LongCount()` → `count(*)`, as `Task<long>`. `Any()` → `SELECT EXISTS (SELECT 1 …)`, as `Task<bool>`, using Task 2's shapes.
  - A terminal after a projection drops the projection.
- `ZAO110` (Warning), "Skip or Take without OrderBy returns rows in no defined order". It fires for a list or streaming shape with `Skip` / `Take` but no `OrderBy`. It doesn't fire for the single-row shape's implicit `LIMIT 1`.
- A transaction parameter on the partial method passes through as in `[Query]`.

- [ ] **Step 1: Write the failing snapshot tests.**
  - `Order_then_by_desc`
  - `Skip_take_parameters`
  - `Keyset_row_comparison_two_and_three_columns`
  - `Count_long_count_any`
  - `Skip_without_order_warns_ZAO110`
- [ ] **Step 2: Write the failing Postgres tests.**
  - `Keyset_paging_walks_all_rows_once`: 25 rows sharing `CreatedAt` values, page size 10, `(CreatedAt, Id) > (@a, @b)`. All 25 ids are seen exactly once, in order.
  - `Count_and_any_with_filter`
  - `Query_runs_on_caller_transaction`: an insert inside a transaction is visible to the LINQ query on the same transaction.
- [ ] **Step 3: Run both; expect FAIL.**
- [ ] **Step 4: Implement.**
- [ ] **Step 5: Run the whole generator project, then the Linq integration filter.**
- [ ] **Step 6: Commit** as `feat: translate LINQ ordering, paging, keyset rows and counts`.

### Task 8: Companion-call diagnostic, AOT smoke, docs

**Files:**
- Modify: `OrmGenerator.cs`, adding a syntax pipeline over invocation expressions, combined with the collected companion symbols.
- Create: `docs/diagnostics/ZAO109.md`.
- Modify: `tests/ZeroAlloc.ORM.AotSmoke/`, adding a `[LinqQuery]` repository with a map, run in its Postgres section behind `ZAORM_AOT_PG`.
- Create: `docs/cookbook/linq.md`.
- Modify: `README.md` (packages table and a quick-start snippet), `docs/README.md`, and `src/ZeroAlloc.ORM.Linq` package README text if the repo uses per-package READMEs. Check `Directory.Build.props` `PackageReadmeFile`.
- Test: `Linq/ZAO109Tests.cs`.

**Interfaces:**
- `ZAO109` (Error), "'{0}' is a [LinqQuery] companion and is never run; call '{1}' instead". It fires on any invocation of a method that some `[LinqQuery]` names as its companion.
  - Keep it incremental. Filter invocations syntactically by name first, and resolve symbols only for names in the companion set.

- [ ] **Step 1: Write the failing test** `Calling_a_companion_reports_ZAO109`, plus a negative test for an unrelated method with the same name in another type.
- [ ] **Step 2: Run it; expect FAIL. Implement it, run it; expect PASS.**
- [ ] **Step 3: Add the AOT smoke case.** Publish with the `aot-smoke.yml` command, alone, and expect zero warnings. Run the binary once against a local postgres:16 with `ZAORM_AOT_PG` set.
- [ ] **Step 4: Write `docs/cookbook/linq.md`.** Cover:
  - map classes;
  - the method + companion pattern;
  - every supported operator with its SQL;
  - optional filters;
  - keyset paging;
  - result shapes;
  - the diagnostics table;
  - what's not supported yet, which is plans 2–4 and scalar-column lists.

  Update both READMEs.
- [ ] **Step 5: Commit** as `feat: report companion calls and document ZeroAlloc.ORM.Linq`.

## After merge

- Confirm the release-please PR lists the `feat:` entries. Check `ZeroAlloc.ORM.Linq` appears on NuGet at the release version, and that the published `ZeroAlloc.ORM.Generator` contains the LINQ translator.
- Tell the Daedalus session which shapes now work, and that plans 2–4 cover the rest.
