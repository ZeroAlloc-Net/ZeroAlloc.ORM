# ZeroAlloc.ORM.Linq parents with children — Implementation Plan (LINQ plan 3 of 4)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Daedalus' seven EF `Include` queries with `[LinqQuery]` methods. A parent and its children load in one round trip, through the ORM's existing multi-result batch, including for a paged set of parents. The plan also lets entity maps expose EF owned types as parent columns.

**Architecture:** The plan builds on plans 1–2 in `src/ZeroAlloc.ORM.Generator/Linq/`. It has three independent pieces.
1. **Owned types.** An entity map can declare an owned member, e.g. `CodeAnalysisRequest.Repository`. The owned type's properties then become columns of the parent table, and LINQ reads `r.Repository.RepositoryUrl` as a column.
2. **Tuple companions.** A companion may return a tuple of `IQuery<T>`. Each element translates to its own statement, the statements are joined with `;`, and the method returns through the existing `[Query]` multi-result path (`MultiResultMaterialization`, `ResolveBatchStrategy`, `IAsyncDbBatch`).
3. **IN-subquery.** A new `IQuery<T>.Contains(value)` operator renders `col IN (SELECT …)`. A child statement can then select the children of the same filtered, ordered and paged parent set.

**Tech Stack:** .NET 10 SDK, a Roslyn incremental generator (`netstandard2.0`), Npgsql 10.0.3, xunit.v3 with AwesomeAssertions, Testcontainers PostgreSQL, GeneratorSnapshot.

**Spec:** `docs/design/2026-10-17-linq-design.md`, which says "Loading a parent with its children (replaces EF Include) … the ORM's existing multi-result head + lines method … run as one IAsyncDbBatch. The caller assembles the object graph." Plus these owner decisions from 2026-10-22:
- one companion returns a tuple;
- children of a paged parent set come through a subquery in `Contains`;
- owned types are in scope.

**Daedalus `Include` sites to replace.** In each case the parent and its children come back in one tuple.

| Site | Parents | Children |
|---|---|---|
| `TaskRepository.cs:21` | one `Task` by id | `Executions` |
| `TaskQueryService.cs:17` | a page of `Task`s, `OrderByDescending(CreatedAt).Skip.Take` | `Executions` |
| `TaskQueryService.cs:36` | one `Task` by id | `Executions` |
| `ProjectQueryService.cs:16` | a page of `Project`s, `OrderBy(ProjectName).Skip.Take` | `Tasks` |
| `ProjectQueryService.cs:77` | one `Project` by id | `Tasks` |
| `CodeAnalysisRepository.cs:22` | one `CodeAnalysisRequest` by id, with owned `Repository`/`Location`/`Outcome` | `Iterations` |
| `BrainstormRepository.cs:37` | one `BrainstormSession` by id | `Messages` ordered by `CreatedAt` |

## Global Constraints

- Ships on the single 2.x release train as an additive minor. New public API goes into `PublicAPI.Unshipped.txt`. Operators are extension methods. No optional parameters on shipped signatures.
- Every generator snapshot on `main` stays byte-identical, including plans 1–2's LINQ snapshots and every `[Query]` multi-result snapshot.
- Nothing is evaluated at run time and nothing uses reflection. A `[LinqQuery]` translates fully at build time or is a build error.
- C# semantics win, as decided for plans 1–2. Where SQL would differ, compensate or make it a build error, and document any known difference.
- PostgreSQL only. Identifiers are quoted.
- No new diagnostic IDs unless a task says otherwise. Reuse ZAO100–ZAO112, ZAO106, ZAO032/033 and ZAO110 with messages that name the construct.
- Every new or changed AST record is added to `PostgresSqlRenderer`, `SqlParameterReferences`, `EnumParameterStorage`, `LinqQueryBinder` and `MayBeNull`, and to `SqlAstExhaustivenessTests`.
- AOT-clean, with no `#pragma`, `SuppressMessage` or `UnconditionalSuppressMessage`. Models stay equatable and symbol-free.
- Commits are conventional. Body lines are ≤ 100 chars, with no nested parentheses. They end with the session's `Co-Authored-By` line. No session URLs.
- Agents VM: one test project or publish at a time. Never use `git checkout` or `git stash` over uncommitted work.

## Review Focus

