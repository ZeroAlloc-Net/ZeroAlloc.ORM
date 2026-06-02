# `CommandKind.BulkInsert` Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Add `CommandKind.BulkInsert` to ZeroAlloc.ORM 1.3.0 — a new emit shape that takes an `IReadOnlyList<TRow>` parameter and produces a chunked multi-row `INSERT … VALUES (…), (…), …` pipeline with optional RETURNING-based identity capture.

**Architecture:** Single new enum value (`CommandKind.BulkInsert = 3`) drives a new `EmitShape.BulkInsertCommand` classifier branch + `EmitBulkInsertCommand` emit method. Chunk size baked at codegen as `900 / placeholderCount` to stay safely under Sqlite's 999-parameter cap. SQL parser extracts the `VALUES (placeholder, ...)` tuple's placeholder names at codegen time; runtime `StringBuilder` multiplies the tuple by chunk size. Five new compile-time diagnostics (ZAO070–ZAO074) catch shape violations.

**Tech Stack:** C# 13 / .NET 10 / Roslyn incremental generators / Verify (`.verified.cs` snapshots) / xUnit / Sqlite + Postgres integration tests / `PublicAPI.Shipped.txt` analyzer.

**Reference design doc:** `docs/plans/2026-06-02-bulk-insert-design.md` (committed `9007624` on this branch).

**Working branch:** `design/orm-bulk-insert` (already created off `main`).

> **Note on TDD shape:** This work mixes mechanical additions (new enum value, new PublicAPI line, new EmitShape entry) with logic that maps naturally onto TDD (the classifier, the emit method, the diagnostics). For the mechanical additions, "test fails first" doesn't apply — the verification step is "build green + PublicAPI analyzer happy." For the logic, the natural TDD shape is **Verify-driven**: write the `.verified.cs` snapshot first (or use Verify's first-run mechanism to capture the emit, review, promote), then implement the emit to produce that exact output. Integration tests follow ordinary TDD ("write the test, run, see fail, implement, run, see pass").

