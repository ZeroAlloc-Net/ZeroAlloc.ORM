namespace ZeroAlloc.ORM.Integration.Tests.AllocationGates;

// #351 — the ParameterNames a hand-written baseline binds, in the spelling its provider
// resolves for free. Built once, so a baseline allocates no name of its own.
internal sealed record WorkloadNames(string Id, string Cust, string Total)
{
    // Microsoft.Data.Sqlite: the name exactly as the SQL writes it.
    public static readonly WorkloadNames Prefixed = new("@id", "@cust", "@total");

    // Npgsql: the bare name, which it does not have to strip.
    public static readonly WorkloadNames Bare = new("id", "cust", "total");
}
