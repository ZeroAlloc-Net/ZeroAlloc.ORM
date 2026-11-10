# Table and column placeholders Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `{table}` and `{col:Name}` in `[Command]` and `[Query]` SQL, resolved at runtime from members of the repository class, with binding and materialisation still generated.

**Architecture:** A new internal scanner finds placeholders outside quotes and comments. The transform resolves each to a member of the class and stores the SQL with placeholders masked, so every existing parameter scan keeps working. The emit builds the final SQL through a per-method static cache plus an instance field.

**Tech Stack:** C# incremental source generator (`src/ZeroAlloc.ORM.Generator`), xUnit v3, SQLite integration tests.

**Spec:** `docs/design/2026-11-09-table-placeholder-design.md` (read it first, section 9 holds the resolved questions)

## Global Constraints

- No public API change: no new attribute, no new public member. `PublicAPI.*.txt` stays as it is. Never add an optional parameter to a shipped signature.
- Diagnostic IDs are **ZAO115 to ZAO118**, nothing else. Each gets `docs/diagnostics/ZAOnnn.md` and a row in `src/ZeroAlloc.ORM.Generator/AnalyzerReleases.Unshipped.md`.
- The value is trusted and spliced verbatim. A null or blank `{table}` value throws `ArgumentException`.
- After warm-up a placeholder method allocates the same bytes as the literal-SQL version.
- Conventional Commits. Commit body lines at most 100 characters, no nested parentheses, no Claude or Co-Authored-By trailer, no session link.
- Shared VM: build and test one project at a time. Run `dotnet test -c Release` as well as Debug before pushing. Do not merge any PR or release PR.
- Work in the worktree `ZeroAlloc.ORM-464` on `feat/table-name-placeholder`. The Outbox.Orm change is a separate later PR, out of scope.

## Review Focus

- A placeholder inside `'{table}'`, `"{table}"`, `[{table}]`, a backtick name, `$$...$$`, `-- {table}` or `/* {table} */` is neither replaced nor diagnosed. Pinned in Task 1.
- A PostgreSQL array literal `'{1,2}'` and an ODBC escape `{d '2020-01-01'}` pass through unchanged. Task 1.
- Two repository instances with different table values in one process each get their own SQL. Task 3.
- Overloads that both use placeholders get distinct generated names. Task 3.
- Concurrent first use of one instance returns the same string and never throws. Task 3.

---

## File Structure

- Create `src/ZeroAlloc.ORM.Generator/SqlPlaceholderScanner.cs`: scan and mask.
- Modify `src/ZeroAlloc.ORM.Generator/Model/QueryMethodModel.cs`: new trailing defaulted fields on the two records.
- Modify `src/ZeroAlloc.ORM.Generator/OrmGenerator.cs`: transform (member resolution, diagnostics), `BuildCommandTextAssignment` and the batch-split emits, `RenderRepository` helper emission.
- Modify `src/ZeroAlloc.ORM.Generator/Diagnostics/DiagnosticDescriptors.cs` and `LookupDescriptor` (OrmGenerator.cs near line 5255).
- Tests in `tests/ZeroAlloc.ORM.Generator.Tests/{Model,Emit,Diagnostics}` and `tests/ZeroAlloc.ORM.Integration.Tests` (plus its `AllocationGates` folder).

### Task 1: SqlPlaceholderScanner

**Files:**
- Create: `src/ZeroAlloc.ORM.Generator/SqlPlaceholderScanner.cs`
- Test: `tests/ZeroAlloc.ORM.Generator.Tests/Model/SqlPlaceholderScannerTests.cs`

**Interfaces:**
- Produces:
  - `internal enum SqlPlaceholderKind { Member, Column }`
  - `internal sealed record SqlPlaceholderSpan(int Start, int Length, SqlPlaceholderKind Kind, string Name)` (for `{col:Id}`, `Name` is `Id`; for `{table}`, `Name` is `table`)
  - `internal sealed record SqlPlaceholderScan(string MaskedSql, ImmutableArray<SqlPlaceholderSpan> Spans)`
  - `internal static SqlPlaceholderScan SqlPlaceholderScanner.Scan(string sql)`

