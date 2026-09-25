namespace ZeroAlloc.ORM.Integration.Tests;

// #249 — a record constructor parameter that is itself a record: Money's
// Amount and Currency are read from their own columns. A NULL in one of them
// must name the column and Money's parameter.
public sealed record NullGuardMoneyRow(int Id, Money Total);
