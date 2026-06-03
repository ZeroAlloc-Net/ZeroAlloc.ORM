# `IAsyncDbTransaction` Parameter Support — Design

**Status:** approved 2026-06-03
**Scope:** ZeroAlloc.ORM, additive, generator-side
**Target version:** v1.5.0
**Closes (downstream):** ZA.Templates #162 (non-atomic Order writes in za-clean)
**Branch:** `feat/orm-transaction-parameter` off `main` at `b234811` (v1.4.0)

## Background

ZA.ORM's emitted commands are transaction-naive — the generator emits `await using var __cmd = __conn.CreateCommand();` without ever setting `__cmd.Transaction`. AdoNet.Async exposes the full transaction surface (`IAsyncDbConnection.BeginTransactionAsync`, `IAsyncDbTransaction`, `IAsyncDbCommand.Transaction` property), so adopters CAN begin transactions today — but:

- **Sqlite + Postgres** auto-bind commands to the connection's pending transaction (works by accident)
- **SqlClient does NOT auto-bind** — commands created on a connection with an open transaction must have `cmd.Transaction = tx` set explicitly or `InvalidOperationException` fires at execute time
- **Adopter ergonomics** suffer: 25-line connection-open/begin/try/commit/rollback boilerplate per call site (surfaced during ZA.Templates #162 triage)

The v1.0 design explicitly de-scoped "unit-of-work / scoped transaction" — that's still the right call. But supporting an **explicit transaction parameter** on `[Command]` / `[Query]` methods is much smaller and addresses the real failure mode: cross-statement atomicity for adopters who want it.

## Decision

Add **optional `IAsyncDbTransaction` parameter support** on every `[Command]` / `[Query]` / `[StoredProcedure]` partial method. When the parameter is present, the generator emits `__cmd.Transaction = @<paramName>;` after `__cmd = __conn.CreateCommand();` at every emit site. Detection is shape-based — mirrors the existing `CancellationToken` special-case. No new attributes, no public API change.

## How it looks adopter-side

**Before (today, ZA.ORM v1.4 — adopter must hand-write the workaround):**

```csharp
public async Task AddAsync(Order order, CancellationToken ct)
{
    var openedHere = conn.State != ConnectionState.Open;
    if (openedHere) await conn.OpenAsync(ct).ConfigureAwait(false);
    try
    {
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        var orderId = await InsertOrderAsync(/* ... */).ConfigureAwait(false);
        order.AssignPersistenceId(new OrderId(orderId));
        foreach (var line in order.Lines)
            await InsertOrderLineAsync(orderId, /* ... */).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }
    finally { if (openedHere) await conn.CloseAsync().ConfigureAwait(false); }
    // Works on Sqlite + Postgres by auto-bind; silently broken on SqlClient.
}

[Command("INSERT INTO Orders ...")]
private partial Task<int> InsertOrderAsync(int customerId, string status, string total, CancellationToken ct);

[Command("INSERT INTO OrderLines ...")]
private partial Task<int> InsertOrderLineAsync(int orderId, string sku, int qty, string price, CancellationToken ct);
```

**After (ZA.ORM v1.5 — adopter just threads a tx):**

```csharp
public async Task AddAsync(Order order, CancellationToken ct)
{
    await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
    var orderId = await InsertOrderAsync(/* ... */, tx, ct).ConfigureAwait(false);
    order.AssignPersistenceId(new OrderId(orderId));
    foreach (var line in order.Lines)
        await InsertOrderLineAsync(orderId, /* ... */, tx, ct).ConfigureAwait(false);
    await tx.CommitAsync(ct).ConfigureAwait(false);
    // Works on every provider — cmd.Transaction is explicitly set.
}

[Command("INSERT INTO Orders ...")]
private partial Task<int> InsertOrderAsync(int customerId, string status, string total, IAsyncDbTransaction tx, CancellationToken ct);

[Command("INSERT INTO OrderLines ...")]
private partial Task<int> InsertOrderLineAsync(int orderId, string sku, int qty, string price, IAsyncDbTransaction tx, CancellationToken ct);
```

The `BeginTransactionAsync` call requires an open connection. Adopters who don't already hold one open via a long-lived `IAsyncDbConnection` registration need an `await conn.OpenAsync()` before `BeginTransactionAsync` — but that's existing ADO.NET semantics, not ZA.ORM-specific. The ref-counted prologue inside each emit method still works: it sees `State == Open` and is a no-op for both connection lifecycle and transaction state.

## What changes (generator-side)

**Parameter classification** — `OrmGenerator.cs:832-879` (where `methodParameters` is built):

- Detect `IAsyncDbTransaction` parameter by `p.Type.ToDisplayString() == "System.Data.Async.IAsyncDbTransaction"` (case-sensitive ordinal, matching the CancellationToken precedent on line 835)
- Add new field `IsTransaction: bool` to the `ParameterInfo` positional record
- When `IsTransaction == true`: skip ConventionDiscovery, skip SQL parameter binding (the parameter is a control signal, not a SQL value — mirrors CT exactly)

**Method-level transaction-parameter name lookup** — analogous to existing `CancellationTokenParameterName` field on `QueryMethodModel` (line 1037):

- New field `TransactionParameterName: string?` on `QueryMethodModel`
- Populated by scanning `methodParameters` for the single `IsTransaction == true` entry (FirstOrDefault — null when none)

**Emit-time transaction assignment** — new helper:

```csharp
private static void EmitTransactionAssignment(StringBuilder sb, QueryMethodModel m, string indent)
{
    if (m.TransactionParameterName is null) return;
    sb.Append(indent).Append("__cmd.Transaction = @").Append(m.TransactionParameterName).AppendLine(";");
}
```

Called immediately after each `CreateCommand()` line at all 9 emit sites:

```csharp
sb.AppendLine("            await using var __cmd = __conn.CreateCommand();");
EmitTransactionAssignment(sb, m, "            ");   // NEW — no-op when no tx param
BuildCommandTextAssignment(sb, m, "__cmd", "            ");
```

Emit sites to thread through (from the existing grep):
- `EmitNonQuery` (4166)
- `EmitFlatRow` (4199)
- `EmitBulkInsertCommand` per-chunk (4397)
- `EmitMultiResultSet` (4600)
- `EmitStreaming` (4856)
- `EmitScalar` (4985, 5051)
- `EmitCommandIdentity` (5454)
- `EmitListResultSet` (5676)

All take the same indent string (`"            "`) — uniform call.

**New diagnostic ZAO080** (next free decade after BulkInsert's ZAO070-074):

- Id: `ZAO080`
- Title: `At most one IAsyncDbTransaction parameter`
- Severity: Warning (mirroring ZAO006 for CancellationToken — multiple CT params is a warning, not an error)
- MessageFormat: `"Method '{0}' has {1} IAsyncDbTransaction parameters; only the first is used."`
- Fires from the diagnostic-emission path adjacent to ZAO005/006 (CancellationToken-count checks)

## Tests

**Generator snapshots** (4 new, covering the most-used emit shapes):

1. `[Command(Kind = NonQuery)]` with `IAsyncDbTransaction tx` parameter → emit shows `__cmd.Transaction = @tx;` line
2. `[Command(Kind = Identity)]` with tx parameter → same, plus the Identity-specific RETURNING handling
3. `[Query]` returning `Task<T?>` (FlatRow) with tx parameter → same, plus single-row read
4. `[Command(Kind = BulkInsert)]` with tx parameter → tx line appears inside every chunk's `__cmd` initialisation

**Diagnostic test (1 new):**

5. `ZAO080DiagnosticsTests.MultipleTxParameters_emits_warning` — method declares two `IAsyncDbTransaction` parameters, assert ZAO080 fires

**Integration tests (2 new, Sqlite):**

6. `TransactionParameterTests.Two_inserts_share_a_transaction_and_commit_atomically` — open conn + begin tx + two inserts using the same tx + commit + verify both rows present
7. `TransactionParameterTests.Two_inserts_share_a_transaction_and_rollback_on_failure` — same setup but second insert violates a constraint (or test throws after first insert) + verify zero rows present after the rollback

(Postgres covered structurally — emit is provider-agnostic; no infra to add.)

## What stays out of scope

- Templates adoption — separate follow-up PR in ZA.Templates once v1.5.0 ships
- Repository-level / class-level transaction scoping (`[ScopedTransaction]` attribute, ambient context) — magical, AOT-unfriendly, scope explosion
- `cmd.Transaction = null` semantics — providers handle this natively; no special emit
- Connection-lifecycle changes (auto-open before BeginTransactionAsync) — adopters open the connection themselves; existing AdoNet.Async surface
- `IDbTransaction` (the non-async base interface) — only `IAsyncDbTransaction` is supported. Adopters using the sync interface get no emit (parameter is treated as a normal SQL value, falls through ConventionDiscovery, likely produces ZAO041)

## Versioning + release

- Conventional commit messages so release-please cuts **v1.5.0** (minor — new public-shaped behavior; no breaking change)
- Squash titles `feat:` per the recurring release-please gotcha
- After merge: trigger `gh workflow run pack-push.yml -f version=1.5.0` to publish to NuGet (manual step — release-please's GITHUB_TOKEN doesn't fire the pack-push trigger; established pattern from v1.2 / v1.3 / v1.4)

## After v1.5.0 ships

Open a small follow-up PR in **ZA.Templates** to:
1. Add `IAsyncDbTransaction tx` to za-clean's `InsertOrderAsync` + `InsertOrderLineAsync` signatures
2. Rewrite `OrderRepository.AddAsync` to the clean 5-line shape (still needs an explicit open for `BeginTransactionAsync`, but no more ref-counted-prologue dance)
3. Add the `CHECK ("Quantity" > 0)` constraint that was dropped from the abandoned templates branch
4. Add the atomicity integration test
5. Close ZA.Templates #162