1. **A missing parent** returns `Head = null` and an empty child list from a non-nullable tuple. A nullable outer tuple `(…)?` returns `null`. Test: Task 2 `Missing_parent_returns_null_head_and_no_children`.
2. **Paged parents and their children stay consistent.** The child IN-subquery repeats the parent statement's order, limit and offset, and ties on the order key are broken deterministically. Test: Task 3 `Paged_parents_and_children_match_with_tied_order_keys`.
3. **An optional owned type that is entirely NULL** reads as nulls into the nullable record members, never as default values. Test: Task 1 `Null_owned_columns_read_as_nulls`.
4. **A parameter used in only one statement** of the batch binds correctly, including a parameter used only in the child statement. Test: Task 2 `Parameter_used_only_by_child_statement`.
5. **An IN-subquery over a joined or aliased outer query** gets no alias collisions: the subquery has its own aliases. Test: Task 3 `Subquery_under_join_uses_its_own_aliases`.

---

### Task 1: Owned types in entity maps

**Files:**
- Create: `src/ZeroAlloc.ORM.Abstractions/OwnedMapAttribute.cs`. Modify its `PublicAPI.Unshipped.txt`.
- Modify: `src/ZeroAlloc.ORM.Generator/Linq/EntityMapModel.cs`, `EntityMapReader.cs`, `LinqQueryReader.Values.cs` (member resolution) and `LinqQueryBinder` / `SqlNameScope`.
- Modify: `docs/cookbook/linq.md` (map section) and `docs/diagnostics/ZAO106.md`.
- Test: `tests/ZeroAlloc.ORM.Generator.Tests/Linq/EntityMapTests.cs` and `Linq/LinqOwnedTypeTests.cs`.
- Test: `tests/ZeroAlloc.ORM.Integration.Tests/Linq/PostgresLinqOwnedTypeTests.cs`. Add an `"AnalysisRequests"` table, entity and map shaped like Daedalus' `CodeAnalysisRequest`:
  - owned `Repository` (Url, Branch, CommitSha, Platform enum);
  - owned `Location` (FilePath, StartLine, EndLine);
  - optional owned `Outcome` (PullRequestUrl, ValidationResult jsonb, HasFailedValidation).

**Interfaces:**
- Produces `[AttributeUsage(Class, AllowMultiple = true)] public sealed class OwnedMapAttribute(string property)` in namespace `ZeroAlloc.ORM`.
  - It goes on a map class and names an entity property, e.g. `[OwnedMap(nameof(CodeAnalysisRequest.Repository))]`.
  - Each public readable instance property of the owned type becomes a column of the parent table.
  - The default column name is EF Core's owned-type convention, `{Owner}_{Property}`, e.g. `Repository_RepositoryUrl`.
  - `[ColumnMap("Repository.RepositoryUrl", Column = "RepositoryUrl")]` overrides it, using a dotted path. `EnumAsString` and `DateTimeStorage` work on owned members as on plain columns.
- `ColumnMapModel` gains the member path. For example, the path `Repository.RepositoryUrl` gives a column entry keyed by the path. This is additive, and plain columns keep a single-segment path.
- The LINQ reader resolves `r.Repository.RepositoryUrl` to that column wherever a column can be read: Where, Select, OrderBy, GroupBy keys, aggregates and join keys.
- A whole owned object (`Select(r => new X(r.Repository))`) is ZAO107. A member of an owned type that isn't declared with `[OwnedMap]` is ZAO105.
- **Null semantics:** an owned member reads as its column. An optional owned type whose columns are all NULL yields NULL members; document that `r.Outcome.PullRequestUrl` reads NULL instead of throwing as C# would on a null `Outcome`.
  - The C# null-compensation rules apply to each owned column by the member's own type.
  - A comparison through a nullable owned reference, e.g. `r.Outcome!.HasFailedValidation == true` where `Outcome` is `Outcome?`, is allowed and documented as reading the column.
- **ZAO106 cases:**
  - `[OwnedMap]` naming a property that doesn't exist;
  - naming a primitive, value-object, collection or enum property;
  - naming the same property twice;
  - a dotted `ColumnMap` whose first segment isn't declared owned;
  - an unknown nested member.

- [ ] **Step 1: Write the failing tests.**
  - Map model: default `Owner_Property` names, dotted overrides, and enum or DateTime storage on owned members.
  - Every ZAO106 case.
  - Snapshots: `Owned_member_in_where_select_order`, and `Whole_owned_object_reports_ZAO107`.
  - Postgres: `Owned_columns_round_trip_with_overrides`, and `Null_owned_columns_read_as_nulls`.
- [ ] **Step 2: Run; expect FAIL.**
- [ ] **Step 3: Implement**, including the PublicAPI entries.
- [ ] **Step 4: Run the whole generator project, then the Linq integration filter.** Old snapshots are unchanged.
- [ ] **Step 5: Commit** as `feat: map EF owned types as columns for LINQ queries`.

### Task 2: Tuple companions return a parent with its children

