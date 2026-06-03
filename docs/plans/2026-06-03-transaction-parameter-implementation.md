# `IAsyncDbTransaction` Parameter Support Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Add shape-based detection + emit support for an optional `IAsyncDbTransaction` parameter on `[Command]` / `[Query]` / `[StoredProcedure]` partial methods, so adopters can thread a transaction across multiple ZA.ORM-generated calls atomically on every provider (Sqlite, Postgres, **SqlClient**).

**Architecture:** Mirrors the existing `CancellationToken` precedent — detect by `Type.ToDisplayString() == "System.Data.Async.IAsyncDbTransaction"`, mark with a new `IsTransaction` flag on `ParameterInfo`, expose method-level via `TransactionParameterName` on `QueryMethodModel`. New `EmitTransactionAssignment` helper threads `__cmd.Transaction = @<paramName>;` into all 9 emit sites after `CreateCommand()`. New `ZAO080` Warning for more-than-one tx parameter.

**Tech Stack:** C# 13 / .NET 10 / Roslyn incremental generators / Verify (`.verified.cs` snapshots) / xUnit / Sqlite integration tests / AdoNet.Async transaction surface.

**Reference design doc:** `docs/plans/2026-06-03-transaction-parameter-design.md` (committed `632af15` on this branch).

**Working branch:** `feat/orm-transaction-parameter` (already created off `main` at `b234811` / v1.4.0).

> **Local SDK pin gotcha** (same as every prior ORM plan): `global.json` pins SDK `10.0.300 latestFeature`; dev machine has 10.0.204 max. Before any `dotnet build`/`dotnet test`:
> ```powershell
> (Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
> ```
> Revert with `git checkout global.json` before each commit. **Never commit the relaxed pin.**

> **TDD shape note:** Tasks 1-4 are mechanical generator additions where "test fails first" doesn't naturally apply (no behavior visible to tests until the emit threads through). For those, verification is "build green + existing tests pass + relevant snapshots unchanged." Task 5+ is Verify-driven TDD: write the snapshot test, see the `.received.cs` reveal the new emit, promote.

---

### Task 1: Add `IsTransaction` flag to `ParameterInfo` + parameter detection

**Files:**
- Modify: `src/ZeroAlloc.ORM.Generator/Model/QueryMethodModel.cs` — add field to `ParameterInfo` record
- Modify: `src/ZeroAlloc.ORM.Generator/OrmGenerator.cs` — extend the per-parameter classification loop around line 832-879

**Step 1: Find the existing `ParameterInfo` record** in `QueryMethodModel.cs`

Use Grep to locate the record declaration. It's a positional record with fields including `IsCancellationToken`. Add a new field `IsTransaction` after it (preserve positional order).

**Step 2: Add the new field**

Use Edit. Find the line:
```csharp
IsCancellationToken: false,
```
…inside the BulkInsert collection-parameter `return new ParameterInfo(...)` block at `OrmGenerator.cs:865-873`. The full positional `ParameterInfo` constructor list needs a new `IsTransaction: false` entry. Apply the same change to every other `new ParameterInfo(...)` site in `OrmGenerator.cs` (use Grep to find them all).

The `ParameterInfo` record itself in `QueryMethodModel.cs` gets a new positional field `bool IsTransaction` immediately after `bool IsCancellationToken`.

**Step 3: Detect `IAsyncDbTransaction` in the classification loop**

In `OrmGenerator.cs` around line 835 (where `isCt` is computed), add:

```csharp
var isTx = string.Equals(
    p.Type.ToDisplayString(),
    "System.Data.Async.IAsyncDbTransaction",
    StringComparison.Ordinal);
```

Just after the existing `isCt` line.

Then add a new early-return branch (right after the existing `isCt` early-return shape, around line 875):

```csharp
if (isTx)
{
    return new ParameterInfo(
        p.Name,
        p.Type.ToDisplayString(parameterDisplayFormat),
        IsCancellationToken: false,
        IsTransaction: true,
        ParamNameOverride: paramNameOverride,
        IsNullable: isNullable,
        Convention: null,
        CompositeFields: default,
        CompositeTypeFullName: null);
}
```

(Match the BulkInsert-collection branch's shape at lines 864-873 — it's the same idiom of "this isn't a SQL value, skip convention discovery.")

Update the CT branch's `new ParameterInfo(...)` to include `IsTransaction: false`.

