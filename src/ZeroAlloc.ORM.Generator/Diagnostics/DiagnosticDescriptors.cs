using Microsoft.CodeAnalysis;

namespace ZeroAlloc.ORM.Generator.Diagnostics;

internal static class DiagnosticDescriptors
{
    private const string Category = "ZeroAlloc.ORM";

    private static DiagnosticDescriptor Make(string id, string title, string message, DiagnosticSeverity severity)
        => new(
            id: id,
            title: title,
            messageFormat: message,
            category: Category,
            defaultSeverity: severity,
            isEnabledByDefault: true,
            helpLinkUri: $"https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/diagnostics/{id}.md");

    public static readonly DiagnosticDescriptor ZAO001_NotPartial = Make(
        "ZAO001", "Annotated method must be partial",
        "Method '{0}' is annotated with [Query] but is not declared partial. Add the 'partial' modifier.",
        DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor ZAO002_BadReturnType = Make(
        "ZAO002", "Unsupported return type",
        "Method '{0}' has return type '{1}'. Expected Task<T>, ValueTask<T>, IAsyncEnumerable<T>, Task, or ValueTask.",
        DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor ZAO003_NoConnection = Make(
        "ZAO003", "No IAsyncDbConnection found on containing type",
        "Type '{0}' contains [Query] methods but has no IAsyncDbConnection field, primary-ctor parameter, or property. Inject IAsyncDbConnection so the generator can wire the command.",
        DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor ZAO004_TypeNotPartial = Make(
        "ZAO004", "Containing type must be partial",
        "Type '{0}' contains generator-annotated methods but is not declared partial. Add the 'partial' modifier.",
        DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor ZAO005_MultipleAttributes = Make(
        "ZAO005", "Multiple ORM attributes on one method",
        "Method '{0}' has more than one [Query] attribute. Apply exactly one.",
        DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor ZAO006_MultipleCancellationTokens = Make(
        "ZAO006", "Method has multiple CancellationToken parameters",
        "Method '{0}' has more than one CancellationToken parameter. Use a single token, position it last.",
        DiagnosticSeverity.Warning);

    public static readonly DiagnosticDescriptor ZAO007_MissingEnumeratorCancellation = Make(
        "ZAO007", "IAsyncEnumerable<T> return without [EnumeratorCancellation]",
        "Method '{0}' returns IAsyncEnumerable<T> but {1}. Add a CancellationToken parameter with [EnumeratorCancellation] so cancellation propagates correctly.",
        DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor ZAO008_SingleResultWithSemicolons = Make(
        "ZAO008", "Multi-statement SQL with single-result return type",
        "Method '{0}' has [Query] SQL containing ';' but returns a single result. Either remove the second statement or change the return type to a tuple.",
        DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor ZAO009_RedundantAsync = Make(
        "ZAO009", "Redundant async keyword on generated partial",
        "Method '{0}' is marked 'async' but the generator emits the async state machine. Remove the 'async' keyword from the partial declaration.",
        DiagnosticSeverity.Warning);

    public static readonly DiagnosticDescriptor ZAO020_FromResourceNotImplemented = Make(
        "ZAO020", "[ORM attribute](FromResource = true) not yet implemented",
        "Method '{0}' uses [{1}](FromResource = true) but the embedded-resource lookup path is deferred to a future milestone. The Sql string is currently treated as literal inline SQL.",
        DiagnosticSeverity.Info);

    // ZAO021 retired in v0.3 Phase B.5 — BatchMode.Always / BatchMode.Never values
    // are now honoured by the MultiResultSet emit (see ClassifyEmitShape +
    // EmitMultiResultSet*). The v0.1 info diagnostic is no longer accurate; removal
    // is tracked in AnalyzerReleases.Unshipped.md under "Removed Rules".

    public static readonly DiagnosticDescriptor ZAO022_UnknownReturnShape = Make(
        "ZAO022", "Return type shape not yet supported in v0.1",
        "Method '{0}' has return type '{1}' which the v0.1 generator cannot materialize. Supported v0.1 shapes: Task<int>, Task<T?> (single-row scalar for 11 primitive types), Task<TRow?> (FlatRow positional record). Other shapes (multi-result tuples, IAsyncEnumerable<T>, etc.) are deferred to later milestones.",
        DiagnosticSeverity.Info);

    public static readonly DiagnosticDescriptor ZAO032_TupleArityExceedsStatements = Make(
        "ZAO032", "Tuple arity exceeds SQL statement count",
        "Method '{0}' returns a {1}-element tuple but the SQL has only {2} statement(s). Add the missing SELECT(s) or reduce the tuple arity.",
        DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor ZAO033_StatementsExceedTupleArity = Make(
        "ZAO033", "SQL statement count exceeds tuple arity",
        "Method '{0}' has {1} SQL statements but the tuple return has only {2} elements. Add missing tuple element types or remove the extra SELECT(s).",
        DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor ZAO040_NoConstructionStrategy = Make(
        "ZAO040", "No construction strategy resolved for type",
        "Cannot materialize type '{0}': no [Materialize], [ValueObject], static From factory, single-arg ctor, enum, or primitive convention matched. Add [Materialize(Factory=\"...\")] or define a convention method.",
        DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor ZAO041_NoUnwrapStrategy = Make(
        "ZAO041", "No binding strategy resolved for parameter",
        "Cannot bind parameter '{0}' of type '{1}': no Value property, primitive, or enum match. Add [Param(Bind=...)] or define a Value property.",
        DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor ZAO042_StoreAsStringNonEnum = Make(
        "ZAO042", "[StoreAsString] requires an enum type",
        "Type '{0}' carries [StoreAsString] but is not an enum. Apply [StoreAsString] to enum types only.",
        DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor ZAO043_MaterializeFactoryMissing = Make(
        "ZAO043", "[Materialize(Factory)] references missing method",
        "Method '{0}' references factory '{1}' via [Materialize(Factory=...)] but the method is not found on type '{2}' or is not static/public. [Materialize] support lands in v0.5.",
        DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor ZAO044_AmbiguousDiscovery = Make(
        "ZAO044", "Ambiguous convention discovery",
        "Type '{0}' matches multiple convention rules with equal priority and no clear precedence. Add an explicit [Materialize(Strategy=...)] to disambiguate.",
        DiagnosticSeverity.Error);

    // v0.4 Phase D fix-up — shipped early (originally scheduled for Phase F.2).
    // Without this guard, `[StoredProcedure("")]` silently emits CommandText = ""
    // and the failure surfaces as a provider-specific runtime error
    // ("Could not find stored procedure ''" on SQL Server, similar on others).
    // Surfacing at compile time with a clear message is materially better than
    // any runtime story we can ship.
    public static readonly DiagnosticDescriptor ZAO061_EmptyProcedureName = Make(
        "ZAO061", "[StoredProcedure] name is empty",
        "Method '{0}' has [StoredProcedure(\"\")] but the procedure name must be non-empty and non-whitespace.",
        DiagnosticSeverity.Error);
}
