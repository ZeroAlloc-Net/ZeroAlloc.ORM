using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.AotSmoke;

// [Json] jsonb parameters. Program.cs only runs these when ZAORM_AOT_PG names a server, but the
// generated code, with its Npgsql type test, is always compiled and analysed by the AOT publish.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class JsonRepo(IAsyncDbConnection connection)
{
    [Command("INSERT INTO documents (id, body, note) VALUES (@id, @body, @note)")]
    public partial Task<int> InsertAsync(Guid id, [Json] string body, [Json] string? note, CancellationToken ct);

    [Query("SELECT id, body, note FROM documents WHERE id = @id")]
    public partial Task<DocumentRow?> GetAsync(Guid id, CancellationToken ct);
}
