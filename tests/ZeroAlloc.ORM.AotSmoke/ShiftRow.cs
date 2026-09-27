namespace ZeroAlloc.ORM.AotSmoke;

// #256 — DateOnly and TimeOnly columns, one of them nullable.
public sealed record ShiftRow(int Id, DateOnly Day, TimeOnly Start, DateOnly? EndDay);
