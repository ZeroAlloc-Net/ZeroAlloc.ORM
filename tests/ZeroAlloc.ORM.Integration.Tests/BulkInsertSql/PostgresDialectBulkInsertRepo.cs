using System.Data.Async;

namespace ZeroAlloc.ORM.Integration.Tests.BulkInsertSql;

// #326 — one BulkInsert repository per SqlDialect, each written in that
// dialect's own SQL: SQL Server returns identities through `OUTPUT INSERTED.Id`
// in the static head, Postgres and Sqlite through a trailing `RETURNING Id`, and
// MySQL has no RETURNING, so its cell carries an `ON DUPLICATE KEY UPDATE` tail
// and a lower-case `values` keyword instead. BulkInsertSqlCharacterizationTests
// pins the CommandText and parameter names each one emits, chunk by chunk.

[Dialect(SqlDialect.PostgreSql)]
public sealed partial class PostgresDialectBulkInsertRepo(IAsyncDbConnection connection)
{
    [Command("INSERT INTO orders (customer_id, total) VALUES (@CustomerId, @Total)", Kind = CommandKind.BulkInsert)]
    public partial Task<int> InsertAsync(IReadOnlyList<BulkOrderRow> orders, CancellationToken ct);

    [Command("INSERT INTO orders (customer_id, total) VALUES (@CustomerId, @Total) RETURNING id", Kind = CommandKind.BulkInsert)]
    public partial Task<IReadOnlyList<int>> InsertReturningIdsAsync(IReadOnlyList<BulkOrderRow> orders, CancellationToken ct);
}
