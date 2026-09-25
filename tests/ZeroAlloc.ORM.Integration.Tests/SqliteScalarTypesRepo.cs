using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// #257 — Sqlite has no DateTimeOffset, TimeSpan or Guid storage class. The
// generated parameters store all three as TEXT, an untyped Microsoft.Data.Sqlite
// parameter stores a Guid as a 16-byte BLOB, and ExecuteScalar hands back that
// string or byte array rather than the CLR type.
public sealed partial class SqliteScalarTypesRepo(IAsyncDbConnection connection)
{
    [Command("INSERT INTO Typed (Id, Stamp, Span, Ident) VALUES (@id, @stamp, @span, @ident)")]
    public partial Task<int> InsertAsync(int id, DateTimeOffset stamp, TimeSpan span, Guid ident, CancellationToken ct);

    [Command("SELECT Stamp FROM Typed WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<DateTimeOffset> StampAsync(int id, CancellationToken ct);

    [Command("SELECT Span FROM Typed WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<TimeSpan> SpanAsync(int id, CancellationToken ct);

    [Command("SELECT Ident FROM Typed WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<Guid> IdentAsync(int id, CancellationToken ct);

    [Command("SELECT Stamp FROM Typed WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<DateTimeOffset?> NullableStampAsync(int id, CancellationToken ct);

    [Command("SELECT Span FROM Typed WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<TimeSpan?> NullableSpanAsync(int id, CancellationToken ct);

    [Command("SELECT Ident FROM Typed WHERE Id = @id", Kind = CommandKind.Scalar)]
    public partial Task<Guid?> NullableIdentAsync(int id, CancellationToken ct);
}
