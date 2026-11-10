# ZeroAlloc.ORM — a static memo for placeholder SQL, no per-instance state

**Status:** design, awaiting approval · **Date:** 2026-11-10
**Origin:** ZeroAlloc.Outbox follow-up to ZeroAlloc.ORM#464 (shipped in 2.16.0, PR #469)

## 1. Intent

ORM 2.16.0 emits, for every `[Command]` or `[Query]` method whose SQL has placeholders, a
per-instance field `__sql{i}_{Method}` in front of a static `ConcurrentDictionary`. Each field is one
reference, 8 B. A repository with ten such methods grows by 80 B. ZeroAlloc.Outbox builds one
repository per DI scope, so every scope pays that and rarely reuses the memo. Outbox's allocation tests
(store construction, scope) go red, and the owner chose to fix the generator, not to raise the budget.

The end state: a repository instance carries no state because of placeholders, and a call still
allocates nothing after warm-up.

## 2. Goals, non-goals, anti-goals

**Goals**

- No per-instance field for a placeholder method. Instance size equals that of a literal-SQL repository.
- No allocation per call after warm-up, for one repository and for two repositories that resolve to
  different values and alternate.
- Keep the `ArgumentException` for a null or blank `{name}` value, the benign-race publication rule
  (one immutable holder, `Volatile` read and write) and the `{col:Name}` quote flag in the key.
- No public API change. Generated members are private.

**Non-goals**

- Changing placeholder syntax, binding or diagnostics (ZAO115 to ZAO118).
- Caching per instance in any form.

**Anti-goals**

- A memo that can return SQL for the wrong table. A false hit is a failure even if it is fast.
- A hot-path cost that grows with the number of repositories in the process.

## 3. Decisions made with the owner

1. Fix it in the ORM generator, not in Outbox, and do not raise the Outbox budget.
2. The direction is a static per-method memo that holds the last resolved holder and the key it was
   built for. The check is reference equality on the member values. A miss falls back to the existing
   dictionary.
3. The memo is filled once, when empty. A dictionary hit does not rewrite it.
4. Commit type `perf:` (a patch bump in release-please).

## 4. Approaches considered

| Approach | Verdict |
|----------|---------|
| Keep the instance field, make it lazy | Does not help. The field exists either way. |
| Dictionary lookup on every call, no memo | Simplest, but costs a string hash per call and drops the fast path. Rejected. |
| Static memo, last hit wins | A hit is more likely, but alternating tables rewrite a shared static on every call and cause cache-line contention across threads. Rejected. |
| **Static memo, fill once, dictionary fallback** | Chosen. The usual case of one table per process always hits. Every other case costs one hash and allocates nothing. |

## 5. Design

### 5.1 Emitted shape per placeholder method

Before (2.16.0):

```csharp
private __ZaoPlaceholderSql? __sql0_M;                                   // per instance
private static readonly ConcurrentDictionary<K, __ZaoPlaceholderSql> __sqlCache0_M = new();
private __ZaoPlaceholderSql __Sql0_M() { ...Volatile.Read(ref __sql0_M)... }
```

After:

```csharp
private static __ZaoPlaceholderSql? __sqlMemo0_M;                        // static
private static readonly ConcurrentDictionary<K, __ZaoPlaceholderSql> __sqlCache0_M = new();
private __ZaoPlaceholderSql __Sql0_M()
{
    var __v0 = @Table;                      // read the members every call
    var __v1 = @QuoteColumns;               // only when the method has {col:} and a flag
    var __m = Volatile.Read(ref __sqlMemo0_M);
    if (__m is not null && ReferenceEquals(__m.Strings[0], __v0) && __m.Quote == __v1) return __m;
    // miss: validate (ThrowIfNullOrWhiteSpace on each string), dictionary GetOrAdd,
    // then publish to the memo only if it is still empty
}
```

The holder `__ZaoPlaceholderSql` gains the key it was built for: the member strings in order of first
appearance (`string[]`) and the quote flag. They are set in the constructor, so the holder stays
immutable and one reference publishes the key together with `Full` and `Parts`. The dictionary factory
passes the key into the holder it builds.

The `{col}`-only case (no member values, no flag) keeps its single static holder. It has no state to
fix.

### 5.2 Correctness

- `ReferenceEquals` on a string implies equal content, so the memo can never be a false hit. A member
  that returns a fresh string per call misses the memo and goes through the dictionary, which is still
  correct and allocates nothing.
- A hit implies the values were non-blank when the holder was built, so validation runs on the miss
  path only. A null member misses the memo (a memo key is never null) and throws on the miss path as
  before.
- Publication: the memo is written with `Volatile.Write` of a reference to an immutable holder. Two
  threads filling an empty memo both write a holder from the same dictionary, so the race is benign.
  The fill-once rule is `Interlocked.CompareExchange(ref memo, holder, null)`.

### 5.3 Behaviour change

Members are read on every call. In 2.16.0 the first resolved value stuck to the instance for its life,
so a member changed afterwards was ignored. Now it is honoured. Repositories whose placeholder members
are `readonly` or init-only are unaffected. The PR body gets a "Behaviour change" section for this, per
the release-notes process.

### 5.4 Tests

- Emit tests (`PlaceholderEmitTests.cs`): no instance field for a placeholder method, a static memo
  field, the holder carries its key, the `{col}`-only output is unchanged, other repositories stay
  byte-identical.
- Allocation gate (`TablePlaceholderAllocationTests.cs`): a fresh repository instance with placeholder
  methods has the same size as one with literal SQL. Two repositories with different tables alternating
  allocate zero bytes per call. A single repository allocates zero bytes per call.
- Behaviour tests: a changed member is honoured on the next call, a null or blank value still throws,
  and a value that is equal but a different reference resolves to the same SQL.

## 6. Constraints

- Conventional Commits, `perf:`. Commit body lines at most 100 characters, no nested parentheses.
- api-compat covers every package. Nothing public changes and no optional parameter is added.
- Build and test one project at a time. Run Release as well as Debug. Eight Integration allocation
  gates already fail in Debug on main, and CI runs Release only.
- Do not merge any PR, and never a release PR. Releases are the owner's batched decision.

## 7. Left to the builder

- Naming of the memo and holder members.
- Whether the key strings sit in a `string[]` or in fixed fields. Fixed fields are fine for the common
  one-string case if the emit stays small.
- Where in the docs a one-line note about member reads belongs.

## 8. After the ORM release

Outbox bumps the ORM and ORM.Generator floors to the fixed version, once it is verified on nuget.org,
re-runs `OrmOutboxStoreAllocationTests` and finishes its own Task 5. If 8 B remain over because of the
store's dialect, table and quoteColumns fields, the owner decides before `IOutboxStatements` changes.
