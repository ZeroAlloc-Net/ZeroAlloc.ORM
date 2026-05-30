# Changelog

## 0.1.0 (2026-05-30)


### ⚠ BREAKING CHANGES

* removed CommandAttribute, CommandKind, StoredProcedureAttribute, MaterializeAttribute, MaterializeStrategy, StoreAsStringAttribute from ZeroAlloc.ORM.Abstractions. Consumers using these types in v0.1.0-preview.x must remove the usages; they had no functional codegen anyway.

### R1

* Trim public API to v0.1 surface; add ZAO020/ZAO021 info diagnostics ([#10](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/issues/10)) ([2dc6025](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/2dc6025ef277a8fd3f851bd90958044bb5d29c33))


### Features

* **abstractions:** add CommandAttribute + CommandKind enum ([2c1ac20](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/2c1ac20766e133f2ee67a417baebfb39ddfbdbcd))
* **abstractions:** add MaterializeAttribute + MaterializeStrategy enum ([9c46069](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/9c4606983fb9459a2322299e5b1c30d409653fe7))
* **abstractions:** add ParamAttribute ([12b8b00](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/12b8b00e3554d00f46be9364317fa29c3a30df53))
* **abstractions:** add QueryAttribute + BatchMode enum ([ba4c6f9](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/ba4c6f9c8141b9c019a6f4e30352e41169554f46))
* **abstractions:** add StoreAsStringAttribute ([6235481](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/62354814715485d5b9ed170b0630102ba2b8f26c))
* **abstractions:** add StoredProcedureAttribute ([3c7543b](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/3c7543bc5beb2b77f899a5c71e7844593488f851))
* **generator:** add diagnostic descriptor catalog ZAO001-ZAO009 ([a6b5340](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/a6b53402eb0beacd3a2fdcb666c57a314b7f3cd7))
* **generator:** add OrmGenerator skeleton (empty Initialize) ([7e2db26](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/7e2db26252869d43e8305a26dd9f33ca8c4dca16))
* **generator:** bind nullable primitive parameters with DBNull guard ([ca457a8](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/ca457a81b77ad226d2caebc2df93c913df4d23ce))
* **generator:** emit FlatRow materialization for positional records ([1074cc4](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/1074cc41d37a81286bd0ba4494158b37ac0e093a))
* **generator:** emit primitive parameter binding (int/string/decimal/Guid/DateTime/...) ([04a59dc](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/04a59dc9a2fe26a9953cffcfeddf1adc14ac4162))
* **generator:** emit Task&lt;int&gt; scalar materialization ([d5aaad1](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/d5aaad1d53e6b66423f4b796622fd62a207e595f))
* **generator:** emit Task&lt;T?&gt; single-row scalar with null guards ([df6a024](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/df6a024caa7e6fc0e87c2b40fab3075ee10c7ef5))
* **generator:** emit ZAO001 when annotated method is not partial ([3281d5d](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/3281d5d5aeb87cd9330f838d76aa87d92d14d530))
* **generator:** emit ZAO002 when return type is unsupported ([39bd934](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/39bd934fce39f684efd7398f66b96c1a496435a2))
* **generator:** emit ZAO003 when containing type has no IAsyncDbConnection source ([1dd3086](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/1dd30864749f8839e58e91c28897d04dd27ed2f8))
* **generator:** emit ZAO004 when containing type is not partial ([61e4e25](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/61e4e259df03efa23092bbd28e78a85f54fb046d))
* **generator:** emit ZAO005 when method has multiple ORM attributes ([35d6a06](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/35d6a061c6f998235fb86ef39606d907ab72d7b9))
* **generator:** emit ZAO006 when method has multiple CancellationToken parameters ([d8e7be1](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/d8e7be1ea7ac4705c060cbdbf5cc93c0697d2f2a))
* **generator:** emit ZAO007 when IAsyncEnumerable return lacks [EnumeratorCancellation] ([afbdd74](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/afbdd74e41e87a9400e4eb17f8283c3521d4a828))
* **generator:** emit ZAO008 when multi-statement SQL has single-result return type ([83344f1](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/83344f18efaf727378b5fd9c59cc1062badb7df6))
* **generator:** emit ZAO009 when partial method declaration has redundant async keyword ([9e2d620](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/9e2d620c2d38eb2999005cc31d331dede1dbcf9f))
* **generator:** extend PrimitiveCatalog with DateTimeOffset, TimeSpan, byte[] ([855e4da](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/855e4da89c4b0b53144d8b609a8d64ef8da086dc))
* **generator:** honor [Param(Name)] for SQL-side parameter override ([c276914](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/c276914e27e252dc7cdf740a2495338799059640))
* **generator:** resolve IAsyncDbConnection from primary-ctor/field/property ([fd36890](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/fd368909e64527e22c27b79dd82569873428b117))
* **generator:** scan [Query]-annotated methods, emit stub partial per containing type ([6e66c0a](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/6e66c0a39e8b2a28bfc3dc5c43464bbcaba02cfd))
* **orm:** add runtime exception types ([c3cff0c](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/c3cff0c93078fa5c76f8f774b1a51e8c57aaa66e))


### Bug Fixes

* **abstractions:** add IsExternalInit polyfill for netstandard2.0 target ([4cb5551](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/4cb55518632914a0b3e25c9bf419be5e770ccd4c))
* **generator:** drop this. prefix on connection access (primary ctor params not field-accessible) ([996d6e8](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/996d6e8e10e46c9739499347c0911eb5d3d9b694))
* **generator:** escape C# keyword parameter names with @ prefix in emit ([e2aa3d4](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/e2aa3d470b10012e25c93a42fddd154b760b9aed))
* **generator:** escape CancellationToken parameter name with @ when it's a keyword ([619f55a](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/619f55ae1fd7603429c839076f7fe02ae6752f44))
* **generator:** preserve user parameter order and CancellationToken name in emit ([8f34d53](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/8f34d531d7508c13c4d0b959b99374652b24f302))
* **generator:** require System.Data.Async namespace for simple-name IAsyncDbConnection match ([3fc3b12](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/3fc3b1244d73185ae37f091b516cd82aaa0ca8ee))
* **generator:** tighten diagnostic checks (ZAO007 message + missing CT case, ZAO008 tuple detection) ([ff4072a](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/ff4072ac1beb30b83bf626e1e30f8964864ca9ac))
* **integration:** add ConfigureAwait(false) to async ops ([7541566](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/754156690ee52c7a0d9ed5a4c1915073766be1f0))


### Code Refactoring

* **abstractions:** drop unused GenericParameter target from MaterializeAttribute ([65ed2f9](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/65ed2f91676575cb66590d63e91e976c6ef6b8c3))
* **generator-tests:** disambiguate PrimaryCtor snapshot to prove the branch fires ([377d474](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/377d474f8cb49fe58cb79d5ff71c7410dca68f74))
* **generator-tests:** simplify ModuleInitializer to use using directive ([2e9a676](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/2e9a676f62699780a9f38fb83ca4aa8935cec928))
* **generator:** add defensive Debug.Assert against empty repo methods ([43a1223](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/43a12236b63182ddf89c32c14d795768847f2a36))
* **generator:** clean up diagnostic plumbing post-3.2 ([4e5de76](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/4e5de767b754c25d993fcf074c81b710dc721955))
* **generator:** drop unused TypeDisplay field from primitive reader info ([d824781](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/d824781169a911e125f67e9f497daa0f83292101))
* **generator:** hoist ContainingTypeName + Namespace to QueryRepositoryModel ([83f45e6](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/83f45e66493bf72f939e3c15ae68fd5329498ac1))
* **generator:** wrap ImmutableArray in EquatableArray for cache correctness ([01d1ee7](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/01d1ee783a6dad4593739b319ada139535dc24a3))
* **packaging:** drop ORM.Analyzers from v0.1, hoist PrimitiveCatalog into TypeConversions ([#11](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/issues/11)) ([7bfd7f2](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/7bfd7f2d230e9cd4fa67a5d47bdef6c634476fa7))


### Documentation

* **backlog:** mark v0.1 milestone complete ([105a004](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/105a00425dc1a0dedfbeaecf4b1b0a4da44f943f))
* **backlog:** record v0.1 implementation progress ([70e2f21](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/70e2f21de7b2a35c3be1e869cb871c8191babf48))
* **generator:** document first-match semantics in ResolveConnectionAccess ([bf78073](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/bf780733145abc30878b72512d9290999ebf7e8e))
* **generator:** document type-scoped emit and ZAO005 Query-only limitation ([023600e](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/023600eeaf9e3641dc8e04f29f01326f65849657))
* **plan:** apply corrections from review (versions, paths, polyfill, harness) ([6a3aef8](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/6a3aef88bfb210b6cc2d852e9e5e4e6ca0b776bf))
* **plan:** forward plan post-v0.1-review (R1-R12 + milestone adjustments) ([#9](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/issues/9)) ([37e6d18](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/37e6d180f78501d96493bd759260bd2558c2510d))
* **plan:** v0.1 milestone implementation plan ([d35a835](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/d35a83539288e1d38a342780d80d7879cedf3158))


### Tests

* **abstractions:** bootstrap xUnit test project ([fb3c089](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/fb3c089254b66b32c2b1b4212fd1961087a7b2a5))
* **aot:** add AOT smoke test consumer + activate CI gate ([1fdeedc](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/1fdeedc15997283ee0cc78f4e029c5cf21258dee))
* **generator:** add compile-smoke coverage for Param(Name) + nullable param emit ([6acebbf](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/6acebbffdca59c75f0f88efa9b8794e6c93e34a6))
* **generator:** add compile-smoke harness to catch emit semantic regressions ([406f4be](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/406f4bebbe578f40e7f9c811b7f632b71522359a))
* **generator:** bootstrap Verify.NET snapshot rig (build will fail until generator skeleton lands) ([9eded7b](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/9eded7b9b7b345b1741b7edf4e8f31be22ef2549))
* **generator:** lock Task&lt;int?&gt; nullable scalar snapshot ([30db555](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/30db555dc35dd6870fea77d7ae657fb8a09e5df7))
* **integration:** bootstrap Sqlite fixture for integration tests ([ad5e79c](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/ad5e79ce0b63e7b32f1e2ef6122097354d77c44d))
* **integration:** FlatRow round-trip against Sqlite (parameterless) ([73c5b77](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/73c5b7774880c858ca40a80e45e5ec45556b5f18))
* **integration:** primitive parameter round-trip suite ([c0ac5b9](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/c0ac5b90a9eac6b2b5410143df6aa069e7d8b3d1))
* **integration:** SELECT 42 returns 42 via Task&lt;int&gt; emit ([7007cc0](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/7007cc093b312cbddbe911a9756992d146ca27dc))
