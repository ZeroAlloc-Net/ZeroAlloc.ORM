# ZeroAlloc.ORM.Linq set-based writes, concurrency and row locks — Implementation Plan (LINQ plan 4 of 4)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Daedalus' EF Core `ExecuteUpdateAsync`/`ExecuteDeleteAsync` calls, its xmin-guarded saves and its `FOR UPDATE` reads with `[LinqCommand]` and `[LinqQuery]` methods, so the stores in `Daedalus.Agents` can leave EF Core.

**Architecture:** The plan builds on plans 1–3 in `src/ZeroAlloc.ORM.Generator/Linq/`.
1. A new `[LinqCommand("Companion")]` attribute. The companion's chain ends in `ExecuteDelete()` or `ExecuteUpdate(set => set.SetProperty(…)…)`, which return `int`. It translates to one `DELETE` or `UPDATE` statement. The method then goes through the existing `[Command]` non-query pipeline, `AttributePipelineKind.Command` with `CommandKind.NonQuery`, as `[LinqQuery]` goes through `[Query]`.
2. Concurrency.
   - `Sql.Xmin(row)` reads PostgreSQL's `xmin` system column as a `uint`. A parameter compared with it binds as `NpgsqlDbType.Xid`.
   - A mapped `uint` property whose column is `xmin`, and a `byte[]` row-version column, work the same way in predicates.
   - `[LinqCommand(ThrowOnNoRows = true)]` turns zero rows affected into a `ZeroAllocOrmConcurrencyException`.
3. Row locks. The operators `ForUpdate()`, `ForUpdateSkipLocked()` and `ForUpdateNoWait()` append `FOR UPDATE`, `FOR UPDATE SKIP LOCKED` and `FOR UPDATE NOWAIT` to a `SELECT`.

**Tech Stack:** .NET 10 SDK, a Roslyn incremental generator (`netstandard2.0`), Npgsql 10.0.3, xunit.v3 with AwesomeAssertions, Testcontainers PostgreSQL, GeneratorSnapshot.

**Spec:** `docs/design/2026-10-17-linq-design.md` §2 decision 7 and the goals in §3:
- "Set-based writes: `ExecuteUpdate(setters)` with computed setters and `ExecuteDelete()` with predicates";
- "Optimistic concurrency: PostgreSQL `xmin` and `byte[]` row-version columns usable in predicates … an option on the command attribute turns 'zero rows affected' into a concurrency-conflict exception";
- "Row locking: a `.ForUpdate()` operator → `SELECT … FOR UPDATE`".

Plus these owner decisions from 2026-10-22:
- a `[LinqCommand]` attribute with `ThrowOnNoRows`;
- a `Sql.Xmin(t)` marker for shadow `xmin`;
- `ForUpdate`, `ForUpdateSkipLocked` and `ForUpdateNoWait`, with a warning when the method takes no transaction.

**Daedalus sites to replace** (Daedalus `main` `4091c648`):

| Site | EF form | Replacement |
|---|---|---|
| `Sessions/AgentSessionCrashRecovery.cs:32` | `Where(State == Running).ExecuteUpdate(State, LastActivityAt)` | `[LinqCommand]`, value setters |
| `Channels/PostgresConversationMap.cs:105` | `Where(...).ExecuteDelete()` | `[LinqCommand]`, `ExecuteDelete` |
| `Skills/PostgresSkillStore.cs:214` | `Where(IsActive && !names.Contains(Id)).ExecuteUpdate(...)` | `[LinqCommand]`, array-parameter predicate |
| `Memory/PostgresMemoryStore.cs:133` | `ExecuteDelete()` | `[LinqCommand]` |
| `Memory/PostgresMemoryStore.cs:182` | `SetProperty(m => m.RecallCount, m => m.RecallCount + 1)` | `[LinqCommand]`, computed setter |
| `Charters/PostgresRoleCharterStore.cs:107` | `Where(IsActive && !roles.Contains(Role)).ExecuteUpdate(...)` | `[LinqCommand]` |
| `Sessions/PostgresAgentSessionStore.cs:179` | counters `+ usage.InputTokens` | `[LinqCommand]`, computed setters |
| `Sessions/PostgresAgentSessionStore.cs:226` | compare-and-swap `Where(Id && State == from)` | `[LinqCommand]`, returns rows affected |
| `Sessions/PostgresAgentSessionStore.cs:119` | `FromSql("SELECT * … FOR UPDATE")` | `[LinqQuery]` with `ForUpdate()` and a transaction |
| `Workflow/WorkflowOutboxDispatchService.cs:134` | `FOR UPDATE SKIP LOCKED` claim | `[LinqQuery]` with `ForUpdateSkipLocked()` |
| `Scheduling/ScheduledRunStore.cs:131`, `ScheduledRunExecutionStore.cs:316,409` | `SaveChanges` guarded by shadow `xmin` | `[LinqCommand(ThrowOnNoRows = true)]` with `Sql.Xmin(r) == expected` |