- [ ] **Step 1: Write failing tests.** Names and assertions:
  - `Scan_FindsTableAndColumn`: `"SELECT {col:Id} FROM {table}"` gives two spans, `(Column, "Id")` then `(Member, "table")`, with `Start` and `Length` covering the exact braces text.
  - `Scan_MaskedSql_HasSameLengthAndNoBraces`: `MaskedSql.Length == sql.Length`, contains no `{`, and each masked span is a run of identifier characters starting with a letter.
  - `Scan_IgnoresQuotedAndCommented` (Theory): `'{table}'`, `"{table}"`, `[{table}]`, `` `{table}` ``, `$$ {table} $$`, `$tag$ {table} $tag$`, `-- {table}\n`, `/* {table} /* {table} */ {table} */` each give zero spans and `MaskedSql == sql`.
  - `Scan_LeavesNonPlaceholderBraces`: `'{1,2}'`, `{d '2020-01-01'}`, `{}`, `{ table }`, `{col:}` and `{1abc}` give zero spans.
- [ ] **Step 2: Run to verify failure.** `dotnet test tests/ZeroAlloc.ORM.Generator.Tests -c Debug --filter SqlPlaceholderScannerTests`. Expected: compile error, type missing.
- [ ] **Step 3: Implement `SqlPlaceholderScanner`** as a single left-to-right pass with a state per skipped region (single quote with doubling, double quote, bracket, backtick, dollar quote with matching tag, line comment, nested block comment). Name grammar is `[A-Za-z_][A-Za-z0-9_]*`. Mask each span with `_` followed by letters so it stays an identifier of the same length (minimum span length is 3, so `{a}` masks to `_zz`-style text of length 3).
- [ ] **Step 4: Run to verify pass.** Same command, expected all pass.
- [ ] **Step 5: Commit.** `feat: scan SQL for table and column placeholders`

### Task 2: Resolve placeholders in the transform, with ZAO115 and ZAO117

**Files:**
- Modify: `src/ZeroAlloc.ORM.Generator/Model/QueryMethodModel.cs`, `OrmGenerator.cs` (transform near lines 464 to 700 and 1459, `ResolveConnectionAccess` at 4923), `Diagnostics/DiagnosticDescriptors.cs`, `AnalyzerReleases.Unshipped.md`
- Create: `docs/diagnostics/ZAO115.md`, `docs/diagnostics/ZAO117.md`
- Test: `tests/ZeroAlloc.ORM.Generator.Tests/Diagnostics/PlaceholderDiagnosticsTests.cs`, `tests/ZeroAlloc.ORM.Generator.Tests/Model/PlaceholderResolutionTests.cs`

**Interfaces:**
- Consumes: `SqlPlaceholderScanner.Scan` from Task 1.
- Produces:
  - `QueryMethodModel` gains `EquatableArray<SqlPlaceholderSpan> SqlPlaceholders = default` as the last positional parameter. `Sql` now holds `MaskedSql` whenever spans exist, so parameter scans, `CountStatements` and `FindPlaceholderSigil` see no braces.
  - `QueryRepositoryModel` gains `EquatableArray<PlaceholderMember> PlaceholderMembers = default` and `string? QuoteColumnsAccess = null`, where `internal sealed record PlaceholderMember(string Name, string Access)` maps a placeholder name to the member expression to read.
  - `ZAO115` descriptor `ZAO115_PlaceholderMemberNotFound` and `ZAO117` descriptor `ZAO117_PlaceholderNotSupported`, both registered in `LookupDescriptor`.

- [ ] **Step 1: Failing tests.** Using the existing `GeneratorHarness` pattern:
  - `Table_BindsToCapturedPrimaryConstructorParameter`, `..._Field`, `..._GetOnlyProperty`: no diagnostics, and the repository model lists `PlaceholderMember("table", "<name>")`.
  - `Table_IgnoresOneLeadingUnderscoreAndCase`: field `_Table` binds.
  - `Table_NoStringMember_ReportsZAO115`; `Table_UncapturedConstructorParameter_ReportsZAO115` (message says to capture it); `Table_TwoCandidates_ReportsZAO115` (`table` and `_table`, message names both); `Table_NonStringMember_ReportsZAO115`.
  - `Placeholder_InBulkInsert_ReportsZAO117`, `..._LinqQuery_...` (fed through the LINQ path if reachable, else assert on the attribute kinds), `..._StoredProcedure_...`.
  - `ExistingSqlWithQuotedBraces_UnchangedModel`: SQL `'{1,2}'` yields empty `SqlPlaceholders` and unchanged `Sql`.
