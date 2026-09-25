namespace ZeroAlloc.ORM.Integration.Tests;

// #249 — constructor binding by column name. A plain class with one public
// constructor takes the DomainEntity path, which reads through GetOrdinal.
public sealed class NullGuardEntity
{
    public int Id { get; }
    public string Name { get; }
    public int Quantity { get; }

    public NullGuardEntity(int id, string name, int quantity)
    {
        Id = id;
        Name = name;
        Quantity = quantity;
    }
}