## Global Constraints

- Ships on the single 2.x release train as an additive minor.
  - New public API goes into the owning project's `PublicAPI.Unshipped.txt`. Operators are extension methods.
  - No optional parameters on shipped signatures.
- Every generator snapshot on `main` stays byte-identical, including plans 1–3's LINQ snapshots and every `[Command]` snapshot.
- Nothing is evaluated at run time and nothing uses reflection. A `[LinqQuery]` or `[LinqCommand]` translates fully at build time or is a build error.
- C# semantics win. Where SQL would differ, compensate or make it a build error, and document any known difference.
- PostgreSQL only. Identifiers are quoted.
- Diagnostics:
  - One new ID, ZAO113. It is a warning: a row lock in a method with no `IAsyncDbTransaction` parameter.
  - Everything else reuses ZAO100–ZAO112, with messages that name the construct.
  - Register ZAO113 in `AnalyzerReleases.Unshipped.md` and give it a docs page with the help-link test.
- The entity map decides columns, owned columns and storage, as plan 3 ruled. The type's shape never does.
- Every new AST record is handled by:
  - `PostgresSqlRenderer`, `SqlParameterReferences`, `EnumParameterStorage`, `SqlSubqueries`, `LinqQueryBinder` and `MayBeNull`;
  - `SqlAstExhaustivenessTests`.
- AOT-clean, with no `#pragma`, `SuppressMessage` or `UnconditionalSuppressMessage`. Models stay equatable and symbol-free.
- Commits are conventional.
  - Body lines are ≤ 100 chars, with no nested parentheses: write `SetProperty of TurnCount`, not code with nested calls.
  - They end with the session's `Co-Authored-By` line. No session URLs.
- Agents VM: one test project or publish at a time. Never use `git checkout` or `git stash` over uncommitted work.

## Review Focus

1. **A stale xmin.** After another writer changed the row, a guarded update affects 0 rows and `ThrowOnNoRows` throws `ZeroAllocOrmConcurrencyException`. The xmin read in a projection is exactly the value the guard compares. Test: Task 3 `Stale_xmin_update_throws_and_fresh_xmin_updates`.
2. **SKIP LOCKED claims.** Two transactions claiming from one queue never get the same row. Test: Task 4 `Skip_locked_claims_are_disjoint_across_transactions`.
3. **Computed setters overflow like PostgreSQL, not like C#.** At `int.MaxValue`, `TurnCount + 1` raises PostgreSQL's "integer out of range" rather than wrapping. This is documented as a known difference. Test: Task 2 `Counter_overflow_raises_postgres_error`.
4. **One enum parameter in a setter and a predicate.** It binds by the column's storage in both. If the two columns store it differently, that is ZAO111. Test: Task 2 `Enum_parameter_in_setter_and_predicate_binds_by_name` and the ZAO111 case.
5. **A write with no `Where` updates or deletes every row**, as EF's does. The SQL has no `WHERE` and the generator adds nothing. Test: Task 1 `Delete_without_where_deletes_every_row`.

---

### Task 1: `[LinqCommand]`, `ExecuteDelete` and `ThrowOnNoRows`

