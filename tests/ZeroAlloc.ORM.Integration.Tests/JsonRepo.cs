using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// [Json] binds a string as jsonb, so none of the SQL below carries a ::jsonb cast.
// PostgresJsonTests runs every method on a full NpgsqlConnection and on a slim data source.
[Dialect(SqlDialect.PostgreSql)]
internal sealed partial class JsonRepo(IAsyncDbConnection connection)
{
    // The SQL lives in constants so a test can assert it carries no ::jsonb cast.
    internal const string InsertSql = "INSERT INTO documents (id, body, note) VALUES (@id, @body, @note)";
    internal const string GetSql = "SELECT id, body, note FROM documents WHERE id = @id";
    internal const string FieldASql = "SELECT body->>'a' FROM documents WHERE id = @id";
    internal const string NoteIsNullSql = "SELECT note IS NULL FROM documents WHERE id = @id";
    internal const string InsertPayloadSql = "INSERT INTO documents (id, body) VALUES (@id, @payload_Doc)";
    internal const string InsertManySql = "INSERT INTO documents (id, body, note) VALUES (@Id, @Body, @Note)";

    [Command(InsertSql)]
    public partial Task<int> InsertAsync(Guid id, [Json] string body, [Json] string? note, CancellationToken ct);

    [Query(GetSql)]
    public partial Task<DocumentRow?> GetAsync(Guid id, CancellationToken ct);

    [Command(FieldASql, Kind = CommandKind.Scalar)]
    public partial Task<string?> GetFieldAAsync(Guid id, CancellationToken ct);

    [Command(NoteIsNullSql, Kind = CommandKind.Scalar)]
    public partial Task<bool> NoteIsNullAsync(Guid id, CancellationToken ct);

    [Command(InsertPayloadSql)]
    public partial Task<int> InsertPayloadAsync(Guid id, JsonPayload payload, CancellationToken ct);

    [Command(InsertManySql, Kind = CommandKind.BulkInsert)]
    public partial Task<int> InsertManyAsync(IReadOnlyList<JsonBulkRow> rows, CancellationToken ct);
}
