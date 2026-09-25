namespace ZeroAlloc.ORM.Integration.Tests;

// #264 — FlatRow row type with a NULLABLE factory-annotated composite. The
// nullable position takes the hoisted all-or-nothing block, which must still
// dispatch `MoneyWithFactory.FromStorage(GetString(N), GetString(N+1))`.
public sealed record MoneyWithFactoryNullableOrderRow(int Id, MoneyWithFactory? Total);