- [ ] **Step 2: Run to verify failure.** Filter on the two new test classes.
- [ ] **Step 3: Implement.** Scan each `[Command]`/`[Query]` SQL (after resource load for `FromResource`), resolve members with the connection path's rules plus the name rule from design 5.2 and 9.2, report ZAO115 and ZAO117 through the same mechanism ZAO003 uses, and add the descriptors, release-tracking rows and docs pages.
- [ ] **Step 4: Run to verify pass,** then the whole `ZeroAlloc.ORM.Generator.Tests` project, Debug.
- [ ] **Step 5: Commit.** `feat: resolve placeholder members and report ZAO115 and ZAO117`

### Task 3: Emit single-command SQL through a cache

**Files:**
- Modify: `OrmGenerator.cs` (`BuildCommandTextAssignment` at 5892, `RenderRepository` at 5578)
- Create: `tests/ZeroAlloc.ORM.Generator.Tests/Emit/PlaceholderEmitTests.cs`, `tests/ZeroAlloc.ORM.Integration.Tests/TablePlaceholderRepo.cs`, `tests/ZeroAlloc.ORM.Integration.Tests/TablePlaceholderTests.cs`, `tests/ZeroAlloc.ORM.Integration.Tests/AllocationGates/TablePlaceholderAllocationTests.cs`

**Interfaces:**
- Consumes: `SqlPlaceholders`, `PlaceholderMembers` from Task 2.
- Produces, per placeholder method with index `i` and name `M` (index-plus-name, so overloads never collide):
  - `private string? __sql{i}_{M};` instance field
  - `private static readonly ConcurrentDictionary<ValueTuple of the member values, (string Full, string[] Parts)> __sqlCache{i}_{M}`
  - `private (string Full, string[] Parts) __Sql{i}_{M}()` returning the resolved entry, filling the field on first use
  - `BuildCommandTextAssignment` writes `__cmd.CommandText = __Sql{i}_{M}().Full;` when the method has placeholders; every other method's output stays byte-identical.

- [ ] **Step 1: Failing tests.**
  - Emit: `Placeholder_Method_EmitsCacheAndUsesIt` (assert generated text contains the field, the `ConcurrentDictionary` and `.Full`), `Literal_Method_EmitIsUnchanged` (output of an existing fixture compares equal to the current snapshot).
  - Integration, against in-memory SQLite: `Insert_And_Query_UseConfiguredTable` (create table `Widgets`, repository constructed with `"Widgets"`, round-trip a row), `TwoInstancesDifferentTables_AreIsolated`, `OverloadsWithPlaceholders_BothWork`, `NullTable_ThrowsArgumentException`, `ConcurrentFirstUse_AllCallsSucceed` (32 concurrent first calls on one fresh instance, every call succeeds and reads back the row it wrote).
  - Allocation gate, using the `AllocationGate` helper style in `AllocationGates`: `PlaceholderMethod_AllocatesSameBytesAsLiteralMethod` on one warmed instance, and `FreshInstancePerCall_AddsNoAllocationOverLiteral`.
- [ ] **Step 2: Run to verify failure.**
- [ ] **Step 3: Implement.** Build the entry from the masked SQL and spans: concatenate literal pieces with the member values, null-checking each with `ArgumentException.ThrowIfNullOrWhiteSpace`. `Parts` holds the per-statement texts (split on the masked SQL with `SqlStatementSplitter.Split`, then resolved) for Task 4. Cache key is the tuple of distinct member values in order of first appearance.
- [ ] **Step 4: Run to verify pass.** Generator tests Debug, then Integration tests Debug.
- [ ] **Step 5: Commit.** `feat: emit placeholder SQL through a per-method cache`

### Task 4: Batch-split and multi-statement paths

**Files:**
- Modify: `OrmGenerator.cs` around lines 8566 to 8660 and 8858 to 8895 (the sites that write `.CommandText = {stmtLiteral}` and the joined fallback)
- Test: extend `PlaceholderEmitTests.cs` and `TablePlaceholderTests.cs`

**Interfaces:**
- Consumes: `__Sql{i}_{M}().Parts` and `.Full` from Task 3.
- Produces: the batch-split emit assigns `Parts[k]` per statement and the joined fallback assigns `.Full`.

- [ ] **Step 1: Failing tests.** Emit: `MultiStatement_UsesPartsPerStatement`. Integration: `MultiStatementQuery_BothStatementsUseTable` against SQLite with a two-statement `[Query]`, and a tuple-result method that reads two lists from `{table}`.
- [ ] **Step 2: Run to verify failure.**
- [ ] **Step 3: Implement** the two assignment sites for methods with placeholders; leave literal methods untouched.
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit.** `feat: resolve placeholders in multi-statement queries`

