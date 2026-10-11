namespace ZeroAlloc.ORM.Integration.Tests.TestDoubles;

// #326 — one command RecordingDbConnection saw: its text and the ParameterName
// of each parameter, in bind order.
internal sealed record RecordedCommand(string CommandText, IReadOnlyList<string> ParameterNames);