**Step 4: Verify build green + existing tests still pass**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet build src/ZeroAlloc.ORM.Generator/ZeroAlloc.ORM.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj
```

Expected:
- Build: green
- Tests: same count as baseline (258 generator tests post-PR #109). No tx parameter has been declared anywhere yet, so detection is dormant.

**If any existing snapshot diffs**, STOP — the new `IsTransaction: false` field on existing `ParameterInfo` entries shouldn't change emit output (the field is read by Task 3's emit code, which doesn't exist yet). Most likely cause of diff: missed a `new ParameterInfo(...)` site that's still positional-arg-mismatched.

**Step 5: Revert + commit**

```powershell
git checkout global.json
git add src/ZeroAlloc.ORM.Generator/Model/QueryMethodModel.cs src/ZeroAlloc.ORM.Generator/OrmGenerator.cs
git commit -m "feat(generator): detect IAsyncDbTransaction parameter (shape-based)

Mirrors the CancellationToken precedent. Adds an IsTransaction bool
to the ParameterInfo positional record, and an isTx detection branch
in the per-parameter classification loop that short-circuits convention
discovery and SQL-binding for tx parameters.

Dormant until the emit side (next commit) threads it through. No
existing snapshots should diff."
```

---

### Task 2: Add `TransactionParameterName` field to `QueryMethodModel` + scan

**Files:**
- Modify: `src/ZeroAlloc.ORM.Generator/Model/QueryMethodModel.cs` — add field
- Modify: `src/ZeroAlloc.ORM.Generator/OrmGenerator.cs` — extract + populate

**Step 1: Add `TransactionParameterName` field**

Find the `QueryMethodModel` record in `QueryMethodModel.cs`. Locate the existing field `CancellationTokenParameterName: string?` (referenced at `OrmGenerator.cs:1037`). Add `TransactionParameterName: string?` immediately after it (preserve positional order).

**Step 2: Populate it from the method scan**

In `OrmGenerator.cs` around line 1016, the existing code is:

```csharp
var cancellationTokenParameterName = methodParameters
    .FirstOrDefault(p => p.IsCancellationToken)?.Name;
```

Add the analog immediately after:

```csharp
var transactionParameterName = methodParameters
    .FirstOrDefault(p => p.IsTransaction)?.Name;
```

In the `new QueryMethodModel(...)` constructor call (around line 1037), add `TransactionParameterName: transactionParameterName,` adjacent to the existing `CancellationTokenParameterName:` line.

**Step 3: Verify build green + existing tests**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet build src/ZeroAlloc.ORM.Generator/ZeroAlloc.ORM.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj
```

Expected: green, same test count. The field is null for all existing methods (none declares a tx param yet), and nothing reads it yet (emit comes in Task 3).

**Step 4: Revert + commit**

```powershell
git checkout global.json
git add src/ZeroAlloc.ORM.Generator/Model/QueryMethodModel.cs src/ZeroAlloc.ORM.Generator/OrmGenerator.cs
git commit -m "feat(generator): surface TransactionParameterName on QueryMethodModel

Method-level lookup analog of CancellationTokenParameterName.
Populated by scanning methodParameters for the single IsTransaction
entry (null when no tx parameter). Emit-side consumer lands in the
next commit."
```

---

### Task 3: Add `EmitTransactionAssignment` helper + wire into all 9 emit sites

**Files:**
- Modify: `src/ZeroAlloc.ORM.Generator/OrmGenerator.cs` — add helper + 9 call sites

**Step 1: Add the helper**

Insert near `BuildConnectionPrologue` (around line 4107) for visibility:

```csharp
// v1.5 — emits `__cmd.Transaction = @<paramName>;` immediately after a
// CreateCommand() line, but only when the method declares an
// IAsyncDbTransaction parameter. When TransactionParameterName is null
// the emit is a no-op — preserves byte-identical output for the
// dominant case where no tx is threaded.
private static void EmitTransactionAssignment(StringBuilder sb, QueryMethodModel m, string indent)
{
    if (m.TransactionParameterName is null) return;
    sb.Append(indent).Append("__cmd.Transaction = @").Append(m.TransactionParameterName).AppendLine(";");
}
```

**Step 2: Call it at every `CreateCommand()` site**

Use Grep to find all `await using var __cmd = __conn.CreateCommand();` lines. Confirmed locations from earlier exploration (line numbers approximate — may have shifted):
- 4166 (EmitNonQuery)
- 4199 (EmitFlatRow)
- 4397 (EmitBulkInsertCommand per-chunk)
- 4600 (EmitMultiResultSet)
- 4856 (EmitStreaming)
- 4985, 5051 (EmitScalar variants)
- 5454 (EmitCommandIdentity)
- 5676 (EmitListResultSet)

