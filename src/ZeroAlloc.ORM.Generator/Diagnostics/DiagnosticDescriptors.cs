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
        "ZAO020", "[Query(FromResource)] not yet implemented in v0.1",
        "Method '{0}' uses [Query(FromResource = true)] but the embedded-resource lookup path is deferred to a future milestone. The Sql string is currently treated as literal inline SQL.",
        DiagnosticSeverity.Info);

    public static readonly DiagnosticDescriptor ZAO021_BatchNotImplemented = Make(
        "ZAO021", "[Query(Batch = ...)] non-Auto value not yet implemented in v0.1",
        "Method '{0}' uses [Query(Batch = {1})] but BatchMode dispatch is deferred to v0.3. The Batch value is currently ignored; queries always use single-command emit.",
        DiagnosticSeverity.Info);

    public static readonly DiagnosticDescriptor ZAO022_UnknownReturnShape = Make(
        "ZAO022", "Return type shape not yet supported in v0.1",
        "Method '{0}' has return type '{1}' which the v0.1 generator cannot materialize. Supported v0.1 shapes: Task<int>, Task<T?> (single-row scalar for 11 primitive types), Task<TRow?> (FlatRow positional record). Other shapes (multi-result tuples, IAsyncEnumerable<T>, etc.) are deferred to later milestones.",
        DiagnosticSeverity.Info);

    public static readonly DiagnosticDescriptor ZAO040_NoConstructionStrategy = Make(
        "ZAO040", "No construction strategy resolved for type",
        "Cannot materialize type '{0}': no [Materialize], [ValueObject], static From factory, single-arg ctor, enum, or primitive convention matched. Add [Materialize(Factory=\"...\")] or define a convention method.",
        DiagnosticSeverity.Error);
}
