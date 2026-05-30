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
ZAO021  | ZeroAlloc.ORM | Info     | [Query(Batch = ...)] non-Auto value not yet implemented in v0.1
ZAO022  | ZeroAlloc.ORM | Info     | Return type shape not yet supported in v0.1
ZAO040  | ZeroAlloc.ORM | Error    | No construction strategy resolved for type
ZAO041  | ZeroAlloc.ORM | Error    | No binding strategy resolved for parameter
