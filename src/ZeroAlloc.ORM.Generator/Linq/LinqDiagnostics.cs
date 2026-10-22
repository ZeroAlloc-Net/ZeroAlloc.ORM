using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using ZeroAlloc.ORM.Generator.Model;

namespace ZeroAlloc.ORM.Generator.Linq;

// The LINQ front end's diagnostics, ZAO100 to ZAO112 but ZAO109, built as cache-safe
// DiagnosticInfo. ZAO109 is reported by LinqCompanionCallAnalyzer.
// The descriptors live in DiagnosticDescriptors; their message arguments are documented there.
internal static class LinqDiagnostics
{
    public static DiagnosticInfo InvalidCompanion(string companion, string method, string reason, Location? location)
        => Create("ZAO100", location, companion, method, reason);

    public static DiagnosticInfo ParameterMismatch(string companion, string method, string expected, Location? location)
        => Create("ZAO101", location, companion, method, expected);

    public static DiagnosticInfo Unsupported(string construct, Location? location)
        => Create("ZAO102", location, construct);

    public static DiagnosticInfo UnsupportedAt(string construct, LocationInfo? location)
        => new("ZAO102", location, Args(construct));

    public static DiagnosticInfo Capture(string captured, Location? location)
        => Create("ZAO103", location, captured);

    public static DiagnosticInfo UnmappedEntity(string entity, LocationInfo? location)
        => new("ZAO104", location, Args(entity));

    public static DiagnosticInfo UnmappedMember(string member, string entity, LocationInfo? location)
        => new("ZAO105", location, Args(member, entity));

    public static DiagnosticInfo UnsupportedProjection(string reason, Location? location)
        => Create("ZAO107", location, reason);

    public static DiagnosticInfo UnsupportedProjectionAt(string reason, LocationInfo? location)
        => new("ZAO107", location, Args(reason));

    public static DiagnosticInfo DialectNotPostgres(string method, string found, Location? location)
        => Create("ZAO108", location, method, found);

    public static DiagnosticInfo UnorderedPaging(Location? location)
        => Create("ZAO110", location);

    public static DiagnosticInfo EnumStorageConflict(string parameter, LocationInfo? location)
        => new("ZAO111", location, Args(parameter));

    public static DiagnosticInfo ShapeMismatch(string method, string returnType, string queryType, Location? location)
        => ShapeMismatchAt(method, "'" + returnType + "'", "'" + queryType + "'", SingleShapes, location);

    // A tuple companion's mismatch: `returned` names the method's type or the tuple element that
    // does not fit, `query` what it should fit, and `guidance` what fits.
    public static DiagnosticInfo ShapeMismatchAt(string method, string returned, string query, string guidance, Location? location)
        => Create("ZAO112", location, method, returned, query, guidance);

    private const string SingleShapes =
        "return Task or ValueTask of List<T>, IReadOnlyList<T>, IList<T> or T?, or IAsyncEnumerable<T>, with T its row type; "
        + "after Count, LongCount or Any, return Task or ValueTask of int, long or bool; after Sum, Average, Min or Max, "
        + "of the value it gives: int, long, double or decimal, one of those or DateTime or DateTimeOffset as nullable, or string?";

    private static DiagnosticInfo Create(string id, Location? location, params string[] args)
        => new(id, LocationInfo.From(location), Args(args));

    private static EquatableArray<string> Args(params string[] args)
        => new(ImmutableArray.Create(args));
}