**Files:**
- Create: `src/ZeroAlloc.ORM.Linq/LinqCommandAttribute.cs` and `src/ZeroAlloc.ORM/Exceptions/ZeroAllocOrmConcurrencyException.cs`.
- Modify: `src/ZeroAlloc.ORM.Linq/QueryOperators.cs` and both projects' `PublicAPI.Unshipped.txt`.
- Modify in `src/ZeroAlloc.ORM.Generator/`:
  - `OrmGenerator.Linq.cs`: a second `ForAttributeWithMetadataName` pipeline for `LinqCommand`, feeding `TransformMethodCore` with `AttributePipelineKind.Command` and `CommandKind.NonQuery`.
  - `OrmGenerator.cs`: only what the LINQ path needs, which is the ThrowOnNoRows emit.
  - `Linq/LinqQueryReader.Companion.cs`, `Linq/LinqQueryReader.Chain.cs`, `Linq/SqlAst.cs`, `Linq/PostgresSqlRenderer.cs`, `Linq/LinqQueryBinder.cs`, and `Linq/LinqCompanionCallAnalyzer.cs`, so that ZAO109 also covers `[LinqCommand]` companions.
- Test: `tests/ZeroAlloc.ORM.Generator.Tests/Linq/LinqCommandTests.cs`, `tests/ZeroAlloc.ORM.Linq.Tests/AggregationSurfaceTests.cs` (the throw) and `tests/ZeroAlloc.ORM.Integration.Tests/Linq/PostgresLinqCommandTests.cs`.

**Interfaces:**
- Produces in `ZeroAlloc.ORM.Linq`:
  - `public sealed class LinqCommandAttribute(string companion) : Attribute`, with `string Companion { get; }` and `bool ThrowOnNoRows { get; init; }`.
  - `public static int ExecuteDelete<T>(this IQuery<T> source)`, whose body throws `Guidance.Exception()`.
- Produces in `ZeroAlloc.ORM`: `public sealed class ZeroAllocOrmConcurrencyException : Exception`.
  - Constructors follow the sibling exceptions.
  - The generated code passes the message "'{Type}.{Method}' affected no rows; the row was changed or removed since it was read".
- Produces `internal sealed record SqlDeleteStatement(SqlTableSource Table, EquatableArray<SqlExpression> Predicates) : SqlStatement`. It renders `DELETE FROM "T" WHERE …` without an alias, or `DELETE FROM "T" AS t0 WHERE …` only when a predicate needs the alias, which the subquery naming rules decide.
- **Allowed chains:** `Sql.Table<T>()`, then any number of `Where` and `WhereIf` calls, including `Contains` subqueries, then `ExecuteDelete()`. Anything else before the terminal is ZAO102, naming the operator: `Join`, `OrderBy`, `Skip`, `Take`, `Select`, `GroupBy`, or a lock operator.
- **Method shapes:** `Task<int>`, `ValueTask<int>`, `Task` and `ValueTask`, plus the usual `CancellationToken` and `IAsyncDbTransaction` parameters. Anything else is ZAO112.
- **Wrong attribute:**
  - A `[LinqCommand]` whose chain does not end in `ExecuteDelete` or `ExecuteUpdate` is ZAO100, with the reason "must end in ExecuteDelete or ExecuteUpdate".
  - A `[LinqQuery]` whose chain ends in either is ZAO100, with the reason "is a write; use [LinqCommand]".
- **ThrowOnNoRows:** after `ExecuteNonQueryAsync`, a result of `0` throws the exception above. This holds for every return shape.
- **Source comment:** `LINQ: <Type>.<Companion>`, as for queries.

- [ ] **Step 1: Write the failing tests.**
  - Surface: `Execute_delete_throws`.
  - Generator snapshots:
    - `Delete_by_id`
    - `Delete_with_array_contains`, as in `!roles.Contains(r.Role)`
    - `Delete_with_subquery`
    - `Delete_without_where`
    - `Throw_on_no_rows_emits_the_check`
  - Generator diagnostic theories:
    - `Command_chain_operators_report_ZAO102`, one row each for Join, OrderBy, Take and Select;
    - `Command_without_write_terminal_reports_ZAO100`;
    - `Query_with_write_terminal_reports_ZAO100`;
    - `Command_return_shape_reports_ZAO112`, for `Task<string>`;
    - `Calling_a_command_companion_reports_ZAO109`.
  - Postgres:
    - `Delete_returns_rows_affected`;
    - `Delete_without_where_deletes_every_row`;
    - `Throw_on_no_rows_throws_concurrency_exception`, which also checks the message names the method;
    - `Delete_inside_caller_transaction_rolls_back`.
- [ ] **Step 2: Run them; expect FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the generator project, `ZeroAlloc.ORM.Linq.Tests`, then the Linq integration filter.**
- [ ] **Step 5: Commit** as `feat: delete rows with a LinqCommand ExecuteDelete chain`.

