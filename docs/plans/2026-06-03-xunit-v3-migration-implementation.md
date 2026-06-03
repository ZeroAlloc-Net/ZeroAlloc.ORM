# xunit.v3 + Verify.XunitV3 Migration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Big-bang migrate all 5 ZA.ORM test projects from `xunit` 2.9.3 (deprecated) to `xunit.v3` + replace `Verify.Xunit` 31.x with its v3-successor package — clearing the Renovate Dependency Dashboard's `xunit` / `Verify.Xunit` deprecation flags without touching test source semantics.

**Architecture:** Four tasks: (1) research the exact target versions for `xunit.v3` / `Verify.XunitV3` / `Microsoft.NET.Test.Sdk` compatibility via NuGet; (2) bump all 5 `*.csproj` files + apply any required source-level `using` changes; (3) build + run all 5 test projects, verifying 266 Verify snapshots stay byte-identical; (4) push + PR + admin-merge with `chore(deps):` squash title.

**Tech Stack:** xunit.v3 (target) / Verify.XunitV3 / Microsoft.NET.Test.Sdk / .NET 10.

**Reference design doc:** `docs/plans/2026-06-03-xunit-v3-migration-design.md` (committed `0738ab2` on this branch).

**Working branch:** `chore/xunit-v3-migration` (already created off `main` at `b234811`).

> **Local SDK pin gotcha:** `global.json` pins `10.0.300 latestFeature`; dev machine has 10.0.204 max. Before any `dotnet` invocation:
> ```powershell
> (Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
> ```
> Revert with `git checkout global.json` before each commit. **Never commit the relaxed pin.**

> **Note on the codebase's xunit usage:** survey of all 125 test files confirms the migration surface is shallow — no `IClassFixture<T>`, no `ICollectionFixture<T>`, no `IAsyncLifetime`, no `ITestOutputHelper`. Just `[Fact]` / `[Theory]` / `[InlineData]` / `Assert.X`. Most xunit.v3 breaking changes don't apply.

---

### Task 1: Research target versions

**Files:** none (research only)

**Step 1: Check `xunit.v3` latest stable on NuGet**

Use WebFetch on `https://www.nuget.org/packages/xunit.v3` — capture the latest stable version (likely `1.0.x` or `2.0.x` as of mid-2026). Note: `xunit.v3` is a **separate package** from `xunit` — the v2 package `xunit` is what's being deprecated.

**Step 2: Check `xunit.runner.visualstudio` compatibility with v3**

WebFetch on `https://www.nuget.org/packages/xunit.runner.visualstudio` — look at the version notes for which release added xunit.v3 support. The 3.x series (current pin is 3.1.5) is xunit-v3-aware; confirm the exact minimum version required for v3 and whether 3.1.5 already works or needs a bump.

**Step 3: Find Verify.Xunit's v3 successor package**

WebFetch on `https://www.nuget.org/packages/Verify.Xunit` — the deprecation message on the package page should name the canonical successor (likely `Verify.XunitV3` but **DO NOT GUESS** — confirm via the official deprecation text).

**Step 4: Check Microsoft.NET.Test.Sdk minimum for xunit.v3**

WebFetch on `https://www.nuget.org/packages/Microsoft.NET.Test.Sdk` — check the release notes for any v18.x requirements specific to xunit.v3. Current pin is 18.6.0; verify whether xunit.v3 requires a higher minimum.

**Step 5: Verify `Verify.SourceGenerators` 2.5.0 compatibility**