For each, the surrounding code looks like:

```csharp
sb.AppendLine("            await using var __cmd = __conn.CreateCommand();");
BuildCommandTextAssignment(sb, m, "__cmd", "            ");
```

Insert the helper call between those two lines:

```csharp
sb.AppendLine("            await using var __cmd = __conn.CreateCommand();");
EmitTransactionAssignment(sb, m, "            ");
BuildCommandTextAssignment(sb, m, "__cmd", "            ");
```

(Each site uses the same `"            "` 12-space indent — confirm by reading each callsite. The BulkInsert per-chunk site at line 4397 uses a deeper indent — read it and match.)

**Step 3: Verify build green + existing snapshots stay unchanged**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet build src/ZeroAlloc.ORM.Generator/ZeroAlloc.ORM.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj
```

Expected: **all existing tests pass** (no `.received.cs` diffs). The helper is a no-op when `TransactionParameterName == null`, which is true for every existing test method.

**If any snapshot diffs**, STOP — most likely cause is a missed `m.TransactionParameterName is null` early-return, OR the `EmitTransactionAssignment` ended up writing something even when the parameter is absent. Inspect the `.received.cs` diff: any diff that adds a non-empty line is a bug.

**Step 4: Revert + commit**

```powershell
git checkout global.json
git add src/ZeroAlloc.ORM.Generator/OrmGenerator.cs
git commit -m "feat(generator): emit cmd.Transaction = @tx; when IAsyncDbTransaction param present

EmitTransactionAssignment helper threads the assignment between
CreateCommand() and CommandText. Wired into all 9 single-method
emit sites plus BulkInsert's per-chunk command init. No-op when
TransactionParameterName is null — existing snapshots unchanged.

Adopter-facing: declare an IAsyncDbTransaction parameter on a
[Command]/[Query]/[StoredProcedure] partial method and the generated
body will participate in the transaction across providers (Sqlite,
Postgres, and now SqlClient — closes the silent-breakage gap).

ZA.Templates #162 unblock starts here; templates adoption is a
separate PR once v1.5.0 ships."
```

---

### Task 4: Add ZAO080 diagnostic + multi-tx-param check + LookupDescriptor wiring

**Files:**
- Modify: `src/ZeroAlloc.ORM.Generator/Diagnostics/DiagnosticDescriptors.cs` — add descriptor
- Modify: `src/ZeroAlloc.ORM.Generator/AnalyzerReleases.Unshipped.md` — register
- Create: `docs/diagnostics/ZAO080.md` — adopter-facing doc (the `DiagnosticHelpLinkTests` test asserts every descriptor has a doc — see ZA.ORM #103/#104 precedent)
- Modify: `src/ZeroAlloc.ORM.Generator/OrmGenerator.cs` — fire site + LookupDescriptor

**Step 1: Add the descriptor**

Locate the existing ZAO074 descriptor (last in DiagnosticDescriptors.cs). Append ZAO080 below it, using the existing `Make(...)` helper (style precedent: ZAO070-074 from BulkInsert). The descriptor:

```csharp
public static readonly DiagnosticDescriptor ZAO080_MultipleTransactionParameters = Make(
    id: "ZAO080",
    title: "At most one IAsyncDbTransaction parameter",
    messageFormat: "Method '{0}' has {1} IAsyncDbTransaction parameters; only the first is used.",
    severity: DiagnosticSeverity.Warning);
```

(Exact `Make(...)` signature — match the surrounding code's style. The `category` is set inside `Make`.)

**Step 2: Register in `AnalyzerReleases.Unshipped.md`**

Append a row to the `### New Rules` table matching the existing pattern (ZAO070-074 entries). Severity column = `Warning`.

**Step 3: Create the help doc**

Create `docs/diagnostics/ZAO080.md` following the `ZAO064.md` / `ZAO074.md` template (Severity / Category / Trigger / Fix / Example / Related). Body:

