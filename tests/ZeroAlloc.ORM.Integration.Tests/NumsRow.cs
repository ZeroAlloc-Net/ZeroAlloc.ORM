namespace ZeroAlloc.ORM.Integration.Tests;

internal sealed record NumsRow(int Id, int[] Ints, IReadOnlyList<long> Longs);