### Task 2: `ExecuteUpdate` with value and computed setters

**Files:**
- Create: `src/ZeroAlloc.ORM.Linq/SetPropertyCalls.cs`.
- Modify: `QueryOperators.cs` and its `PublicAPI.Unshipped.txt`.
- Modify in `src/ZeroAlloc.ORM.Generator/Linq/`:
  - `SqlAst.cs`, adding the update statement and arithmetic;
  - `LinqQueryReader.Values.cs`, which reads arithmetic in setter values only;
  - a new `LinqQueryReader.Setters.cs`;
  - the renderer, the binder, `EnumParameterStorage` and the walkers.
- Test: `tests/ZeroAlloc.ORM.Generator.Tests/Linq/LinqUpdateTests.cs` and `tests/ZeroAlloc.ORM.Integration.Tests/Linq/PostgresLinqUpdateTests.cs`.

**Interfaces:**
- Consumes Task 1's `[LinqCommand]`, its chain rules and its `ThrowOnNoRows`.
- Produces in `ZeroAlloc.ORM.Linq`:
  - `public sealed class SetPropertyCalls<T>`, with methods whose bodies throw:
    - `SetPropertyCalls<T> SetProperty<TProperty>(Func<T, TProperty> property, TProperty value)`
    - `SetPropertyCalls<T> SetProperty<TProperty>(Func<T, TProperty> property, Func<T, TProperty> value)`
  - `public static int ExecuteUpdate<T>(this IQuery<T> source, Func<SetPropertyCalls<T>, SetPropertyCalls<T>> setters)`.

  The names mirror EF Core's, so Daedalus call sites change only the receiver.
- Produces:
  - `internal sealed record SqlUpdateStatement(SqlTableSource Table, EquatableArray<SqlSetter> Setters, EquatableArray<SqlExpression> Predicates) : SqlStatement`;
  - `internal sealed record SqlSetter(string Column, SqlExpression Value)`;
  - `SqlArithmeticExpression(SqlArithmeticOperator Operator, SqlExpression Left, SqlExpression Right)`, with `Add`, `Subtract` and `Multiply`, rendered with parentheses.
- **Setter targets:**
  - A mapped column of the row, including an owned member such as `r.Outcome!.Url`, through the map as in plan 3.
  - These are ZAO102, naming the target: a whole owned object; `Sql.Xmin` or a column mapped to `xmin` ("a system column PostgreSQL sets itself"); a column already set in the same chain; a key or member that is not mapped (ZAO105).
- **Setter values:**
  - A companion parameter, a constant (including `null` for a nullable column), or a column of the same row.
  - Or `+`, `-` and `*` over those, on numeric types only: `int`, `long`, `decimal`, `double` and `float`.
  - String `+`, `/`, `%`, method calls and other operators are ZAO102.
  - Arithmetic stays rejected in predicates, as in plans 1–3.
  - Parameter storage follows the target column: an enum by name, `DateTimeStorage`, `[Json]`, arrays.
- **SQL form:**
  - `UPDATE "T" SET "A" = @a, "B" = ("B" + @n) WHERE …`, in setter order.
  - An enum by name, or an EnumAsString owned member, binds by name.
  - The chain rules are Task 1's.
- **Known difference, documented:** integer overflow raises PostgreSQL's error rather than wrapping as unchecked C# would.

- [ ] **Step 1: Write the failing tests.**
  - Generator snapshots, one per Daedalus shape:
    - `Update_state_and_timestamp`, the crash recovery
    - `Update_with_array_not_contains`, the skill sweep
    - `Update_counters_with_arithmetic`, the turn record
    - `Compare_and_swap_update`
    - `Update_owned_member`
    - `Update_nullable_column_to_null`
    - `Enum_parameter_in_setter_and_predicate_binds_by_name`
  - Diagnostic theory `Setter_reports`, for:
    - a whole owned object (ZAO102);
    - xmin as a target (ZAO102);
    - the same column set twice (ZAO102);
    - string `+` (ZAO102);
    - a method call (ZAO102);
    - an enum parameter stored by name in the setter and by value in the predicate (ZAO111).
  - Postgres:
    - `Crash_recovery_resets_running_sessions`
    - `Counters_increment_atomically`, with two concurrent updates and no lost increment
    - `Compare_and_swap_returns_zero_when_state_moved`
    - `Counter_overflow_raises_postgres_error`
    - `Owned_member_setter_writes_its_column`
