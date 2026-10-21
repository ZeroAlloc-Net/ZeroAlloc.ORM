namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// A projection mixing a column with a parameter and untyped SQL literals, each read back into
// a member type of its own.
internal sealed record LiteralRow(Guid Id, string Label, long Big, double Ratio, string? Missing, Origin Origin);
