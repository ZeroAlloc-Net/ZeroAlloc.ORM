using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// The same shape as JsonRepo without the PostgreSQL dialect. Npgsql is referenced by this
// project, so [Json] is accepted, but the parameter is not an NpgsqlParameter on Sqlite,
// so the type test falls through and the string binds as plain text.
internal sealed partial class JsonSqliteRepo(IAsyncDbConnection connection)
{
    [Command("INSERT INTO documents (id, body, note) VALUES (@id, @body, @note)")]
    public partial Task<int> InsertAsync(long id, [Json] string body, [Json] string? note, CancellationToken ct);

    [Query("SELECT id, body, note FROM documents WHERE id = @id")]
    public partial Task<SqliteDocumentRow?> GetAsync(long id, CancellationToken ct);
}
