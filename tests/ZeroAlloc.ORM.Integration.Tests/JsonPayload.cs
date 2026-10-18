namespace ZeroAlloc.ORM.Integration.Tests;

internal readonly record struct JsonPayload(string Name, [Json] string Doc);
