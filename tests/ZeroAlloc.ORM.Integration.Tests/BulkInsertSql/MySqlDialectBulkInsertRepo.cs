using System.Data.Async;

namespace ZeroAlloc.ORM.Integration.Tests.BulkInsertSql;

// #326 — one BulkInsert repository per SqlDialect, each written in that
// dialect's own SQL: SQL Server returns identities through `OUTPUT INSERTED.Id`
// in the static head, Postgres and Sqlite through a trailing `RETURNING Id`, and
// MySQL has no RETURNING, so its cell carries an `ON DUPLICATE KEY UPDATE` tail
// and a lower-case `values` keyword instead. BulkInsertSqlCharacterizationTests
// pins the CommandText and parameter names each one emits, chunk by chunk.

[Dialect(SqlDialect.MySql)]
public sealed partial class MySqlDialectBulkInsertRepo(IAsyncDbConnection connection)
{
    [Command("INSERT INTO `Orders` (`CustomerId`, `Total`) values (@CustomerId, @Total) ON DUPLICATE KEY UPDATE `Total` = `Total` + 1", Kind = CommandKind.BulkInsert)]
    public partial Task<int> InsertAsync(IReadOnlyList<BulkOrderRow> orders, CancellationToken ct);
}
