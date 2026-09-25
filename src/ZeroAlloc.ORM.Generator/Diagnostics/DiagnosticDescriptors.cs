using Microsoft.CodeAnalysis;

namespace ZeroAlloc.ORM.Generator.Diagnostics;

internal static class DiagnosticDescriptors
{
    private const string Category = "ZeroAlloc.ORM";

    // Each descriptor below calls the DiagnosticDescriptor constructor directly, with the
    // category and severity as constants. The Roslyn release-tracking analyzers read those
    // arguments at the constructor call and check them against AnalyzerReleases.*.md; routed
    // through a shared factory method they only see the ID, so an undeclared change to a
    // shipped rule's severity or category would pass the build.
    private static string HelpLink(string id)
        => $"https://github.com/ZeroAlloc-Net/ZeroAlloc.ORM/blob/main/docs/diagnostics/{id}.md";

    public static readonly DiagnosticDescriptor ZAO001_NotPartial = new(
        id: "ZAO001",
        title: "Annotated method must be partial",
        messageFormat: "Method '{0}' is annotated with [Query] but is not declared partial. Add the 'partial' modifier.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO001"));

    public static readonly DiagnosticDescriptor ZAO002_BadReturnType = new(
        id: "ZAO002",
        title: "Unsupported return type",
        messageFormat: "Method '{0}' has return type '{1}'. Expected Task<T>, ValueTask<T>, IAsyncEnumerable<T>, Task, or ValueTask.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO002"));

    public static readonly DiagnosticDescriptor ZAO003_NoConnection = new(
        id: "ZAO003",
        title: "No IAsyncDbConnection found on containing type",
        messageFormat: "Type '{0}' contains [Query] methods but has no IAsyncDbConnection field, primary-ctor parameter, or property. Inject IAsyncDbConnection so the generator can wire the command.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO003"));

    public static readonly DiagnosticDescriptor ZAO004_TypeNotPartial = new(
        id: "ZAO004",
        title: "Containing type must be partial",
        messageFormat: "Type '{0}' contains generator-annotated methods but is not declared partial. Add the 'partial' modifier.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO004"));

    public static readonly DiagnosticDescriptor ZAO005_MultipleAttributes = new(
        id: "ZAO005",
        title: "Multiple ORM attributes on one method",
        messageFormat: "Method '{0}' has more than one [Query] attribute. Apply exactly one.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO005"));

    public static readonly DiagnosticDescriptor ZAO006_MultipleCancellationTokens = new(
        id: "ZAO006",
        title: "Method has multiple CancellationToken parameters",
        messageFormat: "Method '{0}' has more than one CancellationToken parameter. Use a single token, position it last.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO006"));

    public static readonly DiagnosticDescriptor ZAO007_MissingEnumeratorCancellation = new(
        id: "ZAO007",
        title: "IAsyncEnumerable<T> return without [EnumeratorCancellation]",
        messageFormat: "Method '{0}' returns IAsyncEnumerable<T> but {1}. Add a CancellationToken parameter with [EnumeratorCancellation] so cancellation propagates correctly.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO007"));

    public static readonly DiagnosticDescriptor ZAO008_SingleResultWithSemicolons = new(
        id: "ZAO008",
        title: "Multi-statement SQL with single-result return type",
        messageFormat: "Method '{0}' has [Query] SQL containing ';' but returns a single result. Either remove the second statement or change the return type to a tuple.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO008"));

    public static readonly DiagnosticDescriptor ZAO009_RedundantAsync = new(
        id: "ZAO009",
        title: "Redundant async keyword on generated partial",
        messageFormat: "Method '{0}' is marked 'async' but the generator emits the async state machine. Remove the 'async' keyword from the partial declaration.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO009"));

    public static readonly DiagnosticDescriptor ZAO020_FromResourceNotImplemented = new(
        id: "ZAO020",
        title: "[ORM attribute](FromResource = true) not yet implemented",
        messageFormat: "Method '{0}' uses [{1}](FromResource = true) but the embedded-resource lookup path is deferred to a future milestone. The Sql string is currently treated as literal inline SQL.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO020"));

    // ZAO021 retired in v0.3 Phase B.5 — BatchMode.Always / BatchMode.Never values
    // are now honoured by the MultiResultSet emit (see ClassifyEmitShape +
    // EmitMultiResultSet*). The v0.1 info diagnostic is no longer accurate; its removal
    // is recorded in AnalyzerReleases.Shipped.md, under "Removed Rules" in Release 0.3.0.

    public static readonly DiagnosticDescriptor ZAO022_UnknownReturnShape = new(
        id: "ZAO022",
        title: "Return type shape not yet supported in v0.1",
        messageFormat: "Method '{0}' has return type '{1}' which the v0.1 generator cannot materialize. Supported v0.1 shapes: Task<int>, Task<T?> (single-row scalar for 11 primitive types), Task<TRow?> (FlatRow positional record). Other shapes (multi-result tuples, IAsyncEnumerable<T>, etc.) are deferred to later milestones.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO022"));

    public static readonly DiagnosticDescriptor ZAO032_TupleArityExceedsStatements = new(
        id: "ZAO032",
        title: "Tuple arity exceeds SQL statement count",
        messageFormat: "Method '{0}' returns a {1}-element tuple but the SQL has only {2} statement(s). Add the missing SELECT(s) or reduce the tuple arity.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO032"));

    public static readonly DiagnosticDescriptor ZAO033_StatementsExceedTupleArity = new(
        id: "ZAO033",
        title: "SQL statement count exceeds tuple arity",
        messageFormat: "Method '{0}' has {1} SQL statements but the tuple return has only {2} elements. Add missing tuple element types or remove the extra SELECT(s).",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO033"));

    public static readonly DiagnosticDescriptor ZAO040_NoConstructionStrategy = new(
        id: "ZAO040",
        title: "No construction strategy resolved for type",
        messageFormat: "Cannot materialize type '{0}': no [Materialize], [ValueObject], static From factory, single-arg ctor, enum, or primitive convention matched. Add [Materialize(Factory=\"...\")] or define a convention method.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO040"));

    public static readonly DiagnosticDescriptor ZAO041_NoUnwrapStrategy = new(
        id: "ZAO041",
        title: "No binding strategy resolved for parameter",
        messageFormat: "Cannot bind parameter '{0}' of type '{1}': no Value property, primitive, or enum match. Add [Param(Bind=...)] or define a Value property.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO041"));

    public static readonly DiagnosticDescriptor ZAO042_StoreAsStringNonEnum = new(
        id: "ZAO042",
        title: "[StoreAsString] requires an enum type",
        messageFormat: "Type '{0}' carries [StoreAsString] but is not an enum. Apply [StoreAsString] to enum types only.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO042"));

    // v0.5 Phase D post-review Fix 4 — message format now carries a 4th arg
    // (`{3}`) that names the failure reason ("method not found", "method is not
    // static", "method is not public", "factory parameter type '<TypeName>'
    // could not be resolved by ConventionDiscovery", "[Materialize(Strategy =
    // Custom)] requires a Factory argument", etc.). Threading the reason into
    // the message keeps the descriptor count low and gives adopters an
    // actionable hint without needing a second diagnostic.
    public static readonly DiagnosticDescriptor ZAO043_MaterializeFactoryMissing = new(
        id: "ZAO043",
        title: "[Materialize(Factory)] references missing method",
        messageFormat: "Method '{0}': [Materialize(Factory = \"{1}\")] cannot resolve factory on type '{2}'. Reason: {3}.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO043"));

    // v0.5 Phase D post-review Fix 3 — descriptor message is now attribute-name-
    // agnostic so the same rule covers the original v0.2 ambiguity surface AND
    // the Phase D "two static factory overloads with the same name" case.
    // MessageArgs: {0} = type display, {1} = the ambiguous factory method name,
    // {2} = how many matching static overloads were found. The reason sentences live
    // in the format itself so the analyzers can check its punctuation, see RS1032.
    public static readonly DiagnosticDescriptor ZAO044_AmbiguousDiscovery = new(
        id: "ZAO044",
        title: "Ambiguous convention discovery",
        messageFormat: "Type '{0}' has ambiguous discovery for symbol '{1}'. Found {2} matching static overloads; overload selection by signature is not supported. Reduce to a single static '{1}' or use a distinct factory name.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO044"));

    // v0.5 Phase D post-review Fix 2 — fires when a [Materialize(Factory = "X")]
    // factory's parameter NAME does not match any candidate column name (case-
    // insensitive) at the position the factory dispatch is being built. Today
    // the candidate column names come from the underlying composite type's
    // MultiArgCtor parameter names (PascalCased) — the documented contract is
    // "rename the factory parameter, use SQL 'AS' alias, or align the SELECT
    // column order". For the FlatRow positional path / composite-at-scalar path
    // where no candidate names are statically available, ZAO051 does NOT fire
    // and positional matching is used as the documented fallback.
    public static readonly DiagnosticDescriptor ZAO051_FactoryParameterColumnMismatch = new(
        id: "ZAO051",
        title: "Factory parameter does not match any SELECT column",
        messageFormat: "Method '{0}': [Materialize(Factory = \"{1}\")] factory parameter '{2}' does not match any column name in the SELECT clause (case-insensitive). Rename the factory parameter, use SQL 'AS' alias, or align the SELECT order. Available columns: {3}.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO051"));

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
    public static readonly DiagnosticDescriptor ZAO050_NullableCompositeRuntimeCheck = new(
        id: "ZAO050",
        title: "Nullable composite type requires runtime all-or-nothing check",
        messageFormat: "Method '{0}' uses nullable composite type '{1}' at {2}. The all-or-nothing DBNull check is enforced at runtime; partial-null columns throw ZeroAllocOrmMaterializationException at materialize time (or send DBNull-for-all on bind time). If your schema guarantees these columns are populated or null together, suppress this warning via project-level <NoWarn>ZAO050</NoWarn> or #pragma warning disable ZAO050.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO050"));

    // ZAO060 — RESERVED.
    // Originally scheduled for "[StoredProcedure] async method has out/ref
    // parameter". The C# compiler already forbids `out`/`ref` parameters on
    // `async` methods (CS1988), so any user-facing emit here would be dead code
    // today. The ID is reserved so a future release can swap in a friendlier
    // diagnostic (for example, pointing adopters at the named-tuple output
    // pattern when they try `out`/`ref` on a non-async sproc wrapper that the
    // compiler accepts but we cannot bind). Registered in `LookupDescriptor`
    // for catalog completeness; never reported by any emit path.
    public static readonly DiagnosticDescriptor ZAO060_OutOrRefOnAsync = new(
        id: "ZAO060",
        title: "[StoredProcedure] async method has out/ref parameter (reserved)",
        messageFormat: "Method '{0}' has an out/ref parameter on an async method. C# already forbids this (CS1988); ZAO060 reserves this slot for a future friendlier diagnostic that points at named-tuple output parameters.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO060"));

    // v0.4 Phase D fix-up — shipped early (originally scheduled for Phase F.2).
    // Without this guard, `[StoredProcedure("")]` silently emits CommandText = ""
    // and the failure surfaces as a provider-specific runtime error
    // ("Could not find stored procedure ''" on SQL Server, similar on others).
    // Surfacing at compile time with a clear message is materially better than
    // any runtime story we can ship.
    public static readonly DiagnosticDescriptor ZAO061_EmptyProcedureName = new(
        id: "ZAO061",
        title: "[StoredProcedure] name is empty",
        messageFormat: "Method '{0}' has [StoredProcedure(\"\")] but the procedure name must be non-empty and non-whitespace",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO061"));

    // v0.4 Phase F.3 — emitted from sproc classification when a named-tuple
    // return mixes parameter-matched fields (signalling the output-params
    // pattern is in use) with one or more non-matching fields. The non-matching
    // field is treated as a result column, which may be intentional (multi-
    // result + output) or a typo silently demoting an intended output to a
    // result column. Warning severity gives adopters a hint without forcing a
    // rename when the shape is genuinely desired.
    public static readonly DiagnosticDescriptor ZAO062_TupleFieldNotMatchingParameter = new(
        id: "ZAO062",
        title: "Named-tuple field does not match any parameter",
        messageFormat: "Method '{0}' tuple field '{1}' does not match any parameter — treated as a result column. If '{1}' was intended as an output parameter, ensure the tuple field name matches a parameter name (case-insensitive).",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO062"));

    // v0.5 Phase B — fired when `[Param(Name = "...")]` is applied to a parameter
    // whose type resolves to a MultiArgCtor (composite) convention. The composite
    // binding emit generates N DbParameters positionally
    // (`@{paramName}_{ctorArgName}`), so a single-name override cannot compose
    // with N-way unpacking. Surfacing this at compile time is materially better
    // than the alternative — silently dropping the override — which would
    // mislead adopters into shipping a no-op attribute.
    public static readonly DiagnosticDescriptor ZAO063_ParamNameOnCompositeUnsupported = new(
        id: "ZAO063",
        title: "[Param(Name = ...)] override is not supported on composite parameters",
        messageFormat: "Parameter '{0}' on method '{1}' has [Param(Name = \"{2}\")] but is a composite type. Composite parameters generate suffixes positionally ('@{{param}}_{{ctorArgName}}'); the Name override is ignored. Remove the Name override or rename the C# parameter.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO063"));

    // v0.5 Phase E.1 — fires when a composite type's ctor contains a parameter
    // that itself resolves to another MultiArgCtor (composite-of-composite).
    // Recursive composites are deferred to v0.6+: the v0.5 positional unpack
    // convention is one-level only, and a nested composite would need a
    // different SQL-side naming scheme (`@total_address_street`?) plus
    // multi-level emit machinery. Surfacing this as a dedicated diagnostic
    // (rather than the generic ZAO022) gives adopters an actionable hint
    // (flatten OR use [Materialize(Factory)]).
    //
    // MessageArgs:
    //   {0} = method name
    //   {1} = outer composite type display
    //   {2} = inner ctor parameter name that is itself a composite
    public static readonly DiagnosticDescriptor ZAO052_RecursiveCompositeDeferred = new(
        id: "ZAO052",
        title: "Recursive composite types are not supported in v0.5",
        messageFormat: "Method '{0}' uses composite type '{1}' which contains a nested composite ctor parameter ('{2}'). Recursive composites (composite-of-composite) are deferred to v0.6+. Flatten the nested composite into the outer ctor or use [Materialize(Factory)] to handle the materialization explicitly.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO052"));

    // v1.0 Phase C (v0.4-CLN5) — fires when a method carries
    // `[StoredProcedure(..., Batch = X)]` with X != BatchMode.Never (the sproc
    // pipeline default). The Batch property on StoredProcedureAttribute is
    // accepted only for symmetry with QueryAttribute — stored procedures
    // encapsulate their own batching semantics server-side and the emit always
    // treats the procedure call as a single DbCommand. The non-default value
    // is silently ignored at emit time; ZAO064 surfaces the no-op at compile
    // time so adopters don't ship a misleading attribute.
    //
    // MessageArgs:
    //   {0} = method name
    //   {1} = the explicit BatchMode value the adopter wrote (e.g. "Always")
    //
    // Severity: Info — the shape still emits correctly (the Batch value is
    // ignored, not an error). The diagnostic is a hint, not a build break.
    public static readonly DiagnosticDescriptor ZAO064_BatchOnStoredProcedureIgnored = new(
        id: "ZAO064",
        title: "[StoredProcedure(Batch=...)] is ignored",
        messageFormat: "Method '{0}' has [StoredProcedure(..., Batch = {1})] but stored procedures encapsulate their own batching semantics. The Batch value is ignored. Use BatchMode.Never (the default) or remove the explicit value.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO064"));

    // v2.0, #235 — a decimal output or input-output parameter of a
    // [StoredProcedure] without `[Param(Scale = ...)]`. SqlClient declares such a
    // parameter as scale 0 and rounds the value the procedure assigns, so
    // 1234.5678 comes back as 1235 with no error. Npgsql returns it exactly.
    //
    // Warning: the value is lost without an error, and the generator cannot see
    // which provider runs the procedure. An adopter who targets only Postgres
    // turns it off with `dotnet_diagnostic.ZAO065.severity = none`.
    //
    // MessageArgs:
    //   {0} = parameter name
    //   {1} = method name
    public static readonly DiagnosticDescriptor ZAO065_DecimalOutputWithoutScale = new(
        id: "ZAO065",
        title: "Decimal output parameter has no Scale",
        messageFormat: "Output parameter '{0}' on method '{1}' is a decimal without [Param(Scale = ...)]. SQL Server rounds the value to scale 0, so 1234.5678 comes back as 1235. Set Precision and Scale to match the procedure's declaration. PostgreSQL returns the exact value either way.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO065"));

    // v2.0, #235 — a `[Param]` member the generator cannot honour, so it would
    // otherwise be dropped without a word or fail at run time: any member on a
    // CancellationToken, transaction or BulkInsert collection parameter; DbType,
    // Size, Precision, Scale or Direction on a composite parameter; a Size below
    // -1; a non-Input Direction on a parameter that no named-tuple field reads
    // back, or Direction = Input on one that a field does read back; an output
    // whose length-typed DbType gets a Size of 0, or a fixed-length output
    // without a Size.
    //
    // MessageArgs:
    //   {0} = the member(s), e.g. "Size" or "Direction = ReturnValue"
    //   {1} = parameter name
    //   {2} = method name
    //   {3} = the reason
    public static readonly DiagnosticDescriptor ZAO066_ParamFacetNotApplicable = new(
        id: "ZAO066",
        title: "[Param] member does not apply to this parameter",
        messageFormat: "[Param({0})] on parameter '{1}' of method '{2}' cannot be applied: {3}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO066"));

    // #241 — `[Param(Direction = ParameterDirection.ReturnValue)]` reads the
    // procedure's RETURN value into a tuple field that is not `int` or `int?`.
    // SQL Server's RETURN value is always an int, so no other field type can
    // hold it. Only the explicit Direction is reported: a field named
    // RETURN_VALUE of another type compiled in 2.0.0 as an ordinary output
    // parameter, and the convention leaves it one.
    //
    // MessageArgs:
    //   {0} = parameter name
    //   {1} = method name
    //   {2} = the tuple field's type
    public static readonly DiagnosticDescriptor ZAO067_ReturnValueNotInt = new(
        id: "ZAO067",
        title: "Return value parameter must be int",
        messageFormat: "Parameter '{0}' of method '{1}' reads the procedure's RETURN value into a '{2}' tuple field. SQL Server returns an int, so declare the tuple field as int or int?.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO067"));

    // #241 — more than one parameter of a [StoredProcedure] binds as the RETURN
    // value, through `[Param(Direction = ParameterDirection.ReturnValue)]` or the
    // RETURN_VALUE convention. A procedure has one RETURN value and SqlClient
    // fills one ReturnValue parameter.
    //
    // MessageArgs:
    //   {0} = the later parameter's name
    //   {1} = method name
    //   {2} = the first parameter bound as the RETURN value
    public static readonly DiagnosticDescriptor ZAO068_MultipleReturnValues = new(
        id: "ZAO068",
        title: "More than one return value parameter",
        messageFormat: "Parameter '{0}' of method '{1}' is bound as the procedure's RETURN value, but '{2}' already is. A procedure has one RETURN value; bind only one parameter to it.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO068"));

    // v1.3 — BulkInsert shape diagnostics (design 2026-06-02).
    // The five descriptors below classify the BulkInsert misuse modes detected
    // by the Task 5 classifier. They are declared here so AnalyzerReleases.md
    // and downstream catalog-completeness checks pick them up ahead of the
    // emit-path work that actually fires them.

    public static readonly DiagnosticDescriptor ZAO070_BulkInsertSignature = new(
        id: "ZAO070",
        title: "BulkInsert method must take exactly one collection parameter",
        messageFormat: "[Command(Kind = CommandKind.BulkInsert)] method '{0}' must have exactly one IEnumerable<TRow>-shaped collection parameter (IReadOnlyList<T> preferred); saw {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO070"));

    public static readonly DiagnosticDescriptor ZAO071_BulkInsertValuesParser = new(
        id: "ZAO071",
        title: "BulkInsert SQL must contain exactly one VALUES tuple",
        messageFormat: "[Command(Kind = CommandKind.BulkInsert)] method '{0}' SQL parse failed: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO071"));

    public static readonly DiagnosticDescriptor ZAO072_BulkInsertPlaceholderUnresolved = new(
        id: "ZAO072",
        title: "BulkInsert placeholder doesn't match any TRow property",
        messageFormat: "[Command(Kind = CommandKind.BulkInsert)] method '{0}': TRow '{1}' has no public property matching VALUES placeholder '@{2}' (case-insensitive name match)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO072"));

    public static readonly DiagnosticDescriptor ZAO073_BulkInsertReturnTypeShape = new(
        id: "ZAO073",
        title: "BulkInsert return type must be Task<int> or Task<IReadOnlyList<TIdentity>>",
        messageFormat: "[Command(Kind = CommandKind.BulkInsert)] method '{0}' return type must be Task<int> (rows-affected sum) or Task<IReadOnlyList<TIdentity>> where TIdentity is int/long/Guid or a [ValueObject] wrapping one of those; saw '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO073"));

    public static readonly DiagnosticDescriptor ZAO074_BulkInsertWrongAttribute = new(
        id: "ZAO074",
        title: "CommandKind.BulkInsert is ignored on this attribute",
        messageFormat: "Method '{0}' is annotated with {1} which ignores Kind. CommandKind.BulkInsert only takes effect on [Command]. Change the attribute to [Command] or remove the Kind argument.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO074"));

    // v1.5 — IAsyncDbTransaction parameter support. The emit picks the FIRST
    // IAsyncDbTransaction parameter and assigns `__cmd.Transaction = @<name>;`.
    // Additional tx parameters are silently dropped; ZAO080 surfaces the latent
    // misuse so adopters can clean up the signature. Mirrors ZAO006 for
    // CancellationToken multiplicity.
    //
    // MessageArgs:
    //   {0} = method name
    //   {1} = the count of IAsyncDbTransaction parameters seen
    public static readonly DiagnosticDescriptor ZAO080_MultipleTransactionParameters = new(
        id: "ZAO080",
        title: "At most one IAsyncDbTransaction parameter",
        messageFormat: "Method '{0}' has {1} IAsyncDbTransaction parameters; only the first is used",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO080"));

    // v2.0 — issue #238. A repository declared inside one or more containing
    // types needs the generated partial half wrapped in a matching partial
    // declaration at every level so it joins the user's nested type. ZAO004
    // already requires the repository type itself to be partial; ZAO081 extends
    // the same requirement to every type that CONTAINS the repository type.
    //
    // MessageArgs:
    //   {0} = the non-partial containing type's fully-qualified display name
    public static readonly DiagnosticDescriptor ZAO081_ContainingTypeNotPartial = new(
        id: "ZAO081",
        title: "Containing type must be partial",
        messageFormat: "Type '{0}' contains a nested repository but is not declared partial. Add the 'partial' modifier so the generator can emit a matching partial declaration around the generated code.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink("ZAO081"));
}
