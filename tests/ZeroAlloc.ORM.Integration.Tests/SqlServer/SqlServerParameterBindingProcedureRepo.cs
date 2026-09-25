using System.Data.Async;
using ZeroAlloc.ORM;

namespace ZeroAlloc.ORM.Integration.Tests.SqlServer;

// v2.0, #219 — a [StoredProcedure] whose C# parameters are declared in the
// opposite order to the procedure's. For CommandType.StoredProcedure SqlClient
// sends an RPC with named arguments and writes each name with an `@`
// prepended when the ParameterName lacks one, so the generator's bare
// `amount` reaches the server as `@amount` and binds by name, not position.
public sealed partial class SqlServerParameterBindingProcedureRepo(IAsyncDbConnection connection)
{
    [StoredProcedure("dbo.subtract_proc")]
    public partial Task<int> SubtractAsync(int subtrahend, int amount, CancellationToken ct);
}
