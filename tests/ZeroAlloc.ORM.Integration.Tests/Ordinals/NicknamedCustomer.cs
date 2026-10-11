namespace ZeroAlloc.ORM.Integration.Tests.Ordinals;

// #326 — a DomainEntity, read by column name, with a nullable column. Its
// constructor order differs from OrdinalHoistRepo's SELECT order, so a read that
// used the wrong ordinal would put a value in the wrong property.
public sealed class NicknamedCustomer
{
    public int Id { get; }
    public string Name { get; }
    public string? Nickname { get; }

    public NicknamedCustomer(int id, string name, string? nickname)
    {
        Id = id;
        Name = name;
        Nickname = nickname;
    }
}
