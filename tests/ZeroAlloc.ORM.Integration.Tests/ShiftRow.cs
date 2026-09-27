namespace ZeroAlloc.ORM.Integration.Tests;

// #256 — a row with DateOnly and TimeOnly columns, each also in nullable form.
public sealed record ShiftRow(int Id, DateOnly ShiftDay, TimeOnly StartTime, DateOnly? EndDay, TimeOnly? EndTime);
