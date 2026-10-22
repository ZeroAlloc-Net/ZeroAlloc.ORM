# Float and Double PostgreSQL Arrays Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the PostgreSQL array convention from #377 so that `float` and `double` are array element types, alongside `string`, `int`, `long` and `Guid`. Parameters bind as `real[]` and `double precision[]`, and columns read back as `float[]`, `double[]` and the list shapes.

**Architecture:** The element set is defined once, in `PrimitiveCatalog.IsPrimitiveArrayElement`. Both the discovery rule and the array reader use it, so adding `SpecialType.System_Single` and `SpecialType.System_Double` there is the whole behaviour change. Binding needs no change: `BuildBoundValue` assigns the CLR array, and Npgsql infers `real[]` and `double precision[]` from it. Everything else in this plan is tests and docs that list the element types.

**Tech Stack:** Roslyn source generator, Npgsql, xunit v3, Verify snapshots, Testcontainers PostgreSQL.

**Spec:** GitHub issue ZeroAlloc-Net/ZeroAlloc.ORM#396, which this plan implements.

## Global Constraints

- The behaviour is additive and non-breaking. Shapes (`T[]`, `List<T>`, `IReadOnlyList<T>`), nullable rules, the ZAO082 requirement for Npgsql, and materialization stay exactly as they work for the existing element types.
- One definition only. Do not add a second element list anywhere; every caller goes through `PrimitiveCatalog.IsPrimitiveArrayElement`.
- `byte[]` stays a Primitive (BLOB), as it is today.
- Every doc comment and doc page that lists the array element types must say: string, int, long, float, double, Guid.
- Commits are conventional. Use `feat:` for the change, since it is a user-visible capability, and `test:` or `docs:` where those fit. End each commit with a blank line followed by `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Do not nest parentheses in commit bodies.
- The AOT smoke gate stays green.

## Review Focus

1. A `float[]` column read must use `GetFieldValue<float[]>`, not `double[]`. Npgsql returns `real[]` as `float[]`. Task 2's snapshot pins this.
2. `IReadOnlyList<float>` that is neither an array nor a list is copied to `float[]` before binding, the same as the existing element types. Task 2 snapshot.
3. LINQ `Contains` over a `float[]` (`= ANY(@p)`) goes through the same predicate check. If `LinqQueryReader.Predicates` uses the catalog, it is covered; if it keeps its own list, fix it to use the catalog. Task 1 checks this.
4. A nullable element, as in `float?[]`, classifies the same way the existing types do (`int?[]` does not match today; follow that rule exactly). Task 1 tests this.
5. Integration round-trips preserve the exact values for `float.MaxValue`, `float.Epsilon` and `NaN` through `real[]`. Task 3 tests this.

---

### Task 1: Classify float and double arrays

**Files:**
- Modify: `src/ZeroAlloc.TypeConversions/PrimitiveCatalog.cs:141-148`, both the comment and `IsPrimitiveArrayElement`
- Modify: `src/ZeroAlloc.TypeConversions/ConventionDiscovery.cs:14` (doc list)
- Modify: `src/ZeroAlloc.ORM.Generator/Linq/LinqQueryReader.Predicates.cs:373` (comment, plus code if it keeps its own list)
- Test: `tests/ZeroAlloc.TypeConversions.Tests/PrimitiveArrayDiscoveryTests.cs`

- [ ] **Step 1: Write the failing tests.** These are discovery theories, in the style of the existing tests:
  - `float[]`, `List<float>`, `IReadOnlyList<float>`, `double[]`, `List<double>` and `IReadOnlyList<double>` classify as `PrimitiveArray`, with `ElementType` set to the element and the matching `ArrayShape`.
  - `float?[]` and `List<double?>` follow the same rule as `int?[]` today. Assert whatever the existing nullable-element tests assert for `int`.
  - `PrimitiveCatalog.GetArrayReaderMethod(float)` returns `GetFieldValue<float[]>`, and the same for `double`.
- [ ] **Step 2: Run the tests.** `dotnet test tests/ZeroAlloc.TypeConversions.Tests -c Release`. Expected: the new cases FAIL.
- [ ] **Step 3: Add the element types.** Add `SpecialType.System_Single` and `SpecialType.System_Double` to `IsPrimitiveArrayElement`, and update the doc comments listed above.
- [ ] **Step 4: Run the tests again.** Expected: PASS.
- [ ] **Step 5: Commit.** `feat: bind float and double PostgreSQL arrays`

### Task 2: Generator emit for float and double arrays

**Files:**
- Test: `tests/ZeroAlloc.ORM.Generator.Tests/Emit/ArrayParameterEmitTests.cs`, plus new Verify snapshots under `tests/ZeroAlloc.ORM.Generator.Tests/Snapshots/`
- Modify: the doc comment at `ArrayParameterEmitTests.cs:10`

- [ ] **Step 1: Write the failing tests.** Add snapshot tests, following the existing `String_array_parameter_binds_value` and `Row_with_string_array_column_reads_GetFieldValue`:
  - `Float_array_parameter_binds_value`
  - `Double_readonly_list_parameter_binds_copy`
  - `Row_with_float_array_column_reads_GetFieldValue`
- [ ] **Step 2: Run them.** `dotnet test tests/ZeroAlloc.ORM.Generator.Tests -c Release`. Expected: FAIL, with new `.received` files.
- [ ] **Step 3: Accept the snapshots.** Review each received file. It must bind the array as is, copy `IReadOnlyList<double>` to `double[]`, and read with `GetFieldValue<float[]>`. Then accept them as `.verified`.
- [ ] **Step 4: Run the tests again.** Expected: PASS.
- [ ] **Step 5: Commit.** `test: pin float and double array emit`

### Task 3: PostgreSQL round-trip

**Files:**
- Modify: `tests/ZeroAlloc.ORM.Integration.Tests/ArrayRepo.cs` (add methods), `tests/ZeroAlloc.ORM.Integration.Tests/Postgres/PostgresArrayTests.cs` (add tests and the table DDL)
- Create: `tests/ZeroAlloc.ORM.Integration.Tests/RealsRow.cs` if a row type is needed, following `NumsRow`

- [ ] **Step 1: Write the failing tests.** Add to `ArrayRepo`:
  - `[Command("INSERT INTO reals (id, floats, doubles) VALUES (@id, @floats, @doubles)")] InsertRealsAsync(int id, float[] floats, double[] doubles, ct)`
  - `[Query("SELECT id, floats, doubles FROM reals WHERE id = @id")] GetRealsAsync(int id, ct)`, returning a row with `float[] Floats` and `List<double> Doubles`

  Create the table as `reals (id int primary key, floats real[], doubles double precision[])`. Tests run on both the full `NpgsqlConnection` and the slim data source, exactly as `PostgresArrayTests` does for nums:
  - round-trip `[1.5f, -0f, float.MaxValue, float.Epsilon, float.NaN]` and `[Math.PI, double.MinValue]`;
  - assert exact equality, using bitwise comparison for NaN and negative zero;
  - an empty array stores `'{}'`.
- [ ] **Step 2: Run them.** Use the existing Postgres integration filter or class. Run one suite at a time. Expected: FAIL to compile before Task 1, or PASS if Task 1 is in; confirm the test exercises the new path.
- [ ] **Step 3: Run the tests again.** Expected: PASS.
- [ ] **Step 4: Commit.** `test: round-trip float and double arrays through PostgreSQL`

### Task 4: Docs and AOT smoke

**Files:**
- Modify: `docs/cookbook/provider-quirks.md` ("PostgreSQL arrays" section, around line 260)
- Modify: `README.md` and `docs/README.md`, wherever the array element types are listed
- Modify: `tests/ZeroAlloc.ORM.AotSmoke/ArrayRepo.cs` and `Program.cs`, to add one `float[]` parameter and one `float[]` column round-trip
- Test: the AOT smoke publish

- [ ] **Step 1: Update the docs.** Every list of element types reads "string, int, long, float, double, Guid". The quirks page adds one line: a `float[]` parameter binds as `real[]`, which pgvector can cast with `@embedding::vector`.
- [ ] **Step 2: Extend the AOT smoke.** Add one `float[]` round-trip, mirroring the existing string array smoke.
- [ ] **Step 3: Verify the AOT build.** Run the AOT smoke publish exactly as `.github/workflows/aot-smoke.yml` does. Expected: 0 warnings, and the binary exits 0. If the smoke needs a database the workflow provides, run the publish and say the runtime step needs CI.
- [ ] **Step 4: Commit.** `docs: list float and double as PostgreSQL array elements`