```markdown
# ZAO080 — At most one IAsyncDbTransaction parameter

**Severity:** Warning
**Category:** ZeroAlloc.ORM

## Trigger

A `[Command]` / `[Query]` / `[StoredProcedure]` partial method declares more than one parameter of type `IAsyncDbTransaction`. The generator emits `__cmd.Transaction = @<name>;` using the first one and ignores the rest.

## Fix

Remove all but one `IAsyncDbTransaction` parameter from the method signature.

## Example

Triggers:
\`\`\`csharp
[Command("INSERT INTO X (...) VALUES (...)")]
public partial Task<int> InsertAsync(int id, IAsyncDbTransaction tx1, IAsyncDbTransaction tx2, CancellationToken ct);
\`\`\`

OK:
\`\`\`csharp
[Command("INSERT INTO X (...) VALUES (...)")]
public partial Task<int> InsertAsync(int id, IAsyncDbTransaction tx, CancellationToken ct);
\`\`\`

## Related

- [ZAO006 — At most one CancellationToken parameter](ZAO006.md)
```

**Step 4: Fire the diagnostic**

In `OrmGenerator.cs`, find the ZAO006 emission block (line 428-437 confirmed earlier). Add the analog ZAO080 block immediately after:

```csharp
// ZAO080 — at most one IAsyncDbTransaction parameter (warning).
var txParamCount = method.Parameters.Count(p =>
    string.Equals(p.Type.ToDisplayString(), "System.Data.Async.IAsyncDbTransaction", StringComparison.Ordinal));
if (txParamCount > 1)
{
    diagnostics.Add(new DiagnosticInfo(
        DescriptorId: "ZAO080",
        Location: LocationInfo.From(methodSyntax.Identifier.GetLocation()),
        MessageArgs: new EquatableArray<string>(ImmutableArray.Create(method.Name, txParamCount.ToString(CultureInfo.InvariantCulture)))));
}
```

**Step 5: Wire LookupDescriptor**

In `OrmGenerator.cs:3835-3840` (the switch from id to descriptor used by `ReportDiagnostics`), add:

```csharp
"ZAO080" => DiagnosticDescriptors.ZAO080_MultipleTransactionParameters,
```

> **Why this matters:** ZA.ORM PR #108 caught a silent gap where descriptors existed in `DiagnosticDescriptors.cs` but weren't mapped in `LookupDescriptor` — the diagnostics fired but were silently discarded. Don't repeat that.

**Step 6: Verify build green + existing tests pass**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet build src/ZeroAlloc.ORM.Generator/ZeroAlloc.ORM.Generator.csproj -c Release
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj
```

Expected: green. The `DiagnosticHelpLinkTests` test rail asserts every descriptor has a real help doc — `docs/diagnostics/ZAO080.md` must exist for the build/tests to pass.

**Step 7: Revert + commit**

```powershell
git checkout global.json
git add src/ZeroAlloc.ORM.Generator/Diagnostics/DiagnosticDescriptors.cs src/ZeroAlloc.ORM.Generator/AnalyzerReleases.Unshipped.md docs/diagnostics/ZAO080.md src/ZeroAlloc.ORM.Generator/OrmGenerator.cs
git commit -m "feat(generator): ZAO080 warning for multiple IAsyncDbTransaction parameters

Mirrors the ZAO006 precedent for CancellationToken parameters.
Descriptor + AnalyzerReleases.Unshipped.md row + help doc +
classifier fire-site + LookupDescriptor wiring.

