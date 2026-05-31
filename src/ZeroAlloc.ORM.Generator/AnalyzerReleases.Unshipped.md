; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category      | Severity | Notes
--------|---------------|----------|-------
ZAO001  | ZeroAlloc.ORM | Error    | Annotated method must be partial
ZAO002  | ZeroAlloc.ORM | Error    | Unsupported return type
ZAO003  | ZeroAlloc.ORM | Error    | No IAsyncDbConnection found on containing type
ZAO004  | ZeroAlloc.ORM | Error    | Containing type must be partial
ZAO005  | ZeroAlloc.ORM | Error    | Multiple ORM attributes on one method
ZAO006  | ZeroAlloc.ORM | Warning  | Method has multiple CancellationToken parameters
ZAO007  | ZeroAlloc.ORM | Error    | IAsyncEnumerable<T> return without [EnumeratorCancellation]
ZAO008  | ZeroAlloc.ORM | Error    | Multi-statement SQL with single-result return type
ZAO009  | ZeroAlloc.ORM | Warning  | Redundant async keyword on generated partial
ZAO020  | ZeroAlloc.ORM | Info     | [Query(FromResource)] not yet implemented in v0.1
ZAO022  | ZeroAlloc.ORM | Info     | Return type shape not yet supported in v0.1
ZAO032  | ZeroAlloc.ORM | Error    | Tuple arity exceeds SQL statement count
ZAO033  | ZeroAlloc.ORM | Error    | SQL statement count exceeds tuple arity
ZAO040  | ZeroAlloc.ORM | Error    | No construction strategy resolved for type
ZAO041  | ZeroAlloc.ORM | Error    | No binding strategy resolved for parameter
ZAO042  | ZeroAlloc.ORM | Error    | [StoreAsString] requires an enum type
ZAO043  | ZeroAlloc.ORM | Error    | [Materialize(Factory)] references missing method
ZAO044  | ZeroAlloc.ORM | Error    | Ambiguous convention discovery
ZAO050  | ZeroAlloc.ORM | Warning  | Nullable composite type requires runtime all-or-nothing check
ZAO051  | ZeroAlloc.ORM | Error    | Factory parameter does not match any SELECT column
ZAO052  | ZeroAlloc.ORM | Error    | Recursive composite types are not supported in v0.5
ZAO060  | ZeroAlloc.ORM | Error    | [StoredProcedure] async method has out/ref parameter (reserved)
ZAO061  | ZeroAlloc.ORM | Error    | [StoredProcedure] name is empty
ZAO062  | ZeroAlloc.ORM | Warning  | Named-tuple field does not match any parameter
ZAO063  | ZeroAlloc.ORM | Error    | [Param(Name = ...)] override is not supported on composite parameters