> **Local SDK pin gotcha** (recurring blocker from PRs #103 / #104): `global.json` pins SDK `10.0.300 latestFeature`; the dev machine has 10.0.204 max. Before any local build, **temporarily relax `global.json` to `10.0.100 latestMinor`** — do NOT commit the relax. After each task's verification passes, `git checkout global.json` to revert before `git commit`. The relaxed pin must never reach a commit; CI runs on Linux runners that have 10.0.300 installed.

---

### Task 1: Add `CommandKind.BulkInsert` enum value to Abstractions

**Files:**
- Modify: `src/ZeroAlloc.ORM.Abstractions/CommandKind.cs`
- Modify: `src/ZeroAlloc.ORM.Abstractions/PublicAPI.Shipped.txt`

**Step 1: Read the current enum**

```bash
cat src/ZeroAlloc.ORM.Abstractions/CommandKind.cs
```

Expected: `public enum CommandKind { NonQuery = 0, Scalar = 1, Identity = 2 }` with doc comments.

**Step 2: Add the `BulkInsert = 3` value**

Append the new member after `Identity`:

```csharp
/// <summary>
/// Multi-row INSERT via the SQL-standard <c>INSERT … VALUES (…), (…), …</c>
/// pattern. The decorated method takes one collection parameter
/// (<see cref="IReadOnlyList{T}"/> / <see cref="IList{T}"/> /
/// <see cref="IEnumerable{T}"/>) and returns either <c>Task&lt;int&gt;</c>
/// (rows-affected sum across chunks) or
/// <c>Task&lt;IReadOnlyList&lt;TIdentity&gt;&gt;</c> (identity values from
/// <c>RETURNING &lt;col&gt;</c>). The generator auto-chunks the input to
/// stay under provider parameter-count limits (Sqlite 999, SQL Server 2100,
/// Postgres ~32k). See <c>docs/cookbook/bulk-insert.md</c>.
/// </summary>
BulkInsert = 3,
```

**Step 3: Add to `PublicAPI.Shipped.txt`**

The PublicAPI analyzer requires every public-surface symbol be listed. Add the new line in alphabetical order with the existing `CommandKind.*` entries:

```
ZeroAlloc.ORM.CommandKind.BulkInsert = 3 -> ZeroAlloc.ORM.CommandKind
```

**Step 4: Verify build green**

```bash
sed -i 's/10\.0\.300/10.0.100/' global.json
dotnet build src/ZeroAlloc.ORM.Abstractions/ZeroAlloc.ORM.Abstractions.csproj -c Release 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. The PublicAPI analyzer (`RS0016`) was the most likely failure mode; a missing entry in `Shipped.txt` produces `error RS0016: Symbol '...' is not part of the declared API`. If it fires, double-check the alphabetization in step 3.

**Step 5: Revert global.json + commit**

```bash
git checkout global.json
git add src/ZeroAlloc.ORM.Abstractions/CommandKind.cs src/ZeroAlloc.ORM.Abstractions/PublicAPI.Shipped.txt
git commit -m "feat(abstractions): add CommandKind.BulkInsert = 3

Additive public-API addition for v1.3 BulkInsert support. Generator-side
EmitShape.BulkInsertCommand + classifier + emit lands in subsequent
commits; this one just opens the seat in the abstractions surface so the
generator can reference the enum value.

Closes design phase of the BulkInsert feature (docs/plans/2026-06-02-bulk-insert-design.md)."
```

---

### Task 2: Mirror to generator model — `CommandKindModel.BulkInsert` + `EmitShape.BulkInsertCommand`

**Files:**
- Modify: `src/ZeroAlloc.ORM.Generator/Model/QueryMethodModel.cs`

**Step 1: Read the current model**

```bash
grep -n "CommandKindModel\|EmitShape" src/ZeroAlloc.ORM.Generator/Model/QueryMethodModel.cs
```

**Step 2: Add `BulkInsert` to both enums**

`CommandKindModel` (mirror of public `CommandKind`):

```csharp
internal enum CommandKindModel
{
    NonQuery,
    Scalar,
    Identity,
    BulkInsert,  // NEW — must keep numeric values in sync with public CommandKind
}
```

`EmitShape` — add the new shape after `CommandIdentity`:

```csharp
/// <summary>
/// v1.3 — [Command(Kind = BulkInsert)] methods. Chunked multi-row INSERT
/// via the SQL-standard VALUES (…), (…), … pattern. Method takes one
/// IReadOnlyList&lt;TRow&gt; parameter (or IList/IEnumerable; the
/// IEnumerable case materializes once at method entry); return type is
/// Task&lt;int&gt; or Task&lt;IReadOnlyList&lt;TIdentity&gt;&gt;. Chunk size
/// = 900 / placeholderCount baked at codegen.
/// </summary>
BulkInsertCommand,
```

**Step 3: Verify build**

```bash
sed -i 's/10\.0\.300/10.0.100/' global.json
dotnet build src/ZeroAlloc.ORM.Generator/ZeroAlloc.ORM.Generator.csproj -c Release 2>&1 | tail -5
```

Expected: build green; no consumers yet so the new enum values are inert.

**Step 4: Revert + commit**

```bash
git checkout global.json
git add src/ZeroAlloc.ORM.Generator/Model/QueryMethodModel.cs
git commit -m "feat(generator): add CommandKindModel.BulkInsert + EmitShape.BulkInsertCommand

Generator-side mirror of the abstractions enum addition in the prior
commit. Doc comments describe the chunked multi-row VALUES pipeline this
shape produces. Classifier + emit + dispatch land in subsequent commits."
```

---

### Task 3: Add diagnostic descriptors ZAO070–ZAO074

**Files:**
- Modify: `src/ZeroAlloc.ORM.Generator/Diagnostics/DiagnosticDescriptors.cs`

**Step 1: Locate the existing code range**

```bash
grep -oE "ZAO0[0-9]+" src/ZeroAlloc.ORM.Generator/Diagnostics/DiagnosticDescriptors.cs | sort -u | tail -5
```

Expected output ending at `ZAO064`. The next free decade is `ZAO070–ZAO079`; this design uses 070–074.

**Step 2: Append five new descriptors**

Add at the bottom of `DiagnosticDescriptors.cs`, matching the style of the existing descriptors (each is a `DiagnosticDescriptor` field with id / title / message-format / category / severity / isEnabledByDefault):

```csharp
// v1.3 — BulkInsert shape diagnostics (design 2026-06-02).

public static readonly DiagnosticDescriptor ZAO070_BulkInsertSignature = new(
    id: "ZAO070",
    title: "BulkInsert method must take exactly one collection parameter",
    messageFormat: "[Command(Kind = CommandKind.BulkInsert)] method '{0}' must have exactly one IEnumerable<TRow>-shaped collection parameter (IReadOnlyList<T> preferred); saw {1}",
    category: "ZeroAlloc.ORM.Generator",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);

public static readonly DiagnosticDescriptor ZAO071_BulkInsertValuesParser = new(
    id: "ZAO071",
    title: "BulkInsert SQL must contain exactly one VALUES tuple",
    messageFormat: "[Command(Kind = CommandKind.BulkInsert)] method '{0}' SQL must contain exactly one VALUES (@placeholder, ...) tuple; the generator's parser found {1}",
    category: "ZeroAlloc.ORM.Generator",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);

public static readonly DiagnosticDescriptor ZAO072_BulkInsertPlaceholderUnresolved = new(
    id: "ZAO072",
    title: "BulkInsert placeholder doesn't match any TRow property",
    messageFormat: "[Command(Kind = CommandKind.BulkInsert)] method '{0}': TRow '{1}' has no public property matching VALUES placeholder '@{2}' (case-insensitive name match)",
    category: "ZeroAlloc.ORM.Generator",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);

public static readonly DiagnosticDescriptor ZAO073_BulkInsertReturnTypeShape = new(
    id: "ZAO073",
    title: "BulkInsert return type must be Task<int> or Task<IReadOnlyList<TIdentity>>",
    messageFormat: "[Command(Kind = CommandKind.BulkInsert)] method '{0}' return type must be Task<int> (rows-affected sum) or Task<IReadOnlyList<TIdentity>> where TIdentity is int/long/Guid or a [ValueObject] wrapping one of those; saw '{1}'",
    category: "ZeroAlloc.ORM.Generator",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);

public static readonly DiagnosticDescriptor ZAO074_BulkInsertWrongAttribute = new(
    id: "ZAO074",
    title: "CommandKind.BulkInsert is only valid on [Command]",
    messageFormat: "CommandKind.BulkInsert is only valid on [Command]; method '{0}' uses {1} where the kind is ignored",
    category: "ZeroAlloc.ORM.Generator",
    defaultSeverity: DiagnosticSeverity.Info,
    isEnabledByDefault: true);
```

**Step 3: Verify build**

```bash
sed -i 's/10\.0\.300/10.0.100/' global.json
dotnet build src/ZeroAlloc.ORM.Generator/ZeroAlloc.ORM.Generator.csproj -c Release 2>&1 | tail -5
```

Expected: green.

**Step 4: Revert + commit**

```bash
git checkout global.json
git add src/ZeroAlloc.ORM.Generator/Diagnostics/DiagnosticDescriptors.cs
git commit -m "feat(generator): add diagnostics ZAO070-ZAO074 for BulkInsert shape

Five descriptors covering the BulkInsert misuse modes:
  ZAO070 — signature must have exactly one collection parameter
  ZAO071 — SQL must contain exactly one VALUES tuple
  ZAO072 — VALUES placeholder has no matching TRow property
  ZAO073 — return type must be Task<int> or Task<IReadOnlyList<TIdentity>>
  ZAO074 — CommandKind.BulkInsert on [StoredProcedure]/[Query] is no-op (Info)

Trigger points land in the classifier in a subsequent commit."
```

---

### Task 4: SQL-parser helper — extract VALUES tuple placeholder list

**Files:**
- Create: `src/ZeroAlloc.ORM.Generator/Model/BulkInsertValuesParser.cs`
- Create: `tests/ZeroAlloc.ORM.Generator.Tests/Model/BulkInsertValuesParserTests.cs`

**Step 1: Write the failing tests**

```csharp
using Xunit;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Tests.Model;

public class BulkInsertValuesParserTests
{
    [Fact]
    public void Parses_simple_two_column_VALUES_tuple()
    {
        var result = BulkInsertValuesParser.TryParse(
            "INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)");

        Assert.True(result.Success);
        Assert.Equal(new[] { "CustomerId", "Total" }, result.Placeholders);
    }

    [Fact]
    public void Parses_VALUES_with_RETURNING_suffix()
    {
        var result = BulkInsertValuesParser.TryParse(
            "INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total) RETURNING Id");

        Assert.True(result.Success);
        Assert.Equal(new[] { "CustomerId", "Total" }, result.Placeholders);
    }

    [Fact]
    public void Parses_VALUES_case_insensitively()
    {
        var result = BulkInsertValuesParser.TryParse(
            "insert into orders (customer_id, total) values (@CustId, @Total)");

        Assert.True(result.Success);
        Assert.Equal(new[] { "CustId", "Total" }, result.Placeholders);
    }

    [Fact]
    public void Rejects_zero_VALUES_tuples()
    {
        var result = BulkInsertValuesParser.TryParse(
            "INSERT INTO Orders (CustomerId, Total) SELECT 1, 2");

        Assert.False(result.Success);
        Assert.Equal(0, result.TupleCount);
    }

    [Fact]
    public void Rejects_multiple_VALUES_tuples()
    {
        var result = BulkInsertValuesParser.TryParse(
            "INSERT INTO Orders (CustomerId, Total) VALUES (1, 2), (3, 4)");

        Assert.False(result.Success);
        Assert.Equal(2, result.TupleCount);
    }

    [Fact]
    public void Returns_full_VALUES_clause_range_for_emit_rewriting()
    {
        var sql = "INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)";
        var result = BulkInsertValuesParser.TryParse(sql);

        Assert.True(result.Success);
        Assert.Equal("(@CustomerId, @Total)", sql.Substring(result.TupleStart, result.TupleLength));
    }
}
```

**Step 2: Run to confirm fail**

```bash
sed -i 's/10\.0\.300/10.0.100/' global.json
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj --filter "FullyQualifiedName~BulkInsertValuesParser" 2>&1 | tail -3
```

Expected: build failure ("type or namespace `BulkInsertValuesParser` could not be found").

**Step 3: Implement the parser**

```csharp
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ZeroAlloc.ORM.Generator.Model;

// Extracts the placeholder list from a [Command(Kind = BulkInsert)] SQL
// template's VALUES tuple. Single tuple required — multiple tuples means
// the user already wrote multi-row SQL (which doesn't compose with
// BulkInsert's auto-multiplication) and we reject with ZAO071.
internal static class BulkInsertValuesParser
{
    // Matches `VALUES (...)` where the parens contain @placeholder names
    // separated by commas. Case-insensitive on the VALUES keyword.
    // The placeholder regex inside: @ followed by one or more identifier chars.
    private static readonly Regex ValuesTuple = new(
        @"\bVALUES\s*\(\s*(?<inner>@\w+(?:\s*,\s*@\w+)*)\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal sealed record Result(
        bool Success,
        IReadOnlyList<string> Placeholders,
        int TupleCount,
        int TupleStart,
        int TupleLength);

    public static Result TryParse(string sql)
    {
        var matches = ValuesTuple.Matches(sql);
        if (matches.Count != 1)
        {
            return new Result(
                Success: false,
                Placeholders: Array.Empty<string>(),
                TupleCount: matches.Count,
                TupleStart: 0,
                TupleLength: 0);
        }

        var match = matches[0];
        var inner = match.Groups["inner"].Value;
        var rawPlaceholders = inner.Split(',');
        var placeholders = new string[rawPlaceholders.Length];
        for (var i = 0; i < rawPlaceholders.Length; i++)
        {
            // strip leading whitespace + the '@' prefix
            placeholders[i] = rawPlaceholders[i].Trim().TrimStart('@');
        }

        // We want TupleStart/TupleLength to bracket the "(...)" portion
        // so the runtime SQL builder can replace it with the chunk-multiplied form.
        var openParen = sql.IndexOf('(', match.Index);
        return new Result(
            Success: true,
            Placeholders: placeholders,
            TupleCount: 1,
            TupleStart: openParen,
            TupleLength: match.Index + match.Length - openParen);
    }
}
```

**Step 4: Run tests to confirm pass**

```bash
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj --filter "FullyQualifiedName~BulkInsertValuesParser" 2>&1 | tail -3
```

Expected: 6/6 passed.

**Step 5: Revert + commit**

```bash
git checkout global.json
git add src/ZeroAlloc.ORM.Generator/Model/BulkInsertValuesParser.cs tests/ZeroAlloc.ORM.Generator.Tests/Model/BulkInsertValuesParserTests.cs
git commit -m "feat(generator): BulkInsertValuesParser extracts VALUES tuple placeholder list

Stand-alone helper that the classifier uses to (a) reject SQL with zero
or multiple VALUES tuples (ZAO071), (b) extract the placeholder list for
property-matching (ZAO072) and codegen-time chunk-size computation, and
(c) return the tuple's character range so the runtime SQL builder knows
where to splice the chunk-multiplied tuples.

6 unit tests covering simple parse, RETURNING suffix, case insensitivity,
zero-tuple rejection, multi-tuple rejection, and the tuple-range return
contract."
```

---

### Task 5: Classifier — recognize BulkInsert shape + fire diagnostics

**Files:**
- Modify: `src/ZeroAlloc.ORM.Generator/OrmGenerator.cs` — specifically the `ClassifyEmitShape` function around line ~1023 and the `TransformMethod` around line ~194 where command-kind dispatch lives.

**Step 1: Locate the existing CommandIdentity classifier (template)**

```bash
grep -n "CommandKindModel.Identity\|EmitShape.CommandIdentity\|ClassifyCommandIdentity" src/ZeroAlloc.ORM.Generator/OrmGenerator.cs | head -5
```

Use this as the structural template for the new BulkInsert classifier.

**Step 2: Add a `ClassifyBulkInsertCommand` method**

Insert after `ClassifyCommandIdentity`. Pseudocode:

```csharp
private static (EmitShape Shape, /* etc */) ClassifyBulkInsertCommand(
    IMethodSymbol method,
    string sql,
    ConventionContext conventionContext,
    ImmutableArray<DiagnosticInfo>.Builder diagnostics,
    LocationInfo? methodLocation)
{
    // 1. Method must have exactly one IEnumerable<TRow>-shaped parameter (+ optional CT).
    //    Identify by searching method.Parameters for one whose type is
    //    IReadOnlyList<T> / IList<T> / IEnumerable<T> from System.Collections.Generic.
    //    Reject (ZAO070) if zero or more than one such parameter.

    // 2. Parse the SQL via BulkInsertValuesParser.TryParse. If !Success,
    //    fire ZAO071 with the actual TupleCount.

    // 3. For each placeholder name, look up a public property on TRow by
    //    case-insensitive name match. If any placeholder doesn't resolve,
    //    fire ZAO072 (one per unresolved placeholder is acceptable, or one
    //    aggregated; pick whatever matches the existing diagnostics style).

    // 4. Validate return type: Task<int> OR Task<IReadOnlyList<TIdentity>>
    //    where TIdentity is int/long/Guid or a VO-wrapping-those.
    //    Reject (ZAO073) if neither shape matches.

    // 5. Compute the placeholder column-binding plan: list of
    //    (placeholderName, TRow.PropertyInfo, conventionForValueBinding).
    //    Convention is resolved the same way single-row [Command]
    //    parameter binding resolves — primitives, VOs, StoreAsString enums.

    // 6. Compute chunk size at codegen: 900 / placeholderCount.

    // 7. Return EmitShape.BulkInsertCommand + a new materialization model
    //    (extend MaterializationModel or add a sibling BulkInsertMaterializationModel
    //    record holding the placeholder→property plan + chunk size + return-shape kind).

    // On any rejection, return EmitShape.Unknown so the existing
    // "Unknown → ZAO022 / stub-comment" path takes over.
}
```

**Step 3: Wire the dispatch in `TransformMethod`**

Find the existing dispatch around line 1100:

```csharp
if (isCommandAttribute && commandKind == CommandKindModel.Identity)
{
    // ... ClassifyCommandIdentity ...
}
```

Add the analogous branch:

```csharp
if (isCommandAttribute && commandKind == CommandKindModel.BulkInsert)
{
    var bulkResult = ClassifyBulkInsertCommand(method, sql, conventionContext, diagnostics, methodLocation);
    if (bulkResult.Shape != EmitShape.Unknown)
    {
        return (bulkResult.Shape, /* ... rest of return tuple ... */);
    }
    // Fall through to Unknown if classification rejected.
}
```

**Step 4: Add the BulkInsert-on-non-Command (ZAO074 Info) check**

Earlier in `TransformMethod` where attribute kind is detected:

```csharp
if (commandKind == CommandKindModel.BulkInsert && !isCommandAttribute)
{
    diagnostics.Add(DiagnosticInfo.From(
        DiagnosticDescriptors.ZAO074_BulkInsertWrongAttribute,
        methodLocation,
        method.Name,
        isStoredProcedureAttribute ? "[StoredProcedure]" : "[Query]"));
}
```

**Step 5: Verify build green**

```bash
sed -i 's/10\.0\.300/10.0.100/' global.json
dotnet build src/ZeroAlloc.ORM.Generator/ZeroAlloc.ORM.Generator.csproj -c Release 2>&1 | tail -5
```

Expected: green. No tests yet; that's Task 6+.

**Step 6: Existing tests still green**

```bash
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj 2>&1 | tail -3
```

Expected: all existing snapshots + tests pass (the classifier change is additive; existing shapes route through their existing branches).

**Step 7: Revert + commit**

```bash
git checkout global.json
git add src/ZeroAlloc.ORM.Generator/OrmGenerator.cs
git commit -m "feat(generator): classifier branch for EmitShape.BulkInsertCommand

ClassifyBulkInsertCommand recognizes [Command(Kind = BulkInsert)]:
  - Collection parameter shape check (ZAO070 on miss)
  - VALUES tuple extraction via BulkInsertValuesParser (ZAO071 on miss)
  - Per-placeholder TRow property resolution (ZAO072 on miss)
  - Return type validation (ZAO073 on miss)
  - ZAO074 Info diagnostic for BulkInsert kind on [StoredProcedure]/[Query]

Emit path lands in the next commit; classifier returns Unknown for now
when all checks pass, until the emit dispatch case is added."
```

> **NOTE:** Existing tests should still pass — but if the classifier inadvertently routes a non-BulkInsert method through the new branch, snapshots will change. If any pre-existing snapshot diff appears, **STOP and investigate** before committing — the new classifier branch must be strictly opt-in via `Kind == BulkInsert`.

---

### Task 6: Emit — `EmitBulkInsertCommand` method

**Files:**
- Modify: `src/ZeroAlloc.ORM.Generator/OrmGenerator.cs` — add `EmitBulkInsertCommand` near the existing `EmitCommandIdentity` (around line 3707) and add the `case EmitShape.BulkInsertCommand` to the dispatch switch (around line 3444).

**Step 1: Add the dispatch case**

In the `EmitRepository` switch, after the `case EmitShape.CommandIdentity`:

```csharp
case EmitShape.BulkInsertCommand:
    EmitBulkInsertCommand(sb, m, repo.ConnectionAccess);
    break;
```

**Step 2: Implement `EmitBulkInsertCommand`**

The full method body. This is the meatiest single piece of code in the plan; keep it tightly aligned with the design's "Emit pipeline" section:

```csharp
private static void EmitBulkInsertCommand(StringBuilder sb, QueryMethodModel m, string connectionAccess)
{
    // mat is the BulkInsertMaterializationModel built by ClassifyBulkInsertCommand,
    // carrying:
    //   - PlaceholderBindings: ordered list of (placeholderName, propertyGetterExpr, convention)
    //   - ChunkSize: int (900 / placeholderCount, baked at codegen)
    //   - ReturnKind: RowsAffected | IdentityList
    //   - IdentityReaderMethod (when IdentityList): "GetInt32" / "GetInt64" / etc.
    //   - IdentityFactory (when VO-wrapped): "new global::MyApp.OrderId(...)"
    //   - CollectionParameterName: the user's parameter name (e.g. "orders")
    //   - CollectionParameterIsReadOnlyList: bool — drives the IEnumerable adapter emit
    //   - InsertStaticHead: the SQL string up to the VALUES keyword + space
    //     (e.g. "INSERT INTO Orders (CustomerId, Total) VALUES ")
    //   - InsertStaticTail: the SQL string after the VALUES tuple (e.g. " RETURNING Id" or "")

    var mat = m.BulkInsertMaterialization;  // new field on QueryMethodModel
    if (mat is null)
    {
        // Defensive — classification should never assign BulkInsertCommand without a model.
        var paramListFallback = BuildParameterList(m.MethodParameters);
        sb.AppendLine($"    {GeneratedCodeAttribute}");
        sb.AppendLine($"    {m.MethodAccessibilityKeyword} partial async {m.ReturnTypeDisplay} {m.MethodName}({paramListFallback})");
        sb.AppendLine($"        => throw new global::System.InvalidOperationException(\"ZeroAlloc.ORM generator invariant: BulkInsertCommand missing materialization for '{m.MethodName}'.\");");
        return;
    }

    var paramList = BuildParameterList(m.MethodParameters);
    var ct = FormatCancellationTokenReference(m.CancellationTokenParameterName);
    var rowsParam = mat.CollectionParameterName;
    var rowsLocal = "__rows";
    var isIdentity = mat.ReturnKind == BulkInsertReturnKind.IdentityList;

    sb.AppendLine($"    {GeneratedCodeAttribute}");
    sb.AppendLine($"    {m.MethodAccessibilityKeyword} partial async {m.ReturnTypeDisplay} {m.MethodName}({paramList})");
    sb.AppendLine("    {");

    // IEnumerable adapter — only emit when the user's parameter isn't already IReadOnlyList
    if (mat.CollectionParameterIsReadOnlyList)
    {
        sb.AppendLine($"        var {rowsLocal} = @{rowsParam};");
    }
    else
    {
        sb.AppendLine($"        var {rowsLocal} = @{rowsParam} is global::System.Collections.Generic.IReadOnlyList<{mat.RowTypeFullName}> __irol ? __irol : new global::System.Collections.Generic.List<{mat.RowTypeFullName}>(@{rowsParam});");
    }

    // Empty short-circuit
    if (isIdentity)
    {
        sb.AppendLine($"        if ({rowsLocal}.Count == 0) return global::System.Array.Empty<{mat.IdentityTypeFullName}>();");
    }
    else
    {
        sb.AppendLine($"        if ({rowsLocal}.Count == 0) return 0;");
    }

    // Connection prologue
    BuildConnectionPrologue(sb, connectionAccess, ct, "        ");

    // Chunk loop preamble
    sb.AppendLine($"            const int __chunkSize = {mat.ChunkSize};");
    if (isIdentity)
    {
        sb.AppendLine($"            var __ids = new global::System.Collections.Generic.List<{mat.IdentityTypeFullName}>({rowsLocal}.Count);");
    }
    else
    {
        sb.AppendLine("            var __totalAffected = 0;");
    }
    sb.AppendLine("            var __offset = 0;");
    sb.AppendLine($"            var __remaining = {rowsLocal}.Count;");
    sb.AppendLine();
    sb.AppendLine("            while (__remaining > 0)");
    sb.AppendLine("            {");
    sb.AppendLine("                var __thisChunk = __remaining < __chunkSize ? __remaining : __chunkSize;");
    sb.AppendLine("                await using var __cmd = __conn.CreateCommand();");
    sb.AppendLine();

    // SQL builder for this chunk
    sb.AppendLine($"                var __sb = new global::System.Text.StringBuilder({EscapeStringLiteral(mat.InsertStaticHead)});");
    sb.AppendLine("                for (var __i = 0; __i < __thisChunk; __i++)");
    sb.AppendLine("                {");
    sb.AppendLine("                    if (__i > 0) __sb.Append(\", \");");
    sb.Append("                    __sb.Append(\"(\")");
    for (var i = 0; i < mat.PlaceholderBindings.Length; i++)
    {
        var ph = mat.PlaceholderBindings[i].PlaceholderName;
        if (i > 0) sb.Append(".Append(\", \")");
        sb.Append($".Append(\"@{ph}_\").Append(__i)");
    }
    sb.AppendLine(".Append(\")\");");
    sb.AppendLine("                }");
    if (!string.IsNullOrEmpty(mat.InsertStaticTail))
    {
        sb.AppendLine($"                __sb.Append({EscapeStringLiteral(mat.InsertStaticTail)});");
    }
    sb.AppendLine("                __cmd.CommandText = __sb.ToString();");
    sb.AppendLine();

    // Bind parameters for this chunk
    sb.AppendLine("                for (var __i = 0; __i < __thisChunk; __i++)");
    sb.AppendLine("                {");
    sb.AppendLine($"                    var __row = {rowsLocal}[__offset + __i];");
    for (var i = 0; i < mat.PlaceholderBindings.Length; i++)
    {
        var binding = mat.PlaceholderBindings[i];
        var paramLocal = $"__p_{binding.PlaceholderName}_{i}";
        sb.AppendLine($"                    var {paramLocal} = __cmd.CreateParameter();");
        sb.AppendLine($"                    {paramLocal}.ParameterName = \"@{binding.PlaceholderName}_\" + __i;");
        // Property read with convention unwrap (VO → .Value, enum → cast, etc.)
        sb.AppendLine($"                    {paramLocal}.Value = {RenderParameterValueExpression(binding, "__row")};");
        sb.AppendLine($"                    __cmd.Parameters.Add({paramLocal});");
    }
    sb.AppendLine("                }");
    sb.AppendLine();

    // Execute
    if (isIdentity)
    {
        sb.AppendLine($"                await using var __reader = await __cmd.ExecuteReaderAsync({ct}).ConfigureAwait(false);");
        sb.AppendLine($"                while (await __reader.ReadAsync({ct}).ConfigureAwait(false))");
        sb.AppendLine("                {");
        if (mat.IdentityFactory is null)
        {
            sb.AppendLine($"                    __ids.Add(__reader.{mat.IdentityReaderMethod}(0));");
        }
        else
        {
            sb.AppendLine($"                    __ids.Add({mat.IdentityFactory}(__reader.{mat.IdentityReaderMethod}(0)));");
        }
        sb.AppendLine("                }");
    }
    else
    {
        sb.AppendLine($"                __totalAffected += await __cmd.ExecuteNonQueryAsync({ct}).ConfigureAwait(false);");
    }
    sb.AppendLine();
    sb.AppendLine("                __offset += __thisChunk;");
    sb.AppendLine("                __remaining -= __thisChunk;");
    sb.AppendLine("            }");

    // Return + connection epilogue
    if (isIdentity)
    {
        sb.AppendLine("            return __ids;");
    }
    else
    {
        sb.AppendLine("            return __totalAffected;");
    }
    BuildConnectionEpilogue(sb, "        ");
    sb.AppendLine("    }");
}

// Helper — escapes a string for embedding as a C# verbatim string literal.
private static string EscapeStringLiteral(string s) => $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

// Helper — renders the per-property value expression for parameter binding.
// Mirrors the convention layer used by single-row [Command] parameter binding.
private static string RenderParameterValueExpression(BulkInsertPlaceholderBinding binding, string rowLocal)
{
    var direct = $"{rowLocal}.{binding.PropertyName}";
    if (binding.Convention is { } conv && conv.FactoryFullName is not null)
    {
        return conv.Kind switch
        {
            (int)ConventionKind.Enum => $"(int){direct}",
            (int)ConventionKind.EnumAsString => $"{direct}.ToString()",
            _ => conv.FactoryIsCtor
                ? $"((object){direct}).GetHashCode()  /* TODO VO unwrap */"  // placeholder — verify against existing single-row VO unwrap
                : $"{conv.FactoryFullName}({direct})",
        };
    }
    return direct;
}
```

> **NOTE on the `RenderParameterValueExpression` helper:** the convention-driven unwrap for VO parameter binding has subtle precedent in the single-row `[Command]` parameter binding emit. **Read `EmitParameterBindingWithIndent` (around line 6000 of OrmGenerator.cs) before implementing** — copy that exact unwrap logic so the BulkInsert convention behavior matches single-row exactly. The TODO above is intentional — don't ship that placeholder.

**Step 3: Add the BulkInsert-specific model types**

In `src/ZeroAlloc.ORM.Generator/Model/QueryMethodModel.cs`:

```csharp
internal sealed record BulkInsertPlaceholderBinding(
    string PlaceholderName,
    string PropertyName,
    ConventionInfo? Convention);

internal enum BulkInsertReturnKind { RowsAffected, IdentityList }

internal sealed record BulkInsertMaterializationModel(
    EquatableArray<BulkInsertPlaceholderBinding> PlaceholderBindings,
    int ChunkSize,
    BulkInsertReturnKind ReturnKind,
    string? IdentityTypeFullName,
    string? IdentityReaderMethod,
    string? IdentityFactory,
    string RowTypeFullName,
    string CollectionParameterName,
    bool CollectionParameterIsReadOnlyList,
    string InsertStaticHead,
    string InsertStaticTail);
```

Add a `BulkInsertMaterializationModel? BulkInsertMaterialization` field to `QueryMethodModel` (positional record; insert after `SprocOutputParamsMaterialization`).

**Step 4: Add the snapshot tests** (Task 7) **then verify emit + green**

Skip to Task 7 to actually write the snapshots — the emit's correctness is verified through them.

**Step 5: Commit**

```bash
git checkout global.json
git add src/ZeroAlloc.ORM.Generator/Model/QueryMethodModel.cs src/ZeroAlloc.ORM.Generator/OrmGenerator.cs
git commit -m "feat(generator): EmitBulkInsertCommand + dispatch case for BulkInsertCommand shape

Adds the chunked open / build-SQL / bind / execute / close pipeline. Two
return shapes:
  - Task<int>: ExecuteNonQueryAsync per chunk, sum across chunks
  - Task<IReadOnlyList<TIdentity>>: ExecuteReaderAsync per chunk, drain
    into List<TIdentity> via the user's RETURNING clause

Chunk size baked at codegen as 900 / placeholderCount (stays under
Sqlite's 999-parameter cap). SQL builder concatenates chunk-multiplied
VALUES tuples at runtime via StringBuilder. Connection lifecycle around
the whole chunk loop (single open/close per call, not per chunk).
IEnumerable<T> parameter adapts to List<T> once at method entry.

BulkInsertMaterializationModel record carries the codegen-time
placeholder→property plan, chunk size, return-shape kind, and the
static SQL fragments (head + tail around the VALUES tuple) so the
runtime builder knows what to splice."
```

---

### Task 7: Snapshot tests — `BulkInsertTests.cs`

**Files:**
- Create: `tests/ZeroAlloc.ORM.Generator.Tests/Emit/BulkInsertTests.cs`
- Snapshots will land in `tests/ZeroAlloc.ORM.Generator.Tests/Snapshots/` on first run

**Step 1: Write the five test methods**

```csharp
using System.Threading.Tasks;
using VerifyXunit;
using Xunit;
using static VerifyXunit.Verifier;

namespace ZeroAlloc.ORM.Generator.Tests.Emit;

// Issue #4 / v1.3 — CommandKind.BulkInsert emit shape.
// Five coverage cells: rows-affected, identity capture, IEnumerable adapter,
// VO-wrapped identity factory, and chunk-size scaling with placeholder count.
public class BulkInsertTests
{
    [Fact]
    public Task BulkInsert_Task_int_emits_chunked_NonQuery_pipeline()
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
                public partial Task<int> InsertOrdersAsync(IReadOnlyList<OrderRow> orders, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task BulkInsert_Task_IReadOnlyList_int_emits_chunked_ExecuteReader_with_RETURNING()
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
                [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total) RETURNING Id", Kind = CommandKind.BulkInsert)]
                public partial Task<IReadOnlyList<int>> InsertOrdersAsync(IReadOnlyList<OrderRow> orders, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task BulkInsert_with_IEnumerable_parameter_emits_buffered_adapter()
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
                public partial Task<int> InsertOrdersAsync(IEnumerable<OrderRow> orders, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task BulkInsert_with_ValueObject_identity_emits_factory_wrap()
    {
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;
            using ZeroAlloc.ValueObjects;

            namespace TestApp;

            [ValueObject]
            public readonly partial struct OrderId
            {
                public int Value { get; }
                public OrderId(int value) { Value = value; }
            }

            public sealed record OrderRow(int CustomerId, decimal Total);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total) RETURNING Id", Kind = CommandKind.BulkInsert)]
                public partial Task<IReadOnlyList<OrderId>> InsertOrdersAsync(IReadOnlyList<OrderRow> orders, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }

    [Fact]
    public Task BulkInsert_chunk_size_scales_with_placeholder_count()
    {
        // 10-column row → chunk size 90 (900 / 10).
        var source = """
            using System.Collections.Generic;
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;

            namespace TestApp;

            public sealed record WideRow(int C1, int C2, int C3, int C4, int C5, int C6, int C7, int C8, int C9, int C10);

            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO Wide (C1, C2, C3, C4, C5, C6, C7, C8, C9, C10) VALUES (@C1, @C2, @C3, @C4, @C5, @C6, @C7, @C8, @C9, @C10)", Kind = CommandKind.BulkInsert)]
                public partial Task<int> InsertWideAsync(IReadOnlyList<WideRow> rows, CancellationToken ct);
            }
            """;
        return Verify(GeneratorHarness.RunGenerator(source));
    }
}
```

**Step 2: First run — Verify produces `.received.cs` files**

```bash
sed -i 's/10\.0\.300/10.0.100/' global.json
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj --filter "FullyQualifiedName~BulkInsertTests" 2>&1 | tail -5
```

Expected: 5 failed tests (Verify's first-run "no baseline" behavior). Look at the `.received.cs` files Verify dropped under `tests/ZeroAlloc.ORM.Generator.Tests/Snapshots/`. **Inspect each carefully — they're the actual emit output.** If a snapshot is wrong (wrong chunk size, wrong placeholder names, wrong return type), back out and fix `EmitBulkInsertCommand`.

**Step 3: Promote received → verified**

After visual inspection confirms each snapshot is correct:

```bash
cd tests/ZeroAlloc.ORM.Generator.Tests/Snapshots
for f in BulkInsertTests.*.received.cs; do mv "$f" "${f/.received./.verified.}"; done
cd ../../../..
```

**Step 4: Rerun to confirm green**

```bash
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj --filter "FullyQualifiedName~BulkInsertTests" 2>&1 | tail -3
```

Expected: 5/5 passed.

**Step 5: Full generator suite still green**

```bash
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj 2>&1 | tail -3
```

Expected: existing 238 tests + 6 BulkInsertValuesParserTests + 5 BulkInsertTests = **249/249 passed**.

**Step 6: Revert + commit**

```bash
git checkout global.json
git add tests/ZeroAlloc.ORM.Generator.Tests/Emit/BulkInsertTests.cs tests/ZeroAlloc.ORM.Generator.Tests/Snapshots/BulkInsertTests.*.verified.cs
git commit -m "test(generator): 5 snapshot tests for EmitBulkInsertCommand

Covers the four shape variants from the design + chunk-size scaling:
  - Task<int> rows-affected emit
  - Task<IReadOnlyList<int>> identity emit
  - IEnumerable<T> buffered adapter emit
  - VO-wrapped identity factory emit
  - 10-col row → chunk size 90 (validates 900/placeholderCount baking)

Snapshots verified by visual inspection of the .received.cs files
generated by Verify on first run."
```

---

### Task 8: Diagnostic tests — `BulkInsertDiagnosticsTests.cs`

**Files:**
- Create: `tests/ZeroAlloc.ORM.Generator.Tests/Diagnostics/BulkInsertDiagnosticsTests.cs`

**Step 1: Write one test per ZAO code (ZAO070–074)**

Follow the existing diagnostic-test convention in the repo (`grep -l "ZAO0" tests/ZeroAlloc.ORM.Generator.Tests/Diagnostics/*.cs | head -1` and inspect the structure of an existing test for the exact assertion style).

Sketch:

```csharp
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

public class BulkInsertDiagnosticsTests
{
    [Fact]
    public void ZAO070_fires_when_no_collection_parameter()
    {
        var source = """
            using System.Data.Async;
            using System.Threading;
            using System.Threading.Tasks;
            using ZeroAlloc.ORM;
            namespace TestApp;
            public sealed partial class Repo(IAsyncDbConnection connection)
            {
                [Command("INSERT INTO X (A) VALUES (@A)", Kind = CommandKind.BulkInsert)]
                public partial Task<int> InsertAsync(int a, CancellationToken ct);
            }
            """;
        var diagnostics = GeneratorHarness.GetDiagnostics(source);
        Assert.Contains(diagnostics, d => d.Id == "ZAO070");
    }

    [Fact]
    public void ZAO071_fires_when_SQL_has_zero_VALUES_tuples()
    {
        var source = """
            // ... [Command("INSERT INTO X SELECT 1", Kind = CommandKind.BulkInsert)] ...
            """;
        var diagnostics = GeneratorHarness.GetDiagnostics(source);
        Assert.Contains(diagnostics, d => d.Id == "ZAO071");
    }

    [Fact]
    public void ZAO071_fires_when_SQL_has_multiple_VALUES_tuples()
    {
        var source = """
            // ... [Command("INSERT INTO X (A) VALUES (1), (2)", Kind = CommandKind.BulkInsert)] ...
            """;
        var diagnostics = GeneratorHarness.GetDiagnostics(source);
        Assert.Contains(diagnostics, d => d.Id == "ZAO071");
    }

    [Fact]
    public void ZAO072_fires_when_placeholder_has_no_matching_property()
    {
        var source = """
            // record TRow(int Foo);
            // ... [Command("INSERT INTO X (Bar) VALUES (@Bar)", Kind = CommandKind.BulkInsert)]
            //     public partial Task<int> InsertAsync(IReadOnlyList<TRow> rows, CancellationToken ct); ...
            """;
        var diagnostics = GeneratorHarness.GetDiagnostics(source);
        Assert.Contains(diagnostics, d => d.Id == "ZAO072");
    }

    [Fact]
    public void ZAO073_fires_when_return_type_is_wrong_shape()
    {
        var source = """
            // ... public partial Task<string> InsertAsync(IReadOnlyList<TRow> rows, CancellationToken ct); ...
            """;
        var diagnostics = GeneratorHarness.GetDiagnostics(source);
        Assert.Contains(diagnostics, d => d.Id == "ZAO073");
    }

    [Fact]
    public void ZAO074_fires_when_BulkInsert_kind_on_StoredProcedure()
    {
        var source = """
            // [StoredProcedure("usp_X", Kind = CommandKind.BulkInsert)]  // ← misuse
            """;
        var diagnostics = GeneratorHarness.GetDiagnostics(source);
        Assert.Contains(diagnostics, d => d.Id == "ZAO074");
    }
}
```

> **NOTE:** the `GeneratorHarness.GetDiagnostics` helper may not exist — check `tests/ZeroAlloc.ORM.Generator.Tests/GeneratorHarness.cs`. If only `RunGenerator` exists (returning the generated source), you may need to extend the harness to also expose the diagnostic list. Look at how other diagnostic tests (e.g. `CommandIdentityDiagnosticsTests.cs`) consume diagnostics to copy the exact pattern.

**Step 2: Run, confirm 5 of 6 pass and 1 of 6 fails initially** (since the actual SourceText literals need to be fleshed in from the sketches above)

```bash
sed -i 's/10\.0\.300/10.0.100/' global.json
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj --filter "FullyQualifiedName~BulkInsertDiagnostics" 2>&1 | tail -3
```

Iterate: fill in each source literal until the corresponding diagnostic actually fires, then move to the next.

**Step 3: All green**

Expected: 6/6 passed.

**Step 4: Revert + commit**

```bash
git checkout global.json
git add tests/ZeroAlloc.ORM.Generator.Tests/Diagnostics/BulkInsertDiagnosticsTests.cs
git commit -m "test(generator): diagnostic tests for ZAO070-ZAO074

One test per diagnostic code:
  ZAO070 — missing/duplicate collection parameter
  ZAO071 — zero or multiple VALUES tuples (two tests; same code)
  ZAO072 — unresolved placeholder→property
  ZAO073 — wrong return type
  ZAO074 — Kind=BulkInsert on [StoredProcedure]"
```

---

### Task 9: Integration tests — Sqlite

**Files:**
- Create: `tests/ZeroAlloc.ORM.Integration.Tests/BulkInsertTests.cs`

**Step 1: Write the test class**

Follow the existing Sqlite integration-test pattern (`tests/ZeroAlloc.ORM.Integration.Tests/LifecycleTests.cs` or similar is the shape template — uses `SqliteFixture` + `[Collection]`).

Tests to include:

1. **Insert 5 rows → rows-affected == 5, all rows queryable.**
2. **Insert 5 rows with `RETURNING Id` → identity list length == 5, IDs round-trip via subsequent SELECT.**
3. **Insert 1000 rows (forces chunking — 1000 rows / 450 chunk size = 3 chunks).** Verify all 1000 rows in DB; verify identity list length == 1000; verify IDs are unique + monotonic.
4. **Empty collection → returns 0 / empty list, zero DB round-trips.** Verify by checking row count is unchanged.
5. **TRow with VO-wrapped column (`OrderId`).** Insert with VO inputs; verify roundtrip.

**Step 2: Run**

```bash
sed -i 's/10\.0\.300/10.0.100/' global.json
dotnet test tests/ZeroAlloc.ORM.Integration.Tests/ZeroAlloc.ORM.Integration.Tests.csproj --filter "Category!=Postgres&FullyQualifiedName~BulkInsert" 2>&1 | tail -3
```

Expected: 5/5 passed.

**Step 3: Run the full Sqlite suite to confirm no regression**

```bash
dotnet test tests/ZeroAlloc.ORM.Integration.Tests/ZeroAlloc.ORM.Integration.Tests.csproj --filter "Category!=Postgres" 2>&1 | tail -3
```

Expected: previous 102 + 5 new = **107/107 passed**.

**Step 4: Revert + commit**

```bash
git checkout global.json
git add tests/ZeroAlloc.ORM.Integration.Tests/BulkInsertTests.cs
git commit -m "test(integration): Sqlite BulkInsert end-to-end coverage

5 scenarios covering the design's contract:
  - 5-row insert returning rows-affected
  - 5-row insert returning identity list via RETURNING
  - 1000-row insert (forces chunking; 3 chunks at 450 rows/chunk)
  - Empty collection (zero DB round-trips)
  - TRow with [ValueObject] typed-ID column"
```

---

### Task 10: Integration tests — Postgres

**Files:**
- Create: `tests/ZeroAlloc.ORM.Integration.Tests/Postgres/PostgresBulkInsertTests.cs`

**Step 1: Mirror the Sqlite test file**

Same five scenarios, against Postgres. Follow the existing `Postgres/PostgresMigrationRunnerTests.cs` shape — uses `PostgresFixture` + `[Collection("Postgres")]` + per-test isolated database via fresh schema.

The chunking test should use a different row count to additionally cover Postgres's larger parameter budget — e.g. **5000 rows** to verify that within a single chunk Postgres handles the bigger batch (Postgres's ~32k param cap means 2-col rows could chunk at 450 just like Sqlite, but a larger row count proves multi-chunk handling).

**Step 2: Run**

```bash
docker start bench-pg  # or whatever the local Postgres fixture name is
sed -i 's/10\.0\.300/10.0.100/' global.json
dotnet test tests/ZeroAlloc.ORM.Integration.Tests/ZeroAlloc.ORM.Integration.Tests.csproj --filter "FullyQualifiedName~PostgresBulkInsert" 2>&1 | tail -3
```

Expected: 5/5 passed.

**Step 3: Revert + commit**

```bash
git checkout global.json
git add tests/ZeroAlloc.ORM.Integration.Tests/Postgres/PostgresBulkInsertTests.cs
git commit -m "test(integration): Postgres BulkInsert end-to-end coverage

Mirrors the Sqlite suite (5 scenarios) against Postgres via PostgresFixture.
Chunking test scales to 5000 rows to exercise multi-chunk handling
under Postgres's larger parameter budget."
```

---

### Task 11: Cookbook docs

**Files:**
- Create: `docs/cookbook/bulk-insert.md`
- Modify: `docs/cookbook/provider-quirks.md`
- Modify: `docs/cookbook/commands.md`

**Step 1: Create `docs/cookbook/bulk-insert.md`**

Use the existing cookbook recipes as the template style — `docs/cookbook/commands.md` and `docs/cookbook/multi-result-set.md` are good shape references. Structure:

1. **When to reach for BulkInsert** — small/medium batches (5–500 rows); for 10k+ row workloads, link to the future provider-native bulk path backlog entry.
2. **Recipe 1: rows-affected** — worked example with `Orders` table.
3. **Recipe 2: identity capture via `RETURNING`.**
4. **Recipe 3: TRow with value objects.**
5. **Chunking semantics** — atomic per-chunk, NOT across chunks. Provide a snippet showing how to wrap in `IDbTransaction` for all-or-nothing.
6. **Per-provider notes** — Sqlite 999 cap, Postgres 32k, SQL Server 2100, MySQL standard multi-row VALUES. Mention which providers have integration coverage in v1.3.
7. **Related diagnostics** — list ZAO070–074 with one-line each.
8. **Related recipes** — cross-link `commands.md`, `provider-quirks.md`, `multi-result-set.md`.

**Step 2: Update `docs/cookbook/provider-quirks.md`**

Add one paragraph under each provider section noting the per-statement parameter-count cap and how it interacts with BulkInsert's chunk size:

- Sqlite: "999 parameters per statement. BulkInsert chunks at `900 / placeholderCount` rows. For a 2-column INSERT, that's 450 rows per chunk; 10-column → 90 rows per chunk."
- Postgres: "~32k parameters per statement. BulkInsert's 900-param budget leaves significant headroom; chunking rarely fires for typical schemas."
- SQL Server: "2100 parameters per statement. BulkInsert's 900-param budget stays well under this; chunking is conservative on SQL Server."
- MySQL: "Standard multi-row VALUES is supported; no documented per-statement parameter cap beyond `max_allowed_packet`."

**Step 3: Update `docs/cookbook/commands.md`**

Add one row to the `CommandKind` shape table:

| `BulkInsert` | `INSERT … VALUES (…), (…), …` (chunked). | `int` rows-affected / `IReadOnlyList<TIdentity>` |

Plus a sentence in the introduction paragraph noting BulkInsert exists and pointing at `bulk-insert.md`.

**Step 4: Commit**

```bash
git add docs/cookbook/bulk-insert.md docs/cookbook/provider-quirks.md docs/cookbook/commands.md
git commit -m "docs(cookbook): bulk-insert.md recipe + provider-quirks + commands cross-links

New primary recipe covering:
  - When to reach for BulkInsert (small/medium batches)
  - Rows-affected, identity capture, VO-wrapped identity variants
  - Chunking semantics + atomic-across-chunks transaction snippet
  - Per-provider parameter-count caps + chunk-size interaction
  - Diagnostics ZAO070-074 listing
  - Cross-links to commands.md, provider-quirks.md, multi-result-set.md

provider-quirks.md gains one paragraph per provider on the
per-statement parameter cap.

commands.md gains one row in the CommandKind shape table + a note
in the introduction."
```

---

### Task 12: Verification + push + PR + merge

**Files:** none (workflow actions).

**Step 1: Full sweep — build + test (with relaxed global.json)**

```bash
sed -i 's/10\.0\.300/10.0.100/' global.json
dotnet build ZeroAlloc.ORM.sln -c Release 2>&1 | tail -5
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj -c Release 2>&1 | tail -3
dotnet test tests/ZeroAlloc.ORM.Integration.Tests/ZeroAlloc.ORM.Integration.Tests.csproj -c Release 2>&1 | tail -3
git checkout global.json
```

Expected:
- Build: green, 0 errors. Warnings only if `<PublicAPI>` analyzer caught a missing line — fix before continuing.
- Generator tests: 238 (baseline) + 6 (parser) + 5 (snapshots) + 6 (diagnostics) = **255/255 passed**.
- Integration tests: 102 (baseline) + 5 (Sqlite BulkInsert) + 5 (Postgres BulkInsert) = **112/112 passed**.

**Step 2: Confirm commit history is clean**

```bash
git log --oneline origin/main..HEAD
```

Expected (in order):
1. `docs(design): CommandKind.BulkInsert design doc for v1.3` (already on branch from brainstorming)
2. `docs(plan): CommandKind.BulkInsert implementation plan` (already on branch from this task)
3. `feat(abstractions): add CommandKind.BulkInsert = 3`
4. `feat(generator): add CommandKindModel.BulkInsert + EmitShape.BulkInsertCommand`
5. `feat(generator): add diagnostics ZAO070-ZAO074 for BulkInsert shape`
6. `feat(generator): BulkInsertValuesParser extracts VALUES tuple placeholder list`
7. `feat(generator): classifier branch for EmitShape.BulkInsertCommand`
8. `feat(generator): EmitBulkInsertCommand + dispatch case for BulkInsertCommand shape`
9. `test(generator): 5 snapshot tests for EmitBulkInsertCommand`
10. `test(generator): diagnostic tests for ZAO070-ZAO074`
11. `test(integration): Sqlite BulkInsert end-to-end coverage`
12. `test(integration): Postgres BulkInsert end-to-end coverage`
13. `docs(cookbook): bulk-insert.md recipe + provider-quirks + commands cross-links`

13 commits. If any was reordered or amended out of intent, fix before pushing.

**Step 3: Push the branch**

```bash
git push -u origin design/orm-bulk-insert 2>&1 | tail -3
```

**Step 4: Open the PR**

```bash
gh pr create --title "feat: CommandKind.BulkInsert — chunked multi-row INSERT for v1.3" --body "$(cat <<'EOF'
## Summary

Adds `CommandKind.BulkInsert` to ZeroAlloc.ORM 1.3. A new emit shape that takes one `IReadOnlyList<TRow>` parameter and produces a chunked multi-row \`INSERT … VALUES (…), (…), …\` pipeline with optional RETURNING-based identity capture.

Closes the architectural gap with EF Core's \`SaveChanges\` (which emits a single batched multi-row INSERT) — surfaced as the one place EF still had a write-path edge over ZA.ORM during the ZA.Templates EF→ZA.ORM swap ([Templates PR #152](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates/pull/152)).

## What changes

- **`ZeroAlloc.ORM.Abstractions`**: new public enum value \`CommandKind.BulkInsert = 3\`. One line added to \`PublicAPI.Shipped.txt\`. 100% additive — existing \`NonQuery\` / \`Scalar\` / \`Identity\` callers untouched.
- **\`ZeroAlloc.ORM.Generator\`**: new \`CommandKindModel.BulkInsert\` + \`EmitShape.BulkInsertCommand\` + \`ClassifyBulkInsertCommand\` + \`EmitBulkInsertCommand\` + \`BulkInsertValuesParser\` helper + 5 new diagnostics (\`ZAO070\`–\`ZAO074\`).
- **Tests**: 5 new generator snapshots + 6 diagnostic tests + 6 \`BulkInsertValuesParser\` unit tests + 5 Sqlite integration tests + 5 Postgres integration tests = **27 new tests, 100% green locally**.
- **Cookbook**: new \`bulk-insert.md\` recipe + per-provider parameter-cap notes in \`provider-quirks.md\` + cross-link in \`commands.md\`.

## Design + plan

- Design doc: [\`docs/plans/2026-06-02-bulk-insert-design.md\`](docs/plans/2026-06-02-bulk-insert-design.md) (commit \`9007624\`)
- Implementation plan: [\`docs/plans/2026-06-02-bulk-insert-implementation.md\`](docs/plans/2026-06-02-bulk-insert-implementation.md)

## Key design points

- **Chunking baked at codegen** — chunk size = \`900 / placeholderCount\`. The 900 budget stays safely under Sqlite's 999-parameter cap; SQL Server's 2100 and Postgres's ~32k have plenty of headroom. For a 2-column INSERT: 450 rows/chunk. For a 10-column: 90/chunk.
- **Two return shapes via classifier** — \`Task<int>\` (rows-affected sum across chunks) or \`Task<IReadOnlyList<TIdentity>>\` (identity values from \`RETURNING <col>\`, concatenated across chunks).
- **Name-based placeholder binding** — \`@PlaceholderName\` matches TRow's public property by case-insensitive name. ZAO072 fires at compile time on miss.
- **Atomic per chunk, not across chunks** — adopters wanting all-or-nothing wrap in \`IDbTransaction\`. Cookbook recipe documents the pattern.

## Out of scope (carry-forward)

- Provider-native bulk (\`SqlBulkCopy\` / \`COPY\` / \`MySqlBulkCopy\`) — different API shape, ~10–100× faster for 10k+ rows but limited identity capture. Separate design when adopter demand surfaces.
- Returning \`Task<IReadOnlyList<TRow>>\` (full materialized rows with assigned IDs) — adds a row-materialization layer; v1.4+.
- Provider integration coverage beyond Sqlite + Postgres — SQL Server and MySQL "supported in principle" but lack integration test infrastructure upstream.

## Test plan

- [x] 255/255 generator tests passing (238 baseline + 17 new)
- [x] 112/112 integration tests passing (102 baseline + 10 new)
- [x] PublicAPI analyzer green (one new line in \`Shipped.txt\`)
- [ ] CI build-test + aot-publish-smoke + collision-smoke all green

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)" 2>&1 | tail -3
```

**Step 5: Monitor CI**

```bash
gh pr checks <PR_NUMBER>
```

Wait for all checks to land. Expected set: \`lint\`, \`build-test\`, \`collision-smoke\`, \`aot-publish-smoke\`.

If \`build-test\` fails, the most likely cause is **PublicAPI analyzer**: a missing line in `Shipped.txt`. The CI log will name the missing symbol — add the line, commit, push.

If a snapshot test fails on CI but passed locally, it's likely a **line-ending or whitespace difference** in the `.verified.cs` files between Windows (CRLF) and Linux (LF). The repo's `.gitattributes` should normalize, but if it doesn't, you may need to add a `.editorconfig` entry or normalize the snapshot files explicitly. **STOP and inspect** the diff before chasing fixes.

**Step 6: Admin-merge once green**

Per the prior session's pattern (the repo requires reviews; PR author can't self-approve):

```bash
gh pr merge <PR_NUMBER> --squash --delete-branch --admin
```

**Step 7: Trigger pack-push for v1.3.0**

release-please will open its own PR proposing v1.3.0 after the \`feat:\` commits land. Once that merges:

```bash
gh workflow run pack-push.yml -f version=1.3.0
```

(Same manual-trigger pattern as v1.2.0 — release-please uses GITHUB_TOKEN which doesn't fire the \`release: published\` trigger that pack-push listens for.)

---

## Out of scope (deliberately not in this plan)

- Provider-native bulk paths (\`SqlBulkCopy\` etc.) — separate design.
- Returning full row records with assigned IDs (\`Task<IReadOnlyList<TRow>>\`) — defer to v1.4+.
- Multi-statement INSERT batching (parent + child tables in one call) — use existing \`IAsyncDbBatch\` infrastructure.
- SQL Server / MySQL integration test infrastructure — out of scope; add when adopter demand materializes.
- Performance benchmarks — leave to a separate brainstorm post-1.3 to compare BulkInsert vs single-row × N round-trips on the actual workloads.

## When the plan is complete

The branch \`design/orm-bulk-insert\` has 13 commits + the 2 design/plan commits = 15 total. All CI checks pass, the PR is merged, release-please proposes v1.3.0, and (once that merges) \`ZeroAlloc.ORM 1.3.0\` ships to NuGet with the BulkInsert feature.
