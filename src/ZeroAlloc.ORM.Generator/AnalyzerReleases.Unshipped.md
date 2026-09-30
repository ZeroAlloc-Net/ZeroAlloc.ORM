; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category      | Severity | Notes
--------|---------------|----------|-------
ZAO015  | ZeroAlloc.ORM | Error    | [Param(DbType)] is rejected by the declared dialect
ZAO069  | ZeroAlloc.ORM | Error    | Return value parameter on a dialect without procedure return values
