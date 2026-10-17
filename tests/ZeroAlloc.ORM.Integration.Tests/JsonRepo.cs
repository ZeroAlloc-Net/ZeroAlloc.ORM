using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// [Json] binds a string as jsonb, so none of the SQL below carries a ::jsonb cast.
// PostgresJsonTests runs every method on a full NpgsqlConnection and on a slim data source.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class JsonRepo(IAsyncDbConnection connection)
{
    [Command("INSERT INTO documents (id, body, note) VALUES (@id, @body, @note)")]
    public partial Task<int> InsertAsync(Guid id, [Json] string body, [Json] string? note, CancellationToken ct);

    [Query("SELECT id, body, note FROM documents WHERE id = @id")]
    public partial Task<DocumentRow?> GetAsync(Guid id, CancellationToken ct);

    [Command("SELECT body->>'a' FROM documents WHERE id = @id", Kind = CommandKind.Scalar)]
    public partial Task<string?> GetFieldAAsync(Guid id, CancellationToken ct);

    [Command("SELECT note IS NULL FROM documents WHERE id = @id", Kind = CommandKind.Scalar)]
    public partial Task<bool> NoteIsNullAsync(Guid id, CancellationToken ct);

    [Command("INSERT INTO documents (id, body) VALUES (@id, @payload_Doc)")]
    public partial Task<int> InsertPayloadAsync(Guid id, JsonPayload payload, CancellationToken ct);

    [Command("INSERT INTO documents (id, body, note) VALUES (@Id, @Body, @Note)", Kind = CommandKind.BulkInsert)]
    public partial Task<int> InsertManyAsync(IReadOnlyList<JsonBulkRow> rows, CancellationToken ct);
}
