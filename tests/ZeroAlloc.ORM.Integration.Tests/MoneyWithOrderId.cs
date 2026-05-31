namespace ZeroAlloc.ORM.Integration.Tests;

// v0.5 Phase A.4 — composite whose second ctor parameter is itself a
// ValueObject. Exercises the layered convention emit:
// `new MoneyWithOrderId(reader.GetDecimal(0), OrderId.From(reader.GetInt32(1)))`.
// The semantic shape (an OrderId-as-currency) is a test-only contortion; the
// purpose is to pin the convention layering, not to model a domain.
public readonly record struct MoneyWithOrderId(decimal Amount, OrderId Currency);
