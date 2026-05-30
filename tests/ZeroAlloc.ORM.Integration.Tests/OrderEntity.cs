namespace ZeroAlloc.ORM.Integration.Tests;

// Plain class with a single multi-arg ctor — exercises EmitShape.DomainEntity
// (column-name-keyed reads via GetOrdinal). Contrast with OrderRow which is a
// positional record handled by EmitShape.FlatRow.
public sealed class OrderEntity
{
    public int Id { get; }
    public int CustomerId { get; }
    public decimal Total { get; }

    public OrderEntity(int id, int customerId, decimal total)
    {
        Id = id;
        CustomerId = customerId;
        Total = total;
    }
}