WebFetch on `https://www.nuget.org/packages/Verify.SourceGenerators` — confirm whether this package is xunit-flavor-agnostic (it should be — it's the diff hook for SG output), or whether a v3-companion exists.

**Step 6: Record the findings**

Write down (in your report back) the exact `(package, version)` tuples you'll use in Task 2:

```
xunit.v3              → <VERSION>
xunit.runner.visualstudio → <VERSION_or_unchanged>
Verify.XunitV3 (or whatever package) → <VERSION>
Microsoft.NET.Test.Sdk → <VERSION_or_unchanged>
Verify.SourceGenerators → <VERSION_or_unchanged>
```

ALSO note from each NuGet page:
- Whether `using static VerifyXunit.Verifier;` stays valid in the v3-successor package OR needs to change (e.g. to `VerifyXunit.VerifierV3` or similar)
- Whether xunit.v3 needs explicit `<IsTestProject>true</IsTestProject>` in csproj (likely auto-detected by SDK)
- Whether any other top-level using statements change (e.g. `using Xunit;` is typically unchanged)

This task ends with a research-summary message; no commit.

---

### Task 2: Bump packages + any required source changes

**Files:**
- Modify: `tests/ZeroAlloc.ORM.Abstractions.Tests/ZeroAlloc.ORM.Abstractions.Tests.csproj`
- Modify: `tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj`
- Modify: `tests/ZeroAlloc.ORM.Integration.Tests/ZeroAlloc.ORM.Integration.Tests.csproj`
- Modify: `tests/ZeroAlloc.ORM.Tests/ZeroAlloc.ORM.Tests.csproj`
- Modify: `tests/ZeroAlloc.TypeConversions.Tests/ZeroAlloc.TypeConversions.Tests.csproj`
- Possibly modify: `tests/ZeroAlloc.ORM.Generator.Tests/**/*.cs` (Verify `using` statements, IF Task 1 confirmed namespace change)

**Step 1: Update each `*.csproj` package references**

For each of the 5 csproj files, perform these edits using Edit:

(a) Replace `xunit` reference:
- `old_string`: `<PackageReference Include="xunit" Version="2.9.3" />`
- `new_string`: `<PackageReference Include="xunit.v3" Version="<NEW_VERSION>" />`

(b) If Task 1 found xunit.runner.visualstudio needs a bump (likely not — 3.1.5 already supports v3):
- `old_string`: `<PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />`
- `new_string`: `<PackageReference Include="xunit.runner.visualstudio" Version="<NEW_VERSION>" />`

(c) If Task 1 found Microsoft.NET.Test.Sdk needs a bump:
- `old_string`: `<PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.6.0" />`
- `new_string`: `<PackageReference Include="Microsoft.NET.Test.Sdk" Version="<NEW_VERSION>" />`

**Step 2: For `ZeroAlloc.ORM.Generator.Tests/*.csproj` specifically, replace `Verify.Xunit`**

- `old_string`: `<PackageReference Include="Verify.Xunit" Version="31.12.5" />`
- `new_string`: `<PackageReference Include="<VERIFY_V3_PACKAGE>" Version="<NEW_VERSION>" />`

(`<VERIFY_V3_PACKAGE>` is whatever Task 1 confirmed — e.g. `Verify.XunitV3`.)

**Step 3: Restore packages**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet restore ZeroAlloc.ORM.slnx
```

Expected: restore succeeds. If a package isn't found ("NU1102"), Task 1's version research was wrong — STOP and re-research.

**Step 4: Apply Verify `using` changes if needed**

If Task 1 confirmed that `using static VerifyXunit.Verifier;` needs to change to e.g. `using static VerifyXunit.VerifierV3;`, find and replace across `tests/ZeroAlloc.ORM.Generator.Tests/`:

```powershell
# Pseudo-syntax — use the Edit tool with replace_all=true OR Grep + per-file Edit:
# Grep for "using static VerifyXunit.Verifier;" → list files
# Edit each file with the namespace swap
```

Use the Grep tool to find all occurrences of `using static VerifyXunit.Verifier`. For each file found, apply the Edit. If there are 5+ files, document the count.

**Step 5: Build verification**

```powershell
dotnet build ZeroAlloc.ORM.slnx -c Release 2>&1 | Select-Object -Last 5
git checkout global.json
```

Expected: green, 0 errors. If the build fails:

- **Symbol not found** (e.g. `VerifyXunit.Verifier` no longer exists): your Verify `using` swap is incomplete or in the wrong direction. Re-check Task 1's namespace finding.
- **`[Fact]` attribute resolves ambiguously**: xunit.v3 and xunit are both transitively pulled in. The xunit 2.x package shouldn't appear unless something else depends on it — check `dotnet list package --include-transitive`.
- **CS0246 missing assembly**: a `using Xunit;` needs to change (rare; v3 is xunit-namespace-compatible). Address per the specific error.

If the build is green, **leave the working tree dirty** — Task 3 verifies the test runs before committing.

---

### Task 3: Build + run all 5 test projects + verify snapshot stability

**Files:** none (verification only)

**Step 1: Run all 5 test projects**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json

dotnet test tests/ZeroAlloc.ORM.Abstractions.Tests/ZeroAlloc.ORM.Abstractions.Tests.csproj -c Release
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj -c Release
dotnet test tests/ZeroAlloc.ORM.Integration.Tests/ZeroAlloc.ORM.Integration.Tests.csproj -c Release
dotnet test tests/ZeroAlloc.ORM.Tests/ZeroAlloc.ORM.Tests.csproj -c Release
dotnet test tests/ZeroAlloc.TypeConversions.Tests/ZeroAlloc.TypeConversions.Tests.csproj -c Release

git checkout global.json
```

Expected baselines (per the design doc):
- Abstractions.Tests: unchanged count
- Generator.Tests: **266 passed** (same as pre-migration)
- Integration.Tests: ~114 passed (Postgres-dependent tests skipped if Docker isn't running)
- ZeroAlloc.ORM.Tests: unchanged count
- TypeConversions.Tests: unchanged count

**Step 2: Verify NO `.received.cs` snapshot files appeared**

The 266 Verify snapshots in `Generator.Tests/Snapshots/*.verified.cs` should produce **byte-identical** output. If Verify's defaults changed between v31 and v3, snapshots would diff and `.received.cs` files would appear.

```powershell
Get-ChildItem tests/ZeroAlloc.ORM.Generator.Tests -Filter "*.received.*" -Recurse 2>&1
```

Expected: **no output** (no received files).

**If any `.received.cs` files appeared, STOP and investigate** before promoting:

1. Read 1-2 received files vs their `.verified.cs` siblings — what changed?
2. If cosmetic (whitespace, trailing newlines): may be safe to accept, but DO NOT promote silently. Report the diff to the user with the cause analysis.
3. If semantic (e.g. type names or syntax changed): that's a bigger issue — likely Verify.SourceGenerators behavior change. Report.

**Step 3: If snapshots stable, commit the migration**

```powershell
git checkout global.json
git add tests/ZeroAlloc.ORM.Abstractions.Tests/ZeroAlloc.ORM.Abstractions.Tests.csproj
git add tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj
git add tests/ZeroAlloc.ORM.Integration.Tests/ZeroAlloc.ORM.Integration.Tests.csproj
git add tests/ZeroAlloc.ORM.Tests/ZeroAlloc.ORM.Tests.csproj
git add tests/ZeroAlloc.TypeConversions.Tests/ZeroAlloc.TypeConversions.Tests.csproj
# Plus any source files modified in Task 2 (Verify using statements)
git add tests/ZeroAlloc.ORM.Generator.Tests/  # if Verify usings changed
```

Then commit:

```powershell
git commit -m "chore(deps): migrate test suite from xunit 2.9.3 to xunit.v3

Replaces deprecated xunit 2.9.3 + Verify.Xunit 31.12.5 with xunit.v3
+ <VERIFY_V3_PACKAGE>. Closes the deprecation flags in Renovate
Dependency Dashboard #8.

Migration scope:
  - 5 test projects (Abstractions / Generator / Integration / Tests
    / TypeConversions)
  - xunit 2.9.3 -> xunit.v3 <VERSION>
  - Verify.Xunit 31.12.5 -> <VERIFY_V3_PACKAGE> <VERSION>
  - Microsoft.NET.Test.Sdk: <unchanged or bumped>
  - xunit.runner.visualstudio: <unchanged or bumped>

Source changes: <none / N files with Verify using statement adjustment>.

The codebase deliberately avoids xunit's fixture infrastructure
(PostgresFixture.cs:18-21 documents the choice) — no IClassFixture,
no IAsyncLifetime, no ITestOutputHelper — so most of xunit.v3's
breaking changes don't apply.

All <N> tests pass. 266 Verify snapshots in Generator.Tests stay
byte-identical (verified via no .received.cs files post-run)."
```

(Fill in the actual `<...>` placeholders from Task 1 + Task 3.)

If Task 2 had a separate source-level change (Verify using statements), consider splitting into two commits:
1. `chore(deps): bump xunit 2.9.3 -> xunit.v3 + Verify.Xunit -> <VERIFY_V3_PACKAGE>` (csproj bumps only)
2. `chore(deps): adjust Verify using statements for xunit.v3 namespace` (source-level usings)

Decide based on diff size — if usings touched 10+ files, split for clarity; if 0-2, keep atomic.

---

### Task 4: Push + PR + admin-merge

**Step 1: Pre-flight log check**

```powershell
git log --oneline main..HEAD
```

Expected (depending on commit-split decision in Task 3):
- 2 commits: design doc + migration commit, OR
- 3 commits: design doc + csproj bumps + Verify using adjustment

**Step 2: Final sweep**

```powershell
(Get-Content global.json) -replace '10\.0\.300','10.0.100' | Set-Content global.json
dotnet build ZeroAlloc.ORM.slnx -c Release
dotnet test tests/ZeroAlloc.ORM.Generator.Tests/ZeroAlloc.ORM.Generator.Tests.csproj -c Release
dotnet test tests/ZeroAlloc.ORM.Integration.Tests/ZeroAlloc.ORM.Integration.Tests.csproj -c Release
git checkout global.json
git status
```

Expected: build green, all tests pass, working tree clean.

**Step 3: Push**

```powershell
git push -u origin chore/xunit-v3-migration
```

**Step 4: Open the PR**

```powershell
$prBody = @'
## Summary

Closes the `xunit` 2.9.3 + `Verify.Xunit` 31.12.5 deprecation flags from Renovate Dependency Dashboard #8. Big-bang migrates all 5 ZA.ORM test projects to xunit.v3 + the corresponding Verify v3 package.

## What changes

**Package bumps in 5 test projects** (`tests/*.csproj`):
- `xunit 2.9.3` → `xunit.v3 <VERSION>`
- `Verify.Xunit 31.12.5` → `<VERIFY_V3_PACKAGE> <VERSION>` (in Generator.Tests only)
- `Microsoft.NET.Test.Sdk`: `<unchanged or bumped>`
- `xunit.runner.visualstudio`: `<unchanged or bumped>`

**Source changes:** `<none, or describe — Verify using statement adjustment in N files>`

## Why this is safe

The codebase deliberately avoids xunit''s fixture infrastructure — `PostgresFixture.cs:18-21` explicitly documents the choice:
> Why not xUnit''s `IAsyncLifetime`? [...] it would couple every test class.

Survey of all 125 test files confirms: no `IClassFixture<T>`, no `ICollectionFixture<T>`, no `IAsyncLifetime`, no `ITestOutputHelper`. The migration is essentially package bumps — most of xunit.v3''s breaking changes don''t apply.

## Snapshot stability (the main risk)

266 Verify snapshots in `tests/ZeroAlloc.ORM.Generator.Tests/Snapshots/*.verified.cs` — all stay **byte-identical** after the package swap. Verified by running the full Generator.Tests suite post-migration with no `.received.cs` files appearing.

## Test plan

- [x] Build green
- [x] All 5 test projects pass (Abstractions / Generator 266/266 / Integration ~114/114 / Tests / TypeConversions)
- [x] Generator.Tests Verify snapshots byte-identical (no `.received.cs` files post-run)
- [ ] CI: lint + build-test + collision-smoke + aot-publish-smoke

## Note for release-please

`chore(deps):` commit. Default release-please config doesn''t bump for `chore:` types — this change rolls up into the next versioned PR.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
'@

gh pr create --title "chore(deps): migrate test suite from xunit 2.9.3 to xunit.v3" --body $prBody
```

(Substitute `<VERSION>` and `<VERIFY_V3_PACKAGE>` with the actual values from Task 1.)

Capture the PR number.

**Step 5: Monitor CI**

```powershell
gh pr checks <PR_NUMBER> --watch
```

Expected check set (per recent ORM PRs): `lint`, `build-test`, `collision-smoke`, `aot-publish-smoke`. Wait for all green.

If `build-test` fails on CI (Linux):
- Most likely cause: a `Microsoft.NET.Test.Sdk` Linux runtime mismatch. Inspect log.
- Less likely: a snapshot CRLF/LF normalization difference — would show as a snapshot diff inside the test run. The repo's `.gitattributes` should prevent this.

DON'T push fixes blindly. Read the actual CI log first.

**Step 6: Admin-merge once green**

```powershell
gh pr merge <PR_NUMBER> --squash --delete-branch --admin
```

Squash title starts with `chore(deps):` — release-please default won't trigger a release. Fine.

**Step 7: Verify post-merge**

```powershell
git checkout main
git pull --ff-only
git log --oneline -3
```

**Step 8: Verify Renovate dashboard clears**

The Renovate Dependency Dashboard regenerates on a schedule (typically every hour or on demand). Check after ~1-2 hours:

```powershell
gh issue view 8 2>&1 | head -50
```

Expected: `xunit` 2.9.3 and `Verify.Xunit` 31.12.5 no longer appear under "Deprecations / Replacements". If they still appear, give Renovate another cycle.

**Step 9: Report**

- Versions adopted (xunit.v3 / Verify v3 package / any Microsoft.NET.Test.Sdk bump)
- Source-level changes (none, or N files)
- Test counts per project (should match baseline)
- PR URL + merge SHA on main
- Snapshot stability confirmation (no `.received.cs` files post-migration)
- Renovate dashboard state after ~1-2 hours

Do NOT push fixes blindly to CI failures. Investigate first.

---

## Out of scope (deliberately not in this plan)

- **FluentAssertions update / replacement** — separate concern; FA 8.x has its own license/usage changes that warrant a separate brainstorm if relevant.
- **AotSmoke / GeneratorCollision.AotSmoke / Benchmarks projects** — don't use xunit; not touched.
- **Test refactoring beyond migration** — e.g. adopting xunit.v3-only features. Out of scope.
- **CI workflow YAML changes** — the existing `dotnet test` invocations should work unchanged with xunit.v3's vstest adapter.

## When the plan is complete

The branch `chore/xunit-v3-migration` has 2-3 commits (1 design + 1-2 migration) + merge squash on main. Renovate Dependency Dashboard #8 no longer flags `xunit` 2.9.3 or `Verify.Xunit` 31.12.5 as deprecated. ZA.ORM's test suite runs on xunit.v3 with 266 Verify snapshots byte-identical.
