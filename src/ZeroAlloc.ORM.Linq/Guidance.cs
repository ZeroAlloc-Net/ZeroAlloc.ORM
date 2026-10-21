namespace ZeroAlloc.ORM.Linq;

internal static class Guidance
{
    internal const string NeverRuns =
        "ZeroAlloc.ORM.Linq query methods are read at build time by the ZeroAlloc.ORM generator and never run. "
        + "Call the [LinqQuery] method instead; if this ran, the generator is not referenced.";

    internal static InvalidOperationException Exception() => new(NeverRuns);
}