- [ ] **Step 2: Run them; expect FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the generator project, `ZeroAlloc.ORM.Linq.Tests`, then the Linq integration filter.**
- [ ] **Step 5: Commit** as `feat: update rows with LinqCommand ExecuteUpdate setters`.

### Task 3: `Sql.Xmin`, `uint` xmin columns and row-version guards

**Files:**
- Modify: `src/ZeroAlloc.ORM.Linq/Sql.cs` and its `PublicAPI.Unshipped.txt`.
- Modify in `src/ZeroAlloc.ORM.Generator/Linq/`: `LinqQueryReader.Values.cs`, `SqlAst.cs`, the renderer and the walkers.
- Modify in `OrmGenerator.cs`, only as needed:
  - reading a `uint` column through `GetFieldValue<uint>`;
  - binding a LINQ parameter compared with xmin as `NpgsqlDbType.Xid`.
- Test: `tests/ZeroAlloc.ORM.Generator.Tests/Linq/LinqConcurrencyTests.cs` and `tests/ZeroAlloc.ORM.Integration.Tests/Linq/PostgresLinqConcurrencyTests.cs`.

**Interfaces:**
- Produces `public static uint Xmin<T>(T row)` on `Sql`, whose body throws.
- Produces `internal sealed record SqlSystemColumnExpression(string Name, string? Alias) : SqlExpression`, rendering `"xmin"` or `t0."xmin"`.
- **Where xmin can appear:** in predicates, compared with a `uint` parameter by `==` or `!=`, and in projections, read as `uint`.
- **A mapped property** with `[ColumnMap(nameof(E.RowVersion), Column = "xmin")]` and type `uint` behaves exactly like `Sql.Xmin(e)`. Daedalus' Task maps `RowVersion` to `xmin`.
- **Binding:** a parameter compared with xmin binds as `NpgsqlDbType.Xid`, the same Npgsql-only emit pattern as `[Json]`'s Jsonb. No `::xid` cast appears in the SQL.
- **Rejected forms:** ordering by xmin, `<` or `>` on xmin, and xmin inside arithmetic are ZAO102 ("xmin is a transaction id, compared only for equality").
- **`byte[]` row-version columns:** these compare with `==` against a `byte[]` parameter as `bytea =`. A setter may assign a new `byte[]`, so a guarded update can write the next version itself, because PostgreSQL does not generate one. Pin this with tests; the existing binding should cover it.

- [ ] **Step 1: Write the failing tests.**
  - Surface: `Xmin_throws`.
  - Snapshots:
    - `Xmin_guarded_update`, combining `Sql.Xmin(r) == expected` with ThrowOnNoRows
    - `Xmin_read_in_projection`
    - `Mapped_xmin_property_compares_as_xid`
    - `Row_version_guarded_update_sets_next_version`
  - Theory `Xmin_reports_ZAO102`, for ordering, `>`, a setter target and arithmetic.
  - Postgres:
    - `Stale_xmin_update_throws_and_fresh_xmin_updates`: read the xmin, have a second writer update the row, check that the guarded update throws, then re-read and check that it succeeds.
    - `Mapped_xmin_property_reads_and_guards`
    - `Row_version_guard_detects_concurrent_write`
- [ ] **Step 2: Run them; expect FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the generator project, `ZeroAlloc.ORM.Linq.Tests`, then the Linq integration filter.**
- [ ] **Step 5: Commit** as `feat: guard LINQ writes with xmin and row-version columns`.

### Task 4: Row locks and ZAO113

**Files:**
- Modify: `src/ZeroAlloc.ORM.Linq/QueryOperators.cs` and its `PublicAPI.Unshipped.txt`.
- Modify in `src/ZeroAlloc.ORM.Generator/Linq/`: `SqlAst.cs`, adding a lock field on `SqlSelectStatement`; `LinqQueryReader.Chain.cs`; the renderer; and `LinqDiagnostics.cs`.
- Modify: `AnalyzerReleases.Unshipped.md`.
- Create: `docs/diagnostics/ZAO113.md`.
- Test: `tests/ZeroAlloc.ORM.Generator.Tests/Linq/LinqLockTests.cs` and `tests/ZeroAlloc.ORM.Integration.Tests/Linq/PostgresLinqLockTests.cs`.