### Task 5: Column form, QuoteColumns and ZAO118

**Files:**
- Modify: `OrmGenerator.cs` (transform: find a `bool` member named `QuoteColumns` by the Task 2 rules into `QuoteColumnsAccess`; emit: quote style by the dialect passed to `RenderRepository`; ZAO118 beside the other dialect-dependent diagnostics, following the `SqlServerOnlyDiagnostics` precedent), `Diagnostics/DiagnosticDescriptors.cs`, `AnalyzerReleases.Unshipped.md`
- Create: `docs/diagnostics/ZAO118.md`
- Test: extend `PlaceholderEmitTests.cs`, `PlaceholderDiagnosticsTests.cs`, `TablePlaceholderTests.cs`

**Interfaces:**
- Consumes: Tasks 2 to 4.
- Produces: `{col:Id}` emits `Id` when `QuoteColumns` is false or absent, and the dialect's quoted form otherwise: `"Id"` for SQLite and PostgreSQL, `[Id]` for SQL Server, a backtick form for MySQL. The quote flag joins the cache key only when the method uses `{col:}`.

- [ ] **Step 1: Failing tests.** Emit, one per dialect: `Column_QuotedPerDialect` (Theory over the four dialects, expected texts above). `Column_NoQuoteColumnsMember_EmitsBare`. Diagnostic: `ColumnPlaceholder_NoDialect_ReportsZAO118`, `ColumnPlaceholder_DialectDeclaredOnAssembly_Ok`. Integration (SQLite): `QuotedColumns_RoundTrip` against a table created with quoted mixed-case columns, and `BareColumns_RoundTrip`.
- [ ] **Step 2: Run to verify failure.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit.** `feat: add dialect-aware column placeholders and ZAO118`

### Task 6: ZAO116 and documentation

**Files:**
- Modify: `OrmGenerator.cs` (transform), `DiagnosticDescriptors.cs`, `AnalyzerReleases.Unshipped.md`, `README.md`, the cookbook page that covers `[Query]` under `docs/cookbook`
- Create: `docs/diagnostics/ZAO116.md`
- Test: extend `PlaceholderDiagnosticsTests.cs`

**Interfaces:**
- Consumes: spans from Task 2.
- Produces: `ZAO116_PlaceholderWhereValueBelongs`, an Error.

- [ ] **Step 1: Failing tests.** `Placeholder_AfterComparisonOperator_ReportsZAO116` (Theory: `= {table}`, `< {table}`, `> {table}`, `LIKE {table}`, `IN ({table}`), `Placeholder_InsideValuesTuple_ReportsZAO116`, `Placeholder_NameMatchesUsedParameter_ReportsZAO116` (`WHERE a = @table` plus `{table}`), and negatives `FromAndJoinAndUpdateAndInto_NoDiagnostic`.
- [ ] **Step 2: Run to verify failure.**
- [ ] **Step 3: Implement** the narrow rule from design 5.5 on the masked SQL and spans, nothing broader.
- [ ] **Step 4: Run to verify pass.** Then write the docs: the README and cookbook state the trust rule in the same words (configuration, not input, spliced verbatim), show `{table}` and `{col:}` with a repository example, and give the table of ZAO115 to ZAO118. Every diagnostic page follows the `ZAO114.md` layout.
- [ ] **Step 5: Commit.** `feat: report ZAO116 for placeholders in value positions` and `docs: document table and column placeholders`

### Task 7: Full verification and PR

- [ ] **Step 1:** Run, one project at a time, `dotnet test -c Debug` then `dotnet test -c Release` for `ZeroAlloc.ORM.Generator.Tests`, `ZeroAlloc.ORM.Integration.Tests`, `ZeroAlloc.ORM.Tests`, then the solution build with warnings as errors as CI does. Expected: all green.
- [ ] **Step 2:** Run the api-compat check as CI configures it. Expected: no change reported.
- [ ] **Step 3:** Push and open a PR titled `feat: runtime table and column placeholders in Command and Query`. The body states the trust rule, lists ZAO115 to ZAO118, and has a Behaviour change section: SQL with `{identifier}` outside quotes is now a placeholder. Do not merge. Do not touch release PRs.
- [ ] **Step 4:** Reproduce any red CI check locally before fixing it.
