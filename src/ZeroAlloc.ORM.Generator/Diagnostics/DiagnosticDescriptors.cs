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

    // v0.5 Phase C — fires for every method position that uses a nullable
    // composite type (return: Task<Money?>, parameter: `Money? total`, or a
    // nullable composite ctor param nested in a FlatRow / DomainEntity). The
    // all-or-nothing DBNull contract for nullable composites is enforced at
    // RUNTIME — the generator can't statically prove the underlying schema
    // declares its composite columns NOT NULL together — so ZAO050 surfaces
    // the runtime concern at build time.
    //
    // Per-position firing is intentional: each occurrence is its own runtime
    // contract surface and the warning makes the suppression decision
    // explicit. Established repos that have audited the schema use the
    // project-level `<NoWarn>ZAO050</NoWarn>` once; per-method opt-in uses
    // `#pragma warning disable ZAO050` (ZAO050 reports at user-source
    // locations so the pragma works regardless of TreatWarningsAsErrors).
    // See docs/diagnostics/ZAO050.md for the full suppression matrix.
    //
    // Warning (not Error) severity: nullable composites are a supported emit
    // shape with well-defined runtime semantics (return null on all-DBNull,
    // throw ZeroAllocOrmMaterializationException on mixed-null). The warning
    // exists to make adopters consciously opt into the runtime contract.
    public static readonly DiagnosticDescriptor ZAO050_NullableCompositeRuntimeCheck = Make(
        "ZAO050", "Nullable composite type requires runtime all-or-nothing check",
        "Method '{0}' uses nullable composite type '{1}' at {2}. The all-or-nothing DBNull check is enforced at runtime; partial-null columns throw ZeroAllocOrmMaterializationException at materialize time (or send DBNull-for-all on bind time). If your schema guarantees these columns are populated or null together, suppress this warning via project-level <NoWarn>ZAO050</NoWarn> or #pragma warning disable ZAO050.",
        DiagnosticSeverity.Warning);

    // ZAO060 — RESERVED.
    // Originally scheduled for "[StoredProcedure] async method has out/ref
    // parameter". The C# compiler already forbids `out`/`ref` parameters on
    // `async` methods (CS1988), so any user-facing emit here would be dead code
    // today. The ID is reserved so a future release can swap in a friendlier
    // diagnostic (for example, pointing adopters at the named-tuple output
    // pattern when they try `out`/`ref` on a non-async sproc wrapper that the
    // compiler accepts but we cannot bind). Registered in `LookupDescriptor`
    // for catalog completeness; never reported by any emit path.
    public static readonly DiagnosticDescriptor ZAO060_OutOrRefOnAsync = Make(
        "ZAO060", "[StoredProcedure] async method has out/ref parameter (reserved)",
        "Method '{0}' has an out/ref parameter on an async method. C# already forbids this (CS1988); ZAO060 reserves this slot for a future friendlier diagnostic that points at named-tuple output parameters.",
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

    // v0.4 Phase F.3 — emitted from sproc classification when a named-tuple
    // return mixes parameter-matched fields (signalling the output-params
    // pattern is in use) with one or more non-matching fields. The non-matching
    // field is treated as a result column, which may be intentional (multi-
    // result + output) or a typo silently demoting an intended output to a
    // result column. Warning severity gives adopters a hint without forcing a
    // rename when the shape is genuinely desired.
    public static readonly DiagnosticDescriptor ZAO062_TupleFieldNotMatchingParameter = Make(
        "ZAO062", "Named-tuple field does not match any parameter",
        "Method '{0}' tuple field '{1}' does not match any parameter — treated as a result column. If '{1}' was intended as an output parameter, ensure the tuple field name matches a parameter name (case-insensitive).",
        DiagnosticSeverity.Warning);

    // v0.5 Phase B — fired when `[Param(Name = "...")]` is applied to a parameter
    // whose type resolves to a MultiArgCtor (composite) convention. The composite
    // binding emit generates N DbParameters positionally
    // (`@{paramName}_{ctorArgName}`), so a single-name override cannot compose
    // with N-way unpacking. Surfacing this at compile time is materially better
    // than the alternative — silently dropping the override — which would
    // mislead adopters into shipping a no-op attribute.
    public static readonly DiagnosticDescriptor ZAO063_ParamNameOnCompositeUnsupported = Make(
        "ZAO063", "[Param(Name = ...)] override is not supported on composite parameters",
        "Parameter '{0}' on method '{1}' has [Param(Name = \"{2}\")] but is a composite type. Composite parameters generate suffixes positionally ('@{{param}}_{{ctorArgName}}'); the Name override is ignored. Remove the Name override or rename the C# parameter.",
        DiagnosticSeverity.Error);
}
