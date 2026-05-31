namespace ZeroAlloc.ORM.Integration.Tests;

// v0.5 Phase A (post-review Fix 2) — DomainEntity round-trip partner for
// CompositeOrderRow. Same shape (int Id, Money Total) but declared as a plain
// class with a single multi-arg ctor so the generator picks EmitShape.DomainEntity
// — inner column reads thread through `__reader.GetOrdinal("ColumnName")`.
// Exercises EmitNestedCompositeConstructionByOrdinalName end-to-end against
// Sqlite, catching column-name typos in that emit path at runtime.
public sealed class CompositeOrderEntity
{
    public int Id { get; }
    public Money Total { get; }

    public CompositeOrderEntity(int id, Money total)
    {
        Id = id;
        Total = total;
    }
}
