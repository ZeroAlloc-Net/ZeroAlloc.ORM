namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// A versioned document as it was read; the Version is compared by value in the tests.
public sealed record DocumentVersion(Guid Id, string Body, byte[] Version);
