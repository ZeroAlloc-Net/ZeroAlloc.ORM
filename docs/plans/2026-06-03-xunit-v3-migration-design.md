# xunit.v3 + Verify.XunitV3 Migration — Design

**Status:** approved 2026-06-03
**Scope:** ZeroAlloc.ORM `tests/`, dependency migration
**Closes:** part of Renovate Dependency Dashboard #8 (deprecated `xunit` + `Verify.Xunit` flags)
**Branch:** `chore/xunit-v3-migration` off `main` at `b234811` (v1.5.0)

## Background

Renovate's Dependency Dashboard (#8) flags `xunit` 2.9.3 and `Verify.Xunit` 31.12.5 as deprecated. Five test projects across ZA.ORM use xunit 2.9.3:

1. `tests/ZeroAlloc.ORM.Abstractions.Tests`
2. `tests/ZeroAlloc.ORM.Generator.Tests` — uses Verify.Xunit + 266 snapshot tests
3. `tests/ZeroAlloc.ORM.Integration.Tests` — uses Testcontainers Postgres + ~114 tests
4. `tests/ZeroAlloc.ORM.Tests`
5. `tests/ZeroAlloc.TypeConversions.Tests`

Renovate only flagged 2 of these, but all 5 share the version — migrating partially would diverge the test stack.

## Key finding: shallow xunit-feature usage

Survey of all 125 test files reveals the codebase **deliberately avoids xunit's fixture infrastructure**. `PostgresFixture.cs:18-21` explicitly documents the choice:

> Why not xUnit's `IAsyncLifetime`? The integration suite's existing pattern is per-test instantiation [...] `IAsyncLifetime` needs an `IClassFixture<T>` hook on every test class, which couples the suite.

Confirmed across the 125 test files:
- No `IClassFixture<T>` / `ICollectionFixture<T>`
- No `IAsyncLifetime`
- No `ITestOutputHelper`

The codebase uses only the stable shapes: `[Fact]`, `[Theory]`, `[InlineData]`, `Assert.X`. **Most of xunit.v3's breaking changes don't apply.** The migration is essentially package bumps.

## Decision

Big-bang migration: bump all 5 test projects from `xunit` 2.9.3 → `xunit.v3` (latest stable) + replace `Verify.Xunit` → `Verify.XunitV3` in Generator.Tests, in a single PR. The codebase's shallow xunit usage makes this safe.

## What changes

**Files modified (5 csproj + 0-1 source files):**

1-5. Each test `.csproj`:
   - `xunit 2.9.3` → `xunit.v3` (current stable; the implementer checks NuGet for the latest stable version. As of late 2025 / early 2026 this is roughly `1.x` or `2.x` of `xunit.v3` — confirm).
   - `xunit.runner.visualstudio 3.1.5` → latest version that supports xunit.v3 (likely already-compatible; the v3 runner ships under the same package name).
   - `Microsoft.NET.Test.Sdk 18.6.0` → confirm minimum required version for xunit.v3 (likely no bump needed; 18.x is recent enough).
   - Add `<IsTestProject>true</IsTestProject>` to `<PropertyGroup>` if not already set (may be auto-detected by the SDK based on `<PackageReference Include="xunit.v3" />` but worth verifying).

6. `tests/ZeroAlloc.ORM.Generator.Tests/*.csproj` additionally:
   - `Verify.Xunit 31.12.5` → `Verify.XunitV3` (or whatever the current v3-compatible Verify package name is — check `Verify.Xunit`'s NuGet deprecation page for the canonical successor).
   - `Verify.SourceGenerators 2.5.0` likely stays (it's not xunit-tied; confirm during implementation).

**Source files: likely 0 changes.** The `using` directives may need minor adjustment:
- `using static VerifyXunit.Verifier;` — *may* need to change to a v3 namespace (e.g. `VerifyXunit.VerifierV3` or stay the same; implementer confirms by reading the package's docs after install).
- `using Xunit;` — unchanged in xunit.v3.
- `Assert.X` / `[Fact]` / `[Theory]` / `[InlineData]` — unchanged.

If a source change IS needed for Verify namespaces, it would touch ~20-30 test files (every `Emit/` snapshot test). All identical-shape global usings edit.

**Snapshot stability check (main risk):**
All 266 Verify snapshot tests in `Generator.Tests/Snapshots/*.verified.cs` should produce byte-identical output after the package swap. If any diff:
- Investigate whether Verify changed default serializer settings between v31 (xunit2-compatible) and the v3-compatible package
- If cosmetic (formatting, whitespace), update Verify settings or accept the new shape as the baseline (with a commit explaining)

## Versioning + release

- All commits use `chore(deps):` conventional-commit type
- Default release-please config doesn't bump for `chore:` — this change accumulates until the next user-facing feat/fix triggers a release. Fine; the migration is internal.

## Acceptance criteria

- [ ] All 5 test projects compile on xunit.v3
- [ ] All test runs green: Abstractions / Generator (266 snapshots) / Integration (~114) / Tests / TypeConversions
- [ ] Renovate Dependency Dashboard #8 no longer flags `xunit` or `Verify.Xunit` as deprecated
- [ ] Generator.Tests' 266 Verify snapshots stay byte-identical, OR any diff is documented + intentional

## What stays out of scope

- **FluentAssertions update / replacement** — separate concern (FA 8.x has license/usage changes that warrant their own brainstorm if relevant; not in this PR).
- **AotSmoke / GeneratorCollision.AotSmoke / Benchmarks projects** — don't use xunit; untouched.
- **Test refactoring beyond the migration** — e.g. adopting xunit.v3-only features. Out of scope; this is a deps-only migration.
- **CI workflow changes** — the existing `dotnet test` invocations should work unchanged with xunit.v3's vstest adapter.

## Risk

- **Verify snapshot drift between v31 and v3-compatible packages.** Mitigation: inspect any `.received.cs` diffs carefully; if cosmetic, accept; if behavioral, STOP and investigate before promoting.
- **xunit.v3 `<IsTestProject>` semantics.** Most modern SDKs auto-detect; implementer verifies by checking csproj build output. If not auto-detected, explicit `<IsTestProject>true</IsTestProject>` solves it.
- **`Microsoft.NET.Test.Sdk` minimum version for v3.** Implementer checks the v3 release notes / NuGet metadata for the required minimum.

## Commit shape

Single atomic `chore(deps): migrate test suite from xunit 2.9.3 to xunit.v3` commit. If source changes are needed (e.g. Verify using statements), a second `chore(deps): adjust Verify using statements for xunit.v3 namespace` commit. Either way, `chore(deps):` squash title at merge.