**Files:**
- Modify in `src/ZeroAlloc.ORM.Generator/Linq/`: `LinqQueryReader.Companion.cs` (accept a tuple of `IQuery<T>`), `LinqQueryReader.Shape.cs` (a multi-result shape), `LinqQueryReader.cs`, `LinqTranslation.cs` (several statements) and `LinqQueryBinder.cs`.
- Modify: `src/ZeroAlloc.ORM.Generator/OrmGenerator.Linq.cs` (core wiring) and `OrmGenerator.cs`. Change only what the LINQ path needs: skip ZAO032/033 for `LinqQuery` and recompute the batch strategy.
- Test: `tests/ZeroAlloc.ORM.Generator.Tests/Linq/LinqMultiResultTests.cs` and `tests/ZeroAlloc.ORM.Integration.Tests/Linq/PostgresLinqMultiResultTests.cs`.

**Interfaces:**
- **The companion** may return a `ValueTuple` of 2–7 elements, each `IQuery<Ti>`, with a tuple literal as its body: `=> (chain1, chain2, …)`. Each element is a complete chain, read by the existing `ReadStatement` with its own element shape.
- **Element shapes** are matched against the partial method's `Task<(…)>` / `ValueTask<(…)>` tuple. Allowed per element:
  - `Row`: `TRow?`, a nullable positional record. Render `LIMIT 1` unless the chain has a `Take`, as plan 1's single row does.
  - `List`: `List`, `IReadOnlyList` or `IList` of `TRow`.
  - `Scalar`: an element from a Count/LongCount/Any/Sum/Min/Max/Average terminal, with the result types plan 2 supports.

  A nullable outer tuple `Task<(…)?>` keeps `[Query]`'s meaning: `null` when the first result set is empty. A mismatch in arity or element kind is ZAO112, naming the element.
- **`LinqTranslation`** holds an ordered list of statements, one per element.
  - The binder binds each one and joins the SQL with `;\n`.
  - It unions the parameter references and facets.
  - It produces per-element enum-as-string ordinals on `MultiResultMaterialization.Elements`.
  - The `ReadsSeveralColumns` composite guard checks every element's columns.
- **Core wiring:**
  - The batch strategy for a multi-statement LINQ method is `BatchWithFallback`, i.e. `BatchMode.Auto`. `[LinqQuery]` gets no `Batch` property in this plan.
  - ZAO032/033 don't fire for `LinqQuery`; the translator guarantees arity.
  - Each batch command binds only the parameters its own statement references. If the shared emit binds every method parameter to every command, that is acceptable only if Npgsql ignores unreferenced parameters. Verify that, and pin it with Review Focus 4.
- **Source comment:** `LINQ: <Type>.<Companion>`. The constant handling follows the existing batch-split emit, which writes its own CommandText per statement.

- [ ] **Step 1: Write the failing snapshot tests.**
  - `Head_and_lines_by_id` (Row + List)
  - `Head_lines_and_count` (Row + List + Scalar)
  - `Ordered_and_filtered_children`, the Brainstorm shape
  - `Nullable_outer_tuple`
  - `Tuple_shape_mismatch_reports_ZAO112`
  - `Non_tuple_companion_body_reports_ZAO100`
- [ ] **Step 2: Write the failing Postgres tests.** They use the existing Tasks/Executions schema plus a Brainstorm-like Sessions/Messages pair; add tables as needed.
  - `Daedalus_task_with_executions_by_id`
  - `Daedalus_session_with_ordered_messages`
  - `Missing_parent_returns_null_head_and_no_children`
  - `Parameter_used_only_by_child_statement`
  - `Nullable_outer_tuple_is_null_without_parent`
- [ ] **Step 3: Run both; expect FAIL.**
- [ ] **Step 4: Implement.**
- [ ] **Step 5: Run the whole generator project, then the Linq integration filter.**
- [ ] **Step 6: Commit** as `feat: load a parent with its children from a tuple LINQ companion`.

### Task 3: IN-subquery through `IQuery<T>.Contains`

**Files:**
- Modify: `src/ZeroAlloc.ORM.Linq/QueryOperators.cs` and its `PublicAPI.Unshipped.txt`.
- Modify in `src/ZeroAlloc.ORM.Generator/Linq/`: `LinqQueryReader.Predicates.cs`, `SqlAst.cs`, `PostgresSqlRenderer.cs`, the binder and the walkers.
- Test: `tests/ZeroAlloc.ORM.Linq.Tests/AggregationSurfaceTests.cs` (the throw), `tests/ZeroAlloc.ORM.Generator.Tests/Linq/LinqSubqueryTests.cs` and `tests/ZeroAlloc.ORM.Integration.Tests/Linq/PostgresLinqSubqueryTests.cs`.