The first tx parameter is used by the emit; this warning surfaces
the latent misuse rather than silently picking one and forgetting
the others."
```

---

### Task 5: Snapshot tests (4 new)

**Files:**
- Create: `tests/ZeroAlloc.ORM.Generator.Tests/Emit/TransactionParameterTests.cs`

**Step 1: Write the 4 tests**

Mirror the existing emit-snapshot style (e.g. `ListResultSetTests.cs` from PR #109 is a good shape template). The 4 cells:

```csharp
using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// v1.5 — IAsyncDbTransaction parameter support.
// Four snapshots cover the headline emit shapes:
//   * [Command(Kind = NonQuery)] — the dominant write shape
//   * [Command(Kind = Identity)] — RETURNING-Id case
//   * [Query] returning Task<T?> (FlatRow) — single-row read
//   * [Command(Kind = BulkInsert)] — confirms the per-chunk command picks up the tx line
public class TransactionParameterTests
{
    [Fact]
    public Task NonQuery_with_transaction_parameter_emits_cmd_Transaction_assignment()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE Orders SET Status = @status WHERE Id = @id", Kind = CommandKind.NonQuery)]
                public partial Task<int> UpdateStatusAsync(int id, string status, IAsyncDbTransaction tx, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Identity_with_transaction_parameter_emits_cmd_Transaction_assignment()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (CustomerId) VALUES (@customerId) RETURNING Id", Kind = CommandKind.Identity)]
                public partial Task<int> InsertOrderAsync(int customerId, IAsyncDbTransaction tx, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task Query_FlatRow_with_transaction_parameter_emits_cmd_Transaction_assignment()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int Id, int CustomerId);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Query("SELECT Id, CustomerId FROM Orders WHERE Id = @id")]
                public partial Task<OrderRow?> ReadOrderAsync(int id, IAsyncDbTransaction tx, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task BulkInsert_with_transaction_parameter_emits_cmd_Transaction_per_chunk()
    {
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record OrderRow(int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)", Kind = CommandKind.BulkInsert)]
                public partial Task<int> InsertOrdersAsync(IReadOnlyList<OrderRow> orders, IAsyncDbTransaction tx, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
```

**Step 2: Run — expect 4 "no Verify baseline" fails + 4 `.received.cs` files**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj --filter "FullyQualifiedName~TransactionParameterTests"
```

Expected: 4 failed (no baseline), 4 `.received.cs` files dropped in `Snapshots/`.

**Step 3: Inspect each `.received.cs`**

For each of the 4:
1. Verify the partial method body has `__cmd.Transaction = @tx;` immediately after `await using var __cmd = __conn.CreateCommand();`
2. Verify no `@tx` parameter is added via `CreateParameter()` (the tx isn't a SQL value)
3. Verify the `tx` parameter appears in the partial method signature (it's a declared parameter, even if not bound)
4. For BulkInsert: confirm the `__cmd.Transaction = @tx;` line appears INSIDE the chunk loop (every chunk gets a fresh `__cmd` so the assignment must repeat)

If any property fails, STOP — emit bug. Don't promote.

**Step 4: Promote**

```powershell
Get-ChildItem tests/ZeroAlloc.ORM.Generator.Tests -Filter "TransactionParameterTests*.received.*" -Recurse | ForEach-Object {
    $newName = $_.Name -replace '\.received\.', '.verified.'
    Move-Item $_.FullName (Join-Path $_.Directory $newName) -Force
}
```

**Step 5: Confirm 4/4 green + full suite**

```powershell
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj --filter "FullyQualifiedName~TransactionParameterTests"
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj
```

Expected: 4/4 + full suite green (258 baseline + 4 new = 262).

**Step 6: Revert + commit**

```powershell
git checkout global.json
git add tests/ZeroAlloc.ORM.Generator.Tests/Emit/TransactionParameterTests.cs tests/ZeroAlloc.ORM.Generator.Tests/Snapshots/TransactionParameterTests.*.verified.cs
git commit -m "test(generator): 4 snapshot tests for IAsyncDbTransaction parameter emit

Covers the headline shapes:
  - NonQuery: cmd.Transaction = @tx; before ExecuteNonQueryAsync
  - Identity: same, with RETURNING Id readback
  - Query FlatRow: same, with single-row materialization
  - BulkInsert: cmd.Transaction = @tx; inside the chunk loop (every
    fresh __cmd picks up the tx)

Snapshots verified by visual inspection: tx parameter declared in
method signature but never bound as a SQL parameter (it's a control
signal, like CancellationToken)."
```

---

### Task 6: ZAO080 diagnostic test

**Files:**
- Create: `tests/ZeroAlloc.ORM.Generator.Tests/Diagnostics/TransactionParameterDiagnosticsTests.cs`

**Step 1: Write the test**

Follow the existing diagnostic-test style (e.g. `BulkInsertDiagnosticsTests.cs` from PR #106 is the shape template):

```csharp
using System.Linq;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

public class TransactionParameterDiagnosticsTests
{
    [Fact]
    public void ZAO080_fires_when_method_declares_multiple_IAsyncDbTransaction_parameters()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;
            namespace TestApp;
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE X SET A = @a WHERE Id = @id", Kind = CommandKind.NonQuery)]
                public partial Task<int> UpdateAsync(int id, int a, IAsyncDbTransaction tx1, IAsyncDbTransaction tx2, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        Assert.Contains(diagnostics, d => d.Id == "ZAO080");
    }

    [Fact]
    public void ZAO080_does_not_fire_when_method_declares_one_IAsyncDbTransaction_parameter()
    {
        // Guard against false-positive (count == 1 should be silent).
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;
            namespace TestApp;
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("UPDATE X SET A = @a WHERE Id = @id", Kind = CommandKind.NonQuery)]
                public partial Task<int> UpdateAsync(int id, int a, IAsyncDbTransaction tx, CancellationToken ct);
            }
            """;
        var result = GeneratorHarness.RunGenerator(source);
        var diagnostics = result.Results[0].Diagnostics;
        Assert.DoesNotContain(diagnostics, d => d.Id == "ZAO080");
    }
}
```

**Step 2: Run + verify both pass**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj --filter "FullyQualifiedName~TransactionParameterDiagnosticsTests"
```

Expected: **2/2 passed**.

Confirm full suite still green (was 262 after Task 5):

```powershell
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj
```

Expected: 264 (262 + 2).

**Step 3: Revert + commit**

```powershell
git checkout global.json
git add tests/ZeroAlloc.ORM.Generator.Tests/Diagnostics/TransactionParameterDiagnosticsTests.cs
git commit -m "test(generator): ZAO080 multi-tx-parameter diagnostic test

Positive: 2 tx parameters → ZAO080 fires.
Negative: 1 tx parameter → ZAO080 does not fire (false-positive guard)."
```

---

### Task 7: Sqlite integration tests — commit + rollback paths

**Files:**
- Create: `tests/ZeroAlloc.ORM.Integration.Tests/TransactionParameterTests.cs`

**Step 1: Survey the existing integration-test pattern**

Read 1-2 existing integration test files for the `SqliteFixture` shape — `LifecycleTests.cs` or `BulkInsertTests.cs` (from PR #106) are good templates. Identify:
- How the fixture is constructed
- How partial-method-bearing repo classes are declared (inside or alongside the test file)
- How DDL is set up per test
- How CancellationToken is used (for parity with the new tx parameter)

**Step 2: Write the two tests**

The test file declares a tiny repo with two `[Command]` methods that share a tx, then exercises both happy path and rollback path:

```csharp
using System.Data;
using System.Data.Async;
using System.Data.Async.Adapters;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// v1.5 — IAsyncDbTransaction parameter support.
// Confirms the emitted `__cmd.Transaction = @tx;` actually participates in
// the transaction at runtime (snapshots verify the emit-shape; these verify
// the behavior). Sqlite-only — Postgres covered structurally (emit is
// provider-agnostic).
public sealed partial class TransactionRepo(IAsyncDbConnection conn)
{
    [Command("INSERT INTO Things (Name) VALUES (@name)", Kind = CommandKind.NonQuery)]
    public partial Task<int> InsertAsync(string name, IAsyncDbTransaction tx, CancellationToken ct);

    [Query("SELECT COUNT(*) FROM Things", Kind = QueryKind.Scalar)]
    public partial Task<int> CountAsync(CancellationToken ct);
}

public class TransactionParameterIntegrationTests
{
    private static async Task<IAsyncDbConnection> CreateConnAsync()
    {
        var sqlite = new SqliteConnection("DataSource=:memory:");
        sqlite.Open();
        await using var cmd = sqlite.CreateCommand();
        cmd.CommandText = "CREATE TABLE Things (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL)";
        await cmd.ExecuteNonQueryAsync();
        return sqlite.AsAsync();
    }

    [Fact]
    public async Task Two_inserts_share_transaction_and_commit_atomically()
    {
        var conn = await CreateConnAsync();
        var repo = new TransactionRepo(conn);

        await using var tx = await conn.BeginTransactionAsync();
        await repo.InsertAsync("A", tx, default);
        await repo.InsertAsync("B", tx, default);
        await tx.CommitAsync();

        var count = await repo.CountAsync(default);
        count.Should().Be(2);
    }

    [Fact]
    public async Task Two_inserts_share_transaction_and_rollback_when_not_committed()
    {
        var conn = await CreateConnAsync();
        var repo = new TransactionRepo(conn);

        await using (var tx = await conn.BeginTransactionAsync())
        {
            await repo.InsertAsync("A", tx, default);
            await repo.InsertAsync("B", tx, default);
            // No CommitAsync — DisposeAsync rolls back.
        }

        var count = await repo.CountAsync(default);
        count.Should().Be(0);
    }
}
```

> **Adapt the `Scalar` kind syntax** — confirm by reading existing scalar-shape tests. If the API is `[Command(Kind = CommandKind.Scalar)]` instead of `[Query(Kind = ...)]`, adjust. Or use a `[Query]` returning `Task<int>` if scalar inference works that way.

**Step 2: Run**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet test tests/ZeroAlloc.ORM.Integration.Tests/ZeroAlloc.ORM.Integration.Tests.csproj --filter "Category!=Postgres&FullyQualifiedName~TransactionParameter"
```

Expected: 2/2 passed. Both prove the emitted `__cmd.Transaction = @tx` actually honors the transaction at runtime.

**If either fails**, STOP — the Sqlite/Npgsql auto-bind behavior previously documented for the workaround approach may not work the same way when `cmd.Transaction = tx` is set explicitly. Read the failure carefully.

**Step 3: Confirm full integration suite still green**

```powershell
dotnet test tests/ZeroAlloc.ORM.Integration.Tests/ZeroAlloc.ORM.Integration.Tests.csproj --filter "Category!=Postgres"
```

Expected: previous Sqlite count + 2 new.

**Step 4: Revert + commit**

```powershell
git checkout global.json
git add tests/ZeroAlloc.ORM.Integration.Tests/TransactionParameterTests.cs
git commit -m "test(integration): Sqlite transaction-parameter end-to-end coverage

Two scenarios prove the emitted cmd.Transaction = @tx honors the
active transaction at runtime:
  - commit path: two inserts inside a tx + commit → COUNT == 2
  - rollback path: two inserts inside a tx + dispose-without-commit
    → COUNT == 0

Sqlite-only — Postgres + SqlClient covered structurally (the emit
is provider-agnostic; cmd.Transaction = tx is the standard ADO.NET
contract every provider honors)."
```

---

### Task 8: Push + PR + admin-merge with `feat:` squash title

**Step 1: Pre-flight log check**

```powershell
git log --oneline main..HEAD
```

Expected 8 commits (in order):
1. `docs(design): IAsyncDbTransaction parameter support for v1.5` (`632af15`)
2. `feat(generator): detect IAsyncDbTransaction parameter (shape-based)`
3. `feat(generator): surface TransactionParameterName on QueryMethodModel`
4. `feat(generator): emit cmd.Transaction = @tx; when IAsyncDbTransaction param present`
5. `feat(generator): ZAO080 warning for multiple IAsyncDbTransaction parameters`
6. `test(generator): 4 snapshot tests for IAsyncDbTransaction parameter emit`
7. `test(generator): ZAO080 multi-tx-parameter diagnostic test`
8. `test(integration): Sqlite transaction-parameter end-to-end coverage`

**Step 2: Final full sweep**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet build ZeroAlloc.ORM.slnx -c Release
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj -c Release
dotnet test tests/ZeroAlloc.ORM.Integration.Tests/ZeroAlloc.ORM.Integration.Tests.csproj -c Release
git checkout global.json
git status
```

Expected:
- Build: green
- Generator: 264 passed (258 baseline + 4 snapshots + 2 diagnostic)
- Integration: previous Sqlite count + 2 new, 1 skipped (pre-existing Postgres skip)
- Working tree clean

**Step 3: Push**

```powershell
git push -u origin feat/orm-transaction-parameter
```

**Step 4: Open the PR**

```powershell
$prBody = @'
## Summary

Adds optional `IAsyncDbTransaction` parameter support on `[Command]` / `[Query]` / `[StoredProcedure]` partial methods. When present, the generator emits `__cmd.Transaction = @<paramName>;` after `__cmd = __conn.CreateCommand();` at every emit site. Closes the architectural gap surfaced by ZA.Templates #162 (silently broken atomic writes on SqlClient).

## What changes

- **Generator parameter classification** detects `IAsyncDbTransaction` by `Type.ToDisplayString()` (shape-based, like CancellationToken). New `IsTransaction` flag on `ParameterInfo`.
- **Method-level lookup** via new `TransactionParameterName` field on `QueryMethodModel`.
- **Emit-time helper** `EmitTransactionAssignment` threads the assignment into all 9 emit sites (NonQuery / Scalar / Identity / FlatRow / DomainEntity / Composite / MultiResult / Streaming / ListResultSet / BulkInsert chunk loop). No-op when no tx parameter — every existing snapshot stays byte-identical.
- **New diagnostic ZAO080** (Warning) when multiple tx parameters detected — mirrors ZAO006 for CT.

## How adopters use it

```csharp
public sealed partial class OrderRepository(IAsyncDbConnection conn)
{
    public async Task AddAsync(Order order, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        var orderId = await InsertOrderAsync(/* ... */, tx, ct);
        order.AssignPersistenceId(new OrderId(orderId));
        foreach (var line in order.Lines)
            await InsertOrderLineAsync(orderId, /* ... */, tx, ct);
        await tx.CommitAsync(ct);
        // Works on every provider — cmd.Transaction is set explicitly.
    }

    [Command("INSERT INTO Orders ...")]
    private partial Task<int> InsertOrderAsync(/* ... */, IAsyncDbTransaction tx, CancellationToken ct);

    [Command("INSERT INTO OrderLines ...")]
    private partial Task<int> InsertOrderLineAsync(/* ... */, IAsyncDbTransaction tx, CancellationToken ct);
}
```

## Provider portability

- **Sqlite + Postgres**: previously worked by accident (auto-bind to connection's pending transaction); now works explicitly via `cmd.Transaction = tx`.
- **SqlClient**: previously SILENTLY broken (no auto-bind; commands wouldn't participate in the transaction); now works correctly.

## Test plan

- [x] Generator: 264 / 264 (4 new snapshots + 2 new diagnostic tests on top of 258 baseline)
- [x] Integration: previous + 2 new Sqlite tests proving runtime tx participation (commit + rollback paths)
- [x] Every existing snapshot byte-identical (the emit helper is a no-op when no tx parameter)
- [ ] CI build-test + aot-publish-smoke + collision-smoke

## Release

`feat:` commits trigger release-please for v1.5.0 (minor). Squash title MUST start with `feat:` (recurring release-please gotcha). After merge:

```powershell
gh workflow run pack-push.yml -f version=1.5.0
```

(release-please's GITHUB_TOKEN doesn't fire the pack-push trigger; established pattern from v1.2 / v1.3 / v1.4.)

## Downstream

ZA.Templates #162 will close via a follow-up PR that adopts the new parameter in za-clean's `OrderRepository`, adds the `Quantity > 0` CHECK constraint that was dropped from the abandoned templates branch, and adds the atomicity integration test.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
'@

gh pr create --title "feat: IAsyncDbTransaction parameter support on [Command]/[Query] partial methods (v1.5)" --body $prBody
```

Capture the PR number.

**Step 5: Monitor CI**

```powershell
gh pr checks <PR_NUMBER> --watch
```

Expected check set (same as recent ORM PRs): `lint`, `build-test`, `collision-smoke`, `aot-publish-smoke`. Wait for all green.

If a check fails, investigate before retrying. Most likely failure modes:
- AOT publish trimmer warning on the new help-link doc loading path (unlikely; ZAO080.md is a static MD file)
- Snapshot diff on Linux CI (CRLF/LF) — should be normalized by `.gitattributes`

**Step 6: Admin-merge once green**

```powershell
gh pr merge <PR_NUMBER> --squash --delete-branch --admin
```

**Critical**: squash *title* must start with `feat:`. The PR title `feat: IAsyncDbTransaction parameter support ...` already does — `gh pr merge --squash`'s default-to-PR-title behavior is correct.

**Step 7: Verify post-merge**

```powershell
git checkout main
git pull --ff-only
git log --oneline -3
```

Expected: new squashed `feat: IAsyncDbTransaction ...` commit on top of main.

**Step 8: Wait for release-please**

```powershell
Start-Sleep -Seconds 60
gh pr list --state open --search "release-please"
```

Expected: PR titled `chore(main): release 1.5.0`. Capture its number.

**Step 9: After release-please PR merges, trigger pack-push manually**

```powershell
# Run this AFTER the release-please PR is merged (user decides)
gh workflow run pack-push.yml -f version=1.5.0
```

(Not in this task — user-initiated step.)

**Step 10: Open follow-up ZA.Templates issue**

After v1.5.0 ships to NuGet, open a follow-up issue / PR in `ZA.Templates` referencing #162:
- Bump ZA.ORM pin from 1.2.0 → 1.5.0 in `Directory.Packages.props`
- Add `IAsyncDbTransaction tx` parameter to za-clean's `InsertOrderAsync` + `InsertOrderLineAsync`
- Rewrite `OrderRepository.AddAsync` to the clean shape
- Add `CHECK ("Quantity" > 0)` to both provider migrations
- Add the atomicity test
- Close #162

Out of scope for this task; capture as a follow-up todo.

---

## Out of scope (deliberately not in this plan)

- Templates adoption — separate ZA.Templates PR after v1.5.0 ships
- `IDbTransaction` (sync interface) support — only `IAsyncDbTransaction`
- Repository-level / ambient transaction scoping — non-goal
- Connection-lifecycle changes — adopters call `BeginTransactionAsync` themselves

## When the plan is complete

The branch `feat/orm-transaction-parameter` has 9 commits (1 design + 8 implementation) + the merge squash on main. release-please opens v1.5.0 release PR. After that merges, user runs `gh workflow run pack-push.yml -f version=1.5.0` to publish to NuGet. ZA.Templates #162 stays open, queued for a follow-up template PR.
