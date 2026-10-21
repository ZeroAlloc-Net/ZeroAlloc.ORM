; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category      | Severity | Notes
--------|---------------|----------|-------
ZAO100  | ZeroAlloc.ORM | Error    | Invalid LINQ companion
ZAO101  | ZeroAlloc.ORM | Error    | LINQ companion parameters do not match the method
ZAO102  | ZeroAlloc.ORM | Error    | Unsupported LINQ construct
ZAO103  | ZeroAlloc.ORM | Error    | LINQ query captures a value
ZAO104  | ZeroAlloc.ORM | Error    | LINQ entity has no table map
ZAO105  | ZeroAlloc.ORM | Error    | LINQ member is not a mapped column
ZAO106  | ZeroAlloc.ORM | Error    | Invalid entity map
ZAO107  | ZeroAlloc.ORM | Error    | Unsupported LINQ projection
ZAO108  | ZeroAlloc.ORM | Error    | LINQ query requires the PostgreSQL dialect
ZAO109  | ZeroAlloc.ORM | Error    | LINQ companion is called
ZAO110  | ZeroAlloc.ORM | Warning  | LINQ paging without OrderBy
ZAO111  | ZeroAlloc.ORM | Error    | LINQ enum parameter compared with differently stored columns
ZAO112  | ZeroAlloc.ORM | Error    | LINQ method return type does not fit the query
