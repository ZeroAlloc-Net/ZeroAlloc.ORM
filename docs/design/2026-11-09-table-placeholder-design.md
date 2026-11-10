# ZeroAlloc.ORM — runtime table and column placeholders in `[Command]` and `[Query]`

**Status:** design, awaiting approval · **Date:** 2026-11-09
**Origin:** ZeroAlloc.ORM#464, from ZeroAlloc.Outbox#434 (Outbox 4.5.0, PR #437)

## 1. Intent

A library that ships ORM-generated repositories cannot know the table its adopters use. Outbox.Orm
lets an application pick the outbox table per pipeline. The SQL in `[Command]` and `[Query]` is a
compile-time literal, so Outbox.Orm builds its nine statements at runtime in an internal `OutboxSql`
class and runs them through `IAsyncDbConnection`. For those statements it gives up the generated
parameter binding and materialisation.

The end state: the attribute SQL holds a placeholder, the value comes from the repository instance at
runtime, binding and materialisation stay generated, and Outbox.Orm deletes `OutboxSql`.

## 2. Goals, non-goals, anti-goals

**Goals**

- `{table}` in attribute SQL, resolved at runtime from a member of the containing class.
- `{col:Name}` for a column name, quoted in the declared dialect's style when the class asks for it.
  This replaces Outbox's `QuoteColumns`.
- Generated binding and materialisation unchanged. No per-call allocation after warm-up.
- No public API change: the attributes gain nothing, so api-compat has nothing to flag.

**Non-goals**

- Placeholders in `[Command(Kind = BulkInsert)]`, `[LinqQuery]`, `[LinqCommand]` or stored
  procedures. These are rejected with a diagnostic (section 5).
- Validating or quoting the table value. It is trusted configuration (decision 2).
- A general string-substitution feature. Only the two forms below exist.

**Anti-goals**

- A placeholder must never become a way to pass values. Values stay `@parameters`. Anything that makes
  `{table}` look like a parameter slot is a failure even if it works.
- No runtime string building on the call path once warm.

## 3. Decisions made with the owner

1. **Supply: a member of the class.** `{table}` binds to a string primary-constructor parameter, field
   or get-only property on the partial class, found the way `ResolveConnectionAccess` finds the
   connection. No method signature changes, and DI supplies the value once.
2. **Trust: verbatim and trusted.** The value is spliced in as given and documented as trusted
   configuration, never user input. Quoting is the caller's choice, so `OutboxMessages`,
   `"OutboxMessages"` and `public."Outbox Messages"` all work, as in Outbox today.
3. **Columns: a dialect-aware column form.** `{col:Id}` becomes `Id`, `"Id"` or `[Id]`, by the dialect
   the repository declares and a bool member on the class.

## 4. Approaches considered

| Approach | Trade-off | Verdict |
|---|---|---|
| Member of the class | No signature change. One value per repository, which is how Outbox wants it. | Picked |
| Method parameter (`[SqlName] string table`) | Every call site repeats the value and every method changes. | Rejected |
| Registration-time hook | Adds public API, DI coupling and a runtime contract. | Rejected |

For quoting: a dialect-aware `{col:Id}` was picked over free-form named placeholders (one member per
column, or open and close quote strings passed in) and over deferring columns. Deferring would leave a
runtime shim in Outbox.Orm and defeat the stated end state.

## 5. Design

### 5.1 Syntax

A placeholder is exactly `{name}` or `{col:name}` outside a string literal, a quoted identifier and a
comment. `name` is `[A-Za-z_][A-Za-z0-9_]*`. Braces anywhere else, such as a PostgreSQL array literal
`'{1,2}'` or a JSON literal, are not placeholders, because they sit inside quotes. An ODBC escape such
as `{d '2020-01-01'}` has a space and does not match. The builder verifies that no existing test or
snapshot SQL changes meaning (one test file mentions `{...}` in an attribute today).

### 5.2 Binding the value

`{table}` binds, case-insensitively and ignoring one leading underscore, to a member of the containing
class whose type is `string`. Primary-constructor parameters count only when captured, as for the
connection. Any other name works the same way, so `{schema}` binds to `schema`. That is a side effect,
not a promised feature. Section 5.5 gives the rule for naming the member in diagnostics.

`{col:Id}` uses the member named `QuoteColumns` of type `bool`, if present. Absent means never quoted,
so the placeholder emits `Id`. The quote style comes from the dialect declared with `[Dialect]` on the repository or on the assembly,
so it is fixed per repository: `"` for SQLite and PostgreSQL, `[` and `]` for SQL Server, a backtick for
MySQL. Without a declared dialect `{col:...}` is an error (ZAO118), checked once per repository.

### 5.3 Emission and caching

A method whose SQL has placeholders no longer assigns a literal `CommandText`. The generated partial
class gets, per such method:

- a static cache keyed by the placeholder values, `(string table, bool quote)`, held in a
  `ConcurrentDictionary` with a value-tuple key, so lookup allocates nothing;
- an instance field holding the built string, filled on first use from the static cache.

The result: one allocation per distinct value for the whole process, one dictionary lookup per
repository instance per method, and a field read on every later call. This matters because Outbox's
store is scoped and `OutboxSql` already needed a static cache for the same reason. The instance field
may be racy but benign: two threads can both fill it, with an identical reference from the cache.

