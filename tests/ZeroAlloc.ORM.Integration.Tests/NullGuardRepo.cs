using System.Data.Async;
using System.Runtime.CompilerServices;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests;

// #249 — every reader shape that materializes a row, pointed at the NullGuard
// table that NullColumnGuardTestsBase seeds. The SQL is plain enough to run on
// Sqlite, SQL Server and Postgres unchanged.
public sealed partial class NullGuardRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Id, Name, Quantity, State, Ref FROM NullGuard WHERE Id = @id")]
    public partial Task<NullGuardRow?> GetRowAsync(int id, CancellationToken ct);

    [Query("SELECT Id, Name, Quantity, State, Ref FROM NullGuard WHERE Id = @id")]
    public partial Task<NullableGuardRow?> GetNullableRowAsync(int id, CancellationToken ct);

    [Query("SELECT Id, Amount, Currency FROM NullGuard WHERE Id = @id")]
    public partial Task<NullGuardMoneyRow?> GetMoneyRowAsync(int id, CancellationToken ct);

    [Query("SELECT Quantity, Name, Id FROM NullGuard WHERE Id = @id")]
    public partial Task<NullGuardEntity?> GetEntityAsync(int id, CancellationToken ct);

    [Query("SELECT Id, Name, Quantity, State, Ref FROM NullGuard WHERE Id = @id")]
    public partial Task<IReadOnlyList<NullGuardRow>> ListRowsAsync(int id, CancellationToken ct);

    [Query("SELECT Id, Name, Quantity, State, Ref FROM NullGuard WHERE Id = @id")]
    public partial IAsyncEnumerable<NullGuardRow> StreamRowsAsync(int id, [EnumeratorCancellation] CancellationToken ct);

    // #249 — a value the row type rejects. Row 7 has no NULL, so the value
    // object's own exception is the one that must surface.
    [Query("SELECT Id, Name, Quantity FROM NullGuard WHERE Id = @id")]
    public partial Task<CheckedQuantityRow?> GetCheckedAsync(int id, CancellationToken ct);

    // #249 — Name is text, read here into the int Quantity: a genuine cast error
    // on SQL Server and Postgres, with no NULL anywhere in row 7.
    [Query("SELECT Id, Name, Name AS Quantity, State, Ref FROM NullGuard WHERE Id = @id")]
    public partial Task<NullGuardRow?> GetMistypedRowAsync(int id, CancellationToken ct);

    // #249 — a multi-result tuple: a Row element from the first result set and a
    // List element from the second, each read inside its own NULL-column catch.
    [Query("""
        SELECT Id, Name, Quantity, State, Ref FROM NullGuard WHERE Id = @headId;
        SELECT Id, Name, Quantity, State, Ref FROM NullGuard WHERE Id = @listId;
        """)]
    public partial Task<(NullGuardRow Head, IReadOnlyList<NullGuardRow> Rows)> GetHeadAndRowsAsync(
        int headId, int listId, CancellationToken ct);
}
