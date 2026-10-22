namespace ZeroAlloc.ORM.Integration.Tests.Linq;

// A document guarded by a row version PostgreSQL does not generate: a bytea the application
// writes anew with every change, as EF Core's [Timestamp] byte[] is outside SQL Server.
public sealed class VersionedDocumentEntity
{
    public Guid Id { get; private set; }
    public string Body { get; private set; } = "";
    public byte[] Version { get; private set; } = [];
}
