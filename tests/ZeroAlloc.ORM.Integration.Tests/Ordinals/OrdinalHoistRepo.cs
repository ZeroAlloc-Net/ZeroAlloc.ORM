using System.Data.Async;
using System.Runtime.CompilerServices;

namespace ZeroAlloc.ORM.Integration.Tests.Ordinals;

// #326 — every shape that reads a DomainEntity once per row: a buffered list, a
// stream, and a multi-result tuple of two lists whose columns share names.
public sealed partial class OrdinalHoistRepo(IAsyncDbConnection connection)
{
    [Query("SELECT Nickname, Name, Id FROM Customers ORDER BY Id")]
    public partial Task<IReadOnlyList<NicknamedCustomer>> ListAsync(CancellationToken ct);

    [Query("SELECT Nickname, Name, Id FROM Customers ORDER BY Id")]
    public partial IAsyncEnumerable<NicknamedCustomer> StreamAsync([EnumeratorCancellation] CancellationToken ct);

    [Query(
        "SELECT Nickname, Name, Id FROM Customers WHERE Id <= @split ORDER BY Id; SELECT Id, Name, Nickname FROM Customers WHERE Id > @split ORDER BY Id;",
        Batch = BatchMode.Never)]
    public partial Task<(IReadOnlyList<NicknamedCustomer> Low, IReadOnlyList<NicknamedCustomer> High)?> SplitAsync(
        int split,
        CancellationToken ct);
}
