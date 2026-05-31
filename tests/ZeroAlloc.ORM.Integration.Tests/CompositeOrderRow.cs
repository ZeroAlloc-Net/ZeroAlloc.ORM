namespace ZeroAlloc.ORM.Integration.Tests;

// v0.5 Phase A.4 — outer FlatRow with a composite ctor parameter. The C# ctor
// has 2 parameters (int Id, Money Total) but the SQL SELECT must produce 3
// columns (Id, Amount, Currency) to fill the flattened materialization.
public sealed record CompositeOrderRow(int Id, Money Total);
