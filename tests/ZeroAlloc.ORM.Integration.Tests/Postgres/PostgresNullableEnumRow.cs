namespace ZeroAlloc.ORM.Integration.Tests.Postgres;

public sealed record PostgresNullableEnumRow(int Id, Status? Status, StringStatus? Shape);
