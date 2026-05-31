namespace ZeroAlloc.ORM.Integration.Tests;

// v0.5 Phase D.3 — FlatRow row type that nests the factory-annotated composite.
// The OUTER row stays a plain positional record (`new MoneyWithFactoryOrderRow`);
// the INNER MoneyWithFactory column dispatches
// `MoneyWithFactory.FromStorage(GetString(N), GetString(N+1))`.
public sealed record MoneyWithFactoryOrderRow(int Id, MoneyWithFactory Total);