**Interfaces:**
- Produces `public static bool Contains<T>(this IQuery<T> source, T value)` on `QueryOperators`. The body throws.
- Produces `internal sealed record SqlInSubqueryExpression(SqlExpression Operand, SqlSelectStatement Subquery, bool Negated)`.
  - It renders `<operand> IN (SELECT <col> FROM … WHERE … ORDER BY … LIMIT … OFFSET …)`, or `NOT IN` when negated.
  - ORDER BY, LIMIT and OFFSET stay inside the subquery, so a paged parent set selects exactly the parents' ids. The NULLS FIRST/LAST rule applies there too.
- **The reader** accepts, inside a predicate, `<chain>.Select(p => p.Col).Contains(<outer value>)`.
  - `<chain>` is a full `Sql.Table<P>()` chain: Where, ordering, Skip/Take, joins.
  - The `Select` must project exactly one column.
  - The chain must not reference the outer row; correlated subqueries are ZAO102.
  - Companion parameters inside the subquery are SQL parameters as usual.
- **Null semantics** follow C# `Contains`, which never errs on a null element:
  - The selected column and the outer operand must both be non-nullable, or the query is ZAO102. This avoids SQL's `NOT IN` with NULL trap.
  - The cookbook says so, and shows the `!= null` filter that makes a nullable column eligible.
- **Aliases:** the subquery gets its own scope with aliases `s0`, `s1`, …. Only a subquery that contains a join gets aliases, so a plain subquery renders unaliased. They never collide with the outer `t0`, `t1`, ….
- **ZAO110** fires when the subquery has Skip/Take without OrderBy, as for a top-level chain.

- [ ] **Step 1: Write the failing tests.**
  - Surface: `Query_contains_throws`.
  - Snapshots:
    - `Children_of_paged_parents`, the TaskQueryService shape: a tuple whose second element is `Executions.Where(e => Sql.Table<Task>().OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id).Skip(skip).Take(take).Select(t => t.Id).Contains(e.TaskId))`
    - `Not_contains_renders_not_in`
    - `Subquery_under_join_uses_its_own_aliases`
    - `Correlated_subquery_reports_ZAO102`
    - `Nullable_subquery_column_reports_ZAO102`
    - `Unordered_paged_subquery_warns_ZAO110`
  - Postgres:
    - `Daedalus_paged_tasks_with_executions` and `Daedalus_paged_projects_with_tasks`, each one round trip as a tuple
    - `Paged_parents_and_children_match_with_tied_order_keys`: parents with equal `CreatedAt` and an `Id` tie-breaker; the children are exactly those of the returned parents.
- [ ] **Step 2: Run; expect FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole generator project, `ZeroAlloc.ORM.Linq.Tests`, then the Linq integration filter.**
- [ ] **Step 5: Commit** as `feat: translate IQuery Contains to an IN subquery`.

### Task 4: Daedalus `CodeAnalysisRequest` shape, AOT smoke, docs

**Files:**
- Test: `tests/ZeroAlloc.ORM.Integration.Tests/Linq/PostgresLinqIncludeTests.cs`, with `Daedalus_code_analysis_request_with_iterations`: the owned columns from Task 1 plus `Iterations` children, as one tuple.
- Modify: `tests/ZeroAlloc.ORM.AotSmoke/`, adding a tuple companion with an IN-subquery child statement and an owned member to the `ZAORM_AOT_PG` LINQ section.
- Modify: `docs/cookbook/linq.md`, with new sections "Parents with children", "Children of a paged parent set" and "Owned types", and update the not-supported list. Also `docs/cookbook/multi-result-set.md`, linking to the LINQ form; `docs/README.md`; and the ZAO100, ZAO102, ZAO107 and ZAO112 example lists.

- [ ] **Step 1: Write and run the Postgres test; expect PASS** on Tasks 1–3's code. A failure is a defect in those tasks: fix it here with a test, and say so.
- [ ] **Step 2: Add the AOT smoke case.** Publish with the `aot-smoke.yml` command, alone, and expect zero warnings. Run the binary once against a local PostgreSQL with `ZAORM_AOT_PG` set.
- [ ] **Step 3: Write the docs.** For each of the seven Daedalus `Include` sites, show the `[LinqQuery]` replacement pattern in a table.
- [ ] **Step 4: Commit** as `docs: document LINQ parents with children, subqueries and owned types`.

## After merge

- Confirm the release-please PR lists the `feat:` entries, then verify the release on NuGet.
- Tell the Daedalus session that all seven `Include` sites have a translation, and point it at the cookbook table.
