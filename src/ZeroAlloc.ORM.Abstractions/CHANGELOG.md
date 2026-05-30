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


### Bug Fixes

* **abstractions:** add IsExternalInit polyfill for netstandard2.0 target ([4cb5551](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/4cb55518632914a0b3e25c9bf419be5e770ccd4c))


### Code Refactoring

* **abstractions:** drop unused GenericParameter target from MaterializeAttribute ([65ed2f9](https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/commit/65ed2f91676575cb66590d63e91e976c6ef6b8c3))
