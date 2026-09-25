namespace ZeroAlloc.ORM.Integration.Tests;

// #264 — nullable FlatRow position for the reversed-parameter factory.
public sealed record MoneyWithReversedFactoryNullableOrderRow(int Id, MoneyWithReversedFactory? Total);
