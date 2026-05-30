# Changelog

## 1.0.0 (2026-05-30)


### Features

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


### Bug Fixes

* **generator:** drop this. prefix on connection access (primary ctor params not field-accessible) ([996d6e8](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/996d6e8e10e46c9739499347c0911eb5d3d9b694))
* **generator:** escape C# keyword parameter names with @ prefix in emit ([e2aa3d4](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/e2aa3d470b10012e25c93a42fddd154b760b9aed))
* **generator:** escape CancellationToken parameter name with @ when it's a keyword ([619f55a](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/619f55ae1fd7603429c839076f7fe02ae6752f44))
* **generator:** preserve user parameter order and CancellationToken name in emit ([8f34d53](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/8f34d531d7508c13c4d0b959b99374652b24f302))
* **generator:** require System.Data.Async namespace for simple-name IAsyncDbConnection match ([3fc3b12](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/3fc3b1244d73185ae37f091b516cd82aaa0ca8ee))
* **generator:** tighten diagnostic checks (ZAO007 message + missing CT case, ZAO008 tuple detection) ([ff4072a](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/ff4072ac1beb30b83bf626e1e30f8964864ca9ac))


### Code Refactoring

* **generator:** add defensive Debug.Assert against empty repo methods ([43a1223](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/43a12236b63182ddf89c32c14d795768847f2a36))
* **generator:** clean up diagnostic plumbing post-3.2 ([4e5de76](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/4e5de767b754c25d993fcf074c81b710dc721955))
* **generator:** drop unused TypeDisplay field from primitive reader info ([d824781](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/d824781169a911e125f67e9f497daa0f83292101))
* **generator:** hoist ContainingTypeName + Namespace to QueryRepositoryModel ([83f45e6](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/83f45e66493bf72f939e3c15ae68fd5329498ac1))
* **generator:** wrap ImmutableArray in EquatableArray for cache correctness ([01d1ee7](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/01d1ee783a6dad4593739b319ada139535dc24a3))


### Documentation

* **generator:** document first-match semantics in ResolveConnectionAccess ([bf78073](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/bf780733145abc30878b72512d9290999ebf7e8e))
* **generator:** document type-scoped emit and ZAO005 Query-only limitation ([023600e](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/023600eeaf9e3641dc8e04f29f01326f65849657))