Multi-statement SQL and tuple results resolve every statement. The batch-split emit already writes each
statement's `CommandText`; each statement goes through the same cache. The `FromResource = true` path
substitutes after the resource is read at generator time, so it needs no separate work.

Parameter detection (`FindPlaceholderSigil`, the parameter-name scan) must run on SQL with placeholders
masked, so `{col:Id}` never counts as a parameter name or breaks the `@` and `:` sigil scan (`{col:Id}`
contains a colon).

### 5.4 Allocation tests

Per the AllocationGate convention in the TestHelpers memory: after tier-up, a call to a placeholder
method allocates the same bytes as the same method with a literal. Test against the Sqlite provider.

### 5.5 Diagnostics

IDs are claimed here so a parallel PR cannot collide: **ZAO115 to ZAO118**.

| ID | Severity | Fires when |
|---|---|---|
| ZAO115 | Error | A `{name}` finds no `string` member, or finds more than one candidate. The message names the members it looked at. |
| ZAO116 | Error | A placeholder sits where a value belongs: right after `=`, `<`, `>`, `LIKE` or `IN (`, inside a `VALUES (...)` tuple, or its name equals a method parameter that the SQL also uses as `@name`. The fix text says to use `@name` and a parameter. |
| ZAO117 | Error | A placeholder appears in `BulkInsert`, `[LinqQuery]`, `[LinqCommand]` or stored-procedure SQL. |
| ZAO118 | Error | `{col:...}` is used on a repository with no declared dialect. |

ZAO116 is a heuristic, not a parser. It is deliberately narrow: it flags the positions listed, not
every suspicious one. Each ID gets a `docs/diagnostics/ZAOnnn.md` page and an entry in
`AnalyzerReleases.Unshipped.md`.

### 5.6 Docs

The README and the cookbook state the trust rule in the same words each time: the table value is
configuration, not input, and it is spliced verbatim.

## 6. The Outbox side (follow-up PR in ZeroAlloc.Outbox, after the ORM release is on NuGet)

`OutboxMessageRepository` becomes generated `[Command]` and `[Query]` methods that use `{table}` and
`{col:...}`. Each of the three `Claim*PendingOutboxQuery` classes holds its own dialect spelling as an
attribute on a generated method. `OutboxSql`, its cache and its tests go. `QuoteColumns` stays as the
registration option and feeds the `QuoteColumns` member. This follow-up cannot start until the ORM
release is verified on NuGet, and the release itself is the owner's call.

## 7. Constraints

- Releases of ZeroAlloc.ORM are batched cycles. This work ends at an open PR; no release PR is merged.
- api-compat guards every package. This design adds no public API, so no optional parameter question
  arises.
- A `feat:` commit means a minor bump. Commit bodies stay within 100 characters per line, with no
  nested parentheses and no Claude trailer.
- Build and test one project at a time on the shared VM. Run `dotnet test -c Release` as well as Debug
  before pushing.

## 8. Left to the builder

- Exact emitted field and cache names (follow the index-plus-name convention of the bulk-insert cache).
- The ZAO116 position list, within the narrow rule above.
- Whether `QuoteColumns` absent should instead warn (default here: silent, documented).
- Test layout: emit snapshot tests, diagnostic tests, an Sqlite integration test, and the allocation test.

## 9. Calls made after the builder check (mine, for you to check)

1. **Member shapes.** Reuse the connection's path: a captured primary-constructor parameter, a field or a
   get-only property. A plain constructor parameter that is never stored is not a member, so it gets
   ZAO115 with a message saying to capture it.
2. **Name ties.** Candidates are the string members whose name equals the placeholder, ignoring case and
   one leading underscore. `table` and `_table` are two candidates, so ZAO115 fires. Declaring both is
   ambiguous and the message names them.
3. **Tokenizer.** `SqlStatementSplitter` is a public class in ZeroAlloc.TypeConversions and skips only
   `'..'` and `".."`, not comments, `[..]`, backticks or `$$`. Extending it would add public API and could
   change statement counts. So the generator gets its own internal `SqlPlaceholderScanner` that skips all
   of those, and the splitter stays untouched. Placeholders hold no `;`, so splitting is unaffected.
4. **Cache key.** It is the tuple of every distinct member value the method's SQL uses, in order of first
   appearance, plus the quote flag when `{col:}` appears. `{schema}` and `{table}` together is therefore
   allowed and keys on both. The emit builds the key struct per method, so the "no allocation" claim
   holds for any count.
5. **Dialect scope.** Per repository, never per method. The cache key does not need the dialect.
6. **Emit paths.** Every path that writes `CommandText` from `m.Sql` goes through the cache: single
   command, batch split, tuple results, streaming. Generator-time uses of the SQL (parameter scan,
   statement count, sigil detection) run on the masked text. The generator does not derive columns from
   the SQL, so a placeholder in the column list or `FROM` does not break materialisation. The builder
   confirms this by test and adds a rejection if it turns out wrong.
7. **Allocation test.** It measures after warm-up on one instance, as `AllocationGate` does, and requires
   equal bytes to the literal version. A repository created per call pays one lookup and no allocation,
   since the cached string is shared; the test also covers that case.
8. **Null table.** A null or blank `{table}` value throws `ArgumentException` on first use, as Outbox does
   today, rather than failing inside the cache with a bare `NullReferenceException`.