**Interfaces:**
- **Operators:** `ForUpdate<T>`, `ForUpdateSkipLocked<T>` and `ForUpdateNoWait<T>`. Each is an extension on `IQuery<T>` returning `IQuery<T>`, with an `IOrderedQuery<T>` overload returning `IOrderedQuery<T>`. Their bodies throw.
- **`SqlSelectStatement`** gains `SqlRowLock Lock`, an enum with the values `None`, `Update`, `UpdateSkipLocked` and `UpdateNoWait`. `None` renders nothing, so existing snapshots are unchanged. The others render after `LIMIT`/`OFFSET` as `FOR UPDATE`, `FOR UPDATE SKIP LOCKED` or `FOR UPDATE NOWAIT`.
- **Placement:** at most one lock operator, anywhere after `Sql.Table` and before `Select` or a terminal.
  - A lock on a joined chain locks every joined row, which is PostgreSQL's default `FOR UPDATE`.
  - These are ZAO102: a lock combined with `GroupBy` or an aggregate terminal (`Count`, `Any`, `Sum`, `Min`, `Max`, `Average`); a lock inside a `Contains` subquery; a second lock; a lock in a `[LinqCommand]` chain.
- **ZAO113** is a warning, category `ZeroAlloc.ORM.Linq`. Its message: "'{0}' locks rows but takes no IAsyncDbTransaction; the lock is released when its statement ends". It is reported on the method when a lock appears in any of its statements, tuple elements included, and the method has no `IAsyncDbTransaction` parameter.

- [ ] **Step 1: Write the failing tests.**
  - Surface: `Lock_operators_throw`.
  - Snapshots:
    - `For_update_by_id`
    - `Skip_locked_queue_claim`, which is `Where(…).OrderBy(…).Take(n).ForUpdateSkipLocked()`
    - `No_wait_lock`
    - `Lock_in_tuple_element`
  - Theory `Lock_reports_ZAO102`, for GroupBy, Count, a subquery, two locks and a command chain.
  - `Lock_without_transaction_warns_ZAO113`, and `Lock_with_transaction_has_no_ZAO113`.
  - The help-link test picks up ZAO113.md.
  - Postgres:
    - `For_update_blocks_a_second_writer_until_commit`
    - `Skip_locked_claims_are_disjoint_across_transactions`
    - `No_wait_throws_when_row_is_locked`, which expects `PostgresException` with SqlState `55P03`.
- [ ] **Step 2: Run them; expect FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the generator project, `ZeroAlloc.ORM.Linq.Tests`, then the Linq integration filter.**
- [ ] **Step 5: Commit** as `feat: lock LINQ query rows with ForUpdate, SkipLocked and NoWait`.

### Task 5: AOT smoke and docs

**Files:**
- Modify: `tests/ZeroAlloc.ORM.AotSmoke/`. In the `ZAORM_AOT_PG` LINQ section, add a guarded `ExecuteUpdate` with `Sql.Xmin` and `ThrowOnNoRows`, an `ExecuteDelete`, and a `ForUpdateSkipLocked` claim inside a transaction.
- Modify `docs/cookbook/linq.md`:
  - new sections "Set-based writes", "Optimistic concurrency" and "Row locks";
  - the "Replacing EF Core" table extended with the Daedalus sites listed at the top of this plan;
  - the not-supported list updated.
- Modify: `docs/README.md`, and the example lists in ZAO100, ZAO102, ZAO111 and ZAO112.

- [ ] **Step 1: Add the AOT smoke case.** Publish with the `aot-smoke.yml` command, alone, and expect zero warnings. Run the binary once against a local PostgreSQL with `ZAORM_AOT_PG` set.
- [ ] **Step 2: Write the docs.** Every Daedalus site in the table gets a short `[LinqCommand]` or `[LinqQuery]` example copied from a tested shape. Document the overflow difference and the READ COMMITTED behaviour of compare-and-swap updates.
- [ ] **Step 3: Commit** as `docs: document LINQ set-based writes, concurrency and row locks`.

## After merge

- Confirm the release-please PR lists the four `feat:` entries. Use `BEGIN_COMMIT_OVERRIDE` in the PR body.
- Verify the release on NuGet.
- Tell the Daedalus session which sites now translate, pointing it at the cookbook table.
