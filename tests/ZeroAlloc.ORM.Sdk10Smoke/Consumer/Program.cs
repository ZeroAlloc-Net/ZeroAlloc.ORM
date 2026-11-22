using System.Data.Async;
using System.Data.Async.Adapters;
using Microsoft.Data.Sqlite;
using ZeroAlloc.ORM;
using ZeroAlloc.ORM.Sdk10Smoke;

// SmokeRepo.GetByIdAsync has a body only if the compiler loaded the generator.
var raw = new SqliteConnection("Data Source=:memory:");
await using (raw)
{
    await raw.OpenAsync();
    IAsyncDbConnection connection = raw.AsAsync();
    await using (connection)
    {
        var ddl = connection.CreateCommand();
        await using (ddl)
        {
            ddl.CommandText = """
                CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Total NUMERIC NOT NULL);
                INSERT INTO Orders (Id, CustomerId, Total) VALUES (1, 42, 99.95);
                """;
            await ddl.ExecuteNonQueryAsync();
        }

        var row = await new SmokeRepo(connection).GetByIdAsync(1, CancellationToken.None);
        if (row is null || row.Id != 1 || row.CustomerId != 42 || row.Total != 99.95m)
        {
            Console.Error.WriteLine($"FAIL: unexpected row {row}");
            return 1;
        }
    }
}

Console.WriteLine("SDK 10 consumer: generator loaded, query materialised by generated code.");
return 0;

namespace ZeroAlloc.ORM.Sdk10Smoke
{
    public sealed record OrderRow(int Id, int CustomerId, decimal Total);

    public sealed partial class SmokeRepo(IAsyncDbConnection connection)
    {
        [Query("SELECT Id, CustomerId, Total FROM Orders WHERE Id = @id")]
        public partial Task<OrderRow?> GetByIdAsync(int id, CancellationToken ct);
    }
}
